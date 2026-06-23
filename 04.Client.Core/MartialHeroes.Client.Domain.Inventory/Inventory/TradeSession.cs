namespace MartialHeroes.Client.Domain.Inventory.Inventory;

public enum TradePhase
{
    Idle = 0,

    Requested = 1,

    WindowOpen = 2,

    Locked = 3,

    Committed = 4
}

public readonly record struct TradeSession
{
    public const int MaxItems = 40;

    public const int FinalizePhase = 4;

    public const int CancelPhase = 0;

    public TradePhase Phase { get; init; }

    public uint PartnerActorId { get; init; }

    public int LocalItemCount { get; init; }

    public static TradeSession Idle => default;

    public bool IsIdle => Phase == TradePhase.Idle;

    public bool CanEditSlots => Phase == TradePhase.WindowOpen;

    public (TradeSession Next, bool Accepted) Request(uint partnerActorId)
    {
        if (Phase != TradePhase.Idle || partnerActorId == 0) return (this, false);

        return (new TradeSession { Phase = TradePhase.Requested, PartnerActorId = partnerActorId, LocalItemCount = 0 },
            true);
    }

    public TradeSession OnRequestResult(bool accepted)
    {
        if (Phase != TradePhase.Requested) return this;

        return accepted ? this with { Phase = TradePhase.WindowOpen } : Idle;
    }

    public (TradeSession Next, bool Accepted) AddLocalItem()
    {
        if (Phase != TradePhase.WindowOpen || LocalItemCount >= MaxItems) return (this, false);

        return (this with { LocalItemCount = LocalItemCount + 1 }, true);
    }

    public (TradeSession Next, bool Accepted) RemoveLocalItem()
    {
        if (Phase != TradePhase.WindowOpen || LocalItemCount <= 0) return (this, false);

        return (this with { LocalItemCount = LocalItemCount - 1 }, true);
    }

    public TradeSession Lock()
    {
        return Phase == TradePhase.WindowOpen ? this with { Phase = TradePhase.Locked } : this;
    }

    public (TradeSession Next, bool Committed) OnFinalize(int finalizePhase)
    {
        if (Phase != TradePhase.WindowOpen && Phase != TradePhase.Locked) return (this, false);

        if (finalizePhase == FinalizePhase) return (this with { Phase = TradePhase.Committed }, true);

        return (Idle, false);
    }

    public TradeSession Close()
    {
        return Phase == TradePhase.Committed ? Idle : this;
    }

    public TradeSession Cancel()
    {
        return Idle;
    }
}