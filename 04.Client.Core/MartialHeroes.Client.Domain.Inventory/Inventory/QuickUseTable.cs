namespace MartialHeroes.Client.Domain.Inventory.Inventory;

public sealed class QuickUseTable
{
    private readonly QuickUseSlot[] _slots;

    public QuickUseTable(int slotCount)
    {
        if (slotCount <= 0) throw new ArgumentOutOfRangeException(nameof(slotCount), "Slot count must be greater than zero.");

        _slots = new QuickUseSlot[slotCount];
    }

    public int Capacity => _slots.Length;

    public QuickUseSlot this[int index] => (uint)index < (uint)_slots.Length ? _slots[index] : QuickUseSlot.Empty;

    public bool IsOccupied(int index)
    {
        return this[index].IsOccupied;
    }

    public void Set(int index, in QuickUseSlot slot)
    {
        if ((uint)index >= (uint)_slots.Length) throw new ArgumentOutOfRangeException(nameof(index));

        _slots[index] = slot;
    }

    public void Clear(int index)
    {
        if ((uint)index >= (uint)_slots.Length) throw new ArgumentOutOfRangeException(nameof(index));

        _slots[index] = QuickUseSlot.Empty;
    }

    public void ClearAll()
    {
        Array.Clear(_slots);
    }

    public ReadOnlySpan<QuickUseSlot> AsSpan()
    {
        return _slots;
    }
}
