using System.Globalization;
using System.Text;

namespace SpiritVale.Overlay.Capture.FishNet;

/// <summary>Decodes StatusComponent RPCs into plugin-readable Fields.</summary>
public static class StatusDisplayCodec
{
    public static void WriteToFields(DecodedFishNetPacket packet)
    {
        switch (packet.RpcName)
        {
            case "ApplyEffectDisplays_O":
                WriteDisplays(packet);
                break;
            case "ApplyEffect_T":
            case "RemoveEffect_T":
                WriteIdLevel(packet, statusKey: "statusId");
                break;
            case "ApplySkillDisplay_O":
                WriteIdLevel(packet, statusKey: "id");
                break;
            case "RemoveSkillDisplay_O":
            case "CancelEffect_S":
                WriteIdOnly(packet);
                break;
        }
    }

    private static void WriteDisplays(DecodedFishNetPacket packet)
    {
        if (!TryDecodeDisplays(packet.Payload, out var applies, out var removes))
            return;

        packet.Fields["effectApplies"] = applies;
        packet.Fields["effectRemoves"] = removes;
        packet.Fields["applyCount"] = applies.Count;
        packet.Fields["removeCount"] = removes.Count;
    }

    public static bool TryDecodeDisplays(ReadOnlySpan<byte> payload, out List<string> applies, out List<string> removes)
    {
        applies = new List<string>();
        removes = new List<string>();
        try
        {
            var o = 0;
            var count = ReadPacked(payload, ref o);
            if (count < -1) return false;
            for (var i = 0; i < count; i++)
            {
                var id = ReadString(payload, ref o);
                if (string.IsNullOrWhiteSpace(id)) return false;
                WireReader.RequireBytes(payload, o, 4, "effect remaining");
                var remaining = BitConverter.ToSingle(payload.Slice(o, 4));
                o += 4;
                var stacks = ReadPacked(payload, ref o);
                var maxStacks = ReadPacked(payload, ref o);
                WireReader.RequireBytes(payload, o, 1, "effect flag");
                var flag = payload[o];
                if (flag is not (0 or 1)) return false;
                o++;
                if (stacks < 0) return false;
                var rem = remaining < 0 || !float.IsFinite(remaining)
                    ? -1f
                    : remaining;
                applies.Add(string.Create(CultureInfo.InvariantCulture,
                    $"{id}\t{rem:0.###}\t{stacks}\t{Math.Max(0, maxStacks)}"));
            }

            var removeCount = ReadPacked(payload, ref o);
            if (removeCount < -1) return false;
            for (var i = 0; i < removeCount; i++)
            {
                var id = ReadString(payload, ref o);
                if (!string.IsNullOrWhiteSpace(id))
                    removes.Add(id);
            }

            return o == payload.Length;
        }
        catch
        {
            return false;
        }
    }

    private static void WriteIdLevel(DecodedFishNetPacket packet, string statusKey)
    {
        try
        {
            var o = 0;
            var id = ReadString(packet.Payload, ref o);
            if (string.IsNullOrWhiteSpace(id)) return;
            packet.Fields[statusKey] = id;
            packet.Fields["skillId"] = id;
            packet.Fields["id"] = id;
            if (o < packet.Payload.Length)
                packet.Fields["level"] = ReadPacked(packet.Payload, ref o);
        }
        catch
        {
            // ignore
        }
    }

    private static void WriteIdOnly(DecodedFishNetPacket packet)
    {
        try
        {
            var o = 0;
            var id = ReadString(packet.Payload, ref o);
            if (string.IsNullOrWhiteSpace(id)) return;
            packet.Fields["id"] = id;
            packet.Fields["skillId"] = id;
            packet.Fields["statusId"] = id;
        }
        catch
        {
            // ignore
        }
    }

    private static int ReadPacked(ReadOnlySpan<byte> payload, ref int offset)
    {
        var packed = WireReader.ReadSignedPackedWhole(payload, offset);
        offset = packed.NextOffset;
        return packed.Value;
    }

    private static string ReadString(ReadOnlySpan<byte> payload, ref int offset)
    {
        var length = WireReader.ReadSignedPackedWhole(payload, offset);
        offset = length.NextOffset;
        if (length.Value < 0) throw new FishNetProtocolException("null status id");
        var end = WireReader.CheckedEnd(payload, offset, length.Value);
        var value = Encoding.UTF8.GetString(payload.Slice(offset, length.Value));
        offset = end;
        return value;
    }
}
