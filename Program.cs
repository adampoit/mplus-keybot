using System.Net;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Polly;
using Polly.RateLimit;
using Polly.Wrap;
using SQLite;

var config = new ConfigurationBuilder()
	.AddJsonFile("appsettings.json")
	.Build();

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
var discordClient = new DiscordSocketClient();
var databasePath = "mplus-data.db";

var db = new SQLiteConnection(databasePath);
db.CreateTable<Character>();

discordClient.Log += (LogMessage msg) =>
{
	Console.WriteLine(msg.ToString());

	return Task.CompletedTask;
};

await discordClient.LoginAsync(TokenType.Bot, config["Discord:Token"]).ConfigureAwait(false);
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
			var characterName = command.Data.Options.First(x => x.Name == "character").Value as string;
			var realm = command.Data.Options.First(x => x.Name == "realm").Value as string;
			var region = command.Data.Options.First(x => x.Name == "region").Value as string;

			var character = new Character
			{
				Name = characterName!,
				Realm = realm!,
				Region = region!,
			};
			db.Insert(character);

			await command.RespondAsync($"Now following {characterName} on {realm}-{region}!").ConfigureAwait(false);
			break;
		default:
			throw new InvalidOperationException($"Unknown slash command {command.Data.Name}!");
	}
};
while (!ready) { }

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (sender, eventArgs) =>
{
	eventArgs.Cancel = true;
	cts.Cancel();
};
while (!cts.IsCancellationRequested)
{
	var guild = discordClient.Guilds.Single();
	var channel = guild.Channels.Single(c => c.Name == config["Discord:Channel"]) as IMessageChannel;

	foreach (var character in db.Table<Character>())
	{
		var profile = await GetJsonAsync<CharacterDto>(client, $"https://raider.io/api/v1/characters/profile?region={character.Region}&realm={character.Realm}&name={character.Name}&fields=mythic_plus_recent_runs", apiCallPolicy).ConfigureAwait(false);
		if (profile is null)
			continue;

		var run = profile.Mythic_Plus_Recent_Runs.First();
		var percentage = (double)run.Clear_Time_Ms / (double)run.Par_Time_Ms;
		var percentageString = percentage < 1 ? $"{1 - percentage:P1} remaining" : $"{percentage - 1:P1} over";

		var runId = string.Join("", new Uri(run.Url).Segments.TakeLast(2));
		var additionalRunInfo = await GetJsonAsync<MythicPlusRunDto>(client, $"https://raider.io/api/mythic-plus/runs/{runId}", apiCallPolicy).ConfigureAwait(false);
		if (additionalRunInfo is null)
			continue;

		var rosterString = string.Join(Environment.NewLine, additionalRunInfo.KeystoneRun.Roster.OrderBy(r => r.Role).Select(r => $"{GetRoleEmoji(r.Role)} [{r.Character.Name.Split('-')[0]}](https://raider.io{r.Character.Path}) - **{r.Role}** ({r.Character.Spec.Name} {r.Character.Class.Name}) - {r.Ranks.Score:0} Score"));

		var embed = new EmbedBuilder()
			.WithFooter(footer => footer.Text = "Data provided by Raider.IO")
			.WithTitle($"+{run.Mythic_Level} {run.Dungeon}")
			.WithColor(Color.Gold)
			.WithDescription($@"Cleared in {TimeSpan.FromMilliseconds(run.Clear_Time_Ms):mm':'ss} of {TimeSpan.FromMilliseconds(run.Par_Time_Ms):mm':'ss} ({percentageString}).{Environment.NewLine}{Environment.NewLine}{rosterString}")
			.WithUrl($"https://raider.io/mythic-plus-runs/{runId}")
			.WithImageUrl($"https://cdnassets.raider.io/images/dungeons/expansion{additionalRunInfo.KeystoneRun.Dungeon.Expansion_Id}/base/{additionalRunInfo.KeystoneRun.Dungeon.Slug}.jpg")
			.WithTimestamp(DateTimeOffset.Parse(run.Completed_At));

		await channel!.SendMessageAsync(embed: embed.Build()).ConfigureAwait(false);
	}

	try
	{
		await Task.Delay(TimeSpan.FromMinutes(5), cts.Token).ConfigureAwait(false);
	}
	catch (TaskCanceledException) { }
}

await discordClient.LogoutAsync().ConfigureAwait(false);

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
	public string Name { get; set; }
	public string Realm { get; set; }
	public string Region { get; set; }
}