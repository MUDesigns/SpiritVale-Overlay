using System.Text;

namespace SpiritVale.Overlay.Capture.FishNet;

public readonly record struct DamageFields(
    int Team,
    int Value,
    int Type,
    int Hit,
    int Hits,
    string? DamageSourceId,
    int AttackerId,
    bool IsClone,
    bool IsSummon,
    int Element,
    int WeaponType,
    int Range);

/// <summary>Decodes HealthComponent ApplyDamage_C / Death_C Damage DTO (SpiritVale wire layout).</summary>
public static class DamageCodec
{
    public static bool TryDecode(ReadOnlySpan<byte> payload, out DamageFields fields)
    {
        fields = default;
        try
        {
            var o = 0;
            var team = ReadPacked(payload, ref o);
            var value = ReadPacked(payload, ref o);
            var type = ReadPacked(payload, ref o);
            var hit = ReadPacked(payload, ref o);
            var hits = ReadPacked(payload, ref o);
            var sourceId = ReadString(payload, ref o);
            var attackerId = ReadPacked(payload, ref o);
            var isClone = ReadBool(payload, ref o);
            var isSummon = ReadBool(payload, ref o);
            var element = ReadPacked(payload, ref o);
            var weaponType = ReadPacked(payload, ref o);
            var range = ReadPacked(payload, ref o);

            // AttackerId may be -1 (unsourced); CombatTracker decides credit. Value must be positive.
            if (value <= 0) return false;

            fields = new DamageFields(
                team, value, type, hit, hits, sourceId, attackerId,
                isClone, isSummon, element, weaponType, range);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void WriteToFields(DecodedFishNetPacket packet, DamageFields dmg)
    {
        packet.Fields["dmg.Team"] = dmg.Team;
        packet.Fields["dmg.Value"] = dmg.Value;
        packet.Fields["dmg.Type"] = dmg.Type;
        packet.Fields["dmg.Hit"] = dmg.Hit;
        packet.Fields["dmg.Hits"] = dmg.Hits;
        packet.Fields["dmg.DamageSourceId"] = dmg.DamageSourceId;
        packet.Fields["dmg.AttackerId"] = dmg.AttackerId;
        packet.Fields["dmg.IsClone"] = dmg.IsClone;
        packet.Fields["dmg.IsSummon"] = dmg.IsSummon;
        packet.Fields["damage"] = dmg.Value;
        packet.Fields["amount"] = dmg.Value;
        packet.Fields["sourceId"] = dmg.AttackerId;
        packet.Fields["attackerId"] = dmg.AttackerId;
        packet.Fields["critical"] = dmg.Hit == 1;
    }

    private static int ReadPacked(ReadOnlySpan<byte> payload, ref int offset)
    {
        var packed = WireReader.ReadSignedPackedWhole(payload, offset);
        offset = packed.NextOffset;
        return packed.Value;
    }

    private static bool ReadBool(ReadOnlySpan<byte> payload, ref int offset)
    {
        WireReader.RequireBytes(payload, offset, 1, "boolean");
        var b = payload[offset++];
        if (b is not (0 or 1)) throw new FishNetProtocolException("invalid boolean");
        return b == 1;
    }

    private static string? ReadString(ReadOnlySpan<byte> payload, ref int offset)
    {
        var length = WireReader.ReadSignedPackedWhole(payload, offset);
        offset = length.NextOffset;
        if (length.Value == -1) return null;
        if (length.Value < 0) throw new FishNetProtocolException("invalid string length");
        var end = WireReader.CheckedEnd(payload, offset, length.Value);
        var value = Encoding.UTF8.GetString(payload.Slice(offset, length.Value));
        offset = end;
        return value;
    }
}
