namespace MartialHeroes.Client.Domain.Inventory.Inventory;

public sealed class BagTable
{
    public const int PageSize = 40;

    public const int BasePages = 3;

    public const int HardCap = 240;

    private readonly ItemSlotRecord[] _slots = new ItemSlotRecord[HardCap];

    public int BagCount { get; private set; }

    public int Capacity => HardCap;

    public int ActiveSize => ComputeActiveSize(BagCount);

    public ItemSlotRecord this[int index] => (uint)index < HardCap ? _slots[index] : ItemSlotRecord.Empty;

    public static int ComputeActiveSize(int bagCount)
    {
        if (bagCount < 0) bagCount = 0;

        var size = PageSize * (bagCount + BasePages);
        return size > HardCap ? HardCap : size;
    }

    public void SetBagCount(int bagCount)
    {
        if (bagCount < 0) throw new ArgumentOutOfRangeException(nameof(bagCount));

        BagCount = bagCount;
    }

    public bool IsEmpty(int index)
    {
        return this[index].IsEmpty;
    }

    public void Set(int index, in ItemSlotRecord record)
    {
        if ((uint)index >= (uint)ActiveSize) throw new ArgumentOutOfRangeException(nameof(index));

        _slots[index] = record;
    }

    public void Clear(int index)
    {
        if ((uint)index >= (uint)ActiveSize) throw new ArgumentOutOfRangeException(nameof(index));

        _slots[index] = ItemSlotRecord.Empty;
    }

    public void ClearAll()
    {
        Array.Clear(_slots);
    }

    public int Apply(int baseIndex, ReadOnlySpan<ItemSlotRecord> records)
    {
        if (baseIndex < 0) throw new ArgumentOutOfRangeException(nameof(baseIndex));

        var active = ActiveSize;
        var written = 0;
        for (var i = 0; i < records.Length; i++)
        {
            var destination = baseIndex + i;
            if ((uint)destination >= (uint)active) break;

            _slots[destination] = records[i];
            written++;
        }

        return written;
    }

    public int FreeSlotCount()
    {
        var active = ActiveSize;
        var free = 0;
        for (var i = 0; i < active; i++)
            if (_slots[i].IsEmpty)
                free++;

        return free;
    }

    public ReadOnlySpan<ItemSlotRecord> AsSpan()
    {
        return _slots.AsSpan(0, ActiveSize);
    }
}