using System.Net;
using System.Text;

namespace mplus_keybot.Tests;

public sealed class BlizzardProfileClientTests
{
	[Fact]
	public async Task ParsesProtectedWowProfileCharactersUsingRealmSlug()
	{
		var handler = new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent("""
{
  "wow_accounts": [
    {
      "characters": [
        {
          "character": {
            "id": 123,
            "name": "Keela",
            "realm": { "name": "Area 52", "slug": "area-52" },
            "playable_class": { "name": "Mage" }
          }
        }
      ]
    }
  ]
}
""", Encoding.UTF8, "application/json"),
		});
		var client = new BlizzardProfileClient(new HttpClient(handler));

		var characters = await client.GetProfileCharactersAsync("token", "us");

		var character = Assert.Single(characters);
		Assert.Equal(CharacterKey.From("us", "area-52", "Keela"), character.Key);
		Assert.Equal(123, character.BlizzardCharacterId);
		Assert.Equal("Area 52", character.RealmDisplayName);
		Assert.Equal("Mage", character.Class);
		Assert.Equal("Bearer", handler.Request!.Headers.Authorization!.Scheme);
		Assert.Equal("token", handler.Request.Headers.Authorization.Parameter);
		Assert.Equal("https://us.api.blizzard.com/profile/user/wow?namespace=profile-us&locale=en_US", handler.Request.RequestUri!.ToString());
	}

	private sealed class StubHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
	{
		public HttpRequestMessage? Request { get; private set; }

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Request = request;
			return Task.FromResult(response);
		}
	}
}
