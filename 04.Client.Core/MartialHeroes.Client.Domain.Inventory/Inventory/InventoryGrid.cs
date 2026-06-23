using MartialHeroes.Shared.Kernel.Ids;

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

    public InventorySlot At(int column, int row)
    {
        return _slots[ToIndex(column, row)];
    }

    public bool IsEmpty(int index)
    {
        return _slots[index].IsEmpty;
    }

    public int ToIndex(int column, int row)
    {
        if ((uint)column >= (uint)Columns) throw new ArgumentOutOfRangeException(nameof(column));

        if ((uint)row >= (uint)Rows) throw new ArgumentOutOfRangeException(nameof(row));

        return row * Columns + column;
    }

    public uint Add(ItemId item, uint quantity)
    {
        if (item == ItemId.None) throw new ArgumentException("Cannot add the empty item id.", nameof(item));

        if (quantity == 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than zero.");

        var remaining = quantity;

        for (var i = 0; i < _slots.Length && remaining > 0; i++)
        {
            var slot = _slots[i];
            if (slot.IsEmpty || slot.Item != item || slot.Quantity >= MaxStackSize) continue;

            var room = MaxStackSize - slot.Quantity;
            var take = room < remaining ? room : remaining;
            _slots[i] = new InventorySlot(item, slot.Quantity + take);
            remaining -= take;
        }

        for (var i = 0; i < _slots.Length && remaining > 0; i++)
        {
            if (!_slots[i].IsEmpty) continue;

            var take = MaxStackSize < remaining ? MaxStackSize : remaining;
            _slots[i] = new InventorySlot(item, take);
            remaining -= take;
        }

        return remaining;
    }

    public uint Remove(ItemId item, uint quantity)
    {
        if (item == ItemId.None) throw new ArgumentException("Cannot remove the empty item id.", nameof(item));

        if (quantity == 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than zero.");

        uint removed = 0;
        var remaining = quantity;

        for (var i = 0; i < _slots.Length && remaining > 0; i++)
        {
            var slot = _slots[i];
            if (slot.IsEmpty || slot.Item != item) continue;

            var take = slot.Quantity < remaining ? slot.Quantity : remaining;
            var left = slot.Quantity - take;
            _slots[i] = left == 0 ? InventorySlot.Empty : new InventorySlot(item, left);
            removed += take;
            remaining -= take;
        }

        return removed;
    }

    public uint CountOf(ItemId item)
    {
        if (item == ItemId.None) return 0;

        uint total = 0;
        for (var i = 0; i < _slots.Length; i++)
        {
            var slot = _slots[i];
            if (!slot.IsEmpty && slot.Item == item) total += slot.Quantity;
        }

        return total;
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

    public void Clear()
    {
        Array.Clear(_slots);
    }
}