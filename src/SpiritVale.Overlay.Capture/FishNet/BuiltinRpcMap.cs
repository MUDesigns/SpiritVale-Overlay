using System.Text.Json;

namespace SpiritVale.Overlay.Capture.FishNet;

public static class BuiltinRpcMap
{
    public static FishNetRpcMap Load()
    {
        var assembly = typeof(BuiltinRpcMap).Assembly;
        using var stream = assembly.GetManifestResourceStream("SpiritVale.Overlay.Capture.Resources.rpc-map.json")
            ?? throw new InvalidOperationException("Embedded rpc-map.json resource is missing.");
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;
        var behaviours = new List<FishNetBehaviourDefinition>();
        foreach (var behaviour in root.GetProperty("behaviours").EnumerateArray())
        {
            var typeName = behaviour.GetProperty("typeName").GetString()!;
            var rpcs = new List<FishNetRpcDefinition>();
            foreach (var rpc in behaviour.GetProperty("rpcs").EnumerateArray())
            {
                rpcs.Add(new FishNetRpcDefinition
                {
                    WireHash = rpc.GetProperty("wireHash").GetInt32(),
                    PacketKind = rpc.GetProperty("packetKind").GetString()!,
                    MethodName = rpc.GetProperty("methodName").GetString()!,
                    BehaviourType = typeName,
                    Parameters = ParseParameters(rpc),
                });
            }
            behaviours.Add(new FishNetBehaviourDefinition { TypeName = typeName, Rpcs = rpcs });
        }

        return new FishNetRpcMap
        {
            BuildFingerprint = root.TryGetProperty("buildFingerprint", out var fp) ? fp.GetString() ?? "unknown" : "unknown",
            Behaviours = behaviours,
        };
    }

    private static IReadOnlyList<FishNetRpcParameter>? ParseParameters(JsonElement rpc)
    {
        if (!rpc.TryGetProperty("parameters", out var parameters) || parameters.ValueKind != JsonValueKind.Array)
            return null;
        var list = new List<FishNetRpcParameter>();
        foreach (var p in parameters.EnumerateArray())
            list.Add(ParseParameter(p));
        return list;
    }

    private static FishNetRpcParameter ParseParameter(JsonElement p)
    {
        IReadOnlyList<FishNetRpcParameter>? fields = null;
        if (p.TryGetProperty("fields", out var fieldsEl) && fieldsEl.ValueKind == JsonValueKind.Array)
        {
            var nested = new List<FishNetRpcParameter>();
            foreach (var f in fieldsEl.EnumerateArray())
                nested.Add(ParseParameter(f));
            fields = nested;
        }

        return new FishNetRpcParameter
        {
            Name = p.TryGetProperty("name", out var n) ? n.GetString() : null,
            TypeName = p.TryGetProperty("typeName", out var t) ? t.GetString() : null,
            Codec = p.TryGetProperty("codec", out var c) ? c.GetString() : null,
            Fields = fields,
        };
    }
}
