namespace MartialHeroes.Client.Application.Hud;

/// <summary>
/// A read-only snapshot of the stat-point allocation editor the character / stat window binds to:
/// the five base stats, the pending per-stat deltas the player has staged with the <c>+</c>/<c>-</c>
/// widgets, and the points still available. Immutable — the HUD renders it; the allocation use case
/// owns and mutates the underlying state and publishes a fresh snapshot on every change.
/// </summary>
/// <remarks>
/// The editor models a pending allocation entirely on the client; nothing is sent until "Apply"
/// builds the 2/29 commit. Each stat carries a pending delta; <see cref="PointsAvailable"/> is
/// recomputed as <c>remaining_stat_points − Σ deltas</c>, and each row is shown as
/// <c>"&lt;base&gt;+&lt;pending&gt;"</c> (or just <c>"&lt;base&gt;"</c> when the delta is 0).
/// spec: Docs/RE/specs/progression.md §2 (the window's <c>delta_*</c> / <c>avail_points</c> fields),
/// §7.1 (the pending-delta model: <c>+</c> adds the step and subtracts from available points;
/// available = remaining − Σ deltas), §8.1 (Apply commits five ABSOLUTE stats = base + delta in wire
/// order STR, INT, AGI, DEX, CON). This view orders its members STR, INT, AGI, DEX, CON to match the
/// commit wire order; do not confuse it with the editor's action-id order STR, INT, AGI, CON, DEX
/// (§7.2). Future producer: the stat-allocation use case, refreshed by the 4/29 ack / 5/67 resync.
/// </remarks>
/// <param name="BaseStr">Cached absolute base STR. spec: Docs/RE/specs/progression.md §2.</param>
/// <param name="BaseInt">Cached absolute base INT.</param>
/// <param name="BaseAgi">Cached absolute base AGI.</param>
/// <param name="BaseDex">Cached absolute base DEX.</param>
/// <param name="BaseCon">Cached absolute base CON.</param>
/// <param name="DeltaStr">Pending STR allocation (>= 0). spec: Docs/RE/specs/progression.md §7.1.</param>
/// <param name="DeltaInt">Pending INT allocation (>= 0).</param>
/// <param name="DeltaAgi">Pending AGI allocation (>= 0).</param>
/// <param name="DeltaDex">Pending DEX allocation (>= 0).</param>
/// <param name="DeltaCon">Pending CON allocation (>= 0).</param>
/// <param name="RemainingStatPoints">
/// Authoritative count of unspent points before staging (the server-supplied pool).
/// spec: Docs/RE/specs/progression.md §2 (remaining_stat_points), §8.2 (refreshed by the 4/29 ack).
/// </param>
public sealed record StatAllocationView(
    uint BaseStr,
    uint BaseInt,
    uint BaseAgi,
    uint BaseDex,
    uint BaseCon,
    uint DeltaStr,
    uint DeltaInt,
    uint DeltaAgi,
    uint DeltaDex,
    uint DeltaCon,
    uint RemainingStatPoints) : IHudEvent
{
    /// <summary>An empty view: zero stats, zero deltas, zero points. A safe HUD default before sync.</summary>
    public static StatAllocationView Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>Sum of the five pending deltas. spec: Docs/RE/specs/progression.md §7.1.</summary>
    public long PendingTotal => (long)DeltaStr + DeltaInt + DeltaAgi + DeltaDex + DeltaCon;

    /// <summary>
    /// Points still allocatable: <c>remaining_stat_points − Σ deltas</c>, floored at 0.
    /// spec: Docs/RE/specs/progression.md §7.1 (available = remaining − Σ deltas).
    /// </summary>
    public long PointsAvailable => Math.Max(0, RemainingStatPoints - PendingTotal);

    /// <summary>True when at least one delta is non-zero (the Apply gate's second condition). spec: Docs/RE/specs/progression.md §8.1.</summary>
    public bool HasPendingAllocation => PendingTotal > 0;

    /// <summary>Absolute STR the 2/29 commit would send (base + pending). spec: Docs/RE/specs/progression.md §8.1.</summary>
    public uint AbsoluteStr => BaseStr + DeltaStr;

    /// <summary>Absolute INT the 2/29 commit would send (base + pending). spec: Docs/RE/specs/progression.md §8.1.</summary>
    public uint AbsoluteInt => BaseInt + DeltaInt;

    /// <summary>Absolute AGI the 2/29 commit would send (base + pending). spec: Docs/RE/specs/progression.md §8.1.</summary>
    public uint AbsoluteAgi => BaseAgi + DeltaAgi;

    /// <summary>Absolute DEX the 2/29 commit would send (base + pending). spec: Docs/RE/specs/progression.md §8.1.</summary>
    public uint AbsoluteDex => BaseDex + DeltaDex;

    /// <summary>Absolute CON the 2/29 commit would send (base + pending). spec: Docs/RE/specs/progression.md §8.1.</summary>
    public uint AbsoluteCon => BaseCon + DeltaCon;
}