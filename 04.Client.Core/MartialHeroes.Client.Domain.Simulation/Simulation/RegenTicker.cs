namespace MartialHeroes.Client.Domain.Simulation.Simulation;

public readonly record struct RegenTicker
{
    public RegenTicker(uint stepIntervalMs, uint amountPerStep, uint accumulatedMs = 0)
    {
        if (stepIntervalMs == 0)
            throw new ArgumentOutOfRangeException(
                nameof(stepIntervalMs), "Regen step interval must be greater than zero.");

        StepIntervalMs = stepIntervalMs;
        AmountPerStep = amountPerStep;
        AccumulatedMs = accumulatedMs % stepIntervalMs == accumulatedMs
            ? accumulatedMs
            : accumulatedMs % stepIntervalMs;
    }

    public uint AccumulatedMs { get; }
    public uint StepIntervalMs { get; }
    public uint AmountPerStep { get; }

    public (RegenTicker Next, uint StepsCompleted) Advance(uint deltaMs)
    {
        var total = (ulong)AccumulatedMs + deltaMs;
        var steps = (uint)(total / StepIntervalMs);
        var remainder = (uint)(total % StepIntervalMs);
        return (new RegenTicker(StepIntervalMs, AmountPerStep, remainder), steps);
    }

    public uint AmountFor(uint stepsCompleted)
    {
        var amount = (ulong)AmountPerStep * stepsCompleted;
        return amount > uint.MaxValue ? uint.MaxValue : (uint)amount;
    }
}