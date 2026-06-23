using MartialHeroes.Client.Domain.Simulation.Simulation;
using MartialHeroes.Client.Domain.Stats.Stats;
using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Client.Domain.Actors.Actors;

public sealed class Actor
{
    public Actor(
        ActorKey key,
        ushort level,
        VitalStats vitals,
        uint currentHp,
        uint currentMp,
        uint currentStamina,
        Vector3Fixed position,
        long moveSpeedRawPerSecond = 0,
        int yaw = 0)
    {
        if (moveSpeedRawPerSecond < 0)
            throw new ArgumentOutOfRangeException(
                nameof(moveSpeedRawPerSecond), "Speed must be non-negative.");

        Key = key;
        Level = level;
        Vitals = vitals;
        CurrentHp = Math.Min(currentHp, vitals.MaxHp);
        CurrentMp = Math.Min(currentMp, vitals.MaxMp);
        CurrentStamina = Math.Min(currentStamina, vitals.MaxStamina);
        Position = position;
        MoveTarget = position;
        Yaw = yaw;
        MoveSpeedRawPerSecond = moveSpeedRawPerSecond;
        TargetRawId = 0;
        IsAlive = true;
        IsPkEnabled = false;
        IsInCombat = false;
        Lifecycle = LifecycleState.Refreshing;
    }

    public ActorKey Key { get; }

    public EntitySort Sort => Key.Sort;

    public ushort Level { get; private set; }

    public VitalStats Vitals { get; private set; }

    public uint CurrentHp { get; private set; }

    public uint CurrentMp { get; private set; }

    public uint CurrentStamina { get; private set; }

    public Vector3Fixed Position { get; private set; }

    public Vector3Fixed MoveTarget { get; private set; }

    public int Yaw { get; private set; }

    public long MoveSpeedRawPerSecond { get; private set; }

    public uint TargetRawId { get; private set; }

    public bool IsAlive { get; private set; }

    public bool IsPkEnabled { get; private set; }

    public bool IsInCombat { get; private set; }

    public LifecycleState Lifecycle { get; private set; }

    public MotionIntent Intent { get; private set; }

    public uint MaxHp => Vitals.MaxHp;

    public uint MaxMp => Vitals.MaxMp;

    public uint MaxStamina => Vitals.MaxStamina;

    public bool HasArrived => Position == MoveTarget;


    public void SetVitals(VitalStats vitals)
    {
        Vitals = vitals;
        CurrentHp = Math.Min(CurrentHp, vitals.MaxHp);
        CurrentMp = Math.Min(CurrentMp, vitals.MaxMp);
        CurrentStamina = Math.Min(CurrentStamina, vitals.MaxStamina);
    }

    public void SetCurrentHp(uint value)
    {
        CurrentHp = Math.Min(value, MaxHp);
        if (CurrentHp == 0) Kill();
    }

    public void SetCurrentMp(uint value)
    {
        CurrentMp = Math.Min(value, MaxMp);
    }

    public void SetCurrentStamina(uint value)
    {
        CurrentStamina = Math.Min(value, MaxStamina);
    }

    public void ApplyDamage(uint amount)
    {
        if (!IsAlive) return;

        CurrentHp = amount >= CurrentHp ? 0 : CurrentHp - amount;
        if (CurrentHp == 0) Kill();
    }

    public void Heal(uint amount)
    {
        if (!IsAlive) return;

        var healed = (ulong)CurrentHp + amount;
        CurrentHp = healed > MaxHp ? MaxHp : (uint)healed;
    }

    public void RestoreMp(uint amount)
    {
        var restored = (ulong)CurrentMp + amount;
        CurrentMp = restored > MaxMp ? MaxMp : (uint)restored;
    }

    public void RestoreStamina(uint amount)
    {
        var restored = (ulong)CurrentStamina + amount;
        CurrentStamina = restored > MaxStamina ? MaxStamina : (uint)restored;
    }


    public RegenTicker TickHpRegen(RegenTicker ticker, uint deltaMs)
    {
        var (next, steps) = ticker.Advance(deltaMs);
        if (steps > 0 && IsAlive) Heal(ticker.AmountFor(steps));

        return next;
    }

    public RegenTicker TickMpRegen(RegenTicker ticker, uint deltaMs)
    {
        var (next, steps) = ticker.Advance(deltaMs);
        if (steps > 0) RestoreMp(ticker.AmountFor(steps));

        return next;
    }


    public void SetMoveTarget(Vector3Fixed target)
    {
        MoveTarget = target;
        if (IsAlive && Position != target && Lifecycle != LifecycleState.Dead) Lifecycle = LifecycleState.Walking;
    }

    public void SnapTo(Vector3Fixed position)
    {
        Position = position;
        MoveTarget = position;
    }

    public void SetYaw(int yaw)
    {
        Yaw = yaw;
    }

    public void SetMoveSpeed(long rawPerSecond)
    {
        if (rawPerSecond < 0)
            throw new ArgumentOutOfRangeException(nameof(rawPerSecond), "Speed must be non-negative.");

        MoveSpeedRawPerSecond = rawPerSecond;
    }

    public bool AdvanceMovement(uint deltaMs)
    {
        if (!IsAlive || Position == MoveTarget) return Position == MoveTarget;

        var (next, arrived) =
            LinearMovement.Step(Position, MoveTarget, MoveSpeedRawPerSecond, deltaMs);
        Position = next;
        return arrived;
    }


    public void SetTarget(uint rawId)
    {
        TargetRawId = rawId;
    }

    public void SetPkEnabled(bool enabled)
    {
        IsPkEnabled = enabled;
    }

    public void SetInCombat(bool inCombat)
    {
        IsInCombat = inCombat;
    }

    public void SetLevel(ushort level)
    {
        Level = level;
    }

    public bool SetLifecycle(LifecycleState state)
    {
        if (!IsAlive && (state == LifecycleState.Walking || state == LifecycleState.Running)) return false;

        Lifecycle = state;
        if (state == LifecycleState.Dead) IsAlive = false;

        return true;
    }

    public void SetMotionIntent(MotionIntent intent)
    {
        Intent = intent;
    }

    public void Kill()
    {
        IsAlive = false;
        CurrentHp = 0;
        IsInCombat = false;
        Lifecycle = LifecycleState.Dead;
        MoveTarget = Position;
    }

    public bool Revive(uint hp)
    {
        if (IsAlive) return false;

        IsAlive = true;
        var clamped = Math.Min(hp == 0 ? 1u : hp, MaxHp);
        CurrentHp = clamped == 0 ? 0 : clamped;
        Lifecycle = LifecycleState.Refreshing;
        return true;
    }
}