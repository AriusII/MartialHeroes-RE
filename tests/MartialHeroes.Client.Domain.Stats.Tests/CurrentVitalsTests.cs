using MartialHeroes.Client.Domain.Stats.Stats;
using Xunit;

namespace MartialHeroes.Client.Domain.Stats.Tests;

public sealed class CurrentVitalsTests
{
    [Fact]
    public void FromWire_KeepsHpAsSignedI64()
    {
        var vitals = CurrentVitals.FromWire(75000L, 500, 250);

        Assert.Equal(75000L, vitals.Hp);
        Assert.Equal(500, vitals.Mp);
        Assert.Equal(250, vitals.Stamina);
    }

    [Fact]
    public void FromWire_PreservesLargeHpBeyondU32()
    {
        var beyond = (long)uint.MaxValue + 1024L;

        var vitals = CurrentVitals.FromWire(beyond, 0, 0);

        Assert.Equal(beyond, vitals.Hp);
    }

    [Fact]
    public void FromWire_PreservesNegativeHp()
    {
        var vitals = CurrentVitals.FromWire(-1L, 0, 0);

        Assert.Equal(-1L, vitals.Hp);
        Assert.True(vitals.IsDepleted);
    }

    [Fact]
    public void FromWire_SignClampsNegativeMpToZero()
    {
        var vitals = CurrentVitals.FromWire(100L, -5, 0);

        Assert.Equal(0, vitals.Mp);
    }

    [Fact]
    public void FromWire_SignClampsNegativeStaminaToZero()
    {
        var vitals = CurrentVitals.FromWire(100L, 0, -42);

        Assert.Equal(0, vitals.Stamina);
    }

    [Fact]
    public void FromWire_DoesNotClampPositiveMpOrStamina()
    {
        var vitals = CurrentVitals.FromWire(0L, int.MaxValue, int.MaxValue);

        Assert.Equal(int.MaxValue, vitals.Mp);
        Assert.Equal(int.MaxValue, vitals.Stamina);
    }

    [Fact]
    public void IsDepleted_TrueAtZeroOrBelow()
    {
        Assert.True(CurrentVitals.FromWire(0L, 0, 0).IsDepleted);
        Assert.True(CurrentVitals.FromWire(-100L, 0, 0).IsDepleted);
        Assert.False(CurrentVitals.FromWire(1L, 0, 0).IsDepleted);
    }
}
