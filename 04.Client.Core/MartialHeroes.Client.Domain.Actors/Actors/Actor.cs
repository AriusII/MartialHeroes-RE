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

    public VitalStats Vitals { get; }

    public uint CurrentHp { get; private set; }

    public uint CurrentMp { get; private set; }

    public uint CurrentStamina { get; private set; }

    public Vector3Fixed Position { get; private set; }

    public Vector3Fixed MoveTarget { get; private set; }

    public int Yaw { get; private set; }

    public long MoveSpeedRawPerSecond { get; }

    public uint TargetRawId { get; private set; }

    public bool IsAlive { get; private set; }

    public bool IsPkEnabled { get; private set; }

    public bool IsInCombat { get; private set; }

    public LifecycleState Lifecycle { get; private set; }

    public MotionIntent Intent { get; private set; }

    public uint MaxHp => Vitals.MaxHp;

    public uint MaxMp => Vitals.MaxMp;

    public uint MaxStamina => Vitals.MaxStamina;


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
}