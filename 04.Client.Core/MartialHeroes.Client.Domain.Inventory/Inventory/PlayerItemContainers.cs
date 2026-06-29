namespace MartialHeroes.Client.Domain.Inventory.Inventory;

public sealed class PlayerItemContainers
{
    public const int EquipSlotCount = 20;

    public const int ExtraSlotCount = 120;

    public const int RefreshClearAllSentinel = 0xFF;

    public PlayerItemContainers(int quickUseSlotCount)
    {
        Equip = new ItemSlotTable(EquipSlotCount);
        Bag = new BagTable();
        Extra = new ItemSlotTable(ExtraSlotCount);
        QuickUse = new QuickUseTable(quickUseSlotCount);
    }

    public ItemSlotTable Equip { get; }

    public BagTable Bag { get; }

    public ItemSlotTable Extra { get; }

    public QuickUseTable QuickUse { get; }

    public int ApplyPanelChunk(ItemPanelChunkType chunkType, int startIndex, ReadOnlySpan<ItemSlotRecord> records)
    {
        return chunkType switch
        {
            ItemPanelChunkType.Equipment => Equip.Apply(startIndex, records),
            ItemPanelChunkType.Inventory => Bag.Apply(startIndex, records),
            _ => 0,
        };
    }

    public int RefreshBagSlots(int startIndex, ReadOnlySpan<ItemSlotRecord> records)
    {
        if (startIndex == RefreshClearAllSentinel)
        {
            Bag.ClearAll();
            return 0;
        }

        return Bag.Apply(startIndex, records);
    }

    public void SyncEquipFromActorBlock(ReadOnlySpan<ItemSlotRecord> playerItemBlock)
    {
        Equip.CopyFrom(playerItemBlock);
    }
}
