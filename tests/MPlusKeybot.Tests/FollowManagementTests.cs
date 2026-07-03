using Microsoft.Extensions.Configuration;
using MPlusKeybot.Api;
using MPlusKeybot.Api.Database;
using MPlusKeybot.Api.Services;
using SQLite;

namespace MPlusKeybot.Tests;

public sealed class FollowManagementTests : IDisposable
{
	public FollowManagementTests()
	{
		m_databasePath = Path.Combine(Path.GetTempPath(), $"mplus-keybot-follow-tests-{Guid.NewGuid():N}.db");
		m_db = new SQLiteConnection(m_databasePath);
		m_db.CreateTable<Character>();
		m_db.CreateTable<FollowFlowState>();
		m_db.CreateTable<VerifiedCharacterSession>();
		m_repository = new CharacterRepository(m_db);
	}

	[Fact]
	public void CharacterManagementOnlyTogglesVerifiedCharacters()
	{
		m_db.Insert(new Character { Name = "Keela", Realm = "hyjal", Region = "us", IsFollowed = true });
		m_db.Insert(new Character { Name = "Other", Realm = "hyjal", Region = "us", IsFollowed = true });
		var service = new CharacterManagementService(m_repository);
		var verified = new[]
		{
			new VerifiedCharacter("us", "Hyjal", "Keela", 10, "Hyjal"),
			new VerifiedCharacter("us", "Area 52", "Newmage", 11, "Area 52"),
		};

		var result = service.UpdateFollowState("1234", verified, [CharacterKey.From("us", "Area 52", "Newmage")]);

		Assert.Equal([CharacterKey.From("us", "area-52", "Newmage")], result.Followed);
		Assert.Equal([CharacterKey.From("us", "hyjal", "Keela")], result.Unfollowed);
		Assert.False(m_db.Table<Character>().Single(x => x.Name == "Keela").IsFollowed);
		Assert.True(m_db.Table<Character>().Single(x => x.Name == "Newmage").IsFollowed);
		Assert.True(m_db.Table<Character>().Single(x => x.Name == "Other").IsFollowed);
	}

	[Fact]
	public void CharacterManagementRejectsUnverifiedSelections()
	{
		var service = new CharacterManagementService(m_repository);
		var verified = new[] { new VerifiedCharacter("us", "Hyjal", "Keela") };

		Assert.Throws<InvalidOperationException>(() => service.UpdateFollowState("1234", verified, [CharacterKey.From("us", "Hyjal", "NotMine")]));
	}

	[Fact]
	public void FollowFlowStateCanOnlyBeConsumedOnceBeforeExpiry()
	{
		var service = new FollowFlowStateService(m_db);
		var state = service.Create("1234", TimeSpan.FromMinutes(10));

		var consumed = service.Consume(state.State);
		var replayed = service.Consume(state.State);

		Assert.NotNull(consumed);
		Assert.Equal("1234", consumed.DiscordUserId);
		Assert.Null(replayed);
	}

	[Fact]
	public void ExpiredFollowFlowStateCannotBeConsumed()
	{
		var service = new FollowFlowStateService(m_db);
		var state = service.Create("1234", TimeSpan.FromMinutes(-1));

		Assert.Null(service.Consume(state.State));
	}

	[Fact]
	public void PublicUrlsPreserveConfiguredPathPrefix()
	{
		var config = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?>
			{
				["Web:PublicBaseUrl"] = "https://localhost:5142/mplus-keybot",
				["Web:PathBase"] = "/mplus-keybot",
			})
			.Build();
		var urls = new WebUrlBuilder(config);

		Assert.Equal("/mplus-keybot", urls.PathBase);
		Assert.Equal("/mplus-keybot", urls.CookiePath);
		Assert.Equal("https://localhost:5142/mplus-keybot/auth/blizzard/callback", urls.BuildPublicUrl("/auth/blizzard/callback"));
		Assert.Equal("https://localhost:5142/mplus-keybot/follow/start?state=abc", urls.BuildPublicUrl("/follow/start", ("state", "abc")));
	}

	public void Dispose()
	{
		m_db.Dispose();
		File.Delete(m_databasePath);
	}

	private readonly string m_databasePath;
	private readonly SQLiteConnection m_db;
	private readonly CharacterRepository m_repository;
}
