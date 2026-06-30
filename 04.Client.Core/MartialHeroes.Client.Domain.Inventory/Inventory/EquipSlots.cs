namespace MartialHeroes.Client.Domain.Inventory.Inventory;

public static class EquipSlots
{
    public const int StatExcludedSlot = 8;

    public const int WeaponSlot = 14;

    public const int AppearanceSlot = 15;

    public static bool IsExcludedFromStatSum(int slot)
    {
        return slot == StatExcludedSlot;
    }

    public static bool TriggersVisualRebuild(int slot)
    {
        return slot == AppearanceSlot;
    }
}