namespace MartialHeroes.Client.Domain.Inventory;

/// <summary>
/// The outcome of an item upgrade / enchant commit as reported by the server (4/50).
/// spec: Docs/RE/specs/inventory_trade.md §6.2.
/// </summary>
public enum EnchantOutcome
{
    /// <summary>Failure (success byte == 0): plays the fail / shatter motion. spec: inventory_trade.md §6.2 (motion 9).</summary>
    Failure = 0,

    /// <summary>Success (success byte != 0): plays the success motion, applies the new +N. spec: inventory_trade.md §6.2 (motion 8).</summary>
    Success = 1,
}

/// <summary>
/// The result of applying a 4/50 upgrade verdict: which motion to play and the resulting enchant level.
/// spec: Docs/RE/specs/inventory_trade.md §6.2 / §6.3.
/// </summary>
/// <remarks>
/// The <see cref="NewEnchantLevel"/> is <b>server data</b>, carried in the 32-byte 4/50 body; its exact
/// offset is <c>UNVERIFIED</c> (§6.2 / §11 #7). It is passed through here, never rolled by the client —
/// there is no client-side enchant-roll formula (§6.3). spec: inventory_trade.md §6.2/§6.3.
/// </remarks>
public readonly record struct EnchantResult
{
    /// <summary>The outcome reported by the server. spec: inventory_trade.md §6.2.</summary>
    public EnchantOutcome Outcome { get; init; }

    /// <summary>The motion id to play (8 success / 9 fail). spec: inventory_trade.md §6.2 / §10.</summary>
    public int MotionId { get; init; }

    /// <summary>The resulting +N enchant level (server data; only meaningful on success). spec: inventory_trade.md §6.2/§6.3.</summary>
    public int NewEnchantLevel { get; init; }
}

/// <summary>
/// Pure item upgrade / enchant rules: the timed-channel commit gate (channel a gauge to 100 %, then
/// commit while can-act / not-busy) and the result application (motion id + server-supplied +N).
/// spec: Docs/RE/specs/inventory_trade.md §6.
/// </summary>
/// <remarks>
/// <para>
/// <b>The +N enchant level is server data, not a client formula.</b> The client channels a progress
/// gauge to 100.0, sends the commit (2/50), and on the 4/50 reply only <em>displays</em> the result
/// and plays the success / fail animation — there is no client-side enchant-roll. So this class models
/// the commit <em>gate</em> and the result <em>mapping</em>, never a roll. spec: inventory_trade.md §6.1/§6.3.
/// </para>
/// <para>
/// The motion ids and the gauge-complete threshold are the recovered §6 constants; the success / fail
/// probabilities and the +N magnitude are server data and are never invented here. spec: inventory_trade.md §6.2/§10.
/// </para>
/// </remarks>
public static class EnchantRules
{
    /// <summary>The gauge value at which the commit is sent. spec: inventory_trade.md §6.1 ("When the gauge reaches 100.0").</summary>
    public const double GaugeCompleteValue = 100.0;

    /// <summary>The success motion id played on a successful upgrade. spec: inventory_trade.md §6.2 / §10 (motion 8 = success).</summary>
    public const int SuccessMotionId = 8;

    /// <summary>The fail / shatter motion id played on a failed upgrade. spec: inventory_trade.md §6.2 / §10 (motion 9 = fail).</summary>
    public const int FailMotionId = 9;

    /// <summary>
    /// True when the upgrade commit (2/50) may be sent: the channel gauge has reached 100.0 and the
    /// can-act / not-busy predicates pass. spec: Docs/RE/specs/inventory_trade.md §6.1.
    /// </summary>
    /// <param name="gaugeValue">The current progress-gauge value (counts up over time).</param>
    /// <param name="canAct">The can-act predicate. spec: §6.1.</param>
    /// <param name="notBusy">The not-busy predicate. spec: §6.1.</param>
    public static bool CanCommit(double gaugeValue, bool canAct, bool notBusy) =>
        gaugeValue >= GaugeCompleteValue && canAct && notBusy;

    /// <summary>
    /// Maps a 4/50 upgrade verdict into an <see cref="EnchantResult"/>: success plays motion 8 and
    /// adopts the server-supplied <paramref name="serverEnchantLevel"/>; failure plays motion 9 and
    /// leaves the level unchanged. The client never rolls the outcome. spec: inventory_trade.md §6.2/§6.3.
    /// </summary>
    /// <param name="success">The server success byte (non-zero = success). spec: §6.2.</param>
    /// <param name="serverEnchantLevel">The resulting +N from the server item table (server data). spec: §6.3.</param>
    /// <param name="currentEnchantLevel">The item's current +N (kept on failure). spec: §6.3.</param>
    public static EnchantResult ApplyResult(bool success, int serverEnchantLevel, int currentEnchantLevel)
    {
        return success
            ? new EnchantResult
            {
                Outcome = EnchantOutcome.Success,
                MotionId = SuccessMotionId,
                NewEnchantLevel = serverEnchantLevel,
            }
            : new EnchantResult
            {
                Outcome = EnchantOutcome.Failure,
                MotionId = FailMotionId,
                NewEnchantLevel = currentEnchantLevel,
            };
    }
}