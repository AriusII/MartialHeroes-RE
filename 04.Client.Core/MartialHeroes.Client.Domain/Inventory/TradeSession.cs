namespace MartialHeroes.Client.Domain.Inventory;

/// <summary>
/// The phase of the player-to-player trade state machine.
/// spec: Docs/RE/specs/inventory_trade.md §8.3 (trade state machine, client view).
/// </summary>
/// <remarks>
/// The spec's diagram is <c>IDLE → REQUESTED → WINDOW_OPEN → (confirm) → FINALIZE → IDLE</c>, with any
/// error / cancel returning to IDLE. We add an explicit <see cref="Locked"/> phase between window-open
/// and finalize to model the "both parties marked trade-busy" (5/106 on) lock before commit; the
/// outbound confirm edge is itself <c>UNVERIFIED</c> (§8.1). spec: inventory_trade.md §8.3 / §11 #2.
/// </remarks>
public enum TradePhase
{
    /// <summary>No trade in progress. spec: inventory_trade.md §8.3 (IDLE).</summary>
    Idle = 0,

    /// <summary>A trade request was sent (2/23); awaiting the 4/23 result. spec: inventory_trade.md §8.3 (REQUESTED).</summary>
    Requested = 1,

    /// <summary>The trade window is open; items can be added / updated (2/24). spec: inventory_trade.md §8.3 (WINDOW_OPEN).</summary>
    WindowOpen = 2,

    /// <summary>Both parties are marked trade-busy (5/106 on); the offer is locked pending confirm. spec: inventory_trade.md §8.2/§8.3.</summary>
    Locked = 3,

    /// <summary>The finalize (4/25 phase == 4) committed; items applied. Transient before returning to idle. spec: inventory_trade.md §8.3 (FINALIZE).</summary>
    Committed = 4,
}

/// <summary>
/// A deterministic player-to-player trade state machine: <c>offer → window → lock → confirm →
/// commit / cancel</c>, with explicit, total transitions and the §8 invariants (40-slot capacity,
/// no edits after lock, cancel from any phase). Pure and engine-free.
/// spec: Docs/RE/specs/inventory_trade.md §8.
/// </summary>
/// <remarks>
/// <para>
/// This is an immutable value type; every transition returns a new state. Each (phase, event) pair is
/// total: it either advances deterministically or is explicitly rejected (the method returns the
/// unchanged state and/or <c>false</c>). Cancel is accepted from any non-idle phase. spec: §8.3.
/// </para>
/// <para>
/// <b>Capacity invariant.</b> The local trade window holds at most <see cref="MaxItems"/> items
/// (the 40-entry trade array / the 4/25 <c>item_count</c> cap). spec: inventory_trade.md §8.2 / §10.
/// </para>
/// </remarks>
public readonly record struct TradeSession
{
    /// <summary>Trade window max items (the 4/25 item_count cap / 40-entry trade array). spec: inventory_trade.md §8.2 / §10 (40 / 0x28).</summary>
    public const int MaxItems = 40;

    /// <summary>The 4/25 finalize phase selector. spec: inventory_trade.md §8.2 / §10 (phase 4 = finalize).</summary>
    public const int FinalizePhase = 4;

    /// <summary>The 4/25 cancel phase selector. spec: inventory_trade.md §8.2 / §10 (phase 0 = cancel).</summary>
    public const int CancelPhase = 0;

    /// <summary>Current trade phase. spec: inventory_trade.md §8.3.</summary>
    public TradePhase Phase { get; init; }

    /// <summary>The other actor's id (the trade partner). 0 when idle. spec: inventory_trade.md §8.1 (target actor id).</summary>
    public uint PartnerActorId { get; init; }

    /// <summary>The number of items in the local half of the trade window (0..<see cref="MaxItems"/>). spec: inventory_trade.md §8.2.</summary>
    public int LocalItemCount { get; init; }

    /// <summary>The idle starting state.</summary>
    public static TradeSession Idle => default;

    /// <summary>True when a new trade may be requested (idle). spec: inventory_trade.md §8.3.</summary>
    public bool IsIdle => Phase == TradePhase.Idle;

    /// <summary>True when slot edits are allowed (window open, not yet locked). spec: inventory_trade.md §8.3.</summary>
    public bool CanEditSlots => Phase == TradePhase.WindowOpen;

    /// <summary>
    /// Sends a trade request to <paramref name="partnerActorId"/> (2/23): <c>idle → requested</c>.
    /// Rejected if not idle or if the partner is the local player (the self-target guard handles that
    /// upstream, but the machine also refuses a zero partner). spec: inventory_trade.md §8.1/§8.3.
    /// </summary>
    /// <returns>The next state and whether the request was accepted.</returns>
    public (TradeSession Next, bool Accepted) Request(uint partnerActorId)
    {
        if (Phase != TradePhase.Idle || partnerActorId == 0)
        {
            return (this, false);
        }

        return (new TradeSession { Phase = TradePhase.Requested, PartnerActorId = partnerActorId, LocalItemCount = 0 },
            true);
    }

    /// <summary>
    /// Applies the request result (4/23): <c>result == 1</c> opens the window (<c>requested →
    /// window-open</c>); any other result cancels back to idle. spec: inventory_trade.md §8.2/§8.3.
    /// </summary>
    public TradeSession OnRequestResult(bool accepted)
    {
        if (Phase != TradePhase.Requested)
        {
            return this; // total: ignore an out-of-phase result.
        }

        return accepted ? this with { Phase = TradePhase.WindowOpen } : Idle;
    }

    /// <summary>
    /// Adds an item to the local half of the window (2/24): only while the window is open and below the
    /// 40-item cap. spec: Docs/RE/specs/inventory_trade.md §8.1 / §8.2.
    /// </summary>
    /// <returns>The next state and whether the add was accepted.</returns>
    public (TradeSession Next, bool Accepted) AddLocalItem()
    {
        if (Phase != TradePhase.WindowOpen || LocalItemCount >= MaxItems)
        {
            return (this, false);
        }

        return (this with { LocalItemCount = LocalItemCount + 1 }, true);
    }

    /// <summary>
    /// Removes one item from the local half (only while editable and non-empty). spec: inventory_trade.md §8.2.
    /// </summary>
    public (TradeSession Next, bool Accepted) RemoveLocalItem()
    {
        if (Phase != TradePhase.WindowOpen || LocalItemCount <= 0)
        {
            return (this, false);
        }

        return (this with { LocalItemCount = LocalItemCount - 1 }, true);
    }

    /// <summary>
    /// Marks both parties trade-busy (5/106 on): <c>window-open → locked</c>. Once locked, slot edits
    /// are no longer accepted. spec: Docs/RE/specs/inventory_trade.md §8.2/§8.3.
    /// </summary>
    public TradeSession Lock() => Phase == TradePhase.WindowOpen ? this with { Phase = TradePhase.Locked } : this;

    /// <summary>
    /// Applies the finalize message (4/25): <c>phase == 4</c> commits (window-open or locked →
    /// committed); <c>phase == 0</c> cancels back to idle. spec: inventory_trade.md §8.2/§8.3.
    /// </summary>
    /// <returns>The next state and whether the finalize committed the trade.</returns>
    public (TradeSession Next, bool Committed) OnFinalize(int finalizePhase)
    {
        // Finalize is only meaningful once the window is open (or locked). spec: §8.3.
        if (Phase != TradePhase.WindowOpen && Phase != TradePhase.Locked)
        {
            return (this, false);
        }

        if (finalizePhase == FinalizePhase)
        {
            return (this with { Phase = TradePhase.Committed }, true);
        }

        // Cancel phase (0) or any other value → cancel / close → idle. spec: §8.3.
        return (Idle, false);
    }

    /// <summary>
    /// Acknowledges a committed trade and returns to idle (the window closes after items are applied).
    /// Total: a no-op unless committed. spec: inventory_trade.md §8.3 ("apply items, close → IDLE").
    /// </summary>
    public TradeSession Close() => Phase == TradePhase.Committed ? Idle : this;

    /// <summary>
    /// Cancels the trade from any non-idle phase (4/23 error, 4/24 error, 4/25 phase == 0, or 5/106 off):
    /// returns to idle. Total: idle → idle. spec: Docs/RE/specs/inventory_trade.md §8.3.
    /// </summary>
    public TradeSession Cancel() => Idle;
}