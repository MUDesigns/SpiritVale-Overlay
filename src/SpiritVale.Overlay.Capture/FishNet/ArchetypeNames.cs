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

    /// <summary>Preferred dump/shipped stems that exist in the sprite pack (first match wins).</summary>
    private static readonly Dictionary<int, string[]> PreferredIconStems = new()
    {
        [0] = ["class-warrior"],
        [1] = ["class-mage"],
        [2] = ["class-rogue"],
        [3] = ["class-knight"],
        [4] = ["class-summoner"],
        [5] = ["class-acolyte"],
        [6] = ["class-scout"],
        [10] = ["class-paladin-2", "class-paladin"],
        [11] = ["dragonknight", "class-dragon-knight", "class-dragonknight"],
        [12] = ["class-berserker-2", "class-berserker"],
        [13] = ["revenant", "class-revenant"],
        [14] = ["priest", "class-priest"],
        [15] = ["monk", "class-monk"],
        [16] = ["wizard", "class-wizard"],
        [17] = ["chronomancer", "class-chronomancer"],
        [18] = ["druid", "class-druid"],
        [19] = ["warlock", "class-warlock"],
        [20] = ["assassin", "class-assassin"],
        [21] = ["shinobi", "class-shinobi"],
        [22] = ["gunslinger", "class-gunslinger"],
        [23] = ["ranger", "class-ranger"],
        [24] = ["jester", "class-jester"],
        [25] = ["assassin", "class-nightshade"], // no dedicated nightshade icon in dump
        [26] = ["necromancer", "class-necromancer"],
        [27] = ["wizard", "class-spellblade"],
        [28] = ["dragonknight", "class-blade-master", "blademaster"],
        [29] = ["class-mechanist", "mechanist"],
        [30] = ["class-alchemist", "alchemist"],
        [31] = ["weaver", "class-weaver"],
    };

    public static string? GetName(int? archetypeId)
        => archetypeId is int id && Names.TryGetValue(id, out var n) ? n : null;

    /// <summary>Sprite dump stem candidates for a class icon (first match wins).</summary>
    public static IReadOnlyList<string> ClassIconStems(int? archetypeId)
    {
        if (archetypeId is not int id)
            return Array.Empty<string>();

        var list = new List<string>();
        if (PreferredIconStems.TryGetValue(id, out var preferred))
            list.AddRange(preferred);

        var name = GetName(id);
        if (name is not null)
        {
            var compact = name.Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant();
            var dashed = name.Replace(' ', '-').ToLowerInvariant();
            foreach (var stem in new[]
                     {
                         $"class-{dashed}",
                         $"class-{compact}",
                         $"{compact}-2",
                         $"class-{dashed}-2",
                         compact,
                         dashed,
                     })
            {
                if (!list.Contains(stem, StringComparer.OrdinalIgnoreCase))
                    list.Add(stem);
            }
        }

        return list;
    }
}
