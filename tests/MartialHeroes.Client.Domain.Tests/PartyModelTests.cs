using MartialHeroes.Client.Domain.Social;
using Xunit;

namespace MartialHeroes.Client.Domain.Tests;

public sealed class PartyModelTests
{
    [Fact]
    public void MaxMembers_Is8()
    {
        Assert.Equal(8, PartyModel.MaxMembers);
    }

    [Fact]
    public void Form_SeedsLeaderAsFirstMember()
    {
        var party = new PartyModel();

        Assert.True(party.Form(partyId: 99, leaderActorId: 1));
        Assert.Equal(99, party.PartyId);
        Assert.Equal(1u, party.LeaderActorId);
        Assert.Equal(1, party.Count);
        Assert.True(party.Contains(1));
    }

    [Fact]
    public void Form_RejectsWhenAlreadyFormed_OrZeroLeader()
    {
        var party = new PartyModel();
        party.Form(1, 1);

        Assert.False(party.Form(2, 2)); // already formed
        Assert.False(new PartyModel().Form(1, 0)); // zero leader
    }

    [Fact]
    public void Join_AddsMembers_RejectsDuplicateAndOverflow()
    {
        var party = new PartyModel();
        party.Form(1, 1);

        for (uint i = 2; i <= PartyModel.MaxMembers; i++)
        {
            Assert.True(party.Join(i));
        }

        Assert.True(party.IsFull);
        Assert.False(party.Join(99)); // overflow
        Assert.False(party.Join(2)); // duplicate
        Assert.False(party.Join(0)); // zero id
    }

    [Fact]
    public void Leave_RemovesMember_CompactsRoster()
    {
        var party = new PartyModel();
        party.Form(1, 1);
        party.Join(2);
        party.Join(3);

        Assert.True(party.Leave(2));
        Assert.Equal(2, party.Count);
        Assert.False(party.Contains(2));
        Assert.Equal(1u, party.MemberAt(0));
        Assert.Equal(3u, party.MemberAt(1)); // 3 compacted into index 1.
    }

    [Fact]
    public void Leave_Leader_PassesLeadership()
    {
        var party = new PartyModel();
        party.Form(1, leaderActorId: 1);
        party.Join(2);

        Assert.True(party.Leave(1));
        Assert.Equal(2u, party.LeaderActorId); // leadership passes to next member.
    }

    [Fact]
    public void Leave_LastMember_DissolvesParty()
    {
        var party = new PartyModel();
        party.Form(5, 1);

        Assert.True(party.Leave(1));
        Assert.True(party.IsEmpty);
        Assert.Equal(0, party.PartyId);
        Assert.Equal(0u, party.LeaderActorId);
    }

    [Fact]
    public void Leave_NonMember_Rejected()
    {
        var party = new PartyModel();
        party.Form(1, 1);
        Assert.False(party.Leave(999));
    }

    [Fact]
    public void Kick_OnlyLeaderCanKick_NotSelf()
    {
        var party = new PartyModel();
        party.Form(1, leaderActorId: 1);
        party.Join(2);
        party.Join(3);

        Assert.False(party.Kick(byActorId: 2, targetActorId: 3)); // non-leader cannot kick
        Assert.False(party.Kick(byActorId: 1, targetActorId: 1)); // leader cannot kick self
        Assert.True(party.Kick(byActorId: 1, targetActorId: 3)); // leader kicks member
        Assert.False(party.Contains(3));
    }

    [Fact]
    public void Disband_ClearsEverything()
    {
        var party = new PartyModel();
        party.Form(7, 1);
        party.Join(2);

        party.Disband();

        Assert.True(party.IsEmpty);
        Assert.Equal(0, party.PartyId);
        Assert.Equal(0u, party.LeaderActorId);
    }
}