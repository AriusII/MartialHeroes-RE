using MartialHeroes.Shared.Kernel.Ids;

namespace MartialHeroes.Client.Domain.Inventory;

/// <summary>
/// A fixed rectangular grid of inventory slots with deterministic add / remove / move / stack
/// operations.
/// </summary>
/// <remarks>
/// <para>
/// The backing store is a single flat array sized <c>columns * rows</c>, allocated once at
/// construction. No operation allocates beyond that store; every mutation is an in-place slot
/// assignment. All operations are pure functions of the current grid state and their arguments
/// (no clock, no RNG), so they are exhaustively unit-testable.
/// </para>
/// <para>
/// <b>Modeling choice (ours).</b> The legacy inventory grid dimensions and the per-item maximum
/// stack size are not published in the actor spec (the equipment/stat block is unmapped — spec:
/// Docs/RE/structs/actor.md "Unverified / open questions"). They are therefore caller-supplied
/// parameters rather than hard-coded constants; no original-game number is invented here.
/// </para>
/// </remarks>
public sealed class InventoryGrid
{
    private readonly InventorySlot[] _slots;

    /// <summary>Number of columns (width).</summary>
    public int Columns { get; }

    /// <summary>Number of rows (height).</summary>
    public int Rows { get; }

    /// <summary>Maximum quantity a single slot may hold for one item kind.</summary>
    public uint MaxStackSize { get; }

    /// <summary>Total number of slots (<see cref="Columns"/> * <see cref="Rows"/>).</summary>
    public int Capacity => _slots.Length;

    /// <summary>
    /// Creates an empty grid.
    /// </summary>
    /// <param name="columns">Grid width; must be &gt; 0.</param>
    /// <param name="rows">Grid height; must be &gt; 0.</param>
    /// <param name="maxStackSize">Maximum stack size per slot; must be &gt; 0.</param>
    public InventoryGrid(int columns, int rows, uint maxStackSize)
    {
        if (columns <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(columns), "Columns must be greater than zero.");
        }

        if (rows <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rows), "Rows must be greater than zero.");
        }

        if (maxStackSize == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxStackSize), "Max stack size must be greater than zero.");
        }

        Columns = columns;
        Rows = rows;
        MaxStackSize = maxStackSize;
        _slots = new InventorySlot[columns * rows];
    }

    /// <summary>Reads the slot at a flat index.</summary>
    public InventorySlot this[int index] => _slots[index];

    /// <summary>Reads the slot at (column, row).</summary>
    public InventorySlot At(int column, int row) => _slots[ToIndex(column, row)];

    /// <summary>True when the slot at <paramref name="index"/> holds no item.</summary>
    public bool IsEmpty(int index) => _slots[index].IsEmpty;

    /// <summary>Converts a (column, row) coordinate into a flat slot index.</summary>
    public int ToIndex(int column, int row)
    {
        if ((uint)column >= (uint)Columns)
        {
            throw new ArgumentOutOfRangeException(nameof(column));
        }

        if ((uint)row >= (uint)Rows)
        {
            throw new ArgumentOutOfRangeException(nameof(row));
        }

        return (row * Columns) + column;
    }

    /// <summary>
    /// Adds <paramref name="quantity"/> of <paramref name="item"/>, first topping up existing
    /// stacks of the same item (up to <see cref="MaxStackSize"/>), then filling empty slots.
    /// </summary>
    /// <param name="item">Item to add; must not be <see cref="ItemId.None"/>.</param>
    /// <param name="quantity">Amount to add; must be &gt; 0.</param>
    /// <returns>
    /// The amount that did not fit (0 when everything was placed). The grid is only mutated for the
    /// portion that fit; the unplaced remainder leaves earlier writes intact (partial add).
    /// </returns>
    public uint Add(ItemId item, uint quantity)
    {
        if (item == ItemId.None)
        {
            throw new ArgumentException("Cannot add the empty item id.", nameof(item));
        }

        if (quantity == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than zero.");
        }

        uint remaining = quantity;

        // Pass 1: top up existing stacks of the same item.
        for (int i = 0; i < _slots.Length && remaining > 0; i++)
        {
            InventorySlot slot = _slots[i];
            if (slot.IsEmpty || slot.Item != item || slot.Quantity >= MaxStackSize)
            {
                continue;
            }

            uint room = MaxStackSize - slot.Quantity;
            uint take = room < remaining ? room : remaining;
            _slots[i] = new InventorySlot(item, slot.Quantity + take);
            remaining -= take;
        }

        // Pass 2: fill empty slots with new stacks.
        for (int i = 0; i < _slots.Length && remaining > 0; i++)
        {
            if (!_slots[i].IsEmpty)
            {
                continue;
            }

            uint take = MaxStackSize < remaining ? MaxStackSize : remaining;
            _slots[i] = new InventorySlot(item, take);
            remaining -= take;
        }

        return remaining;
    }

    /// <summary>
    /// Removes up to <paramref name="quantity"/> of <paramref name="item"/> across all stacks,
    /// lowest index first.
    /// </summary>
    /// <returns>The amount actually removed (may be less than requested if not enough was held).</returns>
    public uint Remove(ItemId item, uint quantity)
    {
        if (item == ItemId.None)
        {
            throw new ArgumentException("Cannot remove the empty item id.", nameof(item));
        }

        if (quantity == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than zero.");
        }

        uint removed = 0;
        uint remaining = quantity;

        for (int i = 0; i < _slots.Length && remaining > 0; i++)
        {
            InventorySlot slot = _slots[i];
            if (slot.IsEmpty || slot.Item != item)
            {
                continue;
            }

            uint take = slot.Quantity < remaining ? slot.Quantity : remaining;
            uint left = slot.Quantity - take;
            _slots[i] = left == 0 ? InventorySlot.Empty : new InventorySlot(item, left);
            removed += take;
            remaining -= take;
        }

        return removed;
    }

    /// <summary>Total quantity of <paramref name="item"/> held across the whole grid.</summary>
    public uint CountOf(ItemId item)
    {
        if (item == ItemId.None)
        {
            return 0;
        }

        uint total = 0;
        for (int i = 0; i < _slots.Length; i++)
        {
            InventorySlot slot = _slots[i];
            if (!slot.IsEmpty && slot.Item == item)
            {
                total += slot.Quantity;
            }
        }

        return total;
    }

    /// <summary>
    /// Moves the contents of <paramref name="fromIndex"/> into <paramref name="toIndex"/>.
    /// </summary>
    /// <remarks>
    /// Rules, applied deterministically:
    /// <list type="bullet">
    /// <item>Empty source: nothing happens, returns <c>true</c> (no-op success).</item>
    /// <item>Empty destination: the whole stack moves.</item>
    /// <item>Same item, destination not full: merge up to <see cref="MaxStackSize"/>; any overflow
    /// stays in the source slot.</item>
    /// <item>Different items (or same item but destination full): the two slots swap.</item>
    /// </list>
    /// </remarks>
    /// <returns><c>true</c> when a move/merge/swap occurred or the source was empty.</returns>
    public bool Move(int fromIndex, int toIndex)
    {
        if ((uint)fromIndex >= (uint)_slots.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(fromIndex));
        }

        if ((uint)toIndex >= (uint)_slots.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(toIndex));
        }

        if (fromIndex == toIndex)
        {
            return true;
        }

        InventorySlot source = _slots[fromIndex];
        if (source.IsEmpty)
        {
            return true;
        }

        InventorySlot dest = _slots[toIndex];

        if (dest.IsEmpty)
        {
            _slots[toIndex] = source;
            _slots[fromIndex] = InventorySlot.Empty;
            return true;
        }

        if (dest.Item == source.Item && dest.Quantity < MaxStackSize)
        {
            uint room = MaxStackSize - dest.Quantity;
            uint take = room < source.Quantity ? room : source.Quantity;
            _slots[toIndex] = new InventorySlot(dest.Item, dest.Quantity + take);
            uint left = source.Quantity - take;
            _slots[fromIndex] = left == 0 ? InventorySlot.Empty : new InventorySlot(source.Item, left);
            return true;
        }

        // Swap (different items, or same item with a full destination).
        _slots[fromIndex] = dest;
        _slots[toIndex] = source;
        return true;
    }

    /// <summary>Clears every slot.</summary>
    public void Clear() => Array.Clear(_slots);
}
