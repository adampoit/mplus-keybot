using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

public sealed class BotStatusRotator
{
	internal const int BotStatusMaxLength = 32;
	internal static readonly TimeSpan RotationInterval = TimeSpan.FromHours(1);

	internal const string BotStatusPrefix = "/help | ";

	internal static readonly string[] BotStatusSuffixes =
	{
		"io go up",
		"one more key, then bed",
		"defeat. deplete. repeat.",
		"+3 or it didn't happen",
		"where's my portal?",
		"vibe routing",
		"floor pov",
		"zero interrupts detected",
		"50 io minus",
		"at least i got vault",
		"Leeeroooooy Jeeeenkins!",
		"Grizzly Hills key when?",
		"corpse run simulator",
	};

	internal static IReadOnlyList<string> BotStatuses => BuildStatuses();

	internal static IReadOnlyList<string> BuildStatuses()
	{
		var statuses = new string[BotStatusSuffixes.Length];
		for (var i = 0; i < BotStatusSuffixes.Length; i++)
			statuses[i] = BotStatusPrefix + BotStatusSuffixes[i];
		return statuses;
	}

	private readonly DiscordSocketClient m_discordClient;
	private readonly ILogger<BotStatusRotator> m_logger;
	private readonly object m_gate = new();
	private CancellationTokenSource? m_cancellation;
	private Task? m_loopTask;

	public BotStatusRotator(DiscordSocketClient discordClient, ILogger<BotStatusRotator> logger)
	{
		m_discordClient = discordClient ?? throw new ArgumentNullException(nameof(discordClient));
		m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	public async Task StartAsync()
	{
		lock (m_gate)
		{
			if (m_loopTask is not null)
				return;

			m_cancellation = new CancellationTokenSource();
			m_loopTask = RotateAsync(m_cancellation.Token);
		}

		await SetStatusAsync(GetCurrentStatus()).ConfigureAwait(false);
	}

	public async Task StopAsync()
	{
		Task? loopTask;
		lock (m_gate)
		{
			if (m_cancellation is not null)
			{
				m_cancellation.Cancel();
				m_cancellation.Dispose();
				m_cancellation = null;
			}
			loopTask = m_loopTask;
			m_loopTask = null;
		}

		if (loopTask is not null)
		{
			try
			{
				await loopTask.ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
			}
		}
	}

	internal int GetCurrentRotationIndex()
	{
		return GetRotationIndex(DateTimeOffset.UtcNow, BotStatuses.Count);
	}

	internal static int GetRotationIndex(DateTimeOffset timestamp, int statusCount)
	{
		if (statusCount <= 0)
			throw new ArgumentOutOfRangeException(nameof(statusCount), "At least one bot status is required.");

		var index = GetRotationSlot(timestamp) % statusCount;
		return (int)(index < 0 ? index + statusCount : index);
	}

	internal string GetCurrentStatus()
	{
		var statuses = BotStatuses;
		return statuses[GetRotationIndex(DateTimeOffset.UtcNow, statuses.Count)];
	}

	internal static TimeSpan GetDelayUntilNextRotation(DateTimeOffset timestamp)
	{
		var ticksIntoInterval = GetTicksIntoInterval(timestamp);
		return TimeSpan.FromTicks(ticksIntoInterval == 0 ? RotationInterval.Ticks : RotationInterval.Ticks - ticksIntoInterval);
	}

	private static long GetRotationSlot(DateTimeOffset timestamp)
	{
		return GetTicksSinceEpoch(timestamp) / RotationInterval.Ticks;
	}

	private static long GetTicksIntoInterval(DateTimeOffset timestamp)
	{
		var ticksIntoInterval = GetTicksSinceEpoch(timestamp) % RotationInterval.Ticks;
		return ticksIntoInterval < 0 ? ticksIntoInterval + RotationInterval.Ticks : ticksIntoInterval;
	}

	private static long GetTicksSinceEpoch(DateTimeOffset timestamp)
	{
		return timestamp.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks;
	}

	private async Task RotateAsync(CancellationToken cancellationToken)
	{
		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				await Task.Delay(GetDelayUntilNextRotation(DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);
				await SetStatusAsync(GetCurrentStatus()).ConfigureAwait(false);
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			m_logger.LogError(ex, "Bot status rotation loop terminated unexpectedly.");
		}
	}

	private async Task SetStatusAsync(string status)
	{
		try
		{
			await m_discordClient.SetGameAsync(status, type: ActivityType.CustomStatus).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			m_logger.LogWarning(ex, "Failed to set bot status to {Status}.", status);
		}
	}
}
