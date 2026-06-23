namespace MartialHeroes.Client.Domain.Inventory.Inventory;

public static class EquipSlots
{
    public const int StatExcludedSlot = 8;

    public static bool IsExcludedFromStatSum(int slot)
    {
        return slot == StatExcludedSlot;
    }
}