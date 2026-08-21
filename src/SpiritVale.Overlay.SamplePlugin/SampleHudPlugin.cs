using SpiritVale.Overlay.Api;
using SpiritVale.Overlay.Api.Combat;

namespace SpiritVale.Overlay.SamplePlugin;

public sealed class SampleHudPlugin : ISpiritValePlugin
{
    private ISpiritValeApi? _api;
    private bool _showDps = true;
    private bool _showParty;
    private bool _showSkills;
    private int? _skillsActorId;
    private string? _skillsPlayerName;
    private string _skillsTitle = "Skills";

    // Options
    private bool _hideEncounterHeading;
    private bool _showSelfOnly;
    private bool _hideFeed = true;
    private bool _hideRankNumbers;
    private bool _hidePlayerTotalDamage = true;
    private bool _hideCritRate;
    private bool _hideDeaths = true;
    private string _resetHotkey = "Ctrl+R";

    private readonly List<string> _feed = new();
    private const int FeedMax = 4;

    public string Id => "sample.hud";
    public string Name => "Sample DPS + Party";
    public string Author => "MUDesigns";
    public string Version => "0.6.3";

    public IReadOnlyList<PluginOptionDefinition> OptionDefinitions { get; } =
    [
        new("hideEncounterHeading", "Hide encounter heading", PluginOptionKind.Bool,
            "Hides the timer / total damage line at the top.", PluginOptionDefaults.Bool(false)),
        new("showSelfOnly", "Show only myself", PluginOptionKind.Bool,
            "Hides every other player on the meter.", PluginOptionDefaults.Bool(false)),
        new("hideFeed", "Hide hit feed", PluginOptionKind.Bool,
            "Hides the short recent-hit lines under the meter.", PluginOptionDefaults.Bool(true)),
        new("hideRankNumbers", "Hide rank numbers", PluginOptionKind.Bool,
            "Hides 1/2/3 ranking but still shows DPS and share.", PluginOptionDefaults.Bool(false)),
        new("hidePlayerTotalDamage", "Hide total damage per player", PluginOptionKind.Bool,
            "Hides each player's encounter total (DPS and % remain).", PluginOptionDefaults.Bool(true)),
        new("hideCritRate", "Hide crit rate", PluginOptionKind.Bool,
            "Hides per-player crit % on the meter.", PluginOptionDefaults.Bool(false)),
        new("hideDeaths", "Hide deaths", PluginOptionKind.Bool,
            "Hides death counts on the meter.", PluginOptionDefaults.Bool(true)),
        new("resetEncounterHotkey", "Reset encounter hotkey", PluginOptionKind.Hotkey,
            "Chord that clears the current encounter (e.g. Ctrl+R).", "Ctrl+R"),
    ];

    public void OnLoad(ISpiritValeApi api)
    {
        _api = api;
        api.Combat.Damage += OnDamage;
    }

    public void OnUnload()
    {
        if (_api is not null)
            _api.Combat.Damage -= OnDamage;
        _api = null;
    }

    public void ApplyOptions(IReadOnlyDictionary<string, string> values)
    {
        _hideEncounterHeading = ReadBool(values, "hideEncounterHeading", false);
        _showSelfOnly = ReadBool(values, "showSelfOnly", false);
        _hideFeed = ReadBool(values, "hideFeed", true);
        _hideRankNumbers = ReadBool(values, "hideRankNumbers", false);
        _hidePlayerTotalDamage = ReadBool(values, "hidePlayerTotalDamage", true);
        _hideCritRate = ReadBool(values, "hideCritRate", false);
        _hideDeaths = ReadBool(values, "hideDeaths", true);
        if (values.TryGetValue("resetEncounterHotkey", out var hk) && !string.IsNullOrWhiteSpace(hk))
            _resetHotkey = hk.Trim();
    }

    public IReadOnlyDictionary<string, string> ExportOptions() => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["hideEncounterHeading"] = PluginOptionDefaults.Bool(_hideEncounterHeading),
        ["showSelfOnly"] = PluginOptionDefaults.Bool(_showSelfOnly),
        ["hideFeed"] = PluginOptionDefaults.Bool(_hideFeed),
        ["hideRankNumbers"] = PluginOptionDefaults.Bool(_hideRankNumbers),
        ["hidePlayerTotalDamage"] = PluginOptionDefaults.Bool(_hidePlayerTotalDamage),
        ["hideCritRate"] = PluginOptionDefaults.Bool(_hideCritRate),
        ["hideDeaths"] = PluginOptionDefaults.Bool(_hideDeaths),
        ["resetEncounterHotkey"] = _resetHotkey,
    };

    public void OnOptionHotkey(string key)
    {
        if (key == "resetEncounterHotkey")
            _api?.Combat.ResetEncounter();
    }

    public void Draw(IOverlayUi ui)
    {
        if (_api is null) return;

        if (_showDps)
        {
            ui.SetNextWindowSize(280, 180);
            if (ui.BeginWindow("DPS##meter", ref _showDps))
            {
                var encounter = _api.Combat.CurrentEncounter;
                var board = _api.Combat.Leaderboard;
                var me = _api.Character.Local?.DisplayName;
                if (string.IsNullOrWhiteSpace(me) || me is "You" or "Local Player")
                    me = null;

                if (!_hideEncounterHeading)
                {
                    if (encounter is not null)
                    {
                        ui.TextColored(0.77f, 0.66f, 1f, 1f,
                            $"{encounter.Duration:mm\\:ss}  {FormatDamage(encounter.TotalDamage)}");
                        ui.SameLine(6f);
                        if (ui.SmallButton("Reset"))
                            _api.Combat.ResetEncounter();
                    }
                    else
                        ui.TextColored(0.55f, 0.58f, 0.65f, 1f, "— idle —");
                }

                var rank = 1;
                var localId = _api.Character.Local?.ActorId;
                var any = false;
                foreach (var row in board.Take(10))
                {
                    var mine = localId is int lid && lid == row.ActorId
                        || (me is not null && string.Equals(row.DisplayName, me, StringComparison.OrdinalIgnoreCase));
                    if (_showSelfOnly && !mine) continue;
                    DrawMeterRow(ui, rank, row, mine);
                    rank++;
                    any = true;
                }

                if (!any)
                    ui.TextColored(0.45f, 0.48f, 0.55f, 1f, "no hits yet");

                if (!_hideFeed)
                {
                    string[] feedLines;
                    lock (_feed) feedLines = _feed.ToArray();
                    if (feedLines.Length > 0)
                    {
                        ui.Spacing();
                        foreach (var line in feedLines)
                            ui.TextColored(0.55f, 0.58f, 0.65f, 1f, line);
                    }
                }
            }
            ui.EndWindow();
        }

        if (_showSkills)
        {
            // After death/respawn the FishNet object id changes — rebind via sticky name.
            var board = _api.Combat.Leaderboard;
            if (!string.IsNullOrWhiteSpace(_skillsPlayerName))
            {
                var match = board.FirstOrDefault(r =>
                    string.Equals(r.DisplayName, _skillsPlayerName, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                    _skillsActorId = match.ActorId;
            }
            DrawSkillWindow(ui, _skillsActorId ?? 0, _skillsPlayerName);
        }

        if (_showParty)
        {
            ui.SetNextWindowSize(200, 120);
            if (ui.BeginWindow("PARTY", ref _showParty))
            {
                var members = _api.Party.Members;
                if (members.Count == 0)
                {
                    ui.TextColored(0.55f, 0.58f, 0.65f, 1f, "no party");
                }
                else
                {
                    foreach (var m in members)
                    {
                        var tag = m.IsLeader ? "*" : "";
                        ui.TextUnformatted($"{Truncate(m.DisplayName, 14)}{tag}");
                        var hp = m.MaxHealth > 0 ? m.Health / m.MaxHealth : 0f;
                        ui.ProgressBar(hp, 0.22f, 0.78f, 0.47f, 4f);
                    }
                }
            }
            ui.EndWindow();
        }
    }

    private void DrawMeterRow(IOverlayUi ui, int rank, DpsRow row, bool mine)
    {
        var name = row.DisplayName;
        if (string.IsNullOrWhiteSpace(name) || name.StartsWith("Actor ", StringComparison.Ordinal))
            name = mine ? "You" : "?";
        name = Truncate(name, 10);

        // Icon slot is always reserved (Dummy when missing) so rows stay one-per-line.
        ui.ClassIcon(row.ArchetypeId, 14f);
        ui.SameLine(4f);

        var prefix = new List<string>();
        if (!_hideRankNumbers)
            prefix.Add(rank.ToString());
        if (row.Level is int lv && lv > 0)
            prefix.Add($"Lv{lv}");
        if (prefix.Count > 0)
        {
            ui.TextUnformatted(string.Join(' ', prefix));
            ui.SameLine(4f);
        }

        // Color only your name — ranking is by damage, not forced first place.
        if (mine)
            ui.TextColored(1f, 0.82f, 0.35f, 1f, name);
        else
            ui.TextUnformatted(name);

        var stats = new List<string>();
        if (!_hidePlayerTotalDamage)
            stats.Add(FormatDamage(row.TotalDamage));
        stats.Add($"{FormatDamage((long)row.Dps)}/s");
        stats.Add($"{Math.Clamp(row.Share, 0, 1) * 100:0}%");
        if (!_hideCritRate && row.Hits > 0)
            stats.Add($"{row.CritRate * 100:0}% crit");
        if (!_hideDeaths && row.Deaths > 0)
            stats.Add($"{row.Deaths}d");

        ui.SameLine(4f);
        ui.TextUnformatted(string.Join(' ', stats));

        ui.SameLine(4f);
        if (ui.SmallButton($"sk##{row.ActorId}"))
        {
            _skillsActorId = row.ActorId;
            _skillsPlayerName = row.DisplayName.StartsWith("Actor ", StringComparison.Ordinal)
                ? null
                : row.DisplayName;
            _skillsTitle = $"{name} skills";
            _showSkills = true;
        }

        var share = (float)Math.Clamp(row.Share, 0, 1);
        if (mine)
            ui.ProgressBar(share, 1f, 0.82f, 0.35f, 3f);
        else
            ui.ProgressBar(share, 0.35f, 0.55f, 0.85f, 3f);
    }

    private void DrawSkillWindow(IOverlayUi ui, int actorId, string? playerName)
    {
        ui.SetNextWindowSize(260, 220);
        if (!ui.BeginWindow($"{_skillsTitle}##skills", ref _showSkills))
        {
            ui.EndWindow();
            return;
        }

        var skills = _api!.Combat.GetSkillBreakdown(actorId, playerName);
        if (skills.Count == 0)
        {
            ui.TextColored(0.55f, 0.58f, 0.65f, 1f, "no skill data yet");
            ui.EndWindow();
            return;
        }

        foreach (var s in skills.Take(16))
        {
            ui.Sprite(s.SpriteId, 16f);
            ui.SameLine(4f);
            var pct = $"{Math.Clamp(s.Share, 0, 1) * 100:0}%";
            var crit = s.Hits > 0 ? $"  {s.CritRate * 100:0}% crit" : "";
            ui.TextUnformatted(
                $"{Truncate(s.DisplayName, 14)}  {FormatDamage(s.TotalDamage)}  {FormatDamage((long)s.Dps)}/s  {pct}{crit}");
            ui.ProgressBar((float)Math.Clamp(s.Share, 0, 1), 0.55f, 0.45f, 0.85f, 3f);
        }

        ui.EndWindow();
    }

    private void OnDamage(CombatDamageEvent evt)
    {
        var who = string.IsNullOrWhiteSpace(evt.SourceName) || evt.SourceName.StartsWith("Actor ", StringComparison.Ordinal)
            ? "?"
            : Truncate(evt.SourceName, 8);
        var skill = string.IsNullOrWhiteSpace(evt.SkillLabel) ? "" : Truncate(evt.SkillLabel!, 8);
        var line = string.IsNullOrEmpty(skill)
            ? $"{who} {FormatDamage(evt.Amount)}"
            : $"{who} {skill} {FormatDamage(evt.Amount)}";
        lock (_feed)
        {
            _feed.Insert(0, line);
            while (_feed.Count > FeedMax) _feed.RemoveAt(_feed.Count - 1);
        }
    }

    private static bool ReadBool(IReadOnlyDictionary<string, string> values, string key, bool fallback)
        => values.TryGetValue(key, out var v) ? PluginOptionDefaults.ParseBool(v, fallback) : fallback;

    private static string Truncate(string text, int max)
        => text.Length <= max ? text : text[..(max - 1)] + "…";

    private static string FormatDamage(long value)
    {
        if (value >= 1_000_000) return $"{value / 1_000_000.0:0.#}M";
        if (value >= 1000) return $"{value / 1000.0:0.#}k";
        return value.ToString();
    }
}
