namespace MartialHeroes.Client.Domain.Stats;

/// <summary>
/// The five <b>effective</b> primary stats consumed by the vital formula: the final value of each
/// stat after base-from-level, equipment, set bonuses and buffs are folded in.
/// spec: Docs/RE/structs/stats.md ("Primary stats (effective values)").
/// </summary>
/// <remarks>
/// These are already-resolved inputs. The spec notes the assembly of each effective stat
/// (base-from-level + equipment flat + set bonus + buff + global addend), but the formula here
/// treats them as resolved integers.
/// </remarks>
public readonly record struct PrimaryStats(int Str, int Dex, int Agi, int Con, int Int)
{
    /// <summary>All-zero primary stats.</summary>
    public static readonly PrimaryStats Zero = new(0, 0, 0, 0, 0);
}