using MartialHeroes.Shared.Kernel.Ids;

namespace MartialHeroes.Client.Domain.Inventory;

/// <summary>
/// A single inventory cell: either empty, or holding a stack of one item kind.
/// </summary>
/// <remarks>
/// A slot is empty when <see cref="Item"/> is <see cref="ItemId.None"/> or <see cref="Quantity"/>
/// is 0; the two are kept consistent by the constructor. This is a value type, so an
/// <see cref="InventoryGrid"/> can store slots inline with no per-cell heap allocation.
/// </remarks>
public readonly record struct InventorySlot
{
    /// <summary>An empty slot.</summary>
    public static readonly InventorySlot Empty = default;

    /// <summary>The item held in this slot, or <see cref="ItemId.None"/> when empty.</summary>
    public ItemId Item { get; }

    /// <summary>The stacked quantity; 0 when empty.</summary>
    public uint Quantity { get; }

    /// <summary>Creates a slot, normalising empty representations.</summary>
    public InventorySlot(ItemId item, uint quantity)
    {
        if (item == ItemId.None || quantity == 0)
        {
            Item = ItemId.None;
            Quantity = 0;
        }
        else
        {
            Item = item;
            Quantity = quantity;
        }
    }

    /// <summary>True when the slot holds no item.</summary>
    public bool IsEmpty => Item == ItemId.None || Quantity == 0;
}