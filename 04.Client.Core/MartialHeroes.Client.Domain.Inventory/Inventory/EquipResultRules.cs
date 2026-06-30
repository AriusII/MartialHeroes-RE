namespace MartialHeroes.Client.Domain.Inventory.Inventory;

public enum EquipResultOutcome
{
    ErrorNotice = 0,

    Apply = 1,

    NoOp = 2
}

public static class EquipResultRules
{
    public static EquipResultOutcome ClassifyExplicit(byte result)
    {
        return result switch
        {
            0 => EquipResultOutcome.ErrorNotice,
            1 => EquipResultOutcome.Apply,
            _ => EquipResultOutcome.NoOp
        };
    }

    public static EquipResultOutcome ClassifyTruthy(byte result)
    {
        return result == 0 ? EquipResultOutcome.ErrorNotice : EquipResultOutcome.Apply;
    }
}
