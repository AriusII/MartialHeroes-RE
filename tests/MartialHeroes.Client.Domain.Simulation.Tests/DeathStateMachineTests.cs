using MartialHeroes.Client.Domain.Simulation.Simulation;
using Xunit;

namespace MartialHeroes.Client.Domain.Simulation.Tests;

public sealed class DeathStateMachineTests
{
    [Fact]
    public void Mode0_RecordsKillerVisual()
    {
        var r = DeathStateMachine.ResolveActorDeathState(0, 5u);

        Assert.Equal(ActorDeathOp.KilledByVisual, r.Op);
        Assert.True(r.ConsumesKiller);
        Assert.Equal(0u, r.EffectId);
    }

    [Theory]
    [InlineData(1u, 350000039u)]
    [InlineData(2u, 350000040u)]
    [InlineData(3u, 350000041u)]
    [InlineData(4u, 350000042u)]
    [InlineData(5u, 350000043u)]
    [InlineData(6u, 350000044u)]
    [InlineData(7u, 350000045u)]
    public void Mode1_SelectsDeathEffectByVariant(uint subSelector, uint expectedEffectId)
    {
        var r = DeathStateMachine.ResolveActorDeathState(1, subSelector);

        Assert.Equal(ActorDeathOp.SpawnDeathEffect, r.Op);
        Assert.Equal(expectedEffectId, r.EffectId);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(8u)]
    [InlineData(99u)]
    public void Mode1_OutOfRangeVariant_LeavesEffectZero(uint subSelector)
    {
        var r = DeathStateMachine.ResolveActorDeathState(1, subSelector);

        Assert.Equal(ActorDeathOp.SpawnDeathEffect, r.Op);
        Assert.Equal(0u, r.EffectId);
    }

    [Fact]
    public void Mode2_SubOne_SetsDeathSubState6()
    {
        var r = DeathStateMachine.ResolveActorDeathState(2, 1u);

        Assert.Equal(ActorDeathOp.SetDeathSubState, r.Op);
        Assert.Equal((byte)6, r.DeathSubState);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(2u)]
    [InlineData(255u)]
    public void Mode2_SubNotOne_SetsDeathSubState7(uint subSelector)
    {
        var r = DeathStateMachine.ResolveActorDeathState(2, subSelector);

        Assert.Equal(ActorDeathOp.SetDeathSubState, r.Op);
        Assert.Equal((byte)7, r.DeathSubState);
    }

    [Fact]
    public void Mode3_Revives()
    {
        var r = DeathStateMachine.ResolveActorDeathState(3, 0u);

        Assert.Equal(ActorDeathOp.Revive, r.Op);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(255)]
    public void UnknownActorDeathMode_NoOp(byte mode)
    {
        var r = DeathStateMachine.ResolveActorDeathState(mode, 0u);

        Assert.Equal(ActorDeathOp.NoOp, r.Op);
    }

    [Fact]
    public void PvpMode1_Gate1_EngageSpawnsAura()
    {
        var r = DeathStateMachine.ResolvePvpDeathFx(1, 1);

        Assert.Equal(PvpDeathFxOp.Engage, r.Op);
        Assert.True(r.SpawnAura);
        Assert.Equal(371003701u, r.AuraEffectId);
        Assert.False(r.DeactivateAura);
        Assert.False(r.SpawnBurst);
    }

    [Fact]
    public void PvpMode1_Gate0_EngageNoAura()
    {
        var r = DeathStateMachine.ResolvePvpDeathFx(1, 0);

        Assert.Equal(PvpDeathFxOp.Engage, r.Op);
        Assert.False(r.SpawnAura);
    }

    [Fact]
    public void PvpMode6_Gate1_DisengageDeactivatesAuraAndSpawnsBurst()
    {
        var r = DeathStateMachine.ResolvePvpDeathFx(6, 1);

        Assert.Equal(PvpDeathFxOp.Disengage, r.Op);
        Assert.True(r.DeactivateAura);
        Assert.Equal(371003701u, r.DeactivateEffectId);
        Assert.True(r.SpawnBurst);
        Assert.Equal(371003702u, r.BurstEffectId);
    }

    [Fact]
    public void PvpMode6_Gate0_DisengageDeactivatesAuraNoBurst()
    {
        var r = DeathStateMachine.ResolvePvpDeathFx(6, 0);

        Assert.Equal(PvpDeathFxOp.Disengage, r.Op);
        Assert.True(r.DeactivateAura);
        Assert.False(r.SpawnBurst);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(255)]
    public void UnknownPvpMode_NoOp(byte mode)
    {
        var r = DeathStateMachine.ResolvePvpDeathFx(mode, 1);

        Assert.Equal(PvpDeathFxOp.NoOp, r.Op);
    }
}
