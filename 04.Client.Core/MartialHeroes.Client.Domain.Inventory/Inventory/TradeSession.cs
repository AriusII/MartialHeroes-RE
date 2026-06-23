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
    public TradePhase Phase { get; init; }
    public uint PartnerActorId { get; init; }
    public int LocalItemCount { get; init; }

    public (TradeSession Next, bool Accepted) Request(uint partnerActorId)
    {
        if (Phase != TradePhase.Idle || partnerActorId == 0) return (this, false);

        return (new TradeSession { Phase = TradePhase.Requested, PartnerActorId = partnerActorId, LocalItemCount = 0 },
            true);
    }
}