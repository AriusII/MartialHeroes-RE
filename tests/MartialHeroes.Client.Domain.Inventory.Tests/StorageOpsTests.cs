using MartialHeroes.Client.Domain.Inventory.Inventory;
using Xunit;

namespace MartialHeroes.Client.Domain.Inventory.Tests;

public sealed class StorageOpsTests
{
    [Theory]
    [InlineData(7, 0)]
    [InlineData(8, 1)]
    [InlineData(9, 2)]
    [InlineData(262, 255)]
    public void OpFromWidgetActionId_subtracts_the_offset(int widgetActionId, byte expected)
    {
        Assert.Equal(expected, StorageOps.OpFromWidgetActionId(widgetActionId));
    }

    [Theory]
    [InlineData(6)]
    [InlineData(0)]
    [InlineData(263)]
    public void OpFromWidgetActionId_rejects_out_of_range(int widgetActionId)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StorageOps.OpFromWidgetActionId(widgetActionId));
    }
}
