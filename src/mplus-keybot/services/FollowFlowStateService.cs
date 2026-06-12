using System.Security.Cryptography;
using SQLite;

public sealed class FollowFlowStateService
{
	public FollowFlowStateService(SQLiteConnection db)
	{
		m_db = db ?? throw new ArgumentNullException(nameof(db));
	}

	public FollowFlowState Create(string discordUserId, TimeSpan lifetime)
	{
		var state = new FollowFlowState
		{
			State = GenerateState(),
			DiscordUserId = discordUserId,
			CreatedAt = DateTime.UtcNow,
			ExpiresAt = DateTime.UtcNow + lifetime,
		};

		lock (m_lock)
		{
			m_db.Insert(state);
			m_db.Execute("DELETE FROM FollowFlowState WHERE ExpiresAt < ?", DateTime.UtcNow - TimeSpan.FromHours(1));
		}

		return state;
	}

	public FollowFlowConsumeResult? Consume(string state)
	{
		lock (m_lock)
		{
			var flow = m_db.Table<FollowFlowState>().FirstOrDefault(x => x.State == state);
			if (flow is null || flow.ConsumedAt is not null || flow.ExpiresAt <= DateTime.UtcNow)
				return null;

			flow.ConsumedAt = DateTime.UtcNow;
			m_db.Update(flow);
			return new FollowFlowConsumeResult(flow.DiscordUserId);
		}
	}

	private static string GenerateState() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
		.Replace('+', '-')
		.Replace('/', '_')
		.TrimEnd('=');

	private readonly SQLiteConnection m_db;
	private readonly object m_lock = new();
}

public sealed record FollowFlowConsumeResult(string DiscordUserId);
