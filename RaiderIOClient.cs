using System.Net;
using Newtonsoft.Json;
using Polly;
using Polly.RateLimit;
using Polly.Wrap;

public sealed class RaiderIOClient
{
	public RaiderIOClient(HttpClient client)
	{
		m_client = client ?? throw new ArgumentNullException(nameof(client));

		var rateLimitPolicy = Policy.RateLimitAsync(250, TimeSpan.FromMinutes(1));
		var rateLimitRetryPolicy = Policy
			.Handle<RateLimitRejectedException>()
			.WaitAndRetryForeverAsync((retryAttempt, exception, context) => (exception as RateLimitRejectedException)!.RetryAfter, (_, _, _) => Task.CompletedTask);
		var retryPolicy = Policy
			.HandleResult<HttpResponseMessage>(r => r.StatusCode == HttpStatusCode.BadGateway)
			.WaitAndRetryForeverAsync(retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
		m_apiCallPolicy = retryPolicy
			.WrapAsync(rateLimitRetryPolicy)
			.WrapAsync(rateLimitPolicy);
	}

	public async Task<CharacterDto?> GetCharacterAsync(string name, string realm, string region) =>
		await GetJsonAsync<CharacterDto>(m_client, $"https://raider.io/api/v1/characters/profile?region={region}&realm={realm}&name={name}&fields=mythic_plus_recent_runs", m_apiCallPolicy).ConfigureAwait(false);

	public async Task<MythicPlusRunDto?> GetMythicPlusRunAsync(string runId) => await GetJsonAsync<MythicPlusRunDto>(m_client, $"https://raider.io/api/mythic-plus/runs/{runId}", m_apiCallPolicy).ConfigureAwait(false);

	private static async Task<T?> GetJsonAsync<T>(HttpClient client, string url, AsyncPolicyWrap<HttpResponseMessage> apiCallPolicy)
	{
		var result = await apiCallPolicy.ExecuteAsync(async () => await client.GetAsync(url).ConfigureAwait(false)).ConfigureAwait(false);
		if (!result.IsSuccessStatusCode)
		{
			Console.WriteLine($"ERROR - {await result.Content.ReadAsStringAsync().ConfigureAwait(false)}");
			return default(T);
		}

		return JsonConvert.DeserializeObject<T>(await result.Content.ReadAsStringAsync().ConfigureAwait(false));
	}

	private readonly HttpClient m_client;
	private readonly AsyncPolicyWrap<HttpResponseMessage> m_apiCallPolicy;
}

public sealed class CharacterDto
{
	public string Name { get; set; }
	public long Id { get; set; }
	public IReadOnlyList<MythicPlusRecentRunDto> Mythic_Plus_Recent_Runs { get; set; }
}

public sealed class MythicPlusRecentRunDto
{
	public string Dungeon { get; set; }
	public int Mythic_Level { get; set; }
	public int Clear_Time_Ms { get; set; }
	public int Par_Time_Ms { get; set; }
	public string Url { get; set; }
	public string Completed_At { get; set; }

	public string RunId => string.Join("", new Uri(Url).Segments.TakeLast(2));
}

public sealed class MythicPlusRunDto
{
	public MythicPlusKeystoneRunDto KeystoneRun { get; set; }
}

public sealed class MythicPlusKeystoneRunDto
{
	public IReadOnlyList<RosterMemberDto> Roster { get; set; }
	public DungeonDto Dungeon { get; set; }
	public int Mythic_Level { get; set; }
	public int Clear_Time_Ms { get; set; }
	public int Keystone_Time_Ms { get; set; }
	public string Completed_At { get; set; }
}

public sealed class RosterMemberDto
{
	public RosterCharacterDto Character { get; set; }
	public MythicPlusScoreDto Ranks { get; set; }
	public Role Role { get; set; }
}

public sealed class ClassDto
{
	public string Name { get; set; }
}

public sealed class SpecDto
{
	public string Name { get; set; }
}

public sealed class DungeonDto
{
	public string Name { get; set; }
	public string Slug { get; set; }
	public int Expansion_Id { get; set; }
}

public sealed class RosterCharacterDto
{
	public string Name { get; set; }
	public ClassDto Class { get; set; }
	public SpecDto Spec { get; set; }
	public string Path { get; set; }
}

public sealed class MythicPlusScoreDto
{
	public double Score { get; set; }
}

public enum Role
{
	Tank,
	Healer,
	Dps,
}
