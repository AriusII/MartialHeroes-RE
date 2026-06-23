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
}