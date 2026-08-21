namespace SpiritVale.Overlay.Capture.FishNet;

public static class ArchetypeNames
{
    private static readonly Dictionary<int, string> Names = new()
    {
        [-1] = "Novice",
        [0] = "Warrior",
        [1] = "Mage",
        [2] = "Rogue",
        [3] = "Knight",
        [4] = "Summoner",
        [5] = "Acolyte",
        [6] = "Scout",
        [10] = "Paladin",
        [11] = "Dragon Knight",
        [12] = "Berserker",
        [13] = "Revenant",
        [14] = "Priest",
        [15] = "Monk",
        [16] = "Wizard",
        [17] = "Chronomancer",
        [18] = "Druid",
        [19] = "Warlock",
        [20] = "Assassin",
        [21] = "Shinobi",
        [22] = "Gunslinger",
        [23] = "Ranger",
        [24] = "Jester",
        [25] = "Nightshade",
        [26] = "Necromancer",
        [27] = "Spellblade",
        [28] = "Blade Master",
        [29] = "Mechanist",
        [30] = "Alchemist",
        [31] = "Weaver",
    };

    public static string? GetName(int? archetypeId)
        => archetypeId is int id && Names.TryGetValue(id, out var n) ? n : null;

    /// <summary>Sprite dump stem candidates for a class icon (first match wins).</summary>
    public static IReadOnlyList<string> ClassIconStems(int? archetypeId)
    {
        var name = GetName(archetypeId);
        if (name is null) return Array.Empty<string>();
        var compact = name.Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant();
        var dashed = name.Replace(' ', '-').ToLowerInvariant();
        return new[]
        {
            $"class-{dashed}",
            $"class-{compact}",
            $"{compact}-2",
            $"class-{dashed}-2",
            compact,
            dashed,
        };
    }
}
