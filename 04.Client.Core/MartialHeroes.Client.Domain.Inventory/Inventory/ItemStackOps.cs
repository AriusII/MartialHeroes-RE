namespace MartialHeroes.Client.Domain.Inventory.Inventory;

/// <summary>
///     Pure stack operations over an <see cref="InventoryGrid" />: split, merge, and full-stack move. These
///     extend the grid's built-in move/merge/swap with an explicit split (take part of a stack into an
///     empty slot) and an explicit merge (combine two same-item stacks).
///     spec: Docs/RE/specs/inventory_trade.md §9 (item move / split / stack).
/// </summary>
/// <remarks>
///     <para>
///         <b>No dedicated split / merge opcode exists on the wire.</b> The legacy client carries quantity as
///         an i32 in the sell / trade / item-use paths and an in-bag move rides the equip slot-move opcode
///         (2/16); a split "would plausibly reuse a slot-move minor with a quantity below the full stack" but
///         the exact request is <c>UNVERIFIED / not found</c>. spec: inventory_trade.md §9 / §11 #3.
///     </para>
///     <para>
///         Because the network mechanic is unverified, these operations model the <b>local grid mutation</b>
///         only — the deterministic rules a client (or a future authoritative server) applies to its own grid
///         state. They never assume a particular packet. Each is a pure function of the grid and its
///         arguments. spec: inventory_trade.md §9.
///     </para>
/// </remarks>
public static class ItemStackOps
{
    /// <summary>
    ///     Splits <paramref name="quantity" /> from the stack at <paramref name="fromIndex" /> into the empty
    ///     slot at <paramref name="toIndex" />. The destination must be empty and the quantity must be
    ///     strictly less than the source stack (a true split leaves something behind). spec: inventory_trade.md §9.
    /// </summary>
    /// <returns><c>true</c> when the split was performed; <c>false</c> when the rules reject it.</returns>
    public static bool Split(InventoryGrid grid, int fromIndex, int toIndex, uint quantity)
    {
        ArgumentNullException.ThrowIfNull(grid);

        if ((uint)fromIndex >= (uint)grid.Capacity || (uint)toIndex >= (uint)grid.Capacity)
            throw new ArgumentOutOfRangeException(nameof(fromIndex), "Slot index out of range.");

        if (fromIndex == toIndex) return false;

        var source = grid[fromIndex];
        if (source.IsEmpty || quantity == 0 ||
            quantity >= source.Quantity)
            return false; // nothing to split, or a "split" that would move the whole stack.

        if (!grid.IsEmpty(toIndex)) return false; // a split targets an empty slot only. spec: inventory_trade.md §9.

        if (quantity > grid.MaxStackSize) return false; // the new stack must fit one slot.

        grid.SetSlot(fromIndex, new InventorySlot(source.Item, source.Quantity - quantity));
        grid.SetSlot(toIndex, new InventorySlot(source.Item, quantity));
        return true;
    }

    /// <summary>
    ///     Merges the stack at <paramref name="fromIndex" /> into <paramref name="toIndex" /> when both hold
    ///     the same item, capping the destination at <see cref="InventoryGrid.MaxStackSize" /> and leaving
    ///     any overflow in the source. Returns false for different items / empty source / full destination.
    ///     spec: Docs/RE/specs/inventory_trade.md §9.
    /// </summary>
    /// <returns><c>true</c> when a merge moved at least one unit.</returns>
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