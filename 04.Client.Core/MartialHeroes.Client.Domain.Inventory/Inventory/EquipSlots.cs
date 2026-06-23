namespace MartialHeroes.Client.Domain.Inventory.Inventory;

public static class EquipSlots
{
    public const int ItemArraySize = 20;

    public const int VisualAppearanceSlot = 15;

    public const int SpecialWeaponSlot = 14;

    public const int StatExcludedSlot = 8;

    public static bool IsValidSlot(int slot)
    {
        return (uint)slot < ItemArraySize;
    }

    public static bool TriggersVisualRebuild(int slot)
    {
        return slot == VisualAppearanceSlot;
    }

    public static bool IsExcludedFromStatSum(int slot)
    {
        return slot == StatExcludedSlot;
    }
}