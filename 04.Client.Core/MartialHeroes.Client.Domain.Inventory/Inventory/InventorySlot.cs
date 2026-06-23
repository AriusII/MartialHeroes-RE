using MartialHeroes.Shared.Kernel.Ids;

namespace MartialHeroes.Client.Domain.Inventory.Inventory;

public readonly record struct InventorySlot
{
    public static readonly InventorySlot Empty = default;

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

    public ItemId Item { get; }

    public uint Quantity { get; }

    public bool IsEmpty => Item == ItemId.None || Quantity == 0;
}