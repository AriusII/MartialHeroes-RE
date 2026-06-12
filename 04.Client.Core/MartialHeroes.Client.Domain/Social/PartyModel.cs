namespace MartialHeroes.Client.Domain.Social;

/// <summary>
/// The local party-roster model: up to 8 member actor ids, a leader, a party id, and deterministic
/// invite / join / leave / kick transitions with their invariants.
/// spec: Docs/RE/specs/social.md §6 (party subsystem) / §8 (≤ 8 members).
/// </summary>
/// <remarks>
/// <para>
/// The genuine party is the S2C roster (4:35 / 5:21 / 5:38 / 5:76), distinct from the relation/FATE
/// submit cluster (§7 / open question #2). The party id is mirrored from 4:35; roster add / remove /
/// update is applied from 5:21. This model holds that membership state and enforces the caps / leader
/// invariants. spec: social.md §6.1/§6.2/§6.5.
/// </para>
/// <para>
/// Mutable aggregate with controlled methods; the member set is a fixed-capacity array (no per-op heap
/// churn). All transitions are total — an out-of-bounds op is rejected with a <c>false</c> return.
/// spec: social.md §8 (party members ≤ 8).
/// </para>
/// </remarks>
public sealed class PartyModel
{
    /// <summary>Maximum party members (the 4:35 8-id array). spec: social.md §6.1 / §8 (≤ 8).</summary>
    public const int MaxMembers = 8;

    private readonly uint[] _members = new uint[MaxMembers];
    private int _count;

    /// <summary>The party id (mirrored from 4:35). 0 when not in a party. spec: social.md §6.1/§6.5.</summary>
    public int PartyId { get; private set; }

    /// <summary>The party leader's actor id. 0 when there is no party. spec: social.md §6 (leader actions 4:37).</summary>
    public uint LeaderActorId { get; private set; }

    /// <summary>The current member count (0..<see cref="MaxMembers"/>). spec: social.md §6.1.</summary>
    public int Count => _count;

    /// <summary>True when the party has no members. spec: social.md §6.5.</summary>
    public bool IsEmpty => _count == 0;

    /// <summary>True when the party is at the 8-member cap. spec: social.md §8.</summary>
    public bool IsFull => _count >= MaxMembers;

    /// <summary>Reads the member id at <paramref name="index"/> (0..<see cref="Count"/>-1).</summary>
    public uint MemberAt(int index)
    {
        if ((uint)index >= (uint)_count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return _members[index];
    }

    /// <summary>True when <paramref name="actorId"/> is a current member. spec: social.md §6.2.</summary>
    public bool Contains(uint actorId)
    {
        for (int i = 0; i < _count; i++)
        {
            if (_members[i] == actorId)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Forms a party: sets the party id and seeds the leader as the first member. Rejected if a party
    /// already exists or the leader id is 0. spec: Docs/RE/specs/social.md §6.1/§6.5.
    /// </summary>
    /// <returns><c>true</c> when the party was formed.</returns>
    public bool Form(int partyId, uint leaderActorId)
    {
        if (_count != 0 || leaderActorId == 0)
        {
            return false;
        }

        PartyId = partyId;
        LeaderActorId = leaderActorId;
        _members[0] = leaderActorId;
        _count = 1;
        return true;
    }

    /// <summary>
    /// Adds a member to the roster (a 5:21 roster add / a 5:76 join). Rejected when the id is 0, already
    /// present, or the party is full. spec: Docs/RE/specs/social.md §6.2/§6.4/§8.
    /// </summary>
    /// <returns><c>true</c> when the member was added.</returns>
    public bool Join(uint actorId)
    {
        if (actorId == 0 || IsFull || Contains(actorId))
        {
            return false;
        }

        _members[_count++] = actorId;
        return true;
    }

    /// <summary>
    /// Removes <paramref name="actorId"/> from the roster (a 5:21 roster remove / a self leave). If the
    /// leaving member was the leader and members remain, leadership passes to the next member; if the
    /// party empties, the party is dissolved (id / leader cleared). spec: Docs/RE/specs/social.md §6.2/§6.5.
    /// </summary>
    /// <returns><c>true</c> when a member was removed.</returns>
    public bool Leave(uint actorId)
    {
        int index = IndexOf(actorId);
        if (index < 0)
        {
            return false;
        }

        // Compact the array, preserving order. spec: social.md §6.2 (single member remove).
        for (int i = index; i < _count - 1; i++)
        {
            _members[i] = _members[i + 1];
        }

        _members[--_count] = 0;

        if (_count == 0)
        {
            // Party dissolved. spec: social.md §6.5.
            PartyId = 0;
            LeaderActorId = 0;
            return true;
        }

        // Leadership succession when the leader left. spec: social.md §6 (leader action results).
        if (actorId == LeaderActorId)
        {
            LeaderActorId = _members[0];
        }

        return true;
    }

    /// <summary>
    /// Kicks <paramref name="targetActorId"/> on behalf of <paramref name="byActorId"/> — only the
    /// current leader may kick, and not themselves. spec: Docs/RE/specs/social.md §6 (4:36 member-remove,
    /// 4:37 leader action).
    /// </summary>
    /// <returns><c>true</c> when the kick was applied.</returns>
    public bool Kick(uint byActorId, uint targetActorId)
    {
        if (byActorId != LeaderActorId || targetActorId == LeaderActorId)
        {
            return false; // only the leader kicks, and the leader cannot kick themselves.
        }

        return Leave(targetActorId);
    }

    /// <summary>Disbands the party entirely (clears members, id and leader). spec: social.md §6.5.</summary>
    public void Disband()
    {
        Array.Clear(_members);
        _count = 0;
        PartyId = 0;
        LeaderActorId = 0;
    }

    private int IndexOf(uint actorId)
    {
        for (int i = 0; i < _count; i++)
        {
            if (_members[i] == actorId)
            {
                return i;
            }
        }

        return -1;
    }
}