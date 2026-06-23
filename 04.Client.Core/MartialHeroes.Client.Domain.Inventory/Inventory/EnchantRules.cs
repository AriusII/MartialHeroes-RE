namespace MartialHeroes.Client.Domain.Inventory.Inventory;

public enum EnchantOutcome
{
    Failure = 0,

    Success = 1
}

public readonly record struct EnchantResult
{
    public EnchantOutcome Outcome { get; init; }

    public int MotionId { get; init; }

    public int NewEnchantLevel { get; init; }
}

public static class EnchantRules
{
    public const double GaugeCompleteValue = 100.0;

    public const int SuccessMotionId = 8;

    public const int FailMotionId = 9;

    public static bool CanCommit(double gaugeValue, bool canAct, bool notBusy)
    {
        return gaugeValue >= GaugeCompleteValue && canAct && notBusy;
    }

    public static EnchantResult ApplyResult(bool success, int serverEnchantLevel, int currentEnchantLevel)
    {
        return success
            ? new EnchantResult
            {
                Outcome = EnchantOutcome.Success,
                MotionId = SuccessMotionId,
                NewEnchantLevel = serverEnchantLevel
            }
            : new EnchantResult
            {
                Outcome = EnchantOutcome.Failure,
                MotionId = FailMotionId,
                NewEnchantLevel = currentEnchantLevel
            };
    }
}