using System.Text;

namespace SpiritVale.Overlay.Capture.FishNet;

public readonly record struct SkillStateFields(
    string Id,
    int Level,
    float CurrentCooldown,
    float Cooldown,
    float Duration,
    float CooldownRecoveryRate,
    float MinCooldown,
    int Cost,
    int Charges,
    float CastTime = 0f);

/// <summary>Decodes SkillsComponent SkillStateDto (CastBegin_C / AutoCast_C) and related RPCs.</summary>
public static class SkillStateCodec
{
    public static bool TryDecodeDto(ReadOnlySpan<byte> payload, out SkillStateFields fields)
    {
        fields = default;
        try
        {
            var o = 0;
            var id = ReadString(payload, ref o);
            if (string.IsNullOrWhiteSpace(id)) return false;
            var level = ReadPacked(payload, ref o);
            var current = ReadFloat(payload, ref o);
            var cooldown = ReadFloat(payload, ref o);
            var duration = ReadFloat(payload, ref o);
            var rate = ReadFloat(payload, ref o);
            var minCd = ReadFloat(payload, ref o);
            var cost = ReadPacked(payload, ref o);
            var charges = ReadPacked(payload, ref o);
            var castTime = 0f;
            if (o + 4 <= payload.Length)
            {
                try { castTime = ReadFloat(payload, ref o); }
                catch { /* optional DTO tail */ }
            }

            fields = new SkillStateFields(id, level, current, cooldown, duration, rate, minCd, cost, charges, castTime);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryDecodeReduce(ReadOnlySpan<byte> payload, out string skillId, out float value)
    {
        skillId = "";
        value = 0f;
        try
        {
            var o = 0;
            var id = ReadString(payload, ref o);
            if (string.IsNullOrWhiteSpace(id)) return false;
            value = ReadFloat(payload, ref o);
            skillId = id;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryDecodeToggleId(ReadOnlySpan<byte> payload, out string skillId)
    {
        skillId = "";
        try
        {
            var o = 0;
            var id = ReadString(payload, ref o);
            if (string.IsNullOrWhiteSpace(id)) return false;
            skillId = id;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void WriteDto(DecodedFishNetPacket packet, SkillStateFields dto)
    {
        packet.Fields["dto.Id"] = dto.Id;
        packet.Fields["skillId"] = dto.Id;
        packet.Fields["dto.Level"] = dto.Level;
        packet.Fields["dto.CurrentCooldown"] = dto.CurrentCooldown;
        packet.Fields["dto.Cooldown"] = dto.Cooldown;
        packet.Fields["dto.Duration"] = dto.Duration;
        packet.Fields["dto.CooldownRecoveryRate"] = dto.CooldownRecoveryRate;
        packet.Fields["dto.MinCooldown"] = dto.MinCooldown;
        packet.Fields["dto.Cost"] = dto.Cost;
        packet.Fields["dto.Charges"] = dto.Charges;
        if (dto.CastTime > 0f)
            packet.Fields["dto.CastTime"] = dto.CastTime;
    }

    public static void WriteToFields(DecodedFishNetPacket packet)
    {
        switch (packet.RpcName)
        {
            case "CastBegin_C":
                if (TryDecodeCastBegin(packet.Payload, out var beginDto, out var liveCast))
                {
                    WriteDto(packet, beginDto);
                    if (liveCast > 0f)
                        packet.Fields["castTime"] = liveCast;
                    else if (beginDto.CastTime > 0f)
                        packet.Fields["castTime"] = beginDto.CastTime;
                }
                else if (TryDecodeDto(packet.Payload, out var beginFallback))
                    WriteDto(packet, beginFallback);
                break;
            case "AutoCast_C":
                if (TryDecodeDto(packet.Payload, out var dto))
                    WriteDto(packet, dto);
                break;
            case "ReduceCooldown_T":
                if (TryDecodeReduce(packet.Payload, out var reduceId, out var reduceBy))
                {
                    packet.Fields["skillId"] = reduceId;
                    packet.Fields["value"] = reduceBy;
                }
                break;
            case "ToggleBegin_C":
                if (TryDecodeToggleId(packet.Payload, out var toggleId))
                {
                    packet.Fields["id"] = toggleId;
                    packet.Fields["skillId"] = toggleId;
                }
                break;
        }
    }

    /// <summary>
    /// CastBegin_C: SkillStateDto + targetId + Vector3 + live castTime + animTime.
    /// </summary>
    public static bool TryDecodeCastBegin(ReadOnlySpan<byte> payload, out SkillStateFields dto, out float liveCastTime)
    {
        dto = default;
        liveCastTime = 0f;
        try
        {
            var o = 0;
            var id = ReadString(payload, ref o);
            if (string.IsNullOrWhiteSpace(id)) return false;
            var level = ReadPacked(payload, ref o);
            var current = ReadFloat(payload, ref o);
            var cooldown = ReadFloat(payload, ref o);
            var duration = ReadFloat(payload, ref o);
            var rate = ReadFloat(payload, ref o);
            var minCd = ReadFloat(payload, ref o);
            var cost = ReadPacked(payload, ref o);
            var charges = ReadPacked(payload, ref o);
            var dtoCast = ReadFloat(payload, ref o);
            // Delay, Area, Range, LeapType
            if (o + 12 <= payload.Length)
                o += 12;
            if (o < payload.Length)
            {
                try { ReadPacked(payload, ref o); }
                catch { /* leap type optional */ }
            }

            dto = new SkillStateFields(id, level, current, cooldown, duration, rate, minCd, cost, charges, dtoCast);

            if (o >= payload.Length) return true;
            try { ReadPacked(payload, ref o); } catch { return true; } // targetId
            if (o + 12 <= payload.Length) o += 12; // position
            if (o + 4 <= payload.Length)
                liveCastTime = ReadFloat(payload, ref o);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static int ReadPacked(ReadOnlySpan<byte> payload, ref int offset)
    {
        var packed = WireReader.ReadSignedPackedWhole(payload, offset);
        offset = packed.NextOffset;
        return packed.Value;
    }

    private static float ReadFloat(ReadOnlySpan<byte> payload, ref int offset)
    {
        WireReader.RequireBytes(payload, offset, 4, "float32");
        var value = BitConverter.ToSingle(payload.Slice(offset, 4));
        offset += 4;
        return value;
    }

    private static string ReadString(ReadOnlySpan<byte> payload, ref int offset)
    {
        var length = WireReader.ReadSignedPackedWhole(payload, offset);
        offset = length.NextOffset;
        if (length.Value == -1) return "";
        if (length.Value < 0) throw new FishNetProtocolException("invalid string length");
        var end = WireReader.CheckedEnd(payload, offset, length.Value);
        var value = Encoding.UTF8.GetString(payload.Slice(offset, length.Value));
        offset = end;
        return value;
    }
}
