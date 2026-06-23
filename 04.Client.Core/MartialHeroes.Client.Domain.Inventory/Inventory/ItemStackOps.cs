namespace MartialHeroes.Client.Domain.Inventory.Inventory;

public static class ItemStackOps
{
    public static bool Split(InventoryGrid grid, int fromIndex, int toIndex, uint quantity)
    {
        ArgumentNullException.ThrowIfNull(grid);

        if ((uint)fromIndex >= (uint)grid.Capacity || (uint)toIndex >= (uint)grid.Capacity)
            throw new ArgumentOutOfRangeException(nameof(fromIndex), "Slot index out of range.");

        if (fromIndex == toIndex) return false;

        var source = grid[fromIndex];
        if (source.IsEmpty || quantity == 0 ||
            quantity >= source.Quantity)
            return false;

        if (!grid.IsEmpty(toIndex)) return false;

        if (quantity > grid.MaxStackSize) return false;

        grid.SetSlot(fromIndex, new InventorySlot(source.Item, source.Quantity - quantity));
        grid.SetSlot(toIndex, new InventorySlot(source.Item, quantity));
        return true;
    }

    public static bool Merge(InventoryGrid grid, int fromIndex, int toIndex)
    {
        ArgumentNullException.ThrowIfNull(grid);

        if ((uint)fromIndex >= (uint)grid.Capacity || (uint)toIndex >= (uint)grid.Capacity)
            throw new ArgumentOutOfRangeException(nameof(fromIndex), "Slot index out of range.");

        if (fromIndex == toIndex) return false;

        var source = grid[fromIndex];
        var dest = grid[toIndex];
        if (source.IsEmpty || dest.IsEmpty || source.Item != dest.Item || dest.Quantity >= grid.MaxStackSize)
            return false;

        var room = grid.MaxStackSize - dest.Quantity;
        var take = room < source.Quantity ? room : source.Quantity;
        grid.SetSlot(toIndex, new InventorySlot(dest.Item, dest.Quantity + take));

        var left = source.Quantity - take;
        grid.SetSlot(fromIndex, left == 0 ? InventorySlot.Empty : new InventorySlot(source.Item, left));
        return true;
    }
}