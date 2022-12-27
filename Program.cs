using Discord;
using Discord.Net;
using Discord.WebSocket;
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
		services.AddHostedService<BotService>();

		services.AddHttpClient<RaiderIOClient>();
		services.AddSingleton<DiscordSocketClient>();
		services.AddSingleton<RaiderIOClient>();
		services.AddSingleton<SQLiteConnection>(c =>
		{
			var db = new SQLiteConnection("mplus-data.db");

			db.CreateTable<Character>();

			return db;
		});

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
				.WithIdentity("Combined Configuration Trigger")
				.WithSimpleSchedule(x => x
					.WithIntervalInMinutes(5)
					.RepeatForever())
				.WithDescription("my awesome trigger configured for a job with single call")
			);
		});
		services.AddQuartzHostedService(opt =>
		{
			opt.WaitForJobsToComplete = true;
		});
	})
	.Build();

host.Run();

sealed class BotService : BackgroundService
{
	public BotService(ILogger<BotService> logger, IConfiguration config, SQLiteConnection db, DiscordSocketClient discordClient, RaiderIOClient raiderIOClient)
	{
		m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		m_config = config ?? throw new ArgumentNullException(nameof(config));
		m_db = db ?? throw new ArgumentNullException(nameof(db));
		m_discordClient = discordClient ?? throw new ArgumentNullException(nameof(discordClient));
		m_raiderIOClient = raiderIOClient ?? throw new ArgumentNullException(nameof(raiderIOClient));
	}

	protected async override Task ExecuteAsync(CancellationToken stoppingToken)
	{
		m_discordClient.Log += (LogMessage msg) =>
		{
			Console.WriteLine(msg.ToString());

			return Task.CompletedTask;
		};

		await m_discordClient.LoginAsync(TokenType.Bot, m_config["Discord:Token"]).ConfigureAwait(false);
		await m_discordClient.StartAsync().ConfigureAwait(false);

		var ready = false;
		m_discordClient.Ready += async () =>
		{
			var guild = m_discordClient.Guilds.Single();
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
		m_discordClient.SlashCommandExecuted += async (SocketSlashCommand command) =>
		{
			switch (command.Data.Name)
			{
				case "follow":
					var characterName = (command.Data.Options.First(x => x.Name == "character").Value as string)!;
					var realm = (command.Data.Options.First(x => x.Name == "realm").Value as string)!;
					var region = (command.Data.Options.First(x => x.Name == "region").Value as string)!;

					var profile = await m_raiderIOClient.GetCharacterAsync(characterName, realm, region).ConfigureAwait(false);
					if (profile is null)
					{
						var embed = new EmbedBuilder()
							.WithColor(Color.Red)
							.WithDescription($@"Error! Unable to follow character.{Environment.NewLine}{Environment.NewLine}To follow a character, the general format is `/follow character realm region`.");

						await command.RespondAsync(embed: embed.Build()).ConfigureAwait(false);
					}
					else
					{
						var runId = profile.Mythic_Plus_Recent_Runs.FirstOrDefault()?.RunId;
						var character = new Character
						{
							Name = characterName!,
							Realm = realm!,
							Region = region!,
							MostRecentRunId = runId,
						};
						var rowsInserted = m_db.Insert(character, "OR IGNORE");

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

		await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);

		await m_discordClient.LogoutAsync().ConfigureAwait(false);
	}

	private readonly ILogger<BotService> m_logger;
	private readonly IConfiguration m_config;

	private readonly SQLiteConnection m_db;
	private readonly DiscordSocketClient m_discordClient;
	private readonly RaiderIOClient m_raiderIOClient;
}

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
