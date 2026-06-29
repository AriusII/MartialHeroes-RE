namespace MartialHeroes.Client.Application.Social;

public sealed class RelationStore
{
    private const int SlotCapacity = 256;
    private readonly Dictionary<uint, byte> _pairState = new();

    private readonly RelationSlot[] _slots = new RelationSlot[SlotCapacity];

    public void WriteSlot(byte typeIndex, int field0, int field1, int field2, int field3)
    {
        _slots[typeIndex] = new RelationSlot(field0, field1, field2, field3);
    }

    public RelationSlot GetSlot(byte typeIndex)
    {
        return _slots[typeIndex];
    }

    public byte GetPairState(uint actorId)
    {
        return _pairState.TryGetValue(actorId, out var state) ? state : (byte)0;
    }

    public bool IsBonded(uint actorId)
    {
        return GetPairState(actorId) != 0;
    }

    public void SetPairState(uint actorId, byte state)
    {
        _pairState[actorId] = state;
    }

    public readonly record struct RelationSlot(int Field0, int Field1, int Field2, int Field3);
}