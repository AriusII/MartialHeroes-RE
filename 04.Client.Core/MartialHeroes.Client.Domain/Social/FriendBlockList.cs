namespace MartialHeroes.Client.Domain.Social;

/// <summary>
/// One entry of the flat relation-slot table: a partner id plus the three slot payload words. The
/// table is the canonical client-side relationship membership store (friend / block / FATE bonds).
/// spec: Docs/RE/specs/social.md §7.2 (5:26 local-player relation slot, 16-byte stride).
/// </summary>
/// <remarks>
/// The 5:26 push writes four payload dwords into the slot indexed by a slot byte (slot stride 16
/// bytes). We model the partner id as <see cref="PartnerId"/> (Field0) plus three opaque payload words;
/// their exact semantics are <c>UNVERIFIED</c> (the relation kind lives in the payload). spec: social.md §7.2.
/// </remarks>
public readonly record struct RelationSlot
{
    /// <summary>The partner / target id (Field0). 0 = empty slot. spec: social.md §7.2 (Field0).</summary>
    public uint PartnerId { get; init; }

    /// <summary>Slot payload word 1 (Field1). Opaque. spec: social.md §7.2.</summary>
    public int Field1 { get; init; }

    /// <summary>Slot payload word 2 (Field2). Opaque. spec: social.md §7.2.</summary>
    public int Field2 { get; init; }

    /// <summary>Slot payload word 3 (Field3). Opaque. spec: social.md §7.2.</summary>
    public int Field3 { get; init; }

    /// <summary>An empty slot.</summary>
    public static RelationSlot Empty => default;

    /// <summary>True when the slot holds no partner. spec: social.md §7.2.</summary>
    public bool IsEmpty => PartnerId == 0;
}

/// <summary>
/// The combined friend / block / relation ("FATE") membership store: a flat slot table indexed by slot
/// byte, plus the §1 self-target guard. spec: Docs/RE/specs/social.md §7 / §8.
/// </summary>
/// <remarks>
/// <para>
/// The client folds friend list, block list and special-bond relationships into one relationship model
/// backed by one flat slot table (the §7.2 5:26 store). The relation <em>kind</em> (friend vs block vs
/// bond) lives in the slot payload, which is <c>UNVERIFIED</c>; this model holds the slot table and the
/// membership operations, and exposes a <em>block</em> view as a separate explicit set the caller
/// drives. spec: social.md §7 / §9 #9 (slot count not bounded by the apply path).
/// </para>
/// <para>
/// <b>Slot capacity is injected.</b> The maximum slot count is not bounded by the apply path
/// (open question #9); it is therefore a constructor parameter, not a hard-coded constant. spec: social.md §9 #9.
/// </para>
/// </remarks>
public sealed class FriendBlockList
{
    private readonly RelationSlot[] _slots;

    /// <summary>
    /// Creates an empty relation table with <paramref name="slotCapacity"/> slots and the local
    /// player's own actor id (for the self-target guard).
    /// </summary>
    /// <param name="slotCapacity">The slot table length (injected — not spec-bounded). spec: social.md §9 #9.</param>
    /// <param name="localActorId">The local player's actor id, for the self-target guard. spec: social.md §1.</param>
    public FriendBlockList(int slotCapacity, uint localActorId)
    {
        if (slotCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(slotCapacity), "Slot capacity must be greater than zero.");
        }

        _slots = new RelationSlot[slotCapacity];
        LocalActorId = localActorId;
    }

    /// <summary>The local player's actor id (the self-target guard compares against this). spec: social.md §1.</summary>
    public uint LocalActorId { get; }

    /// <summary>The slot table length. spec: social.md §7.2.</summary>
    public int Capacity => _slots.Length;

    /// <summary>Reads the slot at <paramref name="slotIndex"/>. spec: social.md §7.2.</summary>
    public RelationSlot this[int slotIndex] => _slots[slotIndex];

    /// <summary>The number of occupied (non-empty) slots. spec: social.md §7.2.</summary>
    public int Count
    {
        get
        {
            int c = 0;
            for (int i = 0; i < _slots.Length; i++)
            {
                if (!_slots[i].IsEmpty)
                {
                    c++;
                }
            }

            return c;
        }
    }

    /// <summary>
    /// The §1 self-target guard: true when <paramref name="targetActorId"/> is the local player or the
    /// unset sentinel (0xFFFFFFFF) — such a submit aborts with message id 862010101 and sends nothing.
    /// spec: Docs/RE/specs/social.md §1 (self-target guard) / §8.
    /// </summary>
    public bool IsSelfTarget(uint targetActorId) =>
        targetActorId == LocalActorId || targetActorId == LocalPlayerSentinel;

    /// <summary>The unset local-player id sentinel (0xFFFFFFFF). spec: social.md §1.</summary>
    public const uint LocalPlayerSentinel = 0xFFFFFFFFu;

    /// <summary>
    /// Writes (applies) a relation slot at <paramref name="slotIndex"/> from a 5:26 push. Overwrites the
    /// slot (the table is index-keyed, refresh-by-slot). spec: Docs/RE/specs/social.md §7.2.
    /// </summary>
    public void ApplySlot(int slotIndex, in RelationSlot slot)
    {
        if ((uint)slotIndex >= (uint)_slots.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(slotIndex));
        }

        _slots[slotIndex] = slot;
    }

    /// <summary>
    /// Adds a relationship with <paramref name="partnerId"/> into the first free slot, rejecting a
    /// self-target, a duplicate, a zero id, or a full table. spec: Docs/RE/specs/social.md §7 / §1.
    /// </summary>
    /// <returns>The slot index used, or -1 if rejected.</returns>
    public int Add(uint partnerId, int field1 = 0, int field2 = 0, int field3 = 0)
    {
        if (partnerId == 0 || IsSelfTarget(partnerId) || Contains(partnerId))
        {
            return -1;
        }

        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i].IsEmpty)
            {
                _slots[i] = new RelationSlot
                    { PartnerId = partnerId, Field1 = field1, Field2 = field2, Field3 = field3 };
                return i;
            }
        }

        return -1; // table full.
    }

    /// <summary>
    /// Removes the relationship with <paramref name="partnerId"/> (the "cut" / sever action), clearing
    /// its slot. spec: Docs/RE/specs/social.md §7 / §7.4 ("cut" command).
    /// </summary>
    /// <returns><c>true</c> when a slot was cleared.</returns>
    public bool Remove(uint partnerId)
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i].PartnerId == partnerId && !_slots[i].IsEmpty)
            {
                _slots[i] = RelationSlot.Empty;
                return true;
            }
        }

        return false;
    }

    /// <summary>True when <paramref name="partnerId"/> occupies a slot. spec: social.md §7.</summary>
    public bool Contains(uint partnerId)
    {
        if (partnerId == 0)
        {
            return false;
        }

        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i].PartnerId == partnerId && !_slots[i].IsEmpty)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Clears every slot.</summary>
    public void Clear() => Array.Clear(_slots);
}