using System.Text;

namespace SpiritVale.Overlay.Capture.FishNet;

/// <summary>Minimal FishNet CharacterData reader for name / class / level.</summary>
public sealed class CharacterReader
{
    private readonly byte[] _buffer;
    private int _offset;

    public CharacterReader(ReadOnlySpan<byte> buffer)
    {
        _buffer = buffer.ToArray();
    }

    public bool Boolean()
    {
        Ensure(1);
        return _buffer[_offset++] == 1;
    }

    public bool ObjectPresent() => !Boolean();

    public int Packed()
    {
        ulong raw = 0;
        var shift = 0;
        for (var count = 0; count < 10; count++)
        {
            Ensure(1);
            var b = _buffer[_offset++];
            raw |= (ulong)(b & 0x7f) << shift;
            if ((b & 0x80) == 0)
            {
                var signed = (long)(raw >> 1) ^ -(long)(raw & 1);
                if (signed is < int.MinValue or > int.MaxValue)
                    throw new FishNetProtocolException("packed out of range");
                return (int)signed;
            }
            shift += 7;
        }
        throw new FishNetProtocolException("unterminated packed");
    }

    public string? String(int maxLen)
    {
        var length = Packed();
        if (length == -1) return null;
        if (length < 0 || length > maxLen) throw new FishNetProtocolException("bad string length");
        Ensure(length);
        var value = Encoding.UTF8.GetString(_buffer, _offset, length);
        _offset += length;
        return value;
    }

    public void Float()
    {
        Ensure(4);
        _offset += 4;
    }

    public T[] List<T>(Func<T> read)
    {
        var length = Packed();
        if (length == -1) return Array.Empty<T>();
        if (length < 0 || length > 100_000) throw new FishNetProtocolException("bad list length");
        var arr = new T[length];
        for (var i = 0; i < length; i++) arr[i] = read();
        return arr;
    }

    public void Booleans() => List(Boolean);

    private void Ensure(int n)
    {
        if (_offset + n > _buffer.Length)
            throw new FishNetProtocolException("truncated CharacterData");
    }
}

public readonly record struct CharacterMeta(
    string DisplayName,
    string? Uid,
    int? ArchetypeId,
    string? ClassName,
    int? Level,
    int? JobLevel);

/// <summary>Decodes enough of CharacterData for DPS meter identity (name, class, level).</summary>
public static class CharacterMetaCodec
{
    public static CharacterMeta? TryDecode(ReadOnlySpan<byte> payload)
    {
        foreach (var skipEnum in new[] { true, false })
        {
            try
            {
                var r = new CharacterReader(payload);
                // CharacterCallback_T prefixes an update-type packed int; LoadCharacter_T does not.
                if (skipEnum) r.Packed();
                r.ObjectPresent();
                var uid = r.String(80);
                r.String(80);
                r.Packed();
                r.String(80);
                r.String(80);
                var name = r.String(64);
                if (string.IsNullOrWhiteSpace(name)) continue;

                r.ObjectPresent();
                for (var i = 0; i < 10; i++) r.Packed();
                r.ObjectPresent();
                r.Booleans();
                r.List(() =>
                {
                    r.ObjectPresent();
                    r.Packed();
                    r.String(256);
                    r.Packed();
                    r.Boolean();
                    return 0;
                });
                r.String(256); // title
                r.String(256);
                r.String(256);
                var archetypeIds = r.List(() => r.Packed());
                var level = r.Packed();
                r.Packed(); // exp
                var jobLevel = r.Packed();

                int? archId = archetypeIds.Length > 0 ? archetypeIds[^1] : null;
                var className = ArchetypeNames.GetName(archId);
                return new CharacterMeta(name.Trim(), uid, archId, className, level > 0 ? level : null, jobLevel > 0 ? jobLevel : null);
            }
            catch
            {
                // try next
            }
        }
        return null;
    }
}
