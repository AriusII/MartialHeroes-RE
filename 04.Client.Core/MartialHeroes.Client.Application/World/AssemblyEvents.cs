using MartialHeroes.Client.Application.Contracts.Events;

namespace MartialHeroes.Client.Application.World;

// =============================================================================
// AssemblyEvents — the assembled-area / assembled-cell / assembled-actor seam.
//
// CYCLE 1 Phase 5 Stage C — the layer-04 contracts that expose the Stage-B
// composers' output (AreaComposer in layer-03 Assets.Mapping; ActorComposer in
// this layer) on the Application event bus, so layer-05 consumes the assembled
// payload next-frame. spec: Docs/RE/specs/assembly_graph.md §1/§2/§4
// ("the assembled-area/assembled-actor are consumed by layer 05 next-frame").
//
// REFERENCE-GRAPH DECISION (Deliverable 1).
//   Application references Domain + Network (02) ONLY — it does NOT reference
//   03.Assets.Mapping (where AssembledArea/AssembledCell live). Adding a
//   04.Application → 03.Mapping edge is NOT among the by-design DAG edges
//   (the documented downward edges are: Infrastructure → Application +
//   Assets.Parsers + Assets.Vfs; Client.Godot → Application + Assets.Mapping +
//   Infrastructure). So the AREA/CELL events carry the payload through a
//   LAYER-04-OWNED ABSTRACTION (IAssembledAreaView / IAssembledCellView) that
//   the layer-03 model implements (or the layer-05 root adapts) — NOT the
//   concrete layer-03 type. This keeps the bus's "immutable snapshot, no live
//   reference" rule and the downward DAG intact.
//
//   The ACTOR event carries the concrete AssembledActor directly — it already
//   lives in this layer (Stage B), so no abstraction is needed.
// =============================================================================

/// <summary>
///     The layer-04-owned, engine-free view of an assembled CELL the layer-03 <c>AreaComposer</c> emits
///     per streamed cell. Application does not reference <c>Assets.Mapping</c>, so the concrete
///     <c>AssembledCell</c> is surfaced to this layer (and re-published to layer-05) through this
///     abstraction the layer-05 composition root adapts. spec: Docs/RE/specs/assembly_graph.md §1
///     (World-boot chain re-runs per streamed cell); §4 (AreaComposer contract).
/// </summary>
/// <remarks>
///     Members are the cross-format keying facts a presentation cell node needs without re-parsing: the
///     biased cell coordinate (the <c>mapZ + 100000·mapX</c> identity) and a render-readiness flag. The
///     heavy geometry/texture/building payload stays in the layer-03 model behind this view; layer-05
///     down-casts or reads the richer model via its own reference to <c>Assets.Mapping</c>. spec:
///     Docs/RE/specs/assembly_graph.md §1 (per-cell sub-managers / 9 slots).
/// </remarks>
public interface IAssembledCellView
{
    /// <summary>Biased sector X (the cell-key high part). spec: assembly_graph.md §1 (cell key <c>mapZ + 100000·mapX</c>).</summary>
    int MapX { get; }

    /// <summary>Biased sector Z (the cell-key low part). spec: assembly_graph.md §1 (cell key <c>mapZ + 100000·mapX</c>).</summary>
    int MapZ { get; }

    /// <summary>
    ///     <see langword="true" /> once the cell's <c>.map</c> parse fanned its sub-assets and the cell is
    ///     render-ready; <see langword="false" /> for an empty / not-resolved cell (faithfully empty, never
    ///     synthesised). spec: Docs/RE/specs/assembly_graph.md §1 (the <c>.map</c> parse fans the sub-assets).
    /// </summary>
    bool IsResolved { get; }
}

/// <summary>
///     The layer-04-owned, engine-free view of an assembled AREA the layer-03 <c>AreaComposer</c> emits
///     per area load. Surfaced through this abstraction for the same reference-graph reason as
///     <see cref="IAssembledCellView" />. spec: Docs/RE/specs/assembly_graph.md §1 (Phase A — area load).
/// </summary>
/// <remarks>
///     Carries the area-level <c>.arr</c>-derived <see cref="Spawns" /> list so the composer-driven actor
///     placement reaches layer-05 next-frame (the CYCLE-1 gap: <see cref="AreaAssembledEvent" /> was never
///     published, so this list never crossed the seam). The spawn descriptors are the neutral, layer-04
///     <see cref="AreaSpawnDescriptor" /> (a copy of the layer-03 <c>SpawnDescriptor</c> — Application does
///     not reference <c>Assets.Mapping</c>, so the layer-05 root projects the concrete model onto this
///     abstraction when it binds the area bake). spec: Docs/RE/specs/assembly_graph.md §1 (Phase A — area
///     load → spawns) / §4 (offline-port synthesis from <c>.arr</c>).
/// </remarks>
public interface IAssembledAreaView
{
    /// <summary>The area identifier this assembled area was built for. spec: assembly_graph.md §1 (area id → cell-key set).</summary>
    int AreaId { get; }

    /// <summary>The number of cell keys in the area's <c>d&lt;NNN&gt;.lst</c> set. spec: assembly_graph.md §1.</summary>
    int CellKeyCount { get; }

    /// <summary>
    ///     The area's spawn descriptors (built from <c>npc&lt;NNN&gt;.arr</c> + the offline <c>mob&lt;NNN&gt;.arr</c>),
    ///     already carrying the applied <c>Yaw = π/2 − facing</c> and NO baked Y (re-sampled per frame by
    ///     layer-05). Faithfully empty when the area has no <c>.arr</c> spawns — never synthesised. spec:
    ///     Docs/RE/specs/assembly_graph.md §1 (spawns; ground-Y re-sampled per frame) / §4 (offline synthesis);
    ///     Docs/RE/formats/npc_spawns.md (yaw = π/2 − value; Y re-sampled).
    /// </summary>
    IReadOnlyList<AreaSpawnDescriptor> Spawns { get; }
}

/// <summary>
///     The neutral, engine-free, layer-04 spawn descriptor carried on <see cref="AreaAssembledEvent" />.
///     A 1:1 mirror of the layer-03 <c>Assets.Mapping.SpawnDescriptor</c> — Application cannot reference
///     <c>Assets.Mapping</c> (see the reference-graph decision in this file's header), so the layer-05
///     composition root copies the concrete fields onto this immutable snapshot when it binds the area
///     bake. spec: Docs/RE/specs/assembly_graph.md §1 (spawns) / §4 (offline synthesis from <c>.arr</c>);
///     Docs/RE/formats/npc_spawns.md (record layout: world_x/world_z/facing/mob_id; yaw = π/2 − facing;
///     Ground-Y re-sampled from terrain every frame, NOT baked here).
/// </summary>
/// <param name="WorldX">World-space X spawn origin. spec: Docs/RE/formats/npc_spawns.md (+0x04 world_x).</param>
/// <param name="WorldZ">World-space Z spawn origin (Y is re-sampled per frame). spec: npc_spawns.md (+0x08 world_z).</param>
/// <param name="Yaw">
///     Applied facing in radians (<c>π/2 − rawFacing</c>). spec: npc_spawns.md (+0x12 facing; runtime π/2 −
///     value).
/// </param>
/// <param name="VisualId">
///     The <c>mob_id</c> / NPC template id (the visual-chain primary key). spec: npc_spawns.md (+0x00
///     mob_id).
/// </param>
/// <param name="IsNpc">
///     <see langword="true" /> for npc.arr-derived spawns; the field is retained so server-side/other
///     adapters can distinguish NPC vs mob actors. spec: assembly_graph.md §4.
/// </param>
public readonly record struct AreaSpawnDescriptor(
    float WorldX,
    float WorldZ,
    float Yaw,
    int VisualId,
    bool IsNpc);

/// <summary>
///     Published when the layer-03 <c>AreaComposer</c> has assembled an AREA (its <c>d&lt;NNN&gt;.lst</c>
///     cell-key set + area binaries loaded). Immutable snapshot the presentation consumes next-frame to
///     prepare the area's terrain root. Carries the layer-04 <see cref="IAssembledAreaView" /> abstraction
///     (NOT the layer-03 concrete model — see the reference-graph decision in this file's header). spec:
///     Docs/RE/specs/assembly_graph.md §1 (Phase A — area load); §4 (consumed by layer 05 next-frame).
/// </summary>
/// <param name="Area">The assembled-area view (a layer-04 abstraction the layer-03/05 side supplies).</param>
public sealed record AreaAssembledEvent(IAssembledAreaView Area) : IClientEvent;

/// <summary>
///     Published when the layer-03 <c>AreaComposer</c> has assembled a CELL from a streamed sector's raw
///     bytes (see Deliverable 3 — the composition root invokes <c>AreaComposer.ComposeCell</c> on each
///     <see cref="SectorLoadedEvent" /> and re-publishes the result here). Immutable snapshot the
///     presentation consumes next-frame to build the cell mesh. Carries the layer-04
///     <see cref="IAssembledCellView" /> abstraction (NOT the layer-03 concrete model). spec:
///     Docs/RE/specs/assembly_graph.md §1 (World-boot chain re-runs per streamed cell); §4.
/// </summary>
/// <param name="Cell">The assembled-cell view (a layer-04 abstraction the layer-03/05 side supplies).</param>
public sealed record CellAssembledEvent(IAssembledCellView Cell) : IClientEvent;

/// <summary>
///     Published when an actor has been baked by the layer-04 <see cref="ActorComposer" />. Carries the
///     concrete <see cref="AssembledActor" /> directly — it is already a layer-04, engine-free, immutable
///     descriptor (Stage B), so no abstraction is needed. The presentation consumes it next-frame to
///     build the Godot mesh + skeleton (Phase 6). spec: Docs/RE/specs/assembly_graph.md §2 (Actor-bake
///     chain re-runs per spawned actor); §4 (emit a valid actor descriptor independent of the mesh build).
/// </summary>
/// <remarks>
///     The descriptor carries <c>{ WorldX, WorldZ, Yaw }</c> but NO baked Y — the ground Y is re-sampled
///     from the terrain every frame by layer-05 (the A1-6 timing symptom is Phase 6's fix). spec:
///     Docs/RE/specs/assembly_graph.md §1 ("the ground Y is re-sampled from the terrain each frame"); §5 A1-6.
/// </remarks>
/// <param name="Actor">The neutral assembled-actor descriptor.</param>
public sealed record ActorAssembledEvent(AssembledActor Actor) : IClientEvent;