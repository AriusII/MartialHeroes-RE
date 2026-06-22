namespace MartialHeroes.Client.Application.World;

/// <summary>
///     The application-side ORDERING / CONTRACT the core owns for the in-world (GameState case-5) scene —
///     the engine-free model of the §13.1 scene build/update order, the per-frame loop phases, the
///     view-platform / layer-node counts, the streaming frustum-copy + far-plane clamp, and the teardown.
///     This carries NO rendering: the actual Godot scene-graph (the GScene root, the terrain-manager
///     singleton, the layer nodes) is built in layer 05 NEXT phase. The core models only the ordering and
///     the constants those layer-05 builders must honour 1:1.
/// </summary>
/// <remarks>
///     spec: Docs/RE/specs/world_systems.md §13.1 (the GameState-5 in-game roster: 17-step case-5 build,
///     the 4-phase per-frame loop, 5 view-platforms, GScene root + terrain-manager singleton + 5 layer
///     nodes captioned 2006/2004/2005/2148/2148; streaming frustum copy + 1000 far-plane clamp; teardown).
/// </remarks>
public static class WorldSceneContract
{
    /// <summary>
    ///     The number of build steps in the case-5 (in-world) scene build. spec:
    ///     Docs/RE/specs/world_systems.md §13.1 (17-step case-5 build).
    /// </summary>
    public const int Case5BuildStepCount = 17; // spec: world_systems.md §13.1 (17-step case-5 build)

    /// <summary>
    ///     The number of per-frame loop phases: (1) input pump, (2) per-render-view device step + present,
    ///     (3) round-robin scheduler, (4) frame-rate limiter. spec: Docs/RE/specs/world_systems.md §13.1
    ///     (per-frame loop = 4 phases).
    /// </summary>
    public const int FrameLoopPhaseCount = 4; // spec: world_systems.md §13.1 (4-phase per-frame loop)

    /// <summary>
    ///     The number of view-platform objects the in-game scene-graph builder allocates (consistent with
    ///     the 5 camera view modes). spec: Docs/RE/specs/world_systems.md §13.1 (5 view-platform objects).
    /// </summary>
    public const int ViewPlatformCount = 5; // spec: world_systems.md §13.1 (5 view-platforms)

    /// <summary>
    ///     The number of layer-node objects under the GScene root: <b>5 nodes</b> captioned by message-table
    ///     ids 2006 / 2004 / 2005 / 2148 / 2148 — id 2148 is REUSED for the last two, so the NODE count is 5
    ///     while the DISTINCT-id count is 4 (corrects the earlier "4 layer nodes" reading).
    ///     spec: Docs/RE/specs/world_systems.md §13.1 (5 layer nodes; 4 distinct ids).
    /// </summary>
    public const int LayerNodeCount = 5; // spec: world_systems.md §13.1 (5 layer nodes; 2148 reused)

    /// <summary>
    ///     The far-plane stream-radius clamp: the per-frame streaming copies the camera frustum into the
    ///     terrain manager and clamps the stream radius to <b>1000</b> at the far plane.
    ///     spec: Docs/RE/specs/world_systems.md §13.1 (stream radius clamped to 1000 at the far plane).
    /// </summary>
    public const float StreamRadiusFarPlaneClamp = 1000f; // spec: world_systems.md §13.1 (1000 far-plane clamp)

    /// <summary>
    ///     The 5 layer-node caption message-table ids, in build order: 2006, 2004, 2005, 2148, 2148. The
    ///     last two share id 2148 (5 nodes / 4 distinct ids). spec: Docs/RE/specs/world_systems.md §13.1.
    /// </summary>
    public static ReadOnlySpan<int> LayerNodeMessageIds =>
        [2006, 2004, 2005, 2148, 2148]; // spec: world_systems.md §13.1

    /// <summary>
    ///     Clamps a candidate per-frame stream radius to the §13.1 far-plane ceiling (1000). The camera
    ///     frustum's far plane is copied into the terrain manager each frame; the effective stream radius is
    ///     never larger than 1000. spec: Docs/RE/specs/world_systems.md §13.1.
    /// </summary>
    public static float ClampStreamRadius(float candidateRadius)
    {
        if (candidateRadius < 0f) return 0f;
        return candidateRadius > StreamRadiusFarPlaneClamp
            ? StreamRadiusFarPlaneClamp // copy the frustum, clamp to the 1000 far plane. spec: §13.1.
            : candidateRadius;
    }
}