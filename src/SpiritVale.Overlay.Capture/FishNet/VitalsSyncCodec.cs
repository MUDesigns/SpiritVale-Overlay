namespace SpiritVale.Overlay.Capture.FishNet;

/// <summary>
/// HealthComponent / SkillsComponent SyncType payloads: [index byte][packed int] pairs.
/// Indexes 0/1 are current/max (HP or mana depending on behaviour).
/// </summary>
public static class VitalsSyncCodec
{
    public static void WriteToFields(DecodedFishNetPacket packet)
    {
        if (packet.PacketName != FishNetPacketNames.SyncType)
            return;

        var payload = packet.SyncPayload ?? packet.Payload;
        if (payload is not { Length: > 0 }) return;

        var type = packet.NetworkBehaviourType;
        if (type is not ("HealthComponent" or "SkillsComponent"))
            return;

        var values = ReadPackedPairs(payload);
        if (values.Count == 0) return;

        if (type == "HealthComponent")
        {
            packet.SyncName ??= "Health";
            if (values.TryGetValue(0, out var hp)) packet.Fields["currentHealth"] = hp;
            if (values.TryGetValue(1, out var maxHp)) packet.Fields["maxHealth"] = maxHp;
        }
        else
        {
            packet.SyncName ??= "Mana";
            if (values.TryGetValue(0, out var mp)) packet.Fields["currentMana"] = mp;
            if (values.TryGetValue(1, out var maxMp)) packet.Fields["maxMana"] = maxMp;
        }
    }

    private static Dictionary<int, int> ReadPackedPairs(ReadOnlySpan<byte> payload)
    {
        var values = new Dictionary<int, int>();
        var offset = 0;
        try
        {
            while (offset < payload.Length)
            {
                var index = payload[offset++];
                // Only current/max are packed ints. Stop at the first unknown index so a
                // later syncvar (or leftover bytes) cannot overwrite 0/1 with garbage.
                if (index > 1)
                    break;
                var packed = WireReader.ReadSignedPackedWhole(payload, offset);
                offset = packed.NextOffset;
                if (packed.Value is >= 0 and <= 100_000_000)
                    values[index] = packed.Value;
            }
        }
        catch
        {
            // Stop at the last well-formed pair (unknown tail).
        }
        return values;
    }
}
