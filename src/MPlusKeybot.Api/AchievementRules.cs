public static class AchievementRules
{
	public const int MinimumPersonalBestAnnouncementLevel = 10;

	private static readonly IReadOnlyList<ScoreMilestone> s_scoreMilestones =
	[
		new(2000, "Keystone Master"),
		new(2500, "Keystone Hero"),
		new(3000, "Keystone Legend"),
		new(3400, "Keystone Myth"),
	];

	private static readonly IReadOnlyList<int> s_rankBands = [1000, 500, 250, 100, 50, 25, 10, 5, 1];

	public static int GetHighestScoreMilestone(double score) => s_scoreMilestones
		.Where(x => score >= x.Score)
		.Select(x => x.Score)
		.DefaultIfEmpty(0)
		.Max();

	public static ScoreMilestone? GetHighestNewScoreMilestone(double score, int previousMilestone) => s_scoreMilestones
		.Where(x => score >= x.Score && x.Score > previousMilestone)
		.OrderByDescending(x => x.Score)
		.FirstOrDefault();

	public static int? GetRankBand(int rank)
	{
		if (rank <= 0)
			return null;

		return s_rankBands
			.Where(band => rank <= band)
			.OrderBy(band => band)
			.Cast<int?>()
			.FirstOrDefault();
	}

	public static IEnumerable<(string Category, string Label, string Lane, int Rank)> GetSupportedRanks(CharacterDto profile)
	{
		if (profile.Mythic_Plus_Ranks is null || profile.Class is null)
			yield break;

		foreach (var ((characterClass, spec), category) in s_specRankCategories)
		{
			if (!string.Equals(characterClass, profile.Class, StringComparison.OrdinalIgnoreCase))
				continue;

			if (!profile.Mythic_Plus_Ranks.TryGetValue(category, out var ranks))
				continue;

			var label = $"{spec} {characterClass}";
			foreach (var (lane, rank) in ranks.GetLaneRanks())
				yield return (category, label, lane, rank);
		}
	}

	public static string FormatRankCategory(string category, string? label = null) => label ?? category;

	public static string FormatLane(string lane) => lane switch
	{
		"realm" => "Realm",
		"region" => "Regional",
		"world" => "World",
		_ => lane,
	};

	private static readonly IReadOnlyDictionary<(string Class, string Spec), string> s_specRankCategories = new Dictionary<(string Class, string Spec), string>
	{
		[("Death Knight", "Blood")] = "spec_250",
		[("Death Knight", "Frost")] = "spec_251",
		[("Death Knight", "Unholy")] = "spec_252",
		[("Demon Hunter", "Havoc")] = "spec_577",
		[("Demon Hunter", "Vengeance")] = "spec_581",
		[("Druid", "Balance")] = "spec_102",
		[("Druid", "Feral")] = "spec_103",
		[("Druid", "Guardian")] = "spec_104",
		[("Druid", "Restoration")] = "spec_105",
		[("Evoker", "Devastation")] = "spec_1467",
		[("Evoker", "Preservation")] = "spec_1468",
		[("Evoker", "Augmentation")] = "spec_1473",
		[("Hunter", "Beast Mastery")] = "spec_253",
		[("Hunter", "Marksmanship")] = "spec_254",
		[("Hunter", "Survival")] = "spec_255",
		[("Mage", "Arcane")] = "spec_62",
		[("Mage", "Fire")] = "spec_63",
		[("Mage", "Frost")] = "spec_64",
		[("Monk", "Brewmaster")] = "spec_268",
		[("Monk", "Mistweaver")] = "spec_270",
		[("Monk", "Windwalker")] = "spec_269",
		[("Paladin", "Holy")] = "spec_65",
		[("Paladin", "Protection")] = "spec_66",
		[("Paladin", "Retribution")] = "spec_70",
		[("Priest", "Discipline")] = "spec_256",
		[("Priest", "Holy")] = "spec_257",
		[("Priest", "Shadow")] = "spec_258",
		[("Rogue", "Assassination")] = "spec_259",
		[("Rogue", "Outlaw")] = "spec_260",
		[("Rogue", "Subtlety")] = "spec_261",
		[("Shaman", "Elemental")] = "spec_262",
		[("Shaman", "Enhancement")] = "spec_263",
		[("Shaman", "Restoration")] = "spec_264",
		[("Warlock", "Affliction")] = "spec_265",
		[("Warlock", "Demonology")] = "spec_266",
		[("Warlock", "Destruction")] = "spec_267",
		[("Warrior", "Arms")] = "spec_71",
		[("Warrior", "Fury")] = "spec_72",
		[("Warrior", "Protection")] = "spec_73",
	};
}

public sealed record ScoreMilestone(int Score, string Name);
