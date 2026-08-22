using SpiritVale.Overlay.Api;
using SpiritVale.Overlay.Api.Character;
using SpiritVale.Overlay.Api.Combat;
using SpiritVale.Overlay.Api.Party;
using SpiritVale.Overlay.Api.Protocol;
using SpiritVale.Overlay.Api.World;
using SpiritVale.Overlay.Capture;
using SpiritVale.Overlay.Capture.FishNet;

namespace SpiritVale.Overlay.Domain;

public sealed class SpiritValeApi : ISpiritValeApi, IProtocolApi, IDisposable
{
    private readonly PacketCaptureService _capture;
    private readonly ActorDirectory _actors = new();
    private readonly SpriteCatalog _sprites;
    private readonly CombatTracker _combat;
    private readonly PartyTracker _party;
    private readonly CharacterTracker _character;
    private readonly WorldTracker _world;
    private readonly SynchronizationContext? _sync;

    public SpiritValeApi(PacketCaptureService capture, SynchronizationContext? sync = null, string? spriteDumpPath = null)
    {
        _capture = capture;
        _sync = sync;
        _sprites = new SpriteCatalog(spriteDumpPath);
        _combat = new CombatTracker(_actors, _sprites);
        _party = new PartyTracker(_actors);
        _character = new CharacterTracker(_actors);
        _world = new WorldTracker(_actors);
        _capture.FishNetPacket += OnFishNet;
        _capture.UdpPacket += OnUdp;
        _capture.StatusChanged += s =>
        {
            CaptureStatus = s;
            Post(() => CaptureStateChanged?.Invoke(_capture.IsRunning));
        };
        _capture.Warning += w =>
        {
            CaptureStatus = w;
            Post(() => Warning?.Invoke(w));
        };
    }

    public ICombatApi Combat => _combat;
    public IPartyApi Party => _party;
    public ICharacterApi Character => _character;
    public IWorldApi World => _world;
    public IProtocolApi Protocol => this;
    public ISpriteCatalog Sprites => _sprites;
    public CaptureStats CaptureStats => _capture.Stats;

    public void ConfigureSpriteDump(string? path) => _sprites.SetDumpPath(path);

    public event Action<bool>? CaptureStateChanged;
    public event Action<DecodedFishNetEvent>? Packet;
    public event Action<RawPacketEvent>? RawPacket;
    public event Action<string>? UdpSeen;
    public event Action<string>? Warning;

    public bool IsCapturing => _capture.IsRunning;
    public string? CaptureStatus { get; private set; }
    public bool IncludeRawBytes { get; set; }

    /// <inheritdoc cref="ISpiritValeApi.IsGameFocused" />
    public bool IsGameFocused { get; set; }

    public void StartCapture(int? deviceIndex = null, string? preferredDevice = null)
    {
        _capture.Start(deviceIndex, preferredDevice);
        Post(() => CaptureStateChanged?.Invoke(true));
    }

    public bool TryRecoverCaptureIfSilent(TimeSpan silence)
        => _capture.TryRecoverIfSilent(silence);

    public string? ActiveCaptureDevice => _capture.ActiveDeviceKey;

    public void SeedLocalPlayerName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        _actors.SetLocalIdentity(_actors.LocalActorId, name.Trim());
        if (_character.Local is null || string.IsNullOrWhiteSpace(_character.Local.DisplayName)
            || _character.Local.DisplayName is "You" or "Local Player")
        {
            _character.SetLocal(new CharacterSnapshot(
                name.Trim(),
                _character.Local?.CharacterId,
                _actors.LocalActorId ?? _character.Local?.ActorId,
                _character.Local?.Level,
                _character.Local?.ClassName,
                _character.Local?.Stats ?? new Dictionary<string, double>(),
                _character.Local?.Equipped ?? Array.Empty<SpiritVale.Overlay.Api.Character.InventoryItem>(),
                _character.Local?.Bag ?? Array.Empty<SpiritVale.Overlay.Api.Character.InventoryItem>()));
        }
    }

    public string? LocalPlayerName => _actors.LocalDisplayName;

    public void StopCapture()
    {
        _capture.Stop();
    }

    public void Dispose()
    {
        _capture.FishNetPacket -= OnFishNet;
        _capture.UdpPacket -= OnUdp;
        _capture.Dispose();
    }

    private void OnUdp(CapturedUdpPacket udp)
    {
        var summary = $"{udp.Direction} {udp.SourceAddress}:{udp.SourcePort} → {udp.DestinationAddress}:{udp.DestinationPort} ({udp.Payload.Length}b)";
        Post(() => UdpSeen?.Invoke(summary));

        if (!IncludeRawBytes) return;
        var evt = new RawPacketEvent(udp.Timestamp, udp.Direction, udp.Payload);
        Post(() => RawPacket?.Invoke(evt));
    }

    private void OnFishNet(DecodedFishNetPacket packet, CapturedUdpPacket _)
    {
        PayloadHeuristics.EnrichFields(packet);
        _actors.ConsumeIdentityPacket(packet);
        // Names/world first so combat can resolve display names on the same packet.
        _character.Consume(packet);
        SyncCharacterFromActors();
        _world.Consume(packet);
        _party.Consume(packet);
        _combat.Consume(packet);

        var evt = new DecodedFishNetEvent(
            (int)packet.Tick,
            packet.PacketName,
            packet.PacketId,
            packet.ObjectId,
            packet.RpcName,
            packet.NetworkBehaviourType,
            packet.RpcHash,
            packet.BroadcastName,
            packet.SyncName,
            packet.Fields.ToDictionary(kv => kv.Key, kv => kv.Value));

        Post(() => Packet?.Invoke(evt));
    }

    private void SyncCharacterFromActors()
    {
        var name = _actors.LocalDisplayName;
        if (string.IsNullOrWhiteSpace(name)) return;
        var local = _character.Local;
        if (local is not null
            && string.Equals(local.DisplayName, name, StringComparison.Ordinal)
            && local.ActorId == _actors.LocalActorId)
            return;

        _character.SetLocal(new CharacterSnapshot(
            name,
            local?.CharacterId,
            _actors.LocalActorId ?? local?.ActorId,
            local?.Level,
            local?.ClassName,
            local?.Stats ?? new Dictionary<string, double>(),
            local?.Equipped ?? Array.Empty<InventoryItem>(),
            local?.Bag ?? Array.Empty<InventoryItem>()));
    }

    private void Post(Action action)
    {
        if (_sync is not null)
            _sync.Post(_ => action(), null);
        else
            action();
    }
}
