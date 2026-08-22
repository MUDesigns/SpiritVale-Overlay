using SpiritVale.Overlay.Api.Character;
using SpiritVale.Overlay.Api.Combat;
using SpiritVale.Overlay.Api.Party;
using SpiritVale.Overlay.Api.Protocol;
using SpiritVale.Overlay.Api.World;

namespace SpiritVale.Overlay.Api;

/// <summary>Host services exposed to plugins.</summary>
public interface ISpiritValeApi
{
    ICombatApi Combat { get; }
    IPartyApi Party { get; }
    ICharacterApi Character { get; }
    IWorldApi World { get; }
    IProtocolApi Protocol { get; }
    ISpriteCatalog Sprites { get; }

    /// <summary>Raised when capture starts or stops.</summary>
    event Action<bool>? CaptureStateChanged;

    bool IsCapturing { get; }
    string? CaptureStatus { get; }

    /// <summary>
    /// True when SpiritVale is running and either the game or this overlay owns focus.
    /// False when alt-tabbed to another app (plugin HUDs should hide). Overlay focus alone
    /// still counts so plugin config clicks do not tear down the HUD.
    /// </summary>
    bool IsGameFocused { get; }
}
