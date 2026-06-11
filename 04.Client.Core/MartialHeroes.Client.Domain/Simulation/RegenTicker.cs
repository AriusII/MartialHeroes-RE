namespace MartialHeroes.Client.Domain.Simulation;

/// <summary>
/// Deterministic, frame-rate-independent resource regeneration accumulator.
/// </summary>
/// <remarks>
/// <para>
/// Regeneration is defined as a flat <see cref="AmountPerStep"/> applied once every
/// <see cref="StepIntervalMs"/> of accumulated elapsed time. Elapsed time is supplied explicitly
/// by the caller (<c>deltaMs</c>); this type owns no clock. The accumulator carries the unspent
/// remainder forward so that, for the same total elapsed time and step size, the number of steps
/// applied is identical regardless of how the elapsed time was chunked: one 100&#160;ms advance
/// yields the same result as ten 10&#160;ms advances. spec: roadmap Phase 4.1 (HP/MP regen every
/// X&#160;ms, frame-rate independent). The step interval and amount are simulation parameters
/// supplied by the caller, not original-game constants.
/// </para>
/// <para>
/// This is a <see cref="readonly"/> value type: <see cref="Advance"/> returns the updated ticker
/// rather than mutating in place, keeping every call a pure function of its inputs.
/// </para>
/// </remarks>
public readonly record struct RegenTicker
{
    /// <summary>Milliseconds of accumulated elapsed time not yet converted into steps.</summary>
    public uint AccumulatedMs { get; }

    /// <summary>Milliseconds between successive regen steps. Must be &gt; 0.</summary>
    public uint StepIntervalMs { get; }

    /// <summary>Resource amount applied per completed step.</summary>
    public uint AmountPerStep { get; }

    /// <summary>
    /// Creates a regen ticker.
    /// </summary>
    /// <param name="stepIntervalMs">Milliseconds between regen steps; must be &gt; 0.</param>
    /// <param name="amountPerStep">Resource amount applied per completed step.</param>
    /// <param name="accumulatedMs">Initial carried-over elapsed time (defaults to 0).</param>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="stepIntervalMs"/> is 0.</exception>
    public RegenTicker(uint stepIntervalMs, uint amountPerStep, uint accumulatedMs = 0)
    {
        if (stepIntervalMs == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stepIntervalMs), "Regen step interval must be greater than zero.");
        }

        StepIntervalMs = stepIntervalMs;
        AmountPerStep = amountPerStep;
        AccumulatedMs = accumulatedMs % stepIntervalMs == accumulatedMs
            ? accumulatedMs
            : accumulatedMs % stepIntervalMs;
    }

    /// <summary>
    /// Advances the accumulator by <paramref name="deltaMs"/> and reports how many whole regen
    /// steps completed.
    /// </summary>
    /// <param name="deltaMs">Elapsed time to add, in milliseconds (caller-supplied).</param>
    /// <returns>
    /// The updated ticker (with the unspent remainder carried forward) and the number of completed
    /// steps. Multiply <paramref name="amountPerStep"/> caps are applied by the caller against the
    /// resource maximum.
    /// </returns>
    public (RegenTicker Next, uint StepsCompleted) Advance(uint deltaMs)
    {
        // Use ulong to keep the addition exact even for large accumulated/delta values.
        ulong total = (ulong)AccumulatedMs + deltaMs;
        uint steps = (uint)(total / StepIntervalMs);
        uint remainder = (uint)(total % StepIntervalMs);
        return (new RegenTicker(StepIntervalMs, AmountPerStep, remainder), steps);
    }

    /// <summary>
    /// Total resource amount produced by <paramref name="stepsCompleted"/> steps, saturating at
    /// <see cref="uint.MaxValue"/>.
    /// </summary>
    public uint AmountFor(uint stepsCompleted)
    {
        ulong amount = (ulong)AmountPerStep * stepsCompleted;
        return amount > uint.MaxValue ? uint.MaxValue : (uint)amount;
    }
}
