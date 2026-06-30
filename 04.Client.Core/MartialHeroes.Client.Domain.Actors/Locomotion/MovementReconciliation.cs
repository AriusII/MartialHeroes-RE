using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Client.Domain.Actors.Locomotion;

public enum ReconciliationBand : byte
{
    ExactSet = 0,

    NormalInterp = 1,

    FastCatchUp = 2,

    HardTeleport = 3,

    CellRelocation = 4
}

public readonly record struct ReconciliationOutcome(
    ReconciliationBand Band,
    MoveScale Scale,
    bool SnapToTarget,
    bool ResetToIdle,
    bool RecomputeCell);

public static class MovementReconciliation
{
    public const byte CellRelocationMotionCode = 5;

    public static ReconciliationOutcome Classify(
        Vector3Fixed position, Vector3Fixed serverTarget, byte motionCode)
    {
        if (motionCode == CellRelocationMotionCode)
            return new ReconciliationOutcome(
                ReconciliationBand.CellRelocation, MoveScale.Default, true, false, true);

        var squared = PlanarDistance.SquaredXz(position, serverTarget);

        if (squared < LocomotionThresholds.ExactSetSquared)
            return new ReconciliationOutcome(
                ReconciliationBand.ExactSet, MoveScale.Default, true, false, false);

        if (squared <= LocomotionThresholds.NormalInterpMaxSquared)
            return new ReconciliationOutcome(
                ReconciliationBand.NormalInterp, MoveScale.Default, false, false, false);

        if (squared <= LocomotionThresholds.FastCatchUpMaxSquared)
            return new ReconciliationOutcome(
                ReconciliationBand.FastCatchUp, MoveScale.FastCatchUp, false, false, false);

        return new ReconciliationOutcome(
            ReconciliationBand.HardTeleport, MoveScale.Default, true, true, false);
    }
}
