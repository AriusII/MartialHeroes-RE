using MartialHeroes.Client.Application.Events;

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
/// The layer-04-owned, engine-free view of an assembled CELL the layer-03 <c>AreaComposer</c> emits
/// per streamed cell. Application does not reference <c>Assets.Mapping</c>, so the concrete
/// <c>AssembledCell</c> is surfaced to this layer (and re-published to layer-05) through this
/// abstraction the layer-05 composition root adapts. spec: Docs/RE/specs/assembly_graph.md §1
/// (World-boot chain re-runs per streamed cell); §4 (AreaComposer contract).
/// </summary>
/// <remarks>
/// Members are the cross-format keying facts a presentation cell node needs without re-parsing: the
/// biased cell coordinate (the <c>mapZ + 100000·mapX</c> identity) and a render-readiness flag. The
/// heavy geometry/texture/building payload stays in the layer-03 model behind this view; layer-05
/// down-casts or reads the richer model via its own reference to <c>Assets.Mapping</c>. spec:
/// Docs/RE/specs/assembly_graph.md §1 (per-cell sub-managers / 9 slots).
/// </remarks>
public interface IAssembledCellView
{
    /// <summary>Biased sector X (the cell-key high part). spec: assembly_graph.md §1 (cell key <c>mapZ + 100000·mapX</c>).</summary>
    int MapX { get; }

    /// <summary>Biased sector Z (the cell-key low part). spec: assembly_graph.md §1 (cell key <c>mapZ + 100000·mapX</c>).</summary>
    int MapZ { get; }

    /// <summary>
    /// <see langword="true"/> once the cell's <c>.map</c> parse fanned its sub-assets and the cell is
    /// render-ready; <see langword="false"/> for an empty / not-resolved cell (faithfully empty, never
    /// synthesised). spec: Docs/RE/specs/assembly_graph.md §1 (the <c>.map</c> parse fans the sub-assets).
    /// </summary>
    bool IsResolved { get; }
}

/// <summary>
/// The layer-04-owned, engine-free view of an assembled AREA the layer-03 <c>AreaComposer</c> emits
/// per area load. Surfaced through this abstraction for the same reference-graph reason as
/// <see cref="IAssembledCellView"/>. spec: Docs/RE/specs/assembly_graph.md §1 (Phase A — area load).
/// </summary>
public interface IAssembledAreaView
{
    /// <summary>The area identifier this assembled area was built for. spec: assembly_graph.md §1 (area id → cell-key set).</summary>
    int AreaId { get; }

    /// <summary>The number of cell keys in the area's <c>d&lt;NNN&gt;.lst</c> set. spec: assembly_graph.md §1.</summary>
    int CellKeyCount { get; }
}

/// <summary>
/// Published when the layer-03 <c>AreaComposer</c> has assembled an AREA (its <c>d&lt;NNN&gt;.lst</c>
/// cell-key set + area binaries loaded). Immutable snapshot the presentation consumes next-frame to
/// prepare the area's terrain root. Carries the layer-04 <see cref="IAssembledAreaView"/> abstraction
/// (NOT the layer-03 concrete model — see the reference-graph decision in this file's header). spec:
/// Docs/RE/specs/assembly_graph.md §1 (Phase A — area load); §4 (consumed by layer 05 next-frame).
/// </summary>
/// <param name="Area">The assembled-area view (a layer-04 abstraction the layer-03/05 side supplies).</param>
public sealed record AreaAssembledEvent(IAssembledAreaView Area) : IClientEvent;

/// <summary>
/// Published when the layer-03 <c>AreaComposer</c> has assembled a CELL from a streamed sector's raw
/// bytes (see Deliverable 3 — the composition root invokes <c>AreaComposer.ComposeCell</c> on each
/// <see cref="SectorLoadedEvent"/> and re-publishes the result here). Immutable snapshot the
/// presentation consumes next-frame to build the cell mesh. Carries the layer-04
/// <see cref="IAssembledCellView"/> abstraction (NOT the layer-03 concrete model). spec:
/// Docs/RE/specs/assembly_graph.md §1 (World-boot chain re-runs per streamed cell); §4.
/// </summary>
/// <param name="Cell">The assembled-cell view (a layer-04 abstraction the layer-03/05 side supplies).</param>
public sealed record CellAssembledEvent(IAssembledCellView Cell) : IClientEvent;

/// <summary>
/// Published when an actor has been baked by the layer-04 <see cref="ActorComposer"/>. Carries the
/// concrete <see cref="AssembledActor"/> directly — it is already a layer-04, engine-free, immutable
/// descriptor (Stage B), so no abstraction is needed. The presentation consumes it next-frame to
/// build the Godot mesh + skeleton (Phase 6). spec: Docs/RE/specs/assembly_graph.md §2 (Actor-bake
/// chain re-runs per spawned actor); §4 (emit a valid actor descriptor independent of the mesh build).
/// </summary>
/// <remarks>
/// The descriptor carries <c>{ WorldX, WorldZ, Yaw }</c> but NO baked Y — the ground Y is re-sampled
/// from the terrain every frame by layer-05 (the A1-6 timing symptom is Phase 6's fix). spec:
/// Docs/RE/specs/assembly_graph.md §1 ("the ground Y is re-sampled from the terrain each frame"); §5 A1-6.
/// </remarks>
/// <param name="Actor">The neutral assembled-actor descriptor.</param>
public sealed record ActorAssembledEvent(AssembledActor Actor) : IClientEvent;