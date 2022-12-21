using System.Net;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Polly;
using Polly.RateLimit;
using Polly.Wrap;
using SQLite;

var host = Host.CreateDefaultBuilder(args)
	.UseSystemd()
	.ConfigureServices(services =>
	{
		services.AddHostedService<BotService>();
	})
	.Build();

host.Run();

sealed class BotService : BackgroundService
{
	public BotService(ILogger<BotService> logger, IConfiguration config)
	{
		m_logger = logger;
		m_config = config;
	}

	protected async override Task ExecuteAsync(CancellationToken stoppingToken)
	{
		var rateLimitPolicy = Policy.RateLimitAsync(250, TimeSpan.FromMinutes(1));
		var rateLimitRetryPolicy = Policy
			.Handle<RateLimitRejectedException>()
			.WaitAndRetryForeverAsync((retryAttempt, exception, context) => (exception as RateLimitRejectedException)!.RetryAfter, (_, _, _) => Task.CompletedTask);
		var retryPolicy = Policy
			.HandleResult<HttpResponseMessage>(r => r.StatusCode == HttpStatusCode.BadGateway)
			.WaitAndRetryForeverAsync(retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
		var apiCallPolicy = retryPolicy
			.WrapAsync(rateLimitRetryPolicy)
			.WrapAsync(rateLimitPolicy);

		using var client = new HttpClient();
		using var discordClient = new DiscordSocketClient();
		var databasePath = "mplus-data.db";

		using var db = new SQLiteConnection(databasePath);
		db.CreateTable<Character>();

		discordClient.Log += (LogMessage msg) =>
		{
			Console.WriteLine(msg.ToString());

			return Task.CompletedTask;
		};

		await discordClient.LoginAsync(TokenType.Bot, m_config["Discord:Token"]).ConfigureAwait(false);
		await discordClient.StartAsync().ConfigureAwait(false);

		var ready = false;
		discordClient.Ready += async () =>
		{
			var guild = discordClient.Guilds.Single();
			var guildCommand = new SlashCommandBuilder();
			guildCommand
				.WithName("follow")
				.WithDescription("Follows a specific character on Raider.IO.")
				.AddOption("character", ApplicationCommandOptionType.String, "Your character name.", isRequired: true)
				.AddOption("realm", ApplicationCommandOptionType.String, "Your character's server.", isRequired: true)
				.AddOption("region", ApplicationCommandOptionType.String, "Your character's region.", isRequired: true);

			try
			{
				await guild.CreateApplicationCommandAsync(guildCommand.Build()).ConfigureAwait(false);
			}
			catch (HttpException exception)
			{
				var json = JsonConvert.SerializeObject(exception.Errors, Formatting.Indented);
				Console.WriteLine(json);
			}

			ready = true;

			return;
		};
		discordClient.SlashCommandExecuted += async (SocketSlashCommand command) =>
		{
			switch (command.Data.Name)
			{
				case "follow":
					var characterName = (command.Data.Options.First(x => x.Name == "character").Value as string)!;
					var realm = (command.Data.Options.First(x => x.Name == "realm").Value as string)!;
					var region = (command.Data.Options.First(x => x.Name == "region").Value as string)!;

					var profile = await GetCharacterAsync(client, characterName, realm, region, apiCallPolicy).ConfigureAwait(false);
					if (profile is null)
					{
						var embed = new EmbedBuilder()
							.WithColor(Color.Red)
							.WithDescription($@"Error! Unable to follow character.{Environment.NewLine}{Environment.NewLine}To follow a character, the general format is `/follow character realm region`.");

						await command.RespondAsync(embed: embed.Build()).ConfigureAwait(false);
					}
					else
					{
						var runId = GetMostRecentRunId(profile);
						var character = new Character
						{
							Name = characterName!,
							Realm = realm!,
							Region = region!,
							MostRecentRunId = runId,
						};
						var rowsInserted = db.Insert(character, "OR IGNORE");

						if (rowsInserted == 1)
							await command.RespondAsync($"Now following {characterName} on {realm}-{region}!").ConfigureAwait(false);
						else
							await command.RespondAsync($"Already following {characterName} on {realm}-{region}!").ConfigureAwait(false);
					}

					break;
				default:
					throw new InvalidOperationException($"Unknown slash command {command.Data.Name}!");
			}
		};
		while (!ready) { }

		while (!stoppingToken.IsCancellationRequested)
		{
			var guild = discordClient.Guilds.Single();
			var channel = guild.Channels.Single(c => c.Name == m_config["Discord:Channel"]) as IMessageChannel;

			var charactersToUpdate = new List<Character>();
			var runIds = new HashSet<string>();
			foreach (var character in db.Table<Character>())
			{
				var profile = await GetCharacterAsync(client, character.Name, character.Realm, character.Region, apiCallPolicy).ConfigureAwait(false);
				if (profile is null)
					continue;

				runIds.UnionWith(profile.Mythic_Plus_Recent_Runs
					.Select(run => GetRunId(run))
					.Where(runId => runId is not null)
					.Cast<string>()
					.TakeWhile(runId => runId != character.MostRecentRunId));
				character.MostRecentRunId = GetMostRecentRunId(profile);

				charactersToUpdate.Add(character);
			}

			foreach (var runId in runIds)
			{
				var runInfo = (await GetJsonAsync<MythicPlusRunDto>(client, $"https://raider.io/api/mythic-plus/runs/{runId}", apiCallPolicy).ConfigureAwait(false))?.KeystoneRun;
				if (runInfo is null)
					continue;

				var percentage = (double)runInfo.Clear_Time_Ms / (double)runInfo.Keystone_Time_Ms;
				var percentageString = percentage < 1 ? $"{1 - percentage:P1} remaining" : $"{percentage - 1:P1} over";

				var rosterString = string.Join(Environment.NewLine, runInfo.Roster
					.OrderBy(r => r.Role)
					.Select(r => $"{GetRoleEmoji(r.Role)} [{r.Character.Name.Split('-')[0]}](https://raider.io{r.Character.Path}) - **{r.Role}** ({r.Character.Spec.Name} {r.Character.Class.Name}) - {r.Ranks.Score:0} Score"));

				var embed = new EmbedBuilder()
					.WithFooter(footer => footer.Text = "Data provided by Raider.IO")
					.WithTitle($"+{runInfo.Mythic_Level} {runInfo.Dungeon.Name}")
					.WithColor(Color.Gold)
					.WithDescription($@"Cleared in {TimeSpan.FromMilliseconds(runInfo.Clear_Time_Ms):mm':'ss} of {TimeSpan.FromMilliseconds(runInfo.Keystone_Time_Ms):mm':'ss} ({percentageString}).{Environment.NewLine}{Environment.NewLine}{rosterString}")
					.WithUrl($"https://raider.io/mythic-plus-runs/{runId}")
					.WithImageUrl($"https://cdnassets.raider.io/images/dungeons/expansion{runInfo.Dungeon.Expansion_Id}/base/{runInfo.Dungeon.Slug}.jpg")
					.WithTimestamp(DateTimeOffset.Parse(runInfo.Completed_At));

				await channel!.SendMessageAsync(embed: embed.Build()).ConfigureAwait(false);
			}

			db.UpdateAll(charactersToUpdate);

			await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken).ConfigureAwait(false);
		}

		await discordClient.LogoutAsync().ConfigureAwait(false);
	}

	static async Task<CharacterDto?> GetCharacterAsync(HttpClient client, string name, string realm, string region, AsyncPolicyWrap<HttpResponseMessage> apiCallPolicy) =>
		await GetJsonAsync<CharacterDto>(client, $"https://raider.io/api/v1/characters/profile?region={region}&realm={realm}&name={name}&fields=mythic_plus_recent_runs", apiCallPolicy).ConfigureAwait(false);

	static string? GetMostRecentRunId(CharacterDto character) => GetRunId(character.Mythic_Plus_Recent_Runs.FirstOrDefault());

	static string? GetRunId(MythicPlusRecentRunDto? run) => run is null ? null : string.Join("", new Uri(run.Url).Segments.TakeLast(2));

	static async Task<T?> GetJsonAsync<T>(HttpClient client, string url, AsyncPolicyWrap<HttpResponseMessage> apiCallPolicy)
	{
		var result = await apiCallPolicy.ExecuteAsync(async () => await client.GetAsync(url).ConfigureAwait(false)).ConfigureAwait(false);
		if (!result.IsSuccessStatusCode)
		{
			Console.WriteLine($"ERROR - {await result.Content.ReadAsStringAsync().ConfigureAwait(false)}");
			return default(T);
		}

		return JsonConvert.DeserializeObject<T>(await result.Content.ReadAsStringAsync().ConfigureAwait(false));
	}

	static string GetRoleEmoji(Role role) => role switch
	{
		Role.Tank => "🛡️",
		Role.Healer => "💉",
		Role.Dps => "⚔️",
		_ => throw new InvalidOperationException($"No emoji found for role {role}!"),
	};

	sealed class CharacterDto
	{
		public string Name { get; set; }
		public long Id { get; set; }
		public IReadOnlyList<MythicPlusRecentRunDto> Mythic_Plus_Recent_Runs { get; set; }
	}

	sealed class ClassDto
	{
		public string Name { get; set; }
	}

	sealed class SpecDto
	{
		public string Name { get; set; }
	}

	sealed class MythicPlusRecentRunDto
	{
		public string Dungeon { get; set; }
		public int Mythic_Level { get; set; }
		public int Clear_Time_Ms { get; set; }
		public int Par_Time_Ms { get; set; }
		public string Url { get; set; }
		public string Completed_At { get; set; }
	}

	sealed class MythicPlusRunDto
	{
		public MythicPlusKeystoneRunDto KeystoneRun { get; set; }
	}

	sealed class MythicPlusKeystoneRunDto
	{
		public IReadOnlyList<RosterMemberDto> Roster { get; set; }
		public DungeonDto Dungeon { get; set; }
		public int Mythic_Level { get; set; }
		public int Clear_Time_Ms { get; set; }
		public int Keystone_Time_Ms { get; set; }
		public string Completed_At { get; set; }
	}

	sealed class DungeonDto
	{
		public string Name { get; set; }
		public string Slug { get; set; }
		public int Expansion_Id { get; set; }
	}

	sealed class RosterMemberDto
	{
		public RosterCharacterDto Character { get; set; }
		public MythicPlusScoreDto Ranks { get; set; }
		public Role Role { get; set; }
	}

	sealed class RosterCharacterDto
	{
		public string Name { get; set; }
		public ClassDto Class { get; set; }
		public SpecDto Spec { get; set; }
		public string Path { get; set; }
	}

	sealed class MythicPlusScoreDto
	{
		public double Score { get; set; }
	}

	enum Role
	{
		Tank,
		Healer,
		Dps,
	}

	sealed class Character
	{
		[PrimaryKey, AutoIncrement]
		public int Id { get; set; }
		[Unique(Name = "UK_Character_Name_Realm_Region"), Collation("NOCASE")]
		public string Name { get; set; }
		[Unique(Name = "UK_Character_Name_Realm_Region"), Collation("NOCASE")]
		public string Realm { get; set; }
		[Unique(Name = "UK_Character_Name_Realm_Region"), Collation("NOCASE")]
		public string Region { get; set; }
		public string? MostRecentRunId { get; set; }
	}

	private readonly ILogger<BotService> m_logger;
	private readonly IConfiguration m_config;
}
