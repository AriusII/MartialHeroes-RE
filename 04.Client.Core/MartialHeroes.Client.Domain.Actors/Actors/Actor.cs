using MartialHeroes.Client.Domain.Actors.Locomotion;
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
        SegmentStart = position;
        Yaw = yaw;
        MoveSpeedRawPerSecond = moveSpeedRawPerSecond;
        Scale = MoveScale.Default;
        RunFlag = 0;
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

    public Vector3Fixed SegmentStart { get; private set; }

    public int Yaw { get; private set; }

    public long MoveSpeedRawPerSecond { get; }

    public MoveScale Scale { get; private set; }

    public byte RunFlag { get; private set; }

    public bool IsRunning => RunFlag == 1;

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
        SegmentStart = Position;
        Scale = MoveScale.Default;
        FaceTowards(target);
        if (IsAlive && Position != target && Lifecycle != LifecycleState.Dead) Lifecycle = LifecycleState.Walking;
    }

    public void SnapTo(Vector3Fixed position)
    {
        Position = position;
        MoveTarget = position;
        SegmentStart = position;
    }

    public void SetYaw(int yaw)
    {
        Yaw = yaw;
    }

    public bool AdvanceMovement(uint deltaMs)
    {
        if (!IsAlive || Position == MoveTarget) return Position == MoveTarget;

        var result = LocomotionStep.Advance(new LocomotionStepInput(
            Position, SegmentStart, MoveTarget, MoveSpeedRawPerSecond, Scale, deltaMs));
        Position = result.Position;
        return result.Arrived;
    }

    public void SetRunFlag(byte runFlag)
    {
        RunFlag = runFlag;
        if (!IsAlive || Lifecycle == LifecycleState.Dead) return;
        if (Lifecycle == LifecycleState.Walking || Lifecycle == LifecycleState.Running)
            Lifecycle = MotionActivity.ResolveLocomotion(false, runFlag);
    }

    public ReconciliationBand ApplyReconciliation(Vector3Fixed serverTarget, byte motionCode)
    {
        var outcome = MovementReconciliation.Classify(Position, serverTarget, motionCode);

        Scale = outcome.Scale;

        if (outcome.SnapToTarget)
        {
            Position = serverTarget;
            SegmentStart = serverTarget;
        }
        else
        {
            SegmentStart = Position;
        }

        MoveTarget = serverTarget;
        FaceTowards(serverTarget);

        if (outcome.ResetToIdle)
            SetLifecycle(LifecycleState.Refreshing);
        else if (outcome.Band == ReconciliationBand.NormalInterp ||
                 outcome.Band == ReconciliationBand.FastCatchUp)
            RefreshLocomotionState();

        return outcome.Band;
    }

    public bool BeginMotionFinish(bool isLocalPlayer)
    {
        if (MotionFinish.EmitsMotionEnd(isLocalPlayer))
        {
            MoveTarget = Position;
            SegmentStart = Position;
            Scale = MoveScale.Default;
            if (IsAlive && Lifecycle != LifecycleState.Dead) Lifecycle = LifecycleState.Refreshing;
            return true;
        }

        Scale = MotionFinish.ResolveFinishScale(false, Sort);
        return false;
    }

    private void FaceTowards(Vector3Fixed target)
    {
        if (Facing.TryHeadingRaw(Position, target, out var yawRaw)) Yaw = yawRaw;
    }

    private void RefreshLocomotionState()
    {
        if (!IsAlive || Lifecycle == LifecycleState.Dead) return;
        Lifecycle = MotionActivity.ResolveLocomotion(false, RunFlag);
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