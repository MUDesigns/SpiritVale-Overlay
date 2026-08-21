namespace SpiritVale.Overlay.Capture.FishNet;

/// <summary>
/// Prefab component layouts from spirit-vale-tools capture (collection 0).
/// RPC-link fingerprints often miss HealthComponent; these fill the gap.
/// </summary>
internal static class PrefabLayouts
{
    private static readonly (int Index, string Type)[] Player =
    [
        (0, "PlayerController"),
        (1, "MoveComponent"),
        (2, "HealthComponent"),
        (3, "CombatComponent"),
        (4, "SkillsComponent"),
        (5, "StatusComponent"),
        (6, "SummoningComponent"),
        (7, "PlayerSave"),
        (8, "FishNet.Component.Transforming.NetworkTransform"),
    ];

    private static readonly (int Index, string Type)[] Monster =
    [
        (0, "MonsterController"),
        (1, "FishNet.Component.Transforming.NetworkTransform"),
        (2, "MoveComponent"),
        (3, "HealthComponent"),
        (4, "CombatComponent"),
        (5, "SkillsComponent"),
        (6, "StatusComponent"),
        (7, "SummoningComponent"),
    ];

    public static List<(string Key, string TypeName)> Bind(int collectionId, int prefabId, int objectId)
    {
        var layout = LayoutForPrefab(collectionId, prefabId);
        var result = new List<(string, string)>();
        if (layout is null) return result;
        foreach (var (index, type) in layout)
            result.Add(($"{objectId}:{index}", type));
        return result;
    }

    /// <summary>
    /// When one behaviour on an object is already typed, fill Health/Status/etc. from the
    /// matching prefab (Player vs Monster).
    /// </summary>
    public static string? RecoverType(
        Dictionary<string, string> components,
        int objectId,
        int componentIndex)
    {
        var known = new Dictionary<int, string>();
        var prefix = objectId + ":";
        foreach (var (key, typeName) in components)
        {
            if (!key.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (!int.TryParse(key.AsSpan(prefix.Length), out var index)) continue;
            known[index] = typeName;
        }

        var layout = LayoutFromKnown(known);
        if (layout is null) return null;
        foreach (var (index, type) in layout)
        {
            if (index == componentIndex) return type;
        }
        return null;
    }

    public static void FillSiblings(Dictionary<string, string> components, int objectId)
    {
        var known = new Dictionary<int, string>();
        var prefix = objectId + ":";
        foreach (var (key, typeName) in components)
        {
            if (!key.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (!int.TryParse(key.AsSpan(prefix.Length), out var index)) continue;
            known[index] = typeName;
        }
        var layout = LayoutFromKnown(known);
        if (layout is null) return;
        foreach (var (index, type) in layout)
            components.TryAdd($"{objectId}:{index}", type);
    }

    private static (int Index, string Type)[]? LayoutForPrefab(int collectionId, int prefabId)
    {
        if (collectionId != 0) return null;
        return prefabId switch
        {
            1 or 4 => Player,
            5 => Monster,
            _ => null,
        };
    }

    private static (int Index, string Type)[]? LayoutFromKnown(Dictionary<int, string> known)
    {
        foreach (var (index, type) in known)
        {
            if (type is "PlayerController" or "PlayerSave") return Player;
            if (type is "MonsterController") return Monster;
            if (type == "SkillsComponent" && index == 4) return Player;
            if (type == "SkillsComponent" && index == 5) return Monster;
            if (type == "HealthComponent" && index == 2) return Player;
            if (type == "HealthComponent" && index == 3) return Monster;
            if (type == "StatusComponent" && index == 5) return Player;
            if (type == "StatusComponent" && index == 6) return Monster;
        }
        return null;
    }
}
