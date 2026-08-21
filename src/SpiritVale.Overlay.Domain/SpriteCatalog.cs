using System.Text.Json;
using SpiritVale.Overlay.Api;
using SpiritVale.Overlay.Capture.FishNet;

namespace SpiritVale.Overlay.Domain;

public sealed class SpriteCatalog : ISpriteCatalog
{
    private readonly Dictionary<string, string> _stemToPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SkillInfo> _skills = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<SkillCatalogEntry> _skillList = new();
    private string? _shippedPath;
    private string? _dumpPath;

    private readonly record struct SkillInfo(string DisplayName, string? SpriteId, int? ArchetypeId, string? ClassName);

    public string? SpriteDumpPath => _dumpPath;
    public string? ShippedSpritesPath => _shippedPath;

    public SpriteCatalog(string? dumpPath = null, string? skillCatalogJsonPath = null, string? shippedSpritesPath = null)
    {
        SetShippedSpritesPath(shippedSpritesPath ?? DefaultShippedSpritesPath());
        SetDumpPath(dumpPath);
        LoadSkillCatalog(skillCatalogJsonPath);
    }

    public static string DefaultShippedSpritesPath()
        => Path.Combine(AppContext.BaseDirectory, "Assets", "Sprites");

    /// <summary>Bundled PNGs shipped next to the host (Assets/Sprites/{stem}.png).</summary>
    public void SetShippedSpritesPath(string? path)
    {
        _shippedPath = string.IsNullOrWhiteSpace(path) ? null : path.Trim();
        RebuildIndex();
    }

    /// <summary>Optional Il2CPP dump overlay — merges on top of shipped sprites for local/dev use.</summary>
    public void SetDumpPath(string? dumpPath)
    {
        _dumpPath = string.IsNullOrWhiteSpace(dumpPath) ? null : dumpPath.Trim();
        RebuildIndex();
    }

    public string? ResolveSpritePath(string? spriteId)
    {
        if (string.IsNullOrWhiteSpace(spriteId)) return null;
        return _stemToPath.TryGetValue(spriteId.Trim(), out var path) ? path : null;
    }

    public string? ResolveClassIconPath(int? archetypeId)
    {
        foreach (var stem in ArchetypeNames.ClassIconStems(archetypeId))
        {
            if (_stemToPath.TryGetValue(stem, out var path))
                return path;
        }
        return null;
    }

    public string? ResolveClassName(int? archetypeId) => ArchetypeNames.GetName(archetypeId);

    public (string DisplayName, string? SpriteId) ResolveSkill(string? skillId)
    {
        if (string.IsNullOrWhiteSpace(skillId))
            return ("Unknown", null);
        if (_skills.TryGetValue(skillId, out var s))
            return (s.DisplayName, s.SpriteId);
        return (skillId, null);
    }

    public IReadOnlyList<SkillCatalogEntry> ListSkills() => _skillList;

    /// <summary>
    /// Infer archetype from damage-weighted skills. Prefers known VisualData class when provided.
    /// </summary>
    public int? InferArchetype(IEnumerable<(string SkillId, long Damage)> skills, int? knownArchetype = null)
    {
        if (knownArchetype is not null) return knownArchetype;

        var votes = new Dictionary<int, long>();
        foreach (var (skillId, damage) in skills)
        {
            if (string.IsNullOrWhiteSpace(skillId) || damage <= 0) continue;
            if (!_skills.TryGetValue(skillId, out var info) || info.ArchetypeId is not int arch)
                continue;
            // Skip universal / non-class noise.
            if (skillId.Equals("AutoAttack", StringComparison.OrdinalIgnoreCase)
                || skillId.Equals("Mount", StringComparison.OrdinalIgnoreCase)
                || skillId.StartsWith("NPC_", StringComparison.OrdinalIgnoreCase))
                continue;
            votes[arch] = votes.GetValueOrDefault(arch) + damage;
        }

        if (votes.Count == 0) return null;

        // Prefer the most specific (highest id among ties that share a base line) by damage first.
        return votes.OrderByDescending(kv => kv.Value)
            .ThenByDescending(kv => kv.Key) // advanced jobs tend to be higher ids
            .First().Key;
    }

    private void RebuildIndex()
    {
        _stemToPath.Clear();
        IndexDirectory(_shippedPath, stripSharedAssetsSuffix: false);
        // Dump files use {stem}-sharedassets….png; shipped pack uses {stem}.png.
        IndexDirectory(_dumpPath, stripSharedAssetsSuffix: true);
    }

    private void IndexDirectory(string? directory, bool stripSharedAssetsSuffix)
    {
        if (directory is null || !Directory.Exists(directory)) return;

        foreach (var file in Directory.EnumerateFiles(directory, "*.png", SearchOption.TopDirectoryOnly))
        {
            var stem = Path.GetFileNameWithoutExtension(file);
            if (stripSharedAssetsSuffix)
            {
                var cut = stem.IndexOf("-sharedassets", StringComparison.OrdinalIgnoreCase);
                if (cut > 0) stem = stem[..cut];
            }

            // Later sources (dump) override earlier (shipped).
            _stemToPath[stem] = file;
        }
    }

    private void LoadSkillCatalog(string? path)
    {
        try
        {
            Stream? stream = null;
            if (path is not null && File.Exists(path))
                stream = File.OpenRead(path);
            else
                stream = typeof(SpriteCatalog).Assembly.GetManifestResourceStream(
                    "SpiritVale.Overlay.Domain.Resources.skill-catalog.json");

            if (stream is null) return;
            using (stream)
            {
                using var doc = JsonDocument.Parse(stream);
                if (!doc.RootElement.TryGetProperty("skills", out var skills)) return;
                foreach (var skill in skills.EnumerateArray())
                {
                    var id = skill.GetProperty("id").GetString();
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    var name = skill.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? id : id;
                    string? sprite = skill.TryGetProperty("spriteId", out var sp) && sp.ValueKind != JsonValueKind.Null
                        ? sp.GetString()
                        : null;
                    int? arch = null;
                    if (skill.TryGetProperty("archetypeId", out var ai) && ai.ValueKind == JsonValueKind.Number)
                        arch = ai.GetInt32();
                    string? className = skill.TryGetProperty("className", out var cn) && cn.ValueKind == JsonValueKind.String
                        ? cn.GetString()
                        : ArchetypeNames.GetName(arch);
                    var info = new SkillInfo(name, sprite, arch, className);
                    _skills[id] = info;
                    _skillList.Add(new SkillCatalogEntry(id, name, sprite, className, arch));
                }
            }
        }
        catch
        {
            // Catalog optional at runtime.
        }
    }
}
