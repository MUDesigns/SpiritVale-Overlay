namespace SpiritVale.Overlay.Api;

/// <summary>Resolves SpiritVale sprite dump PNGs for HUD icons.</summary>
public interface ISpriteCatalog
{
    string? SpriteDumpPath { get; }

    /// <summary>Resolve a catalog spriteId (e.g. Barbarian18) to an absolute PNG path.</summary>
    string? ResolveSpritePath(string? spriteId);

    /// <summary>Resolve an archetype id to a class icon PNG path.</summary>
    string? ResolveClassIconPath(int? archetypeId);

    string? ResolveClassName(int? archetypeId);

    /// <summary>Skill catalog: id → display name + spriteId.</summary>
    (string DisplayName, string? SpriteId) ResolveSkill(string? skillId);
}
