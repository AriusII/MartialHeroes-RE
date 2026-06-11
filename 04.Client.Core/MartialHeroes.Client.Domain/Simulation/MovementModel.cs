using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Client.Domain.Simulation;

/// <summary>
/// The deterministic client-side movement model: click-to-move target resolution (step-clamp + dead
/// zone) and per-frame forward integration against the terrain-collision port. Engine-free and
/// reproducible — all time / input is caller-supplied. spec: Docs/RE/specs/camera_movement.md Part B.
/// </summary>
/// <remarks>
/// <para>
/// <b>Y = 0 in simulation.</b> World vertical is forced to 0 and never crosses the wire; the heightmap
/// is a visual surface only, not authoritative for the 2D XZ collision. spec: camera_movement.md §B.0/§B.6.
/// </para>
/// <para>
/// <b>Fixed-point divergence (ours, documented).</b> The legacy mover integrates on a variable
/// per-frame delta with a <c>speed · 4.0</c> step in floats; this core integrates on the fixed logic
/// tick in <see cref="Vector3Fixed"/> (Q16.16) via <see cref="LinearMovement"/> so client and a future
/// server compute identical trajectories. The §B.5 fixed code immediates (step clamp 12.0, dead-zone
/// 2.0) are kept; the <c>· 4.0</c> step multiplier and per-frame delta become the
/// <see cref="LinearMovement"/> step. spec: camera_movement.md §B.3/§B.5 / Part C ("Determinism caution").
/// </para>
/// <para>
/// <b>Speeds are injected, not invented.</b> The concrete walk / run speeds are map config data
/// (<c>MAP_SPEED</c>), not code literals (§B.5 / open question 11). The mover takes a raw speed
/// (Q16.16 units/second), exactly as <see cref="Actor.MoveSpeedRawPerSecond"/> does. spec: camera_movement.md §B.5.
/// </para>
/// </remarks>
public static class MovementModel
{
    /// <summary>Max advance per move-issue when far from the target (real units; squared = 144.0). spec: camera_movement.md §B.2 step 3 / §B.5 (12.0).</summary>
    public const int MoveStepClampUnits = 12;

    /// <summary>Squared move-step clamp distance (12² = 144). spec: camera_movement.md §B.2 step 3 / §B.5 (sq 144.0).</summary>
    public const long MoveStepClampSquaredUnits = (long)MoveStepClampUnits * MoveStepClampUnits;

    /// <summary>Move dead-zone radius (real units; squared = 4.0 → 2 units). spec: camera_movement.md §B.2 step 4 / §B.5 (dead-zone sq 4.0).</summary>
    public const int MoveDeadZoneUnits = 2;

    /// <summary>Squared move dead-zone (2² = 4). Moves whose squared distance is ≤ this are ignored. spec: camera_movement.md §B.2 step 4 / §B.5 (sq 4.0).</summary>
    public const long MoveDeadZoneSquaredUnits = (long)MoveDeadZoneUnits * MoveDeadZoneUnits;

    /// <summary>
    /// Resolves a click-to-move target from the player's current position toward <paramref name="rawTarget"/>:
    /// if the squared planar distance exceeds the 144-unit clamp, the move is clamped to a 12-unit step
    /// along the direction; otherwise the full target is used. Y is forced to 0. spec: camera_movement.md §B.2 step 3.
    /// </summary>
    /// <returns>
    /// The resolved destination (Q16.16, Y = 0) and whether the move clears the §B.2 step-4 dead-zone
    /// (a move within 2 units of the current position is a no-op — <c>Commit = false</c>).
    /// </returns>
    public static (Vector3Fixed Destination, bool Commit) ResolveClickTarget(
        in Vector3Fixed current,
        in Vector3Fixed rawTarget)
    {
        // Planar (XZ) delta, in raw Q16.16 units; Y is ignored / forced to 0. spec: §B.0/§B.2.
        long dx = (long)rawTarget.RawX - current.RawX;
        long dz = (long)rawTarget.RawZ - current.RawZ;

        long oneSquared = (long)Vector3Fixed.One * Vector3Fixed.One; // raw units per (unit²)
        System.Int128 rawSquared = ((System.Int128)dx * dx) + ((System.Int128)dz * dz);

        // Dead-zone: ignore moves shorter than 2 units (squared ≤ 4). spec: §B.2 step 4 / §B.5 (sq 4.0).
        System.Int128 deadZoneRaw = (System.Int128)MoveDeadZoneSquaredUnits * oneSquared;
        if (rawSquared <= deadZoneRaw)
        {
            return (current, false);
        }

        // Step clamp: when squared distance > 144, scale the direction to a 12-unit step. spec: §B.2 step 3.
        System.Int128 clampRaw = (System.Int128)MoveStepClampSquaredUnits * oneSquared;
        if (rawSquared <= clampRaw)
        {
            // Within reach: target the click point directly (planar, Y = 0). spec: §B.2 step 3.
            return (new Vector3Fixed(rawTarget.RawX, 0, rawTarget.RawZ), true);
        }

        // Far: clamp to a 12-unit step along the unit direction. spec: §B.2 step 3 (scale to length 12.0).
        long distance = LinearMovement.IntegerSqrt(rawSquared);
        long stepRaw = (long)MoveStepClampUnits * Vector3Fixed.One;
        long nx = current.RawX + ScaleAxis(dx, stepRaw, distance);
        long nz = current.RawZ + ScaleAxis(dz, stepRaw, distance);
        return (new Vector3Fixed((int)nx, 0, (int)nz), true);
    }

    /// <summary>
    /// Integrates one tick of forward movement toward <paramref name="destination"/>, then resolves the
    /// candidate move against <paramref name="collision"/>: a clear move snaps to the stepped point (or
    /// the destination on arrival); a blocked move snaps to the clamped wall-slide / stop point.
    /// spec: Docs/RE/specs/camera_movement.md §B.3.
    /// </summary>
    /// <param name="current">Current position (Q16.16, Y = 0).</param>
    /// <param name="destination">Move destination (Q16.16, Y = 0).</param>
    /// <param name="speedRawPerSecond">Mover speed in raw Q16.16 units/second (injected; map data). spec: §B.5.</param>
    /// <param name="deltaMs">Elapsed time for this tick (ms).</param>
    /// <param name="collision">The terrain-collision port (§B.4).</param>
    /// <returns>The next position and whether the mover reached its destination this tick.</returns>
    public static (Vector3Fixed Position, bool Arrived) IntegrateTick(
        in Vector3Fixed current,
        in Vector3Fixed destination,
        long speedRawPerSecond,
        uint deltaMs,
        ITerrainCollision collision)
    {
        ArgumentNullException.ThrowIfNull(collision);

        if (current == destination)
        {
            return (destination, true);
        }

        (Vector3Fixed candidate, bool arrived) =
            LinearMovement.Step(current, destination, speedRawPerSecond, deltaMs);

        // Sweep the candidate move against static solids. spec: §B.3 step 3 / §B.4.
        CollisionResult result = collision.Sweep(in current, in candidate);
        if (result.Blocked)
        {
            // Wall-slide / stop: snap to the clamped point; not arrived. spec: §B.3 step 3.
            return (result.ClampedPoint, false);
        }

        return (candidate, arrived);
    }

    private static long ScaleAxis(long axisDelta, long stepLength, long distance)
    {
        if (distance == 0)
        {
            return 0;
        }

        System.Int128 numerator = (System.Int128)axisDelta * stepLength;
        System.Int128 half = distance / 2;
        System.Int128 rounded = numerator >= 0
            ? (numerator + half) / distance
            : (numerator - half) / distance;
        return (long)rounded;
    }
}
