using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Quartz;
using Quartz.Logging;
using SQLite;

LogProvider.SetCurrentLogProvider(new ConsoleLogProvider());

var host = Host.CreateDefaultBuilder(args)
	.UseSystemd()
	.ConfigureServices((context, services) =>
	{
		services.AddHttpClient<RaiderIOClient>();
		services.AddSingleton<DiscordSocketClient>();
		services.AddSingleton<RaiderIOClient>();
		services.AddSingleton<SQLiteConnection>(c =>
		{
			var db = new SQLiteConnection("mplus-data.db");

			db.CreateTable<Character>();
			db.CreateTable<AffixInfo>();
			db.CreateTable<MythicPlusRun>();

			return db;
		});
		services.AddSingleton<IMemoryCache>(c => new MemoryCache(new MemoryCacheOptions { ExpirationScanFrequency = TimeSpan.FromMinutes(5) }));

		services.AddQuartz(q =>
		{
			q.UseMicrosoftDependencyInjectionJobFactory();

			q.UseSimpleTypeLoader();
			q.UseInMemoryStore();
			q.UseDefaultThreadPool(tp =>
			{
				tp.MaxConcurrency = 10;
			});

			q.ScheduleJob<CheckRunsJob>(trigger => trigger
				.WithIdentity("Every 5 Minutes")
				.WithSimpleSchedule(x => x
					.WithIntervalInMinutes(5)
					.RepeatForever())
				.WithDescription("Checks Raider.IO for recent mythic plus runs on followed characters.")
			);

			q.ScheduleJob<CheckAffixesJob>(trigger => trigger
				.WithIdentity("Every Hour")
				.WithSimpleSchedule(x => x
					.WithIntervalInMinutes(60)
					.RepeatForever())
				.WithDescription("Checks Raider.IO for updated weekly affixes.")
			);
		});
		services.AddQuartzHostedService(opt =>
		{
			opt.WaitForJobsToComplete = true;
		});
	})
	.Build();

var discordClient = host.Services.GetRequiredService<DiscordSocketClient>();
var logger = host.Services.GetRequiredService<ILogger<DiscordSocketClient>>();
var config = host.Services.GetRequiredService<IConfiguration>();
var raiderIOClient = host.Services.GetRequiredService<RaiderIOClient>();
var db = host.Services.GetRequiredService<SQLiteConnection>();

discordClient.Log += (LogMessage msg) =>
{
	logger.LogInformation(msg.ToString());

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
		logger.LogError(json);
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

			var profile = await raiderIOClient.GetCharacterAsync(characterName, realm, region).ConfigureAwait(false);
			if (profile.IsFailure)
			{
				var embed = new EmbedBuilder()
					.WithColor(Color.Red)
					.WithDescription($@"Error! Unable to follow character.{Environment.NewLine}{Environment.NewLine}To follow a character, the general format is `/follow character realm region`.");

				await command.RespondAsync(embed: embed.Build()).ConfigureAwait(false);
			}
			else
			{
				db.InsertAll(profile.Result!.Mythic_Plus_Recent_Runs.Select(x => new MythicPlusRun { Id = x.RunId, Date = DateTimeOffset.Parse(x.Completed_At) }), "OR IGNORE");
				var character = new Character
				{
					Name = characterName!,
					Realm = realm!,
					Region = region!,
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

SpinWait.SpinUntil(() => ready);

host.Run();

sealed class ConsoleLogProvider : ILogProvider
{
	public Logger GetLogger(string name)
	{
		return (level, func, exception, parameters) =>
		{
			if (level >= Quartz.Logging.LogLevel.Info && func != null)
			{
				Console.WriteLine("[" + DateTime.Now.ToLongTimeString() + "] [" + level + "] " + func(), parameters);
			}
			return true;
		};
	}

	public IDisposable OpenNestedContext(string message)
	{
		throw new NotImplementedException();
	}

	public IDisposable OpenMappedContext(string key, object value, bool destructure = false)
	{
		throw new NotImplementedException();
	}
}
