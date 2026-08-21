using System.Text;

namespace SpiritVale.Overlay.Capture.FishNet;

/// <summary>Extracts player display names from CharacterData / VisualData / spawn payloads.</summary>
public static class ActorIdentityCodec
{
    private static readonly System.Text.RegularExpressions.Regex GuidPattern = new(
        @"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    public readonly record struct Identity(string DisplayName, string? Uid = null, int? Archetype = null);

    /// <summary>LoadCharacter_T / CharacterCallback_T CharacterData layout.</summary>
    public static Identity? TryDecodeCharacterData(ReadOnlySpan<byte> payload)
    {
        foreach (var skipEnum in new[] { true, false })
        {
            try
            {
                var offset = 0;
                if (skipEnum)
                    offset = WireReader.ReadSignedPackedWhole(payload, offset).NextOffset;

                // Caps are byte counts (not char counts) — match spirit-vale-tools-character.
                var lead = ReadString(payload, ref offset, 80);
                _ = lead;
                var uid = ReadString(payload, ref offset, 80);
                if (!GuidPattern.IsMatch(uid)) continue;
                var account = ReadString(payload, ref offset, 80);
                _ = account;
                offset = WireReader.ReadSignedPackedWhole(payload, offset).NextOffset;
                var guildId = ReadString(payload, ref offset, 80);
                if (guildId.Length > 0 && !GuidPattern.IsMatch(guildId)) continue;
                _ = ReadString(payload, ref offset, 80); // role
                var name = ReadString(payload, ref offset, 64);
                if (name.Trim().Length > 0)
                    return new Identity(name.Trim(), uid);
            }
            catch
            {
                // try next offset strategy
            }
        }
        return null;
    }

    /// <summary>PlayerController VisualData sync (index 5): Appearance.DisplayName + Archetype.</summary>
    public static Identity? TryDecodeVisualData(ReadOnlySpan<byte> syncPayload)
    {
        if (syncPayload.Length < 2) return null;
        // Prefer an entry that starts at index 5; otherwise scan the whole blob.
        if (syncPayload[0] == 5)
        {
            var at = TryReadVisualNameAt(syncPayload, 1);
            if (at is not null) return at;
        }
        return TryScanSpawnIdentity(syncPayload);
    }

    /// <summary>Scan spawn blob for VisualData identity (same approach as spirit-vale-tools-capture).</summary>
    public static Identity? TryScanSpawnIdentity(ReadOnlySpan<byte> payload)
    {
        var matches = new Dictionary<string, Identity>(StringComparer.Ordinal);
        for (var i = 0; i < payload.Length - 2; i++)
        {
            if (payload[i] != 5) continue;
            var identity = TryReadVisualNameAt(payload, i + 1);
            if (identity is null) continue;
            matches[$"{identity.Value.DisplayName}|{identity.Value.Archetype}"] = identity.Value;
        }
        return matches.Count == 1 ? matches.Values.First() : null;
    }

    /// <summary>
    /// Last-resort: find a GUID then a nearby printable name string in CharacterData-like blobs.
    /// </summary>
    public static string? TryScanLooseDisplayName(ReadOnlySpan<byte> payload)
    {
        var strings = new List<string>();
        for (var i = 0; i < payload.Length - 2; i++)
        {
            try
            {
                var length = WireReader.ReadSignedPackedWhole(payload, i);
                if (length.Value is < 2 or > 48) continue;
                var end = WireReader.CheckedEnd(payload, length.NextOffset, length.Value);
                var s = Encoding.UTF8.GetString(payload.Slice(length.NextOffset, length.Value));
                if (HasControl(s)) continue;
                if (s.All(char.IsDigit)) continue;
                strings.Add(s);
            }
            catch
            {
                // continue
            }
        }

        // Prefer a string that looks like a character name near a GUID.
        var guidIdx = strings.FindIndex(s => GuidPattern.IsMatch(s));
        if (guidIdx >= 0)
        {
            for (var i = guidIdx + 1; i < strings.Count; i++)
            {
                var s = strings[i];
                if (GuidPattern.IsMatch(s)) continue;
                if (s.Length is >= 2 and <= 24 && s.Any(char.IsLetter))
                    return s;
            }
        }

        return strings.FirstOrDefault(s =>
            !GuidPattern.IsMatch(s)
            && s.Length is >= 3 and <= 20
            && s.Any(char.IsLetter)
            && s.All(c => char.IsLetterOrDigit(c) || c is '_' or '-' or ' '));
    }

    private static Identity? TryReadVisualNameAt(ReadOnlySpan<byte> payload, int offset)
    {
        try
        {
            var length = WireReader.ReadSignedPackedWhole(payload, offset);
            if (length.Value is < 1 or > 128) return null;
            var nameEnd = WireReader.CheckedEnd(payload, length.NextOffset, length.Value);
            var name = Encoding.UTF8.GetString(payload.Slice(length.NextOffset, length.Value));
            if (string.IsNullOrWhiteSpace(name) || HasControl(name)) return null;
            var archetype = WireReader.ReadSignedPackedWhole(payload, nameEnd);
            if (archetype.Value is < 0 or > 1000) return null;
            return new Identity(name.Trim(), Archetype: archetype.Value);
        }
        catch
        {
            return null;
        }
    }

    private static string ReadString(ReadOnlySpan<byte> payload, ref int offset, int maxLen)
    {
        var length = WireReader.ReadSignedPackedWhole(payload, offset);
        if (length.Value < 0 || length.Value > maxLen)
            throw new FishNetProtocolException("implausible CharacterData string");
        var end = WireReader.CheckedEnd(payload, length.NextOffset, length.Value);
        var value = Encoding.UTF8.GetString(payload.Slice(length.NextOffset, length.Value));
        if (HasControl(value)) throw new FishNetProtocolException("control chars");
        offset = end;
        return value;
    }

    private static bool HasControl(string value)
    {
        foreach (var c in value)
        {
            if (c < 0x20 || c == 0x7f) return true;
        }
        return false;
    }
}
