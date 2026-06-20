using MartialHeroes.Client.Application.Contracts.Events;

namespace MartialHeroes.Client.Application.World;

// =============================================================================
// AreaAssemblyHandoff — the AreaComposer → area-publish seam (layer-04 side).
//
// CYCLE 2 Phase 2-A.5. The CYCLE-1 sibling CellAssemblyHandoff re-publishes a
// CellAssembledEvent per streamed cell, but NOTHING ever published the
// AREA-level AreaAssembledEvent — so the area's .arr spawn list never reached
// layer-05 (the layer-05 RealWorldRenderer.OnAreaAssembled handler never fired).
// This handoff closes that gap.
//
// THE HANDOFF (invoked by the layer-05 composition root, which references BOTH
// Assets.Mapping and Application):
//   on area (re)bind for streaming  →  AreaComposer.ComposeArea(source)
//                                   →  re-publish ONCE as AreaAssembledEvent(IAssembledAreaView).
//
// This adapter is the LAYER-04 side: it hooks the area-bind path and re-publishes
// an AreaAssembledEvent, delegating the actual area compose to a caller-supplied
// callback. layer-04 never references layer-03 — the callback
// (AreaComposer.ComposeArea, adapted to IAssembledAreaView) is wired in by the
// layer-05 root. Mirrors the CellAssemblyHandoff delegate-bake pattern exactly.
// spec: Docs/RE/specs/assembly_graph.md §1 (Phase A — area load → spawns) / §4
// (AreaComposer contract; offline-port synthesis from .arr).
//
// IDEMPOTENCE. The area is composed + published exactly ONCE per area-enter — the
// per-cell stream (CellAssemblyHandoff) must NOT trigger a re-publish, and a
// re-bind of the SAME area id is a no-op. A genuine area CHANGE (a new area id)
// composes + publishes the new area. spec: assembly_graph.md §1 (Phase A runs once
// per area load; the per-cell chain re-runs per streamed cell, NOT the area chain).
// =============================================================================

/// <summary>
///     Composes an AREA once per area-enter and publishes it as an <see cref="AreaAssembledEvent" />. The
///     area compose itself is delegated to a caller-supplied callback (the layer-05 composition root binds
///     it to the layer-03 <c>AreaComposer.ComposeArea</c>, projecting the concrete <c>AssembledArea</c> onto
///     the layer-04 <see cref="IAssembledAreaView" />), so this layer-04 seam never references
///     <c>Assets.Mapping</c>. spec: Docs/RE/specs/assembly_graph.md §1/§4.
/// </summary>
/// <remarks>
///     <para>
///         <b>Usage.</b> The layer-05 root calls <see cref="OnAreaBound" /> when an area is (re)bound for
///         streaming — typically on the first streamed cell of a newly-entered area, or on an explicit
///         enter-world transition. The handoff invokes the compose callback and publishes the resulting
///         <see cref="AreaAssembledEvent" /> exactly once per distinct area id. A callback that returns
///         <see langword="null" /> (an unresolved / absent area) publishes nothing — faithfully empty, never
///         synthesised. spec: Docs/RE/specs/assembly_graph.md §1 (an absent area has no membership set).
///     </para>
///     <para>
///         <b>Idempotence.</b> Re-binding the area id that is already the live published area is a no-op (the
///         per-cell stream never re-publishes the area). Binding a DIFFERENT area id composes + publishes that
///         new area and adopts it as the live area. spec: Docs/RE/specs/assembly_graph.md §1 (Phase A — once
///         per area load).
///     </para>
///     <para>
///         <b>Threading.</b> Single-owner: invoked by the same logical owner that drains the bus (mirrors
///         <see cref="CellAssemblyHandoff" /> / <see cref="SectorStreamingService" />). The compose callback is
///         expected to be pure/deterministic per the AreaComposer contract; this handoff is deliberately
///         lock-free — do NOT call concurrently.
///     </para>
/// </remarks>
public sealed class AreaAssemblyHandoff
{
    /// <summary>
    ///     The area→assembled-area compose the layer-05 root binds to <c>AreaComposer.ComposeArea</c>,
    ///     projected onto the layer-04 <see cref="IAssembledAreaView" /> (the concrete <c>AssembledArea</c>
    ///     is a layer-03 type Application does not reference). Returns <see langword="null" /> for an
    ///     unresolved / absent area (nothing is published). spec: Docs/RE/specs/assembly_graph.md §1/§4.
    /// </summary>
    /// <param name="AreaId">The area id being (re)bound for streaming.</param>
    /// <returns>The assembled-area view, or <see langword="null" /> for an absent area.</returns>
    public delegate IAssembledAreaView? AreaBake(int AreaId);

    private readonly AreaBake _bake;

    private readonly IClientEventBus _eventBus;

    // The id of the live published area, or null when no area has been published yet. Tracks the
    // once-per-area-enter idempotence (a re-bind of this id is a no-op).
    // spec: Docs/RE/specs/assembly_graph.md §1 (Phase A runs once per area load).

    /// <summary>
    ///     Creates the handoff over the outbound bus and the caller-supplied area compose (bound by the
    ///     layer-05 root to <c>AreaComposer.ComposeArea</c>).
    /// </summary>
    public AreaAssemblyHandoff(IClientEventBus eventBus, AreaBake bake)
    {
        ArgumentNullException.ThrowIfNull(eventBus);
        ArgumentNullException.ThrowIfNull(bake);
        _eventBus = eventBus;
        _bake = bake;
    }

    /// <summary>
    ///     The id of the area whose <see cref="AreaAssembledEvent" /> is currently live, or
    ///     <see langword="null" /> when none has been published. Exposed for the owner to reconcile the
    ///     streaming area against the published area.
    /// </summary>
    public int? PublishedAreaId { get; private set; }

    /// <summary>
    ///     Handles an area (re)bind: when <paramref name="areaId" /> is a NEW area (not the live published
    ///     one), invokes the compose callback and, when it yields a resolved area, publishes a single
    ///     <see cref="AreaAssembledEvent" /> and adopts <paramref name="areaId" /> as the live area. A re-bind
    ///     of the already-published area id is a no-op (idempotent — the per-cell stream never re-publishes
    ///     the area). An unresolved area (a <see langword="null" /> compose result) publishes nothing and does
    ///     NOT adopt the id. spec: Docs/RE/specs/assembly_graph.md §1 (Phase A — once per area load) / §4.
    /// </summary>
    /// <param name="areaId">The area id being (re)bound for streaming.</param>
    /// <returns><see langword="true" /> if an <see cref="AreaAssembledEvent" /> was published this call.</returns>
    public bool OnAreaBound(int areaId)
    {
        // Idempotent: the live area is already published — re-binds (incl. the per-cell stream) are no-ops.
        // spec: Docs/RE/specs/assembly_graph.md §1 (Phase A runs once per area load).
        if (PublishedAreaId == areaId) return false;

        var area = _bake(areaId);
        if (area is null)
            return false; // unresolved / absent area → faithfully nothing published. spec: assembly_graph.md §1

        PublishedAreaId = areaId;
        return _eventBus.Publish(new AreaAssembledEvent(area));
    }
}