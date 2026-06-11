using MartialHeroes.Client.Domain.Inventory;
using Xunit;

namespace MartialHeroes.Client.Domain.Tests;

public sealed class EnchantRulesTests
{
    [Fact]
    public void Constants_MatchSpec()
    {
        Assert.Equal(100.0, EnchantRules.GaugeCompleteValue);
        Assert.Equal(8, EnchantRules.SuccessMotionId);
        Assert.Equal(9, EnchantRules.FailMotionId);
    }

    [Theory]
    [InlineData(100.0, true, true, true)]
    [InlineData(99.9, true, true, false)]   // gauge not full
    [InlineData(100.0, false, true, false)] // cannot act
    [InlineData(100.0, true, false, false)] // busy
    public void CanCommit_GatedOnGauge_AndPredicates(double gauge, bool canAct, bool notBusy, bool expected)
    {
        Assert.Equal(expected, EnchantRules.CanCommit(gauge, canAct, notBusy));
    }

    [Fact]
    public void ApplyResult_Success_PlaysMotion8_AdoptsServerLevel()
    {
        EnchantResult result = EnchantRules.ApplyResult(success: true, serverEnchantLevel: 7, currentEnchantLevel: 6);

        Assert.Equal(EnchantOutcome.Success, result.Outcome);
        Assert.Equal(8, result.MotionId);
        Assert.Equal(7, result.NewEnchantLevel);
    }

    [Fact]
    public void ApplyResult_Failure_PlaysMotion9_KeepsCurrentLevel()
    {
        EnchantResult result = EnchantRules.ApplyResult(success: false, serverEnchantLevel: 7, currentEnchantLevel: 6);

        Assert.Equal(EnchantOutcome.Failure, result.Outcome);
        Assert.Equal(9, result.MotionId);
        Assert.Equal(6, result.NewEnchantLevel);
    }
}
