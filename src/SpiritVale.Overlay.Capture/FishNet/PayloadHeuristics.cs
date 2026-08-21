using System.Text;

namespace SpiritVale.Overlay.Capture.FishNet;

/// <summary>Best-effort payload decoding until full SpiritVale codecs exist.</summary>
public static class PayloadHeuristics
{
    public static void EnrichFields(DecodedFishNetPacket packet)
    {
        var payload = packet.Payload;
        if (payload.Length == 0 && packet.SyncPayload is null) return;

        switch (packet.RpcName)
        {
            case "ApplyDamage_C":
            case "Death_C":
                if (DamageCodec.TryDecode(payload, out var dmg))
                    DamageCodec.WriteToFields(packet, dmg);
                break;
            case "CastBegin_C":
            case "AutoCast_C":
            case "ReduceCooldown_T":
            case "ToggleBegin_C":
                SkillStateCodec.WriteToFields(packet);
                break;
            case "Recover_C":
                try
                {
                    var amount = WireReader.ReadSignedPackedWhole(payload, 0);
                    if (amount.Value > 0)
                    {
                        packet.Fields["amount"] = amount.Value;
                        packet.Fields["damage"] = amount.Value;
                    }
                }
                catch { /* ignore */ }
                break;
            case "LoadCharacter_T":
            case "CharacterCallback_T":
            {
                var identity = ActorIdentityCodec.TryDecodeCharacterData(payload);
                if (identity is not null)
                {
                    packet.Fields["displayName"] = identity.Value.DisplayName;
                    if (identity.Value.Uid is not null)
                        packet.Fields["uid"] = identity.Value.Uid;
                }
                var meta = CharacterMetaCodec.TryDecode(payload);
                if (meta is not null)
                {
                    if (!packet.Fields.ContainsKey("displayName"))
                        packet.Fields["displayName"] = meta.Value.DisplayName;
                    if (meta.Value.Uid is not null && !packet.Fields.ContainsKey("uid"))
                        packet.Fields["uid"] = meta.Value.Uid;
                    if (meta.Value.Level is int lv)
                        packet.Fields["level"] = lv;
                    if (meta.Value.JobLevel is int job)
                        packet.Fields["jobLevel"] = job;
                    if (meta.Value.ClassName is not null)
                        packet.Fields["className"] = meta.Value.ClassName;
                    if (meta.Value.ArchetypeId is int arch)
                        packet.Fields["archetype"] = arch;
                }
                break;
            }
            case "ApplyEffectDisplays_O":
            case "ApplyEffect_T":
            case "RemoveEffect_T":
            case "ApplySkillDisplay_O":
            case "RemoveSkillDisplay_O":
            case "CancelEffect_S":
                StatusDisplayCodec.WriteToFields(packet);
                break;
        }

        if (packet.PacketName == FishNetPacketNames.SyncType
            || packet.SyncPayload is not null)
        {
            VitalsSyncCodec.WriteToFields(packet);
            if (packet.NetworkBehaviourType is "HealthComponent" or "SkillsComponent")
            {
                // Don't try VisualData on vitals sync.
            }
            else
            {
                var sync = packet.SyncPayload ?? payload;
                var identity = ActorIdentityCodec.TryDecodeVisualData(sync);
                if (identity is not null)
                {
                    packet.Fields["displayName"] = identity.Value.DisplayName;
                    packet.SyncName = "VisualData";
                    packet.SyncIndex ??= 5;
                    packet.NetworkBehaviourType ??= "PlayerController";
                    if (identity.Value.Archetype is int arch)
                        packet.Fields["archetype"] = arch;
                }
            }
        }

        if (packet.PacketName == FishNetPacketNames.ObjectSpawn)
        {
            var identity = ActorIdentityCodec.TryScanSpawnIdentity(packet.Raw.AsSpan());
            if (identity is not null)
            {
                packet.Fields["displayName"] = identity.Value.DisplayName;
                if (identity.Value.Archetype is int arch)
                    packet.Fields["archetype"] = arch;
            }
        }

        // CharacterData without a resolved RPC name (map / mid-session).
        if (!packet.Fields.ContainsKey("displayName") && payload.Length >= 16)
        {
            var character = ActorIdentityCodec.TryDecodeCharacterData(payload);
            if (character is not null)
            {
                packet.Fields["displayName"] = character.Value.DisplayName;
                if (character.Value.Uid is not null)
                    packet.Fields["uid"] = character.Value.Uid;
            }
        }
    }

    public static IReadOnlyList<string> ExtractPrintableStrings(ReadOnlySpan<byte> payload)
    {
        var found = new List<string>();
        for (var i = 0; i < payload.Length - 2; i++)
        {
            try
            {
                var len = WireReader.ReadSignedPackedWhole(payload, i);
                if (len.Value is < 2 or > 32) continue;
                if (len.NextOffset + len.Value > payload.Length) continue;
                var slice = payload.Slice(len.NextOffset, len.Value);
                if (!LooksLikeAsciiName(slice)) continue;
                var s = Encoding.UTF8.GetString(slice);
                if (!found.Contains(s, StringComparer.Ordinal))
                    found.Add(s);
                if (found.Count >= 4) break;
            }
            catch
            {
                // continue scan
            }
        }
        return found;
    }

    private static bool LooksLikeAsciiName(ReadOnlySpan<byte> bytes)
    {
        var letters = 0;
        foreach (var b in bytes)
        {
            if (b is < 0x20 or 0x7f or >= 0x80) return false;
            if (char.IsLetter((char)b)) letters++;
        }
        return letters >= 2;
    }
}
