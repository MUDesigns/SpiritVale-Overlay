namespace SpiritVale.Overlay.Api;

public sealed record SkillCatalogEntry(
    string Id,
    string DisplayName,
    string? SpriteId,
    string? ClassName,
    int? ArchetypeId);

/// <summary>Resolves HUD icons from the shipped sprite pack (optional dump overlay).</summary>
public interface ISpriteCatalog
{
    /// <summary>Optional Il2CPP dump folder that overlays the shipped pack (dev / extras).</summary>
    string? SpriteDumpPath { get; }

    /// <summary>Resolve a catalog spriteId (e.g. Barbarian18) to an absolute PNG path.</summary>
    string? ResolveSpritePath(string? spriteId);

    /// <summary>Resolve an archetype id to a class icon PNG path.</summary>
    string? ResolveClassIconPath(int? archetypeId);

    string? ResolveClassName(int? archetypeId);

    /// <summary>Skill catalog: id → display name + spriteId.</summary>
    (string DisplayName, string? SpriteId) ResolveSkill(string? skillId);

    /// <summary>All known catalog skills (for pickers). Empty if the catalog failed to load.</summary>
    IReadOnlyList<SkillCatalogEntry> ListSkills();
}
