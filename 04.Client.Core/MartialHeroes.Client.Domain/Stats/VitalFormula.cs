namespace MartialHeroes.Client.Domain.Stats;

/// <summary>
/// The recovered maximum-HP / maximum-MP formula for the local player, as a pure deterministic
/// function of explicit inputs. spec: Docs/RE/structs/stats.md.
/// </summary>
/// <remarks>
/// <para>
/// Three-stage pipeline shared by HP and MP (spec: stats.md "Three-stage pipeline"):
/// <list type="number">
/// <item>Stage 1 — stat-weighted floating-point score with a +30.0 constant.</item>
/// <item>Stage 2 — integer flat base: <c>floor(score)</c> + equipment flat + set bonus + level base
///   + server base.</item>
/// <item>Stage 3 — percentage multiplier, then <c>floor(base * pct_mult)</c>.</item>
/// </list>
/// <c>floor</c> (truncation toward zero of a non-negative quantity) is applied at <b>exactly two
/// points</b>: the end of Stage 2 and the end of Stage 3 (spec: stats.md "floor is applied ...
/// exactly at those two points, not anywhere else").
/// </para>
/// <para>
/// <b>Rounding choice (ours, made explicit per the spec).</b> The spec describes a float-to-integer
/// truncation toward zero of a non-negative quantity. We compute the float stages in
/// <see cref="double"/> and truncate with <see cref="Math.Floor(double)"/>, which for the
/// non-negative values produced here equals truncation toward zero. Stat weights are stored as
/// <see cref="float"/> literals and widened to <see cref="double"/> for the accumulation, matching
/// the spec's bit-exact-parity guidance (spec: stats.md "store the weights as float and widen to
/// double for the accumulation").
/// </para>
/// <para>
/// <b>Provisional / UNVERIFIED inputs.</b> Per the spec, <c>level_base</c> and <c>server_base</c>
/// (for both HP and MP) come from server / catalog data we do not have; they are injected inputs
/// that default to <c>0</c>. Any max computed with the defaults is structurally correct but
/// numerically incomplete (provisional). The class-id → <c>CharacterClass</c> mapping and the
/// magnitudes of the extra equip HP/MP slots are likewise UNVERIFIED (see
/// <see cref="VitalFormulaInputs"/> and <see cref="ClassHpTable"/>).
/// </para>
/// </remarks>
public static class VitalFormula
{
    // -------------------------------------------------------------------------
    // Stage 1 stat weights (spec: stats.md "Stage 1 — stat weights").
    // Stored as float literals, widened to double for accumulation, per the spec.
    // -------------------------------------------------------------------------

    private const float HpWeightStr = 2.2f; // spec: Docs/RE/structs/stats.md (Max HP weights)
    private const float HpWeightDex = 2.5f; // spec: Docs/RE/structs/stats.md
    private const float HpWeightAgi = 2.4f; // spec: Docs/RE/structs/stats.md
    private const float HpWeightCon = 1.5f; // spec: Docs/RE/structs/stats.md
    private const float HpWeightInt = 1.6f; // spec: Docs/RE/structs/stats.md

    private const float MpWeightStr = 1.4f; // spec: Docs/RE/structs/stats.md (Max MP weights)
    private const float MpWeightDex = 1.5f; // spec: Docs/RE/structs/stats.md
    private const float MpWeightAgi = 1.7f; // spec: Docs/RE/structs/stats.md
    private const float MpWeightCon = 1.5f; // spec: Docs/RE/structs/stats.md
    private const float MpWeightInt = 3.5f; // spec: Docs/RE/structs/stats.md

    private const double ScoreConstant = 30.0; // spec: Docs/RE/structs/stats.md (+30.0 constant)

    /// <summary>HP aura buff-kind discriminator. spec: stats.md (HP aura = 1).</summary>
    public const byte HpAuraKind = 1;

    /// <summary>MP aura buff-kind discriminator. spec: stats.md (MP aura = 2).</summary>
    public const byte MpAuraKind = 2;

    /// <summary>
    /// Computes the resolved maximum HP and MP from <paramref name="inputs"/>, following the
    /// recovered three-stage pipeline. Pure and deterministic. spec: Docs/RE/structs/stats.md.
    /// </summary>
    public static (long MaxHp, long MaxMp) Compute(in VitalFormulaInputs inputs)
    {
        return (ComputeMaxHp(in inputs), ComputeMaxMp(in inputs));
    }

    /// <summary>
    /// Computes maximum HP only. spec: Docs/RE/structs/stats.md ("Max HP").
    /// </summary>
    public static long ComputeMaxHp(in VitalFormulaInputs inputs)
    {
        PrimaryStats s = inputs.Stats;

        // Stage 1 — stat-weighted score (double accumulation of float weights).
        double score =
            s.Str * (double)HpWeightStr +
            s.Dex * (double)HpWeightDex +
            s.Agi * (double)HpWeightAgi +
            s.Con * (double)HpWeightCon +
            s.Int * (double)HpWeightInt +
            ScoreConstant;

        // Stage 2 — integer flat base. floor() at the Stage-2 boundary (point 1 of 2).
        long baseHp =
            (long)Math.Floor(score) +
            inputs.EquipmentHpFlat +
            (inputs.IsSetComplete ? inputs.SetBonusHp : 0) +
            inputs.ResolveLevelBaseHp() + // curve(Level) + flat override; spec: config_tables.md §2.4
            inputs.ServerBaseHp;

        // Stage 3 — class multiplier + optional %HP buff slot + HP auras.
        double pctMult = ClassHpTable.MultiplierFor(inputs.ClassId);
        pctMult += inputs.HpPercentBuffPercent / 100.0;
        pctMult += SumAuraPercent(inputs.Aura0, inputs.Aura1, HpAuraKind);

        // floor() at the Stage-3 boundary (point 2 of 2).
        return (long)Math.Floor(baseHp * pctMult);
    }

    /// <summary>
    /// Computes maximum MP only. spec: Docs/RE/structs/stats.md ("Max MP").
    /// </summary>
    public static long ComputeMaxMp(in VitalFormulaInputs inputs)
    {
        PrimaryStats s = inputs.Stats;

        // Stage 1 — stat-weighted score.
        double score =
            s.Str * (double)MpWeightStr +
            s.Dex * (double)MpWeightDex +
            s.Agi * (double)MpWeightAgi +
            s.Con * (double)MpWeightCon +
            s.Int * (double)MpWeightInt +
            ScoreConstant;

        // Stage 2 — integer flat base. floor() at the Stage-2 boundary (point 1 of 2).
        long baseMp =
            (long)Math.Floor(score) +
            inputs.EquipmentMpFlat +
            (inputs.IsSetComplete ? inputs.SetBonusMp : 0) +
            inputs.ResolveLevelBaseMp() + // curve(Level) + flat override; spec: config_tables.md §2.4
            inputs.ServerBaseMp;

        // Stage 3 — MP has no class table; multiplier starts at 1.0 and only auras adjust it.
        // spec: stats.md ("The multiplier starts at 1.0 (100%) and only auras adjust it").
        double pctMult = 1.0 + SumAuraPercent(inputs.Aura0, inputs.Aura1, MpAuraKind);

        // floor() at the Stage-3 boundary (point 2 of 2).
        return (long)Math.Floor(baseMp * pctMult);
    }

    /// <summary>
    /// Sums the percentage contribution of the (up to two) active auras whose buff-kind matches
    /// <paramref name="kind"/>. Each qualifying aura contributes <c>aura_percent / 100.0</c>.
    /// spec: Docs/RE/structs/stats.md ("Aura terms").
    /// </summary>
    private static double SumAuraPercent(in AuraTerm a0, in AuraTerm a1, byte kind)
    {
        double sum = 0.0;
        if (a0.IsActive && a0.Kind == kind)
        {
            sum += a0.PercentValue / 100.0;
        }

        if (a1.IsActive && a1.Kind == kind)
        {
            sum += a1.PercentValue / 100.0;
        }

        return sum;
    }
}