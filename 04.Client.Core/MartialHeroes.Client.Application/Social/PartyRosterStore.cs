namespace MartialHeroes.Client.Application.Social;

public sealed class PartyRosterStore
{
    public const int MaxMembers = 8;

    private readonly uint[] _members = new uint[MaxMembers];

    public int PartyId { get; private set; }

    public ReadOnlySpan<uint> Members => _members;

    public void SetPartyId(int partyId)
    {
        PartyId = partyId;
    }

    public void SetMembers(ReadOnlySpan<uint> members)
    {
        var count = Math.Min(members.Length, MaxMembers);
        for (var i = 0; i < MaxMembers; i++) _members[i] = i < count ? members[i] : 0u;
    }

    public int CountActive()
    {
        var active = 0;
        for (var i = 0; i < MaxMembers; i++)
            if (_members[i] != 0u)
                active++;

        return active;
    }
}