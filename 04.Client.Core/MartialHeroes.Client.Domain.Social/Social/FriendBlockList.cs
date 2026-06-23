namespace MartialHeroes.Client.Domain.Social.Social;

public readonly record struct RelationSlot
{
    public uint PartnerId { get; init; }

    public int Field1 { get; init; }

    public int Field2 { get; init; }

    public int Field3 { get; init; }

    public static RelationSlot Empty => default;

    public bool IsEmpty => PartnerId == 0;
}

public sealed class FriendBlockList
{
    public const uint LocalPlayerSentinel = 0xFFFFFFFFu;

    private readonly RelationSlot[] _slots;

    public FriendBlockList(int slotCapacity, uint localActorId)
    {
        if (slotCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(slotCapacity), "Slot capacity must be greater than zero.");

        _slots = new RelationSlot[slotCapacity];
        LocalActorId = localActorId;
    }

    public uint LocalActorId { get; }

    public int Capacity => _slots.Length;

    public RelationSlot this[int slotIndex] => _slots[slotIndex];

    public int Count
    {
        get
        {
            var c = 0;
            for (var i = 0; i < _slots.Length; i++)
                if (!_slots[i].IsEmpty)
                    c++;

            return c;
        }
    }

    public bool IsSelfTarget(uint targetActorId)
    {
        return targetActorId == LocalActorId || targetActorId == LocalPlayerSentinel;
    }

    public void ApplySlot(int slotIndex, in RelationSlot slot)
    {
        if ((uint)slotIndex >= (uint)_slots.Length) throw new ArgumentOutOfRangeException(nameof(slotIndex));

        _slots[slotIndex] = slot;
    }

    public int Add(uint partnerId, int field1 = 0, int field2 = 0, int field3 = 0)
    {
        if (partnerId == 0 || IsSelfTarget(partnerId) || Contains(partnerId)) return -1;

        for (var i = 0; i < _slots.Length; i++)
            if (_slots[i].IsEmpty)
            {
                _slots[i] = new RelationSlot
                    { PartnerId = partnerId, Field1 = field1, Field2 = field2, Field3 = field3 };
                return i;
            }

        return -1;
    }

    public bool Remove(uint partnerId)
    {
        for (var i = 0; i < _slots.Length; i++)
            if (_slots[i].PartnerId == partnerId && !_slots[i].IsEmpty)
            {
                _slots[i] = RelationSlot.Empty;
                return true;
            }

        return false;
    }

    public bool Contains(uint partnerId)
    {
        if (partnerId == 0) return false;

        for (var i = 0; i < _slots.Length; i++)
            if (_slots[i].PartnerId == partnerId && !_slots[i].IsEmpty)
                return true;

        return false;
    }

    public void Clear()
    {
        Array.Clear(_slots);
    }
}