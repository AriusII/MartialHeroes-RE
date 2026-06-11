using MartialHeroes.Client.Domain.Social;
using Xunit;

namespace MartialHeroes.Client.Domain.Tests;

public sealed class FriendBlockListTests
{
    [Fact]
    public void Add_StoresPartner_InFirstFreeSlot()
    {
        var list = new FriendBlockList(slotCapacity: 8, localActorId: 1);

        int slot = list.Add(partnerId: 100);

        Assert.Equal(0, slot);
        Assert.True(list.Contains(100));
        Assert.Equal(1, list.Count);
    }

    [Fact]
    public void Add_RejectsSelfTarget_Sentinel_AndDuplicate_AndZero()
    {
        var list = new FriendBlockList(8, localActorId: 1);
        list.Add(100);

        Assert.Equal(-1, list.Add(1));                                 // self
        Assert.Equal(-1, list.Add(FriendBlockList.LocalPlayerSentinel)); // sentinel
        Assert.Equal(-1, list.Add(100));                               // duplicate
        Assert.Equal(-1, list.Add(0));                                 // zero id
    }

    [Fact]
    public void Add_RejectsWhenFull()
    {
        var list = new FriendBlockList(slotCapacity: 2, localActorId: 1);
        Assert.Equal(0, list.Add(10));
        Assert.Equal(1, list.Add(20));
        Assert.Equal(-1, list.Add(30)); // full
    }

    [Fact]
    public void Remove_ClearsSlot()
    {
        var list = new FriendBlockList(8, 1);
        list.Add(100);

        Assert.True(list.Remove(100));
        Assert.False(list.Contains(100));
        Assert.Equal(0, list.Count);
        Assert.False(list.Remove(100)); // already gone
    }

    [Fact]
    public void ApplySlot_WritesRelationSlot_AtIndex()
    {
        var list = new FriendBlockList(8, 1);
        var slot = new RelationSlot { PartnerId = 55, Field1 = 7 };

        list.ApplySlot(3, slot);

        Assert.Equal(55u, list[3].PartnerId);
        Assert.Equal(7, list[3].Field1);
        Assert.True(list.Contains(55));
    }

    [Fact]
    public void IsSelfTarget_MatchesLocalAndSentinel()
    {
        var list = new FriendBlockList(8, localActorId: 42);

        Assert.True(list.IsSelfTarget(42));
        Assert.True(list.IsSelfTarget(FriendBlockList.LocalPlayerSentinel));
        Assert.False(list.IsSelfTarget(43));
    }

    [Fact]
    public void Constructor_RejectsNonPositiveCapacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FriendBlockList(0, 1));
    }
}
