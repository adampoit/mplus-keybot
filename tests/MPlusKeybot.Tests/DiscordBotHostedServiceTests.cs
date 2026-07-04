using MPlusKeybot.Api.Services;
namespace MPlusKeybot.Tests;

public sealed class DiscordBotHostedServiceTests
{
	[Fact]
	public void AllBotStatusesStartWithHelpPrefix()
	{
		Assert.NotEmpty(BotStatusRotator.BotStatuses);
		Assert.True(BotStatusRotator.BotStatuses.Count > 1, "Expected more than one rotating bot status.");

		foreach (var status in BotStatusRotator.BotStatuses)
		{
			Assert.StartsWith(BotStatusRotator.BotStatusPrefix, status);
		}
	}

	[Fact]
	public void AllBotStatusesFitMemberListLimit()
	{
		Assert.NotEmpty(BotStatusRotator.BotStatuses);

		foreach (var status in BotStatusRotator.BotStatuses)
		{
			Assert.True(
				status.Length <= BotStatusRotator.BotStatusMaxLength,
				$"Bot status \"{status}\" is {status.Length} chars, must be {BotStatusRotator.BotStatusMaxLength} or fewer.");
		}
	}

	[Fact]
	public void BuildStatusesCombinesPrefixWithEverySuffix()
	{
		var statuses = BotStatusRotator.BuildStatuses();

		Assert.Equal(BotStatusRotator.BotStatusSuffixes.Length, statuses.Count);
		for (var i = 0; i < BotStatusRotator.BotStatusSuffixes.Length; i++)
			Assert.Equal(BotStatusRotator.BotStatusPrefix + BotStatusRotator.BotStatusSuffixes[i], statuses[i]);
	}

	[Fact]
	public void GetRotationIndexStaysStableWithinSameHour()
	{
		var statusCount = BotStatusRotator.BotStatuses.Count;
		var start = new DateTimeOffset(2026, 6, 20, 10, 5, 0, TimeSpan.Zero);
		var laterSameHour = start.AddMinutes(50);

		var first = BotStatusRotator.GetRotationIndex(start, statusCount);
		var second = BotStatusRotator.GetRotationIndex(laterSameHour, statusCount);

		Assert.InRange(first, 0, statusCount - 1);
		Assert.Equal(first, second);
	}

	[Fact]
	public void GetRotationIndexAdvancesAtHourBoundary()
	{
		var statusCount = BotStatusRotator.BotStatuses.Count;
		var firstHour = new DateTimeOffset(2026, 6, 20, 10, 30, 0, TimeSpan.Zero);
		var nextHour = firstHour.AddHours(1);

		var first = BotStatusRotator.GetRotationIndex(firstHour, statusCount);
		var second = BotStatusRotator.GetRotationIndex(nextHour, statusCount);

		Assert.Equal((first + 1) % statusCount, second);
	}

	[Fact]
	public void GetRotationIndexWorksWithDynamicStatusCounts()
	{
		var timestamp = DateTimeOffset.UnixEpoch.AddHours(47);

		Assert.Equal(2, BotStatusRotator.GetRotationIndex(timestamp, 5));
		Assert.Equal(11, BotStatusRotator.GetRotationIndex(timestamp, 12));
		Assert.Equal(47, BotStatusRotator.GetRotationIndex(timestamp, 60));
	}

	[Fact]
	public void GetDelayUntilNextRotationAlignsToHourBoundary()
	{
		var quarterPast = new DateTimeOffset(2026, 6, 20, 10, 15, 0, TimeSpan.Zero);
		var hourBoundary = new DateTimeOffset(2026, 6, 20, 10, 0, 0, TimeSpan.Zero);

		Assert.Equal(TimeSpan.FromMinutes(45), BotStatusRotator.GetDelayUntilNextRotation(quarterPast));
		Assert.Equal(TimeSpan.FromHours(1), BotStatusRotator.GetDelayUntilNextRotation(hourBoundary));
	}
}
