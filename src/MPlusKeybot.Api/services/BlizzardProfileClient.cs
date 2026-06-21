using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

public interface IBlizzardProfileClient
{
	Task<IReadOnlyList<VerifiedCharacter>> GetProfileCharactersAsync(string accessToken, string region, CancellationToken cancellationToken = default);
}

public sealed class BlizzardProfileClient : IBlizzardProfileClient
{
	public BlizzardProfileClient(HttpClient client, IConfiguration configuration, ILogger<BlizzardProfileClient>? logger = null)
	{
		m_client = client ?? throw new ArgumentNullException(nameof(client));
		m_configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
		m_logger = logger;
	}

	public async Task<IReadOnlyList<VerifiedCharacter>> GetProfileCharactersAsync(string accessToken, string region, CancellationToken cancellationToken = default)
	{
		var normalizedRegion = region.Trim().ToLowerInvariant();
		var path = $"profile/user/wow?namespace=profile-{normalizedRegion}&locale=en_US";
		var configuredBaseUrl = m_configuration["Blizzard:ApiBaseUrl"];
		var requestUrl = string.IsNullOrWhiteSpace(configuredBaseUrl)
			? $"https://{normalizedRegion}.api.blizzard.com/{path}"
			: $"{configuredBaseUrl.TrimEnd('/')}/{path}";
		using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

		using var response = await m_client.SendAsync(request, cancellationToken).ConfigureAwait(false);
		response.EnsureSuccessStatusCode();

		var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
		using var document = JsonDocument.Parse(json);
		var profile = JsonSerializer.Deserialize<BlizzardWowProfileDto>(json, s_jsonOptions);
		var characters = profile?.WowAccounts?
			.SelectMany(account => account.Characters ?? [])
			.Select(character => ToVerifiedCharacter(character, normalizedRegion))
			.Where(character => character is not null)
			.Select(character => character!)
			.ToList() ?? [];

		if (characters.Count == 0)
			characters = FindCharacters(document.RootElement, normalizedRegion).ToList();

		if (characters.Count == 0)
			LogProfileShape(document.RootElement);

		return characters
			.GroupBy(character => character.Key)
			.Select(group => group.First())
			.OrderByDescending(character => character.Level)
			.ThenBy(character => character.RealmDisplayName ?? character.Realm)
			.ThenBy(character => character.Name)
			.ToList();
	}

	private static VerifiedCharacter? ToVerifiedCharacter(BlizzardAccountCharacterDto accountCharacter, string defaultRegion)
	{
		var character = accountCharacter.Character ?? accountCharacter;
		var realm = character.Realm;
		var realmSlug = realm?.Slug ?? realm?.Name;
		if (string.IsNullOrWhiteSpace(character.Name) || string.IsNullOrWhiteSpace(realmSlug))
			return null;

		return new VerifiedCharacter(
			character.Region?.Slug ?? defaultRegion,
			realmSlug,
			character.Name,
			character.Id,
			realm?.Name,
			character.Level,
			character.PlayableClass?.Name);
	}

	private static IEnumerable<VerifiedCharacter> FindCharacters(JsonElement element, string defaultRegion)
	{
		if (TryReadCharacter(element, defaultRegion, out var character))
			yield return character;

		switch (element.ValueKind)
		{
			case JsonValueKind.Object:
				foreach (var property in element.EnumerateObject())
				{
					foreach (var childCharacter in FindCharacters(property.Value, defaultRegion))
						yield return childCharacter;
				}
				break;
			case JsonValueKind.Array:
				foreach (var item in element.EnumerateArray())
				{
					foreach (var childCharacter in FindCharacters(item, defaultRegion))
						yield return childCharacter;
				}
				break;
		}
	}

	private static bool TryReadCharacter(JsonElement element, string defaultRegion, out VerifiedCharacter character)
	{
		character = null!;
		if (element.ValueKind != JsonValueKind.Object)
			return false;

		if (element.TryGetProperty("character", out var nestedCharacter) && TryReadCharacter(nestedCharacter, defaultRegion, out character))
			return true;

		if (!TryGetString(element, "name", out var name) || !element.TryGetProperty("realm", out var realm) || realm.ValueKind != JsonValueKind.Object)
			return false;

		if (!TryGetString(realm, "slug", out var realmSlug) && !TryGetString(realm, "name", out realmSlug))
			return false;

		long? characterId = null;
		if (element.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.Number && idElement.TryGetInt64(out var id))
			characterId = id;

		var region = defaultRegion;
		if (element.TryGetProperty("region", out var regionElement) && regionElement.ValueKind == JsonValueKind.Object && TryGetString(regionElement, "slug", out var regionSlug))
			region = regionSlug;

		TryGetString(realm, "name", out var realmDisplayName);

		int? level = null;
		if (element.TryGetProperty("level", out var levelElement) && levelElement.ValueKind == JsonValueKind.Number && levelElement.TryGetInt32(out var lvl))
			level = lvl;

		string? className = null;
		if (element.TryGetProperty("playable_class", out var classElement) && classElement.ValueKind == JsonValueKind.Object && TryGetString(classElement, "name", out var clsName))
			className = clsName;

		character = new VerifiedCharacter(region, realmSlug, name, characterId, realmDisplayName, level, className);
		return true;
	}

	private static bool TryGetString(JsonElement element, string propertyName, out string value)
	{
		value = string.Empty;
		if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
			return false;

		value = property.GetString() ?? string.Empty;
		return !string.IsNullOrWhiteSpace(value);
	}

	private void LogProfileShape(JsonElement root)
	{
		if (m_logger is null || !m_logger.IsEnabled(LogLevel.Warning))
			return;

		var topLevelProperties = root.ValueKind == JsonValueKind.Object
			? string.Join(", ", root.EnumerateObject().Select(x => x.Name))
			: root.ValueKind.ToString();
		var wowAccountCount = GetArrayLength(root, "wow_accounts");
		var accountCount = GetArrayLength(root, "accounts");
		var characterCount = CountPropertiesNamed(root, "characters");

		m_logger.LogWarning(
			"Battle.net profile response contained no parseable characters. Top-level properties: {TopLevelProperties}; wow_accounts count: {WowAccountCount}; accounts count: {AccountCount}; characters arrays found: {CharacterArraysFound}; first character shape: {FirstCharacterShape}",
			topLevelProperties,
			wowAccountCount,
			accountCount,
			characterCount,
			GetFirstCharacterShape(root));
	}

	private static int? GetArrayLength(JsonElement root, string propertyName)
	{
		if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
			return null;

		return property.GetArrayLength();
	}

	private static int CountPropertiesNamed(JsonElement element, string propertyName)
	{
		var count = 0;
		if (element.ValueKind == JsonValueKind.Object)
		{
			foreach (var property in element.EnumerateObject())
			{
				if (property.NameEquals(propertyName) && property.Value.ValueKind == JsonValueKind.Array)
					count++;
				count += CountPropertiesNamed(property.Value, propertyName);
			}
		}
		else if (element.ValueKind == JsonValueKind.Array)
		{
			foreach (var item in element.EnumerateArray())
				count += CountPropertiesNamed(item, propertyName);
		}

		return count;
	}

	private static string GetFirstCharacterShape(JsonElement element)
	{
		if (element.ValueKind == JsonValueKind.Object)
		{
			foreach (var property in element.EnumerateObject())
			{
				if (property.NameEquals("characters") && property.Value.ValueKind == JsonValueKind.Array && property.Value.GetArrayLength() > 0)
				{
					var firstCharacter = property.Value.EnumerateArray().First();
					return DescribeObject(firstCharacter);
				}

				var childShape = GetFirstCharacterShape(property.Value);
				if (!string.IsNullOrWhiteSpace(childShape))
					return childShape;
			}
		}
		else if (element.ValueKind == JsonValueKind.Array)
		{
			foreach (var item in element.EnumerateArray())
			{
				var childShape = GetFirstCharacterShape(item);
				if (!string.IsNullOrWhiteSpace(childShape))
					return childShape;
			}
		}

		return string.Empty;
	}

	private static string DescribeObject(JsonElement element)
	{
		if (element.ValueKind != JsonValueKind.Object)
			return element.ValueKind.ToString();

		var properties = string.Join(", ", element.EnumerateObject().Select(x => $"{x.Name}:{x.Value.ValueKind}"));
		if (element.TryGetProperty("character", out var nestedCharacter))
			return $"[{properties}] nested character [{DescribeObject(nestedCharacter)}]";

		return $"[{properties}]";
	}

	private static readonly JsonSerializerOptions s_jsonOptions = new() { PropertyNameCaseInsensitive = true };
	private readonly HttpClient m_client;
	private readonly IConfiguration m_configuration;
	private readonly ILogger<BlizzardProfileClient>? m_logger;
}

public sealed class BlizzardWowProfileDto
{
	[JsonPropertyName("wow_accounts")]
	public IReadOnlyList<BlizzardWowAccountDto>? WowAccounts { get; set; }
}

public sealed class BlizzardWowAccountDto
{
	[JsonPropertyName("characters")]
	public IReadOnlyList<BlizzardAccountCharacterDto>? Characters { get; set; }
}

public class BlizzardAccountCharacterDto
{
	[JsonPropertyName("character")]
	public BlizzardAccountCharacterDto? Character { get; set; }

	[JsonPropertyName("id")]
	public long? Id { get; set; }

	[JsonPropertyName("name")]
	public string? Name { get; set; }

	[JsonPropertyName("realm")]
	public BlizzardRealmDto? Realm { get; set; }

	[JsonPropertyName("region")]
	public BlizzardRegionDto? Region { get; set; }

	[JsonPropertyName("level")]
	public int? Level { get; set; }

	[JsonPropertyName("playable_class")]
	public BlizzardPlayableClassDto? PlayableClass { get; set; }
}

public sealed class BlizzardPlayableClassDto
{
	[JsonPropertyName("name")]
	public string? Name { get; set; }
}

public sealed class BlizzardRealmDto
{
	[JsonPropertyName("name")]
	public string? Name { get; set; }

	[JsonPropertyName("slug")]
	public string? Slug { get; set; }
}

public sealed class BlizzardRegionDto
{
	[JsonPropertyName("slug")]
	public string? Slug { get; set; }
}
