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
			.HandleResult<HttpResponseMessage>(r =>
				r.StatusCode == HttpStatusCode.BadGateway ||
				r.StatusCode == HttpStatusCode.InternalServerError ||
				r.StatusCode == HttpStatusCode.GatewayTimeout)
			.WaitAndRetryForeverAsync(retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
		m_apiCallPolicy = retryPolicy
			.WrapAsync(rateLimitRetryPolicy)
			.WrapAsync(rateLimitPolicy);
	}

	public async Task<ServiceResult<CharacterDto>> GetCharacterAsync(string name, string realm, string region) => await GetJsonAsync<CharacterDto>(
		$"https://raider.io/api/v1/characters/profile?region={region}&realm={realm}&name={name}&fields=mythic_plus_recent_runs,mythic_plus_scores_by_season:current,mythic_plus_ranks",
		(HttpStatusCode code, string content) =>
		{
			if (code == HttpStatusCode.BadRequest && content.Contains("Could not find requested character"))
				return ErrorResult.CharacterNotFound;

			return null;
		}).ConfigureAwait(false);

	public async Task<ServiceResult<MythicPlusRunDto>> GetMythicPlusRunAsync(string runId) => await GetJsonAsync<MythicPlusRunDto>($"https://raider.io/api/mythic-plus/runs/{runId}").ConfigureAwait(false);


	private async Task<ServiceResult<T>> GetJsonAsync<T>(string url, Func<HttpStatusCode, string, ErrorResult?>? handleNonSuccess = null)
	{
		var result = await m_apiCallPolicy.ExecuteAsync(async () => await m_client.GetAsync(url).ConfigureAwait(false)).ConfigureAwait(false);
		if (!result.IsSuccessStatusCode)
		{
			var content = await result.Content.ReadAsStringAsync().ConfigureAwait(false);
			if (handleNonSuccess is not null)
			{
				var errorResult = handleNonSuccess(result.StatusCode, content);
				if (errorResult is not null)
					return ServiceResult<T>.CreateError(errorResult.Value);
			}

			Console.WriteLine($"ERROR - {content}");
			return ServiceResult<T>.CreateError(ErrorResult.Unknown);
		}

		return ServiceResult<T>.CreateSuccess(JsonConvert.DeserializeObject<T>(await result.Content.ReadAsStringAsync().ConfigureAwait(false))!);
	}

	private readonly HttpClient m_client;
	private readonly AsyncPolicyWrap<HttpResponseMessage> m_apiCallPolicy;
}

public sealed class ServiceResult<T>
{
	public T? Result { get; }

	public ErrorResult? Error { get; }

	public bool IsFailure => Error is not null;

	public static ServiceResult<T> CreateSuccess(T result) => new ServiceResult<T>(result, null);

	public static ServiceResult<T> CreateError(ErrorResult error) => new ServiceResult<T>(default(T), error);

	private ServiceResult(T? result, ErrorResult? error)
	{
		if (result is null && error is null)
			throw new ArgumentException($"One of {nameof(result)} and {nameof(error)} must be set.");

		Result = result;
		Error = error;
	}
}

public enum ErrorResult
{
	Unknown,
	CharacterNotFound,
}

public sealed class CharacterDto
{
	public required string Name { get; set; }
	public required long Id { get; set; }
	public string? Region { get; set; }
	public string? Realm { get; set; }
	public string? Class { get; set; }
	public string? Active_Spec_Name { get; set; }
	public string? Active_Spec_Role { get; set; }
	public required IReadOnlyList<MythicPlusRecentRunDto> Mythic_Plus_Recent_Runs { get; set; }
	public IReadOnlyList<MythicPlusSeasonScoreDto> Mythic_Plus_Scores_By_Season { get; set; } = [];
	public IReadOnlyDictionary<string, MythicPlusRankDto>? Mythic_Plus_Ranks { get; set; }

	public string? CurrentMythicPlusSeason => Mythic_Plus_Scores_By_Season.FirstOrDefault()?.Season;
	public double CurrentMythicPlusScore => Mythic_Plus_Scores_By_Season.FirstOrDefault()?.Scores.All ?? 0;
}

public sealed class MythicPlusRecentRunDto
{
	public required string Dungeon { get; set; }
	public required int Mythic_Level { get; set; }
	public required int Clear_Time_Ms { get; set; }
	public required int Par_Time_Ms { get; set; }
	public required string Url { get; set; }
	public required string Completed_At { get; set; }

	public string RunId => string.Join("", new Uri(Url).Segments.TakeLast(2));
}

public sealed class MythicPlusRunDto
{
	public required MythicPlusKeystoneRunDto KeystoneRun { get; set; }
}

public sealed class MythicPlusKeystoneRunDto
{
	public required IReadOnlyList<RosterMemberDto> Roster { get; set; }
	public required DungeonDto Dungeon { get; set; }
	public required int Mythic_Level { get; set; }
	public required int Clear_Time_Ms { get; set; }
	public required int Keystone_Time_Ms { get; set; }
	public required string Completed_At { get; set; }
}

public sealed class RosterMemberDto
{
	public required RosterCharacterDto Character { get; set; }
	public required MythicPlusScoreDto Ranks { get; set; }
	public required Role Role { get; set; }
}

public sealed class ClassDto
{
	public required string Name { get; set; }
}

public sealed class SpecDto
{
	public required string Name { get; set; }
}

public sealed class DungeonDto
{
	public required string Name { get; set; }
	public required string Slug { get; set; }
	public required int Expansion_Id { get; set; }
}

public sealed class RosterCharacterDto
{
	public required string Name { get; set; }
	public required ClassDto Class { get; set; }
	public required SpecDto Spec { get; set; }
	public required string Path { get; set; }
}

public sealed class MythicPlusScoreDto
{
	public required double Score { get; set; }
}

public sealed class MythicPlusSeasonScoreDto
{
	public required string Season { get; set; }
	public required MythicPlusSeasonScoresDto Scores { get; set; }
}

public sealed class MythicPlusSeasonScoresDto
{
	public required double All { get; set; }
}

public sealed class MythicPlusRankDto
{
	public int World { get; set; }
	public int Region { get; set; }
	public int Realm { get; set; }

	public IEnumerable<(string Lane, int Rank)> GetLaneRanks()
	{
		yield return ("realm", Realm);
		yield return ("region", Region);
		yield return ("world", World);
	}
}

public enum Role
{
	Tank,
	Healer,
	Dps,
}
