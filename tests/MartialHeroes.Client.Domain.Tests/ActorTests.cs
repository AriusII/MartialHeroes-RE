using MartialHeroes.Client.Domain.Actors;
using MartialHeroes.Client.Domain.Simulation;
using MartialHeroes.Client.Domain.Stats;
using MartialHeroes.Shared.Kernel.Numerics;
using Xunit;

namespace MartialHeroes.Client.Domain.Tests;

public sealed class ActorTests
{
    private static Actor NewPlayer(uint baseHp = 100, uint hpBonus = 0, uint currentHp = 100)
    {
        var vitals = new VitalStats(baseHp, 50, 30, hpBonus, 0, 0);
        var key = new ActorKey(42, EntitySort.PlayerCharacter);
        return new Actor(key, level: 1, vitals, currentHp, currentMp: 50, currentStamina: 30,
            position: Vector3Fixed.Zero);
    }

    [Fact]
    public void MaxHp_IsComputed_FromBasePlusEquipment()
    {
        Actor actor = NewPlayer(baseHp: 100, hpBonus: 25, currentHp: 100);

        Assert.Equal(125u, actor.MaxHp);
        Assert.Equal(100u, actor.CurrentHp);
    }

    [Fact]
    public void Constructor_ClampsCurrentHp_ToComputedMax()
    {
        Actor actor = NewPlayer(baseHp: 80, hpBonus: 0, currentHp: 999);

        Assert.Equal(80u, actor.MaxHp);
        Assert.Equal(80u, actor.CurrentHp);
    }

    [Fact]
    public void SetVitals_ReclampsCurrentValues()
    {
        Actor actor = NewPlayer(baseHp: 100, currentHp: 100);
        actor.SetVitals(new VitalStats(60, 50, 30, 0, 0, 0));

        Assert.Equal(60u, actor.MaxHp);
        Assert.Equal(60u, actor.CurrentHp);
    }

    [Fact]
    public void ApplyDamage_KillsActor_AtZeroHp()
    {
        Actor actor = NewPlayer(currentHp: 30);

        actor.ApplyDamage(50);

        Assert.Equal(0u, actor.CurrentHp);
        Assert.False(actor.IsAlive);
        Assert.Equal(LifecycleState.Dead, actor.Lifecycle);
    }

    [Fact]
    public void Heal_SaturatesAtMaxHp()
    {
        Actor actor = NewPlayer(baseHp: 100, currentHp: 90);
        actor.Heal(50);

        Assert.Equal(100u, actor.CurrentHp);
    }

    [Fact]
    public void Heal_HasNoEffect_OnDeadActor()
    {
        Actor actor = NewPlayer(currentHp: 10);
        actor.ApplyDamage(10); // dead
        actor.Heal(50);

        Assert.False(actor.IsAlive);
        Assert.Equal(0u, actor.CurrentHp);
    }

    [Fact]
    public void Revive_RestoresAlive_WithClampedHp()
    {
        Actor actor = NewPlayer(baseHp: 100, currentHp: 10);
        actor.ApplyDamage(10);

        bool revived = actor.Revive(40);

        Assert.True(revived);
        Assert.True(actor.IsAlive);
        Assert.Equal(40u, actor.CurrentHp);
        Assert.Equal(LifecycleState.Refreshing, actor.Lifecycle);
    }

    [Fact]
    public void Revive_OnLivingActor_ReturnsFalse()
    {
        Actor actor = NewPlayer();
        Assert.False(actor.Revive(10));
    }

    [Fact]
    public void AdvanceMovement_ArrivesExactly_OverDeterministicTicks()
    {
        Actor actor = NewPlayer();
        actor.SetMoveSpeed(1L * Vector3Fixed.One); // 1 unit/sec
        actor.SetMoveTarget(Vector3Fixed.FromWholeUnits(5, 0, 0));

        bool arrived = false;
        for (int i = 0; i < 5; i++)
        {
            arrived = actor.AdvanceMovement(1000);
        }

        Assert.True(arrived);
        Assert.Equal(Vector3Fixed.FromWholeUnits(5, 0, 0), actor.Position);
        Assert.True(actor.HasArrived);
    }

    [Fact]
    public void AdvanceMovement_DeadActor_DoesNotMove()
    {
        Actor actor = NewPlayer(currentHp: 5);
        actor.SetMoveSpeed(10L * Vector3Fixed.One);
        actor.SetMoveTarget(Vector3Fixed.FromWholeUnits(5, 0, 0));
        actor.ApplyDamage(5); // kill -> Kill() clears the pending move target

        // A dead actor never moves. Kill() collapsed the move target onto the position,
        // so the position stays put regardless of the returned arrived flag.
        actor.AdvanceMovement(1000);

        Assert.Equal(Vector3Fixed.Zero, actor.Position);
    }

    [Fact]
    public void SnapTo_ClearsPendingMovement()
    {
        Actor actor = NewPlayer();
        actor.SetMoveTarget(Vector3Fixed.FromWholeUnits(9, 0, 9));
        actor.SnapTo(Vector3Fixed.FromWholeUnits(3, 0, 3));

        Assert.Equal(Vector3Fixed.FromWholeUnits(3, 0, 3), actor.Position);
        Assert.True(actor.HasArrived);
    }

    [Fact]
    public void HpRegen_IsFrameRateIndependent()
    {
        Actor big = NewPlayer(baseHp: 100, currentHp: 50);
        var bigTicker = new RegenTicker(10, 1);
        big.TickHpRegen(bigTicker, 100); // one 100ms tick -> 10 hp

        Actor small = NewPlayer(baseHp: 100, currentHp: 50);
        var smallTicker = new RegenTicker(10, 1);
        for (int i = 0; i < 10; i++)
        {
            smallTicker = small.TickHpRegen(smallTicker, 10); // ten 10ms ticks -> 10 hp
        }

        Assert.Equal(60u, big.CurrentHp);
        Assert.Equal(60u, small.CurrentHp);
        Assert.Equal(big.CurrentHp, small.CurrentHp);
    }

    [Fact]
    public void HpRegen_DoesNotExceedMax()
    {
        Actor actor = NewPlayer(baseHp: 100, currentHp: 95);
        var ticker = new RegenTicker(10, 5);
        actor.TickHpRegen(ticker, 100); // would add 50, capped at max

        Assert.Equal(100u, actor.CurrentHp);
    }

    [Fact]
    public void SetLifecycle_RejectsWalkRun_OnDeadActor()
    {
        Actor actor = NewPlayer(currentHp: 5);
        actor.ApplyDamage(5); // dead

        Assert.False(actor.SetLifecycle(LifecycleState.Walking));
        Assert.False(actor.SetLifecycle(LifecycleState.Running));
        Assert.Equal(LifecycleState.Dead, actor.Lifecycle);
    }

    [Fact]
    public void SetLifecycle_ToDead_MarksNotAlive()
    {
        Actor actor = NewPlayer();
        Assert.True(actor.SetLifecycle(LifecycleState.Dead));
        Assert.False(actor.IsAlive);
    }

    [Fact]
    public void Flags_AndTarget_AreControlled()
    {
        Actor actor = NewPlayer();
        actor.SetPkEnabled(true);
        actor.SetInCombat(true);
        actor.SetTarget(99);

        Assert.True(actor.IsPkEnabled);
        Assert.True(actor.IsInCombat);
        Assert.Equal(99u, actor.TargetRawId);
        Assert.Equal(EntitySort.PlayerCharacter, actor.Sort);
    }

    [Fact]
    public void Kill_ClearsCombat_AndStopsMovement()
    {
        Actor actor = NewPlayer();
        actor.SetInCombat(true);
        actor.SetMoveTarget(Vector3Fixed.FromWholeUnits(5, 0, 0));
        actor.Kill();

        Assert.False(actor.IsAlive);
        Assert.False(actor.IsInCombat);
        Assert.True(actor.HasArrived);
        Assert.Equal(LifecycleState.Dead, actor.Lifecycle);
    }
}
