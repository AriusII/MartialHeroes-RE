namespace MartialHeroes.Client.Domain.Stats;

/// <summary>
/// One of the (up to two) active aura slots the local player tracks for vital percentage bonuses.
/// spec: Docs/RE/structs/stats.md ("Aura terms").
/// </summary>
/// <remarks>
/// An aura contributes to the HP or MP multiplier only when it is active and its
/// <see cref="Kind"/> matches the discriminator for that quantity (HP aura = 1, MP aura = 2 —
/// spec: stats.md). The contribution is <c>PercentValue / 100.0</c>.
/// </remarks>
public readonly record struct AuraTerm(bool IsActive, byte Kind, int PercentValue)
{
    /// <summary>An inactive aura slot (contributes nothing).</summary>
    public static readonly AuraTerm None = new(false, 0, 0);

    /// <summary>Creates an active HP aura granting <paramref name="percentValue"/>% (spec: HP aura kind = 1).</summary>
    public static AuraTerm Hp(int percentValue) => new(true, VitalFormula.HpAuraKind, percentValue);

    /// <summary>Creates an active MP aura granting <paramref name="percentValue"/>% (spec: MP aura kind = 2).</summary>
    public static AuraTerm Mp(int percentValue) => new(true, VitalFormula.MpAuraKind, percentValue);
}