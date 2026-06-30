using MartialHeroes.Client.Domain.Inventory.Inventory;
using Xunit;

namespace MartialHeroes.Client.Domain.Inventory.Tests;

public sealed class EquipResultRulesTests
{
    [Fact]
    public void ClassifyExplicit_zero_is_error_notice()
    {
        Assert.Equal(EquipResultOutcome.ErrorNotice, EquipResultRules.ClassifyExplicit(0));
    }

    [Fact]
    public void ClassifyExplicit_one_is_apply()
    {
        Assert.Equal(EquipResultOutcome.Apply, EquipResultRules.ClassifyExplicit(1));
    }

    [Theory]
    [InlineData((byte)2)]
    [InlineData((byte)3)]
    [InlineData((byte)200)]
    [InlineData((byte)255)]
    public void ClassifyExplicit_two_or_more_is_no_op(byte result)
    {
        Assert.Equal(EquipResultOutcome.NoOp, EquipResultRules.ClassifyExplicit(result));
    }

    [Fact]
    public void ClassifyTruthy_zero_is_error_notice()
    {
        Assert.Equal(EquipResultOutcome.ErrorNotice, EquipResultRules.ClassifyTruthy(0));
    }

    [Theory]
    [InlineData((byte)1)]
    [InlineData((byte)2)]
    [InlineData((byte)200)]
    [InlineData((byte)255)]
    public void ClassifyTruthy_any_non_zero_is_apply(byte result)
    {
        Assert.Equal(EquipResultOutcome.Apply, EquipResultRules.ClassifyTruthy(result));
    }
}
