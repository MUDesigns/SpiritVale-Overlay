using SpiritVale.Overlay.Api;
using SpiritVale.Overlay.Api.Combat;
using SpiritVale.Overlay.Api.Party;

namespace SpiritVale.Overlay.SamplePlugin;

public sealed class SampleHudPlugin : ISpiritValePlugin
{
    private ISpiritValeApi? _api;
    private bool _showDps = true;
    private bool _showParty = true;
    private readonly List<string> _log = new();

    public string Id => "sample.hud";
    public string Name => "Sample DPS + Party";
    public string Author => "MUDesigns";
    public string Version => "0.1.0";

    public void OnLoad(ISpiritValeApi api)
    {
        _api = api;
        api.Combat.Damage += OnDamage;
        api.Combat.Death += OnDeath;
        api.Party.InviteReceived += invite =>
            _log.Add($"Invite from {invite.InviterName}");
    }

    public void OnUnload()
    {
        if (_api is null) return;
        _api.Combat.Damage -= OnDamage;
        _api.Combat.Death -= OnDeath;
    }

    public void Draw(IOverlayUi ui)
    {
        if (_api is null) return;

        ui.SetNextWindowSize(360, 280);
        if (_showDps)
        {
            if (ui.BeginWindow("Sample DPS", ref _showDps))
            {
                var encounter = _api.Combat.CurrentEncounter;
                if (encounter is null)
                {
                    ui.Text("No encounter yet — fight something in SpiritVale.");
                }
                else
                {
                    ui.Text($"Duration: {encounter.Duration:mm\\:ss}");
                    ui.Text($"Total: {encounter.TotalDamage:N0}");
                    ui.Separator();
                    foreach (var row in encounter.Rows)
                        ui.TextUnformatted($"{row.DisplayName,-16} {row.TotalDamage,10:N0}  {row.Dps,8:N0} dps  {row.Share,6:P0}");
                }

                if (_log.Count > 0)
                {
                    ui.Separator();
                    ui.Text("Events:");
                    foreach (var line in _log.TakeLast(8))
                        ui.TextUnformatted(line);
                }
            }
            ui.EndWindow();
        }

        if (_showParty)
        {
            ui.SetNextWindowSize(280, 200);
            if (ui.BeginWindow("Sample Party", ref _showParty))
            {
                var members = _api.Party.Members;
                if (members.Count == 0)
                    ui.Text("Not in a party (or party packets not seen yet).");
                else
                {
                    foreach (var m in members)
                    {
                        var hp = m.MaxHealth > 0 ? m.Health / m.MaxHealth : 0;
                        ui.TextUnformatted($"{m.DisplayName}  HP {hp:P0}");
                    }
                }
            }
            ui.EndWindow();
        }
    }

    private void OnDamage(CombatDamageEvent e)
        => _log.Add($"DMG {e.SourceName} -> {e.TargetName}: {e.Amount}");

    private void OnDeath(CombatDeathEvent e)
        => _log.Add($"DEATH {e.ActorName}" + (e.KillerName is not null ? $" by {e.KillerName}" : ""));
}
