namespace MartialHeroes.Client.Domain.Social.Social;

public sealed class PartyModel
{
    public const int MaxMembers = 8;

    private readonly uint[] _members = new uint[MaxMembers];

    public int PartyId { get; private set; }

    public uint LeaderActorId { get; private set; }

    public int Count { get; private set; }

    public bool IsEmpty => Count == 0;

    public bool IsFull => Count >= MaxMembers;

    public uint MemberAt(int index)
    {
        if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));

        return _members[index];
    }

    public bool Contains(uint actorId)
    {
        for (var i = 0; i < Count; i++)
            if (_members[i] == actorId)
                return true;

        return false;
    }

    public bool Form(int partyId, uint leaderActorId)
    {
        if (Count != 0 || leaderActorId == 0) return false;

        PartyId = partyId;
        LeaderActorId = leaderActorId;
        _members[0] = leaderActorId;
        Count = 1;
        return true;
    }

    public bool Join(uint actorId)
    {
        if (actorId == 0 || IsFull || Contains(actorId)) return false;

        _members[Count++] = actorId;
        return true;
    }

    public bool Leave(uint actorId)
    {
        var index = IndexOf(actorId);
        if (index < 0) return false;

        for (var i = index; i < Count - 1; i++) _members[i] = _members[i + 1];

        _members[--Count] = 0;

        if (Count == 0)
        {
            PartyId = 0;
            LeaderActorId = 0;
            return true;
        }

        if (actorId == LeaderActorId) LeaderActorId = _members[0];

        return true;
    }

    public bool Kick(uint byActorId, uint targetActorId)
    {
        if (byActorId != LeaderActorId ||
            targetActorId ==
            LeaderActorId) return false;

        return Leave(targetActorId);
    }

    public void Disband()
    {
        Array.Clear(_members);
        Count = 0;
        PartyId = 0;
        LeaderActorId = 0;
    }

    private int IndexOf(uint actorId)
    {
        for (var i = 0; i < Count; i++)
            if (_members[i] == actorId)
                return i;

        return -1;
    }
}