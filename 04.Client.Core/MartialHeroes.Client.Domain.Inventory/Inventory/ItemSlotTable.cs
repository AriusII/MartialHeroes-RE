namespace MartialHeroes.Client.Domain.Inventory.Inventory;

public sealed class ItemSlotTable
{
    private readonly ItemSlotRecord[] _slots;

    public ItemSlotTable(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be greater than zero.");

        _slots = new ItemSlotRecord[capacity];
    }

    public int Capacity => _slots.Length;

    public ItemSlotRecord this[int index] => (uint)index < (uint)_slots.Length ? _slots[index] : ItemSlotRecord.Empty;

    public bool IsEmpty(int index)
    {
        return this[index].IsEmpty;
    }

    public void Set(int index, in ItemSlotRecord record)
    {
        if ((uint)index >= (uint)_slots.Length) throw new ArgumentOutOfRangeException(nameof(index));

        _slots[index] = record;
    }

    public void Clear(int index)
    {
        if ((uint)index >= (uint)_slots.Length) throw new ArgumentOutOfRangeException(nameof(index));

        _slots[index] = ItemSlotRecord.Empty;
    }

    public void ClearAll()
    {
        Array.Clear(_slots);
    }

    public int Apply(int baseIndex, ReadOnlySpan<ItemSlotRecord> records)
    {
        if (baseIndex < 0) throw new ArgumentOutOfRangeException(nameof(baseIndex));

        var written = 0;
        for (var i = 0; i < records.Length; i++)
        {
            var destination = baseIndex + i;
            if ((uint)destination >= (uint)_slots.Length) break;

            _slots[destination] = records[i];
            written++;
        }

        return written;
    }

    public void CopyFrom(ReadOnlySpan<ItemSlotRecord> source)
    {
        var count = source.Length < _slots.Length ? source.Length : _slots.Length;
        source[..count].CopyTo(_slots);
    }

    public ReadOnlySpan<ItemSlotRecord> AsSpan()
    {
        return _slots;
    }

    public int FreeSlotCount()
    {
        var free = 0;
        for (var i = 0; i < _slots.Length; i++)
            if (_slots[i].IsEmpty)
                free++;

        return free;
    }
}