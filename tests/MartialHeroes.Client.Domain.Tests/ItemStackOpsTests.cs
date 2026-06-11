using MartialHeroes.Client.Domain.Inventory;
using MartialHeroes.Shared.Kernel.Ids;
using Xunit;

namespace MartialHeroes.Client.Domain.Tests;

public sealed class ItemStackOpsTests
{
    private static readonly ItemId Apple = new(10);
    private static readonly ItemId Sword = new(20);

    [Fact]
    public void Split_TakesPartOfStack_IntoEmptySlot()
    {
        var grid = new InventoryGrid(4, 1, 99);
        grid.Add(Apple, 10);

        bool ok = ItemStackOps.Split(grid, fromIndex: 0, toIndex: 1, quantity: 4);

        Assert.True(ok);
        Assert.Equal(6u, grid[0].Quantity);
        Assert.Equal(4u, grid[1].Quantity);
        Assert.Equal(Apple, grid[1].Item);
    }

    [Fact]
    public void Split_RejectsWholeStack()
    {
        var grid = new InventoryGrid(4, 1, 99);
        grid.Add(Apple, 5);

        Assert.False(ItemStackOps.Split(grid, 0, 1, quantity: 5)); // == full stack, not a split
        Assert.False(ItemStackOps.Split(grid, 0, 1, quantity: 6)); // > full stack
        Assert.Equal(5u, grid[0].Quantity);
        Assert.True(grid[1].IsEmpty);
    }

    [Fact]
    public void Split_RejectsNonEmptyDestination()
    {
        var grid = new InventoryGrid(4, 1, 99);
        grid.Add(Apple, 10);
        grid.SetSlot(1, new InventorySlot(Sword, 1));

        Assert.False(ItemStackOps.Split(grid, 0, 1, 3));
    }

    [Fact]
    public void Split_RejectsZeroQuantity_AndSameIndex()
    {
        var grid = new InventoryGrid(4, 1, 99);
        grid.Add(Apple, 10);

        Assert.False(ItemStackOps.Split(grid, 0, 1, 0));
        Assert.False(ItemStackOps.Split(grid, 0, 0, 3));
    }

    [Fact]
    public void Merge_CombinesSameItem_CapsAtMaxStack()
    {
        var grid = new InventoryGrid(4, 1, maxStackSize: 99);
        grid.SetSlot(0, new InventorySlot(Apple, 95));
        grid.SetSlot(1, new InventorySlot(Apple, 10));

        bool ok = ItemStackOps.Merge(grid, fromIndex: 1, toIndex: 0);

        Assert.True(ok);
        Assert.Equal(99u, grid[0].Quantity);   // capped
        Assert.Equal(6u, grid[1].Quantity);    // overflow stays
    }

    [Fact]
    public void Merge_FullDestination_Fails()
    {
        var grid = new InventoryGrid(4, 1, 99);
        grid.SetSlot(0, new InventorySlot(Apple, 99));
        grid.SetSlot(1, new InventorySlot(Apple, 5));

        Assert.False(ItemStackOps.Merge(grid, 1, 0));
    }

    [Fact]
    public void Merge_DifferentItems_Fails()
    {
        var grid = new InventoryGrid(4, 1, 99);
        grid.SetSlot(0, new InventorySlot(Apple, 5));
        grid.SetSlot(1, new InventorySlot(Sword, 1));

        Assert.False(ItemStackOps.Merge(grid, 1, 0));
    }

    [Fact]
    public void Merge_EntireSourceMoves_ClearsSource()
    {
        var grid = new InventoryGrid(4, 1, 99);
        grid.SetSlot(0, new InventorySlot(Apple, 10));
        grid.SetSlot(1, new InventorySlot(Apple, 5));

        bool ok = ItemStackOps.Merge(grid, 1, 0);

        Assert.True(ok);
        Assert.Equal(15u, grid[0].Quantity);
        Assert.True(grid[1].IsEmpty);
    }

    [Fact]
    public void Move_Existing_StillWorks_GridUnchanged()
    {
        // Sanity: the pre-existing InventoryGrid.Move is untouched and additive helpers coexist.
        var grid = new InventoryGrid(2, 1, 99);
        grid.SetSlot(0, new InventorySlot(Apple, 3));

        Assert.True(grid.Move(0, 1));
        Assert.True(grid[0].IsEmpty);
        Assert.Equal(3u, grid[1].Quantity);
    }
}
