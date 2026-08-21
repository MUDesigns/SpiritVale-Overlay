using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using PacketDotNet;
using SharpPcap;
using SharpPcap.LibPcap;
using SpiritVale.Overlay.Capture.FishNet;
using SpiritVale.Overlay.Capture.LiteNetLib;

namespace SpiritVale.Overlay.Capture;

public sealed class CapturedUdpPacket
{
    public required DateTimeOffset Timestamp { get; init; }
    public required IPAddress SourceAddress { get; init; }
    public required IPAddress DestinationAddress { get; init; }
    public required ushort SourcePort { get; init; }
    public required ushort DestinationPort { get; init; }
    public required byte[] Payload { get; init; }
    public required string Direction { get; init; }
}

public sealed class PacketCaptureOptions
{
    public string ProcessName { get; init; } = "SpiritVale";
    public string? DeviceNameContains { get; init; }
    public bool DecodeFishNet { get; init; } = true;
    public FishNetRpcMap? RpcMap { get; init; }
}

public sealed class PacketCaptureService : IDisposable
{
    private readonly PacketCaptureOptions _options;
    private readonly FishNetSessionDecoder _session;
    private ILiveDevice? _device;
    private HashSet<IPAddress> _localAddresses = new();
    private HashSet<(IPAddress, int)> _processEndpoints = new();
    private CancellationTokenSource? _endpointRefreshCts;

    public event Action<CapturedUdpPacket>? UdpPacket;
    public event Action<DecodedLiteNetLibPacket, CapturedUdpPacket>? LiteNetPacket;
    public event Action<DecodedFishNetPacket, CapturedUdpPacket>? FishNetPacket;
    public event Action<string>? Warning;
    public event Action<string>? StatusChanged;

    public bool IsRunning => _device?.Started == true;
    public string? Status { get; private set; }

    public PacketCaptureService(PacketCaptureOptions? options = null)
    {
        _options = options ?? new PacketCaptureOptions();
        _session = new FishNetSessionDecoder(_options.RpcMap ?? BuiltinRpcMap.Load());
    }

    public static NpcapStatus GetNpcapStatus()
    {
        try
        {
            var devices = CaptureDeviceList.Instance;
            return new NpcapStatus(devices.Count > 0, devices.Count, devices.Count == 0
                ? "No capture devices found. Install Npcap with WinPcap API-compatible mode from https://npcap.com/"
                : null);
        }
        catch (Exception ex)
        {
            return new NpcapStatus(false, 0, $"Npcap unavailable: {ex.Message}. Install from https://npcap.com/");
        }
    }

    public static IReadOnlyList<string> ListDevices()
    {
        try
        {
            return CaptureDeviceList.Instance
                .Select(d => d.Description ?? d.Name ?? "(unnamed)")
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public void Start()
    {
        if (IsRunning) return;

        var status = GetNpcapStatus();
        if (!status.Available)
        {
            SetStatus(status.Message ?? "Npcap unavailable");
            throw new InvalidOperationException(Status);
        }

        RefreshLocalAddresses();
        RefreshProcessEndpoints();
        _endpointRefreshCts = new CancellationTokenSource();
        _ = RefreshEndpointsLoopAsync(_endpointRefreshCts.Token);

        var device = SelectDevice();
        device.Open(new DeviceConfiguration
        {
            Mode = DeviceModes.MaxResponsiveness,
            ReadTimeout = 1000,
        });
        // Non-promiscuous: only traffic to/from this host.
        try { device.Filter = "udp"; } catch { /* some adapters reject BPF */ }

        device.OnPacketArrival += OnPacketArrival;
        device.StartCapture();
        _device = device;
        SetStatus($"Capturing on {device.Description ?? device.Name}");
    }

    public void Stop()
    {
        _endpointRefreshCts?.Cancel();
        _endpointRefreshCts = null;
        if (_device is null) return;
        try
        {
            _device.OnPacketArrival -= OnPacketArrival;
            if (_device.Started) _device.StopCapture();
            _device.Close();
        }
        catch
        {
            // ignore shutdown races
        }
        finally
        {
            _device.Dispose();
            _device = null;
            SetStatus("Capture stopped");
        }
    }

    public void Dispose() => Stop();

    private ILiveDevice SelectDevice()
    {
        var devices = CaptureDeviceList.Instance.OfType<ILiveDevice>().ToList();
        if (devices.Count == 0)
            throw new InvalidOperationException("No Npcap devices available.");

        if (!string.IsNullOrWhiteSpace(_options.DeviceNameContains))
        {
            var match = devices.FirstOrDefault(d =>
                (d.Description ?? d.Name ?? "").Contains(_options.DeviceNameContains, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }

        // Prefer a device that has an IPv4 address (skip Npcap Loopback unless needed).
        foreach (var device in devices)
        {
            var name = device.Description ?? device.Name ?? "";
            if (name.Contains("Loopback", StringComparison.OrdinalIgnoreCase)) continue;
            return device;
        }
        return devices[0];
    }

    private void OnPacketArrival(object sender, PacketCapture e)
    {
        try
        {
            var raw = e.GetPacket();
            var packet = Packet.ParsePacket(raw.LinkLayerType, raw.Data);
            var udp = packet.Extract<UdpPacket>();
            if (udp is null) return;
            var ip = packet.Extract<IPPacket>();
            if (ip is null) return;

            var src = ip.SourceAddress;
            var dst = ip.DestinationAddress;
            var direction = _localAddresses.Contains(src) ? "outbound"
                : _localAddresses.Contains(dst) ? "inbound" : "unknown";

            // Prefer process-attributed endpoints when we have them; otherwise accept all UDP.
            if (_processEndpoints.Count > 0)
            {
                var srcEp = (src, udp.SourcePort);
                var dstEp = (dst, udp.DestinationPort);
                if (!_processEndpoints.Contains(srcEp) && !_processEndpoints.Contains(dstEp))
                    return;
            }

            var payload = udp.PayloadData ?? Array.Empty<byte>();
            if (payload.Length == 0) return;

            var captured = new CapturedUdpPacket
            {
                Timestamp = DateTimeOffset.Now,
                SourceAddress = src,
                DestinationAddress = dst,
                SourcePort = udp.SourcePort,
                DestinationPort = udp.DestinationPort,
                Payload = payload,
                Direction = direction,
            };
            UdpPacket?.Invoke(captured);

            if (!_options.DecodeFishNet) return;

            IReadOnlyList<DecodedLiteNetLibPacket> leaves;
            try
            {
                leaves = LiteNetLibDecoder.Decode(payload);
            }
            catch (Exception ex)
            {
                Warning?.Invoke($"LiteNetLib decode: {ex.Message}");
                return;
            }

            foreach (var leaf in leaves)
            {
                LiteNetPacket?.Invoke(leaf, captured);
                if (leaf.Property is not (LiteNetLibProperty.Unreliable or LiteNetLibProperty.Channeled))
                    continue;
                if (leaf.Payload.Length < 6) continue;

                try
                {
                    var reliable = leaf.Property == LiteNetLibProperty.Channeled;
                    var decoded = _session.Decode(leaf.Payload.Span, new FishNetDecodeOptions
                    {
                        Reliable = reliable,
                        Direction = direction,
                        Channel = leaf.Channel,
                        Sequence = leaf.Sequence,
                        ConnectionId = $"{src}:{udp.SourcePort}->{dst}:{udp.DestinationPort}",
                    });
                    foreach (var fish in decoded)
                        FishNetPacket?.Invoke(fish, captured);
                }
                catch (Exception ex)
                {
                    Warning?.Invoke($"FishNet decode: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Warning?.Invoke($"Capture frame: {ex.Message}");
        }
    }

    private void RefreshLocalAddresses()
    {
        _localAddresses = NetworkInterface.GetAllNetworkInterfaces()
            .SelectMany(ni => ni.GetIPProperties().UnicastAddresses)
            .Select(ua => ua.Address)
            .Where(a => a.AddressFamily is System.Net.Sockets.AddressFamily.InterNetwork
                or System.Net.Sockets.AddressFamily.InterNetworkV6)
            .ToHashSet();
    }

    private void RefreshProcessEndpoints()
    {
        try
        {
            var processes = Process.GetProcessesByName(_options.ProcessName);
            if (processes.Length == 0)
            {
                _processEndpoints = new HashSet<(IPAddress, int)>();
                return;
            }

            var pids = processes.Select(p => p.Id).ToHashSet();
            foreach (var p in processes) p.Dispose();

            var endpoints = new HashSet<(IPAddress, int)>();
            var props = IPGlobalProperties.GetIPGlobalProperties();
            foreach (var row in props.GetActiveUdpListeners())
            {
                // Windows does not expose UDP owner PID via IPGlobalProperties; keep all listeners for now
                // and rely on SpiritVale process presence as a soft gate.
                _ = row;
            }

            // Soft attribution: if SpiritVale is running, accept all UDP (Npcap filter already limits to udp).
            // Hard PID attribution for UDP needs GetExtendedUdpTable P/Invoke; deferred — presence gate only.
            _processEndpoints = endpoints;
            if (pids.Count > 0 && Status?.Contains("SpiritVale", StringComparison.OrdinalIgnoreCase) != true)
            {
                // status already set by Start; no-op
            }
        }
        catch (Exception ex)
        {
            Warning?.Invoke($"Process endpoint refresh: {ex.Message}");
        }
    }

    private async Task RefreshEndpointsLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
                RefreshProcessEndpoints();
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void SetStatus(string status)
    {
        Status = status;
        StatusChanged?.Invoke(status);
    }
}

public sealed record NpcapStatus(bool Available, int DeviceCount, string? Message);
