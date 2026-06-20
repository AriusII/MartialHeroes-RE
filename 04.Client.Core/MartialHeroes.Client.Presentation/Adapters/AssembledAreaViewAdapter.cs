// Adapters/AssembledAreaViewAdapter.cs
//
// Public layer-05 adapter: wraps the layer-03 AssembledArea as the layer-04 IAssembledAreaView
// abstraction so the AreaAssemblyHandoff (layer-04) can publish it without importing Assets.Mapping,
// while layer-05 consumers can receive the area view on AreaAssembledEvent and drive actor placement.
//
// The projection from layer-03 SpawnDescriptor to layer-04 AreaSpawnDescriptor is a 1:1 field copy
// (WorldX, WorldZ, Yaw, VisualId, IsNpc are identical in both types).
//
// Mirror of AssembledCellViewAdapter — same role, same pattern, for the area-level event.
//
// spec: Docs/RE/specs/assembly_graph.md §1 (Phase A — area load → spawns)
// spec: Docs/RE/specs/assembly_graph.md §4 (AreaComposer contract; offline-port synthesis from .arr)

using MartialHeroes.Assets.Mapping;
using MartialHeroes.Client.Application.World;

namespace MartialHeroes.Client.Presentation.Adapters;

/// <summary>
///     Public layer-05 adapter that wraps the layer-03
///     <see cref="global::MartialHeroes.Assets.Mapping.AssembledArea" /> as the layer-04
///     <see cref="IAssembledAreaView" /> abstraction.
///     Created by the composition root (<c>ClientContext.BuildApplicationGraph</c>) in the
///     <c>AreaBake</c> delegate wired to <c>AreaAssemblyHandoff</c>. Projects:
///     <list type="bullet">
///         <item><see cref="IAssembledAreaView.AreaId" /> from <c>AssembledArea.AreaId</c>.</item>
///         <item><see cref="IAssembledAreaView.CellKeyCount" /> from <c>AssembledArea.CellKeys.Count</c>.</item>
///         <item>
///             <see cref="IAssembledAreaView.Spawns" /> from <c>AssembledArea.Spawns</c>, projecting each
///             layer-03 <c>SpawnDescriptor</c> to a layer-04 <see cref="AreaSpawnDescriptor" /> (1:1 copy of
///             all five fields — no transformation).
///         </item>
///     </list>
///     spec: Docs/RE/specs/assembly_graph.md §1 (Phase A — area load → spawns) / §4 (AreaComposer).
///     spec: Docs/RE/formats/npc_spawns.md (yaw = π/2 − rawFacing already applied by AreaComposer).
/// </summary>
public sealed class AssembledAreaViewAdapter : IAssembledAreaView
{
    private readonly AssembledArea _area;

    // Lazily-projected spawn list: built once on first access, then cached.
    // Lazy avoids the allocation on callers that only need AreaId / CellKeyCount.
    private IReadOnlyList<AreaSpawnDescriptor>? _spawns;

    /// <summary>
    ///     Creates the adapter around the given assembled area.
    /// </summary>
    /// <param name="area">The concrete layer-03 assembled area to wrap. Must not be null.</param>
    public AssembledAreaViewAdapter(AssembledArea area)
    {
        _area = area ?? throw new ArgumentNullException(nameof(area));
    }

    /// <inheritdoc />
    public int AreaId => _area.AreaId;
    // spec: assembly_graph.md §1 (area id → cell-key set)

    /// <inheritdoc />
    public int CellKeyCount => _area.CellKeys.Count;
    // spec: assembly_graph.md §1 (d<NNN>.lst membership count)

    /// <inheritdoc />
    public IReadOnlyList<AreaSpawnDescriptor> Spawns
    {
        get
        {
            if (_spawns is not null) return _spawns;

            // Project layer-03 SpawnDescriptor → layer-04 AreaSpawnDescriptor.
            // All five fields (WorldX, WorldZ, Yaw, VisualId, IsNpc) are identical in both types;
            // the Yaw has already had π/2 − rawFacing applied by AreaComposer.
            // spec: Docs/RE/formats/npc_spawns.md (facing: runtime applies π/2 − value). CONFIRMED.
            // spec: Docs/RE/specs/assembly_graph.md §4 (offline synthesis from .arr). CONFIRMED.
            var src = _area.Spawns;
            var projected = new AreaSpawnDescriptor[src.Count];
            for (var i = 0; i < src.Count; i++)
            {
                var s = src[i];
                projected[i] = new AreaSpawnDescriptor(
                    s.WorldX,
                    s.WorldZ,
                    s.Yaw,
                    s.VisualId,
                    s.IsNpc);
            }

            _spawns = projected;
            return _spawns;
        }
    }
}