using SpiritVale.Overlay.Api.Party;
using SpiritVale.Overlay.Capture.FishNet;

namespace SpiritVale.Overlay.Domain;

public sealed class PartyTracker : IPartyApi
{
    private readonly object _gate = new();
    private readonly List<PartyMember> _members = new();

    public event Action? PartyChanged;
    public event Action<PartyInviteEvent>? InviteReceived;

    public IReadOnlyList<PartyMember> Members
    {
        get { lock (_gate) return _members.ToList(); }
    }

    public int? PartyId { get; private set; }
    public bool InParty => Members.Count > 0;

    public void Consume(DecodedFishNetPacket packet)
    {
        switch (packet.RpcName)
        {
            case "ShowPartyInvite_T":
                InviteReceived?.Invoke(new PartyInviteEvent(
                    ReadString(packet, "inviterName") ?? "Unknown",
                    ReadString(packet, "inviterId") ?? "",
                    ReadInt(packet, "partyId")));
                break;
            case "PartyUpdate_C":
            case "PartyMemberUpdate_T":
                // Without full payload codecs, keep a placeholder self member when party RPCs appear.
                lock (_gate)
                {
                    if (_members.Count == 0)
                    {
                        _members.Add(new PartyMember(
                            "local", "You", packet.ObjectId, 1, 1, 1, 1,
                            Array.Empty<string>(), true, true));
                        PartyId ??= 1;
                        PartyChanged?.Invoke();
                    }
                }
                break;
        }
    }

    public void UpdateMemberVitals(int actorId, float health, float maxHealth, float mana, float maxMana)
    {
        lock (_gate)
        {
            for (var i = 0; i < _members.Count; i++)
            {
                if (_members[i].ActorId != actorId) continue;
                var m = _members[i];
                _members[i] = m with
                {
                    Health = health,
                    MaxHealth = maxHealth,
                    Mana = mana,
                    MaxMana = maxMana,
                };
                PartyChanged?.Invoke();
                return;
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _members.Clear();
            PartyId = null;
        }
        PartyChanged?.Invoke();
    }

    private static string? ReadString(DecodedFishNetPacket packet, string name)
        => packet.Fields.TryGetValue(name, out var v) ? v?.ToString() : null;

    private static int? ReadInt(DecodedFishNetPacket packet, string name)
    {
        if (!packet.Fields.TryGetValue(name, out var v) || v is null) return null;
        if (v is int i) return i;
        return int.TryParse(v.ToString(), out var p) ? p : null;
    }
}
