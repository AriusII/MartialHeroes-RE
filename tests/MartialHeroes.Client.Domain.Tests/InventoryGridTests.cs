using MartialHeroes.Client.Domain.Inventory;
using MartialHeroes.Shared.Kernel.Ids;
using Xunit;

namespace MartialHeroes.Client.Domain.Tests;

public sealed class InventoryGridTests
{
    private static readonly ItemId Apple = new(10);
    private static readonly ItemId Sword = new(20);

    [Fact]
    public void Add_FillsEmptySlot_AndCounts()
    {
        var grid = new InventoryGrid(4, 4, maxStackSize: 99);

        uint overflow = grid.Add(Apple, 5);

        Assert.Equal(0u, overflow);
        Assert.Equal(5u, grid.CountOf(Apple));
        Assert.Equal(5u, grid[0].Quantity);
        Assert.Equal(Apple, grid[0].Item);
    }

    [Fact]
    public void Add_TopsUpExistingStack_BeforeUsingNewSlot()
    {
        var grid = new InventoryGrid(4, 4, 99);
        grid.Add(Apple, 90);

        uint overflow = grid.Add(Apple, 5);

        Assert.Equal(0u, overflow);
        Assert.Equal(95u, grid[0].Quantity);
        Assert.True(grid[1].IsEmpty);
    }

    [Fact]
    public void Add_SpillsToNewStack_WhenFirstStackFull()
    {
        var grid = new InventoryGrid(4, 4, 99);
        grid.Add(Apple, 95);

        uint overflow = grid.Add(Apple, 10);

        Assert.Equal(0u, overflow);
        Assert.Equal(99u, grid[0].Quantity);
        Assert.Equal(6u, grid[1].Quantity);
        Assert.Equal(105u, grid.CountOf(Apple));
    }

    [Fact]
    public void Add_ReturnsOverflow_WhenGridFull()
    {
        var grid = new InventoryGrid(1, 1, 10); // single slot, max 10

        uint overflow = grid.Add(Apple, 25);

        Assert.Equal(15u, overflow);
        Assert.Equal(10u, grid.CountOf(Apple));
    }

    [Fact]
    public void Remove_TakesFromStacks_LowestIndexFirst()
    {
        var grid = new InventoryGrid(4, 4, 99);
        grid.Add(Apple, 150); // -> slot0=99, slot1=51

        uint removed = grid.Remove(Apple, 120);

        Assert.Equal(120u, removed);
        Assert.Equal(30u, grid.CountOf(Apple));
    }

    [Fact]
    public void Remove_ReturnsActualRemoved_WhenNotEnough()
    {
        var grid = new InventoryGrid(4, 4, 99);
        grid.Add(Apple, 5);

        uint removed = grid.Remove(Apple, 10);

        Assert.Equal(5u, removed);
        Assert.Equal(0u, grid.CountOf(Apple));
        Assert.True(grid[0].IsEmpty);
    }

    [Fact]
    public void Move_ToEmptySlot_MovesWholeStack()
    {
        var grid = new InventoryGrid(4, 4, 99);
        grid.Add(Sword, 1); // slot0

        bool ok = grid.Move(0, 5);

        Assert.True(ok);
        Assert.True(grid[0].IsEmpty);
        Assert.Equal(Sword, grid[5].Item);
        Assert.Equal(1u, grid[5].Quantity);
    }

    [Fact]
    public void Move_MergesSameItem_AndCapsAtMaxStack()
    {
        var grid = new InventoryGrid(4, 4, 99);
        grid.Add(Apple, 60); // slot0 = 60
        // Force a second separate stack in slot1.
        grid.Move(0, 1); // now slot1 = 60
        grid.Add(Apple, 60); // tops slot1 to 99 then slot0 = 21
        // State: slot0 = 21, slot1 = 99. Re-distribute: move 0 -> 1 (full) => swap.
        bool ok = grid.Move(0, 1);

        Assert.True(ok);
        // slot1 was full (99) same item => swap.
        Assert.Equal(99u, grid[0].Quantity);
        Assert.Equal(21u, grid[1].Quantity);
    }

    [Fact]
    public void Move_MergesSameItem_WithLeftoverStayingInSource()
    {
        var grid = new InventoryGrid(4, 4, 99);
        grid.Add(Apple, 50); // slot0 = 50
        grid.Move(0, 1); // slot1 = 50
        grid.Add(Apple, 70); // slot1 -> 99 (49 used), slot0 = 21
        // Actually distribute manually to a clean scenario:
        grid.Clear();
        grid.Add(Apple, 50); // slot0
        grid.Move(0, 1); // slot1 = 50
        grid.Add(Apple, 60); // slot1 -> 99 (49), slot0 = 11
        // Now move slot1 (99 full) into slot0 (11): dest not full -> merge 88 into... wait dest=slot0=11
        bool ok = grid.Move(1, 0); // source=99, dest=11 same item, room=88 -> dest=99, source=11

        Assert.True(ok);
        Assert.Equal(99u, grid[0].Quantity);
        Assert.Equal(11u, grid[1].Quantity);
    }

    [Fact]
    public void Move_SwapsDifferentItems()
    {
        var grid = new InventoryGrid(4, 4, 99);
        grid.Add(Apple, 5); // slot0
        grid.Move(0, 1); // slot1 = apple 5
        grid.Add(Sword, 1); // slot0 = sword 1

        bool ok = grid.Move(0, 1);

        Assert.True(ok);
        Assert.Equal(Apple, grid[0].Item);
        Assert.Equal(5u, grid[0].Quantity);
        Assert.Equal(Sword, grid[1].Item);
        Assert.Equal(1u, grid[1].Quantity);
    }

    [Fact]
    public void Move_EmptySource_IsNoOpSuccess()
    {
        var grid = new InventoryGrid(4, 4, 99);
        Assert.True(grid.Move(0, 1));
        Assert.True(grid[1].IsEmpty);
    }

    [Fact]
    public void ToIndex_RoundTrips()
    {
        var grid = new InventoryGrid(5, 3, 99);
        Assert.Equal(7, grid.ToIndex(2, 1)); // row1*5 + col2
        Assert.Equal(grid[7], grid.At(2, 1));
    }

    [Fact]
    public void Constructor_RejectsInvalidDimensions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InventoryGrid(0, 4, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InventoryGrid(4, 0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InventoryGrid(4, 4, 0));
    }
}