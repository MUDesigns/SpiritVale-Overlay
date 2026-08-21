namespace SpiritVale.Overlay.Api.Party;

public interface IPartyApi
{
    event Action? PartyChanged;
    event Action<PartyInviteEvent>? InviteReceived;

    IReadOnlyList<PartyMember> Members { get; }
    int? PartyId { get; }
    bool InParty { get; }
}

public sealed record PartyMember(
    string PlayerId,
    string DisplayName,
    int? ActorId,
    float Health,
    float MaxHealth,
    float Mana,
    float MaxMana,
    IReadOnlyList<string> Statuses,
    bool IsLeader,
    bool IsSelf);

public sealed record PartyInviteEvent(
    string InviterName,
    string InviterId,
    int? PartyId);
