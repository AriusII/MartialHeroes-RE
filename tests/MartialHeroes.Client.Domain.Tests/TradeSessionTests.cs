using MartialHeroes.Client.Domain.Inventory;
using Xunit;

namespace MartialHeroes.Client.Domain.Tests;

public sealed class TradeSessionTests
{
    [Fact]
    public void Constants_MatchSpec()
    {
        Assert.Equal(40, TradeSession.MaxItems);
        Assert.Equal(4, TradeSession.FinalizePhase);
        Assert.Equal(0, TradeSession.CancelPhase);
    }

    [Fact]
    public void HappyPath_Request_To_Commit()
    {
        var session = TradeSession.Idle;

        var (requested, accepted) = session.Request(partnerActorId: 555);
        Assert.True(accepted);
        Assert.Equal(TradePhase.Requested, requested.Phase);
        Assert.Equal(555u, requested.PartnerActorId);

        TradeSession window = requested.OnRequestResult(accepted: true);
        Assert.Equal(TradePhase.WindowOpen, window.Phase);

        (window, _) = window.AddLocalItem();
        (window, _) = window.AddLocalItem();
        Assert.Equal(2, window.LocalItemCount);

        TradeSession locked = window.Lock();
        Assert.Equal(TradePhase.Locked, locked.Phase);

        var (committed, didCommit) = locked.OnFinalize(TradeSession.FinalizePhase);
        Assert.True(didCommit);
        Assert.Equal(TradePhase.Committed, committed.Phase);

        Assert.Equal(TradePhase.Idle, committed.Close().Phase);
    }

    [Fact]
    public void Request_RejectedWhenNotIdle_OrZeroPartner()
    {
        var (req, _) = TradeSession.Idle.Request(1);
        Assert.False(req.Request(2).Accepted); // already requested
        Assert.False(TradeSession.Idle.Request(0).Accepted); // zero partner
    }

    [Fact]
    public void OnRequestResult_Declined_ReturnsToIdle()
    {
        var (req, _) = TradeSession.Idle.Request(7);
        Assert.Equal(TradePhase.Idle, req.OnRequestResult(accepted: false).Phase);
    }

    [Fact]
    public void AddLocalItem_OnlyWhenWindowOpen_AndUnderCap()
    {
        var (req, _) = TradeSession.Idle.Request(7);
        Assert.False(req.AddLocalItem().Accepted); // not window-open yet

        TradeSession window = req.OnRequestResult(true);
        for (int i = 0; i < TradeSession.MaxItems; i++)
        {
            (window, bool ok) = window.AddLocalItem();
            Assert.True(ok);
        }

        Assert.Equal(TradeSession.MaxItems, window.LocalItemCount);
        Assert.False(window.AddLocalItem().Accepted); // at cap
    }

    [Fact]
    public void Locked_RejectsSlotEdits()
    {
        var (req, _) = TradeSession.Idle.Request(7);
        TradeSession locked = req.OnRequestResult(true).Lock();

        Assert.False(locked.CanEditSlots);
        Assert.False(locked.AddLocalItem().Accepted);
        Assert.False(locked.RemoveLocalItem().Accepted);
    }

    [Fact]
    public void OnFinalize_CancelPhase_ReturnsToIdle()
    {
        var (req, _) = TradeSession.Idle.Request(7);
        TradeSession window = req.OnRequestResult(true);

        var (next, committed) = window.OnFinalize(TradeSession.CancelPhase);

        Assert.False(committed);
        Assert.Equal(TradePhase.Idle, next.Phase);
    }

    [Fact]
    public void Cancel_FromAnyPhase_ReturnsToIdle()
    {
        var (req, _) = TradeSession.Idle.Request(7);
        TradeSession window = req.OnRequestResult(true);

        Assert.Equal(TradePhase.Idle, req.Cancel().Phase);
        Assert.Equal(TradePhase.Idle, window.Cancel().Phase);
        Assert.Equal(TradePhase.Idle, window.Lock().Cancel().Phase);
    }

    [Fact]
    public void OnFinalize_OutOfPhase_NoCommit()
    {
        var (next, committed) = TradeSession.Idle.OnFinalize(TradeSession.FinalizePhase);
        Assert.False(committed);
        Assert.Equal(TradePhase.Idle, next.Phase);
    }
}