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
    private readonly CombatTracker _combat = new();
    private readonly PartyTracker _party = new();
    private readonly CharacterTracker _character = new();
    private readonly WorldTracker _world = new();
    private readonly SynchronizationContext? _sync;

    public SpiritValeApi(PacketCaptureService capture, SynchronizationContext? sync = null)
    {
        _capture = capture;
        _sync = sync;
        _capture.FishNetPacket += OnFishNet;
        _capture.UdpPacket += OnUdp;
        _capture.StatusChanged += s =>
        {
            CaptureStatus = s;
            Post(() => CaptureStateChanged?.Invoke(_capture.IsRunning));
        };
        _capture.Warning += w => CaptureStatus = w;
    }

    public ICombatApi Combat => _combat;
    public IPartyApi Party => _party;
    public ICharacterApi Character => _character;
    public IWorldApi World => _world;
    public IProtocolApi Protocol => this;

    public event Action<bool>? CaptureStateChanged;
    public event Action<DecodedFishNetEvent>? Packet;
    public event Action<RawPacketEvent>? RawPacket;

    public bool IsCapturing => _capture.IsRunning;
    public string? CaptureStatus { get; private set; }
    public bool IncludeRawBytes { get; set; }

    public void StartCapture()
    {
        _capture.Start();
        Post(() => CaptureStateChanged?.Invoke(true));
    }

    public void StopCapture()
    {
        _capture.Stop();
        Post(() => CaptureStateChanged?.Invoke(false));
    }

    public void Dispose()
    {
        _capture.FishNetPacket -= OnFishNet;
        _capture.UdpPacket -= OnUdp;
        _capture.Dispose();
    }

    private void OnUdp(CapturedUdpPacket udp)
    {
        if (!IncludeRawBytes) return;
        var evt = new RawPacketEvent(udp.Timestamp, udp.Direction, udp.Payload);
        Post(() => RawPacket?.Invoke(evt));
    }

    private void OnFishNet(DecodedFishNetPacket packet, CapturedUdpPacket _)
    {
        _combat.Consume(packet);
        _party.Consume(packet);
        _character.Consume(packet);
        _world.Consume(packet);

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

    private void Post(Action action)
    {
        if (_sync is not null)
            _sync.Post(_ => action(), null);
        else
            action();
    }
}
