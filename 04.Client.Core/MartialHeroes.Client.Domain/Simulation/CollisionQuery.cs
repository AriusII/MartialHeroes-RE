using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Client.Domain.Simulation;

/// <summary>
/// The result of a 2D (XZ) swept collision query: whether the move was blocked and, if so, the
/// clamped (wall-slide / stop) point the mover should snap to.
/// spec: Docs/RE/specs/camera_movement.md §B.4 (the resolver returns the nearest hit point).
/// </summary>
/// <remarks>
/// Collision is strictly 2D in the XZ plane (no vertical component); world Y is forced to 0 for
/// simulation. spec: camera_movement.md §B.0/§B.4.
/// </remarks>
public readonly record struct CollisionResult
{
    /// <summary>True when the swept segment hit a static solid. spec: camera_movement.md §B.4.</summary>
    public bool Blocked { get; init; }

    /// <summary>The nearest hit / clamped point to snap to when <see cref="Blocked"/> (XZ; Y = 0). spec: camera_movement.md §B.3 step 3 / §B.4.</summary>
    public Vector3Fixed ClampedPoint { get; init; }

    /// <summary>A clear (no-hit) result that reaches <paramref name="destination"/>. spec: camera_movement.md §B.4.</summary>
    public static CollisionResult Clear(in Vector3Fixed destination) =>
        new() { Blocked = false, ClampedPoint = destination };

    /// <summary>A blocked result clamped to <paramref name="hitPoint"/>. spec: camera_movement.md §B.4.</summary>
    public static CollisionResult Hit(in Vector3Fixed hitPoint) =>
        new() { Blocked = true, ClampedPoint = hitPoint };
}

/// <summary>
/// The port the movement model uses to test a swept move segment against static terrain solids. The
/// Domain defines this <b>contract</b>; the implementation (against <c>.sod</c> / <c>.ted</c> data and
/// the per-cell 2D solid quadtree) lives in Application / Assets, never here.
/// spec: Docs/RE/specs/camera_movement.md §B.4.
/// </summary>
/// <remarks>
/// <para>
/// The query is a swept test of the player's movement line segment (from-XZ → to-XZ) against static
/// solid line segments organised in a per-cell spatial quadtree; it returns the nearest hit. The cell /
/// grid index math (cell size 1024, base index 10000, 16×16 bins) and the quadtree are owned by the
/// implementation; the contract only exposes the move-side query the mover calls. spec: §B.4.
/// </para>
/// <para>
/// All inputs / outputs are deterministic <see cref="Vector3Fixed"/> (XZ; Y = 0), so a pure test
/// double can satisfy the contract with no terrain data. spec: camera_movement.md §B.0/§B.4.
/// </para>
/// </remarks>
public interface ITerrainCollision
{
    /// <summary>
    /// Sweeps the segment <paramref name="from"/> → <paramref name="to"/> (XZ) against static solids and
    /// returns the nearest hit, or a clear result reaching <paramref name="to"/>.
    /// spec: Docs/RE/specs/camera_movement.md §B.4.
    /// </summary>
    CollisionResult Sweep(in Vector3Fixed from, in Vector3Fixed to);

    /// <summary>
    /// Tests whether the destination cell is loaded and the point is in-bounds and walkable (the
    /// cell-walkability / bounds / point-in-solid test). Used before committing a click move.
    /// spec: Docs/RE/specs/camera_movement.md §B.4 ("Walkability gate before committing a click").
    /// </summary>
    bool IsWalkable(in Vector3Fixed point);
}

/// <summary>
/// A no-op terrain-collision port that never blocks and always reports walkable. Useful for headless
/// tests and for a server / map that has no static solids loaded. spec: Docs/RE/specs/camera_movement.md §B.4.
/// </summary>
public sealed class OpenTerrainCollision : ITerrainCollision
{
    /// <summary>The shared singleton instance.</summary>
    public static readonly OpenTerrainCollision Instance = new();

    /// <inheritdoc />
    public CollisionResult Sweep(in Vector3Fixed from, in Vector3Fixed to) => CollisionResult.Clear(in to);

    /// <inheritdoc />
    public bool IsWalkable(in Vector3Fixed point) => true;
}