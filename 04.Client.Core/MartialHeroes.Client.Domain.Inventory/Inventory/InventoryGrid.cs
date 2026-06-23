namespace MartialHeroes.Client.Domain.Inventory.Inventory;

public sealed class InventoryGrid
{
    private readonly InventorySlot[] _slots;

    public InventoryGrid(int columns, int rows, uint maxStackSize)
    {
        if (columns <= 0) throw new ArgumentOutOfRangeException(nameof(columns), "Columns must be greater than zero.");

        if (rows <= 0) throw new ArgumentOutOfRangeException(nameof(rows), "Rows must be greater than zero.");

        if (maxStackSize == 0)
            throw new ArgumentOutOfRangeException(nameof(maxStackSize), "Max stack size must be greater than zero.");

        Columns = columns;
        Rows = rows;
        MaxStackSize = maxStackSize;
        _slots = new InventorySlot[columns * rows];
    }

    public int Columns { get; }

    public int Rows { get; }

    public uint MaxStackSize { get; }

    public int Capacity => _slots.Length;

    public InventorySlot this[int index] => _slots[index];

    public bool IsEmpty(int index)
    {
        return _slots[index].IsEmpty;
    }

    public bool Move(int fromIndex, int toIndex)
    {
        if ((uint)fromIndex >= (uint)_slots.Length) throw new ArgumentOutOfRangeException(nameof(fromIndex));

        if ((uint)toIndex >= (uint)_slots.Length) throw new ArgumentOutOfRangeException(nameof(toIndex));

        if (fromIndex == toIndex) return true;

        var source = _slots[fromIndex];
        if (source.IsEmpty) return true;

        var dest = _slots[toIndex];

        if (dest.IsEmpty)
        {
            _slots[toIndex] = source;
            _slots[fromIndex] = InventorySlot.Empty;
            return true;
        }

        if (dest.Item == source.Item && dest.Quantity < MaxStackSize)
        {
            var room = MaxStackSize - dest.Quantity;
            var take = room < source.Quantity ? room : source.Quantity;
            _slots[toIndex] = new InventorySlot(dest.Item, dest.Quantity + take);
            var left = source.Quantity - take;
            _slots[fromIndex] = left == 0 ? InventorySlot.Empty : new InventorySlot(source.Item, left);
            return true;
        }

        _slots[fromIndex] = dest;
        _slots[toIndex] = source;
        return true;
    }

    public void SetSlot(int index, InventorySlot slot)
    {
        if ((uint)index >= (uint)_slots.Length) throw new ArgumentOutOfRangeException(nameof(index));

        _slots[index] = slot;
    }
}