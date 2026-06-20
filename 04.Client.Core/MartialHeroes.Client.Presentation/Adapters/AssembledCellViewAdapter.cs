// Adapters/AssembledCellViewAdapter.cs
//
// Public layer-05 adapter: wraps the layer-03 AssembledCell as the layer-04 IAssembledCellView
// abstraction so the CellAssemblyHandoff (layer-04) can publish it without importing Assets.Mapping,
// while layer-05 nodes (RealWorldRenderer, SlotRenderer) can down-cast to the concrete type for
// rich slot access.
//
// This type is PUBLIC (not file-local) so that both ClientContext (the composition root that
// creates it) and RealWorldRenderer (the rendering node that reads it) can reference it without
// reflection or a separate interface. The old file-local AssembledCellViewAdapter in ClientContext.cs
// is REPLACED by this type.
//
// spec: Docs/RE/specs/assembly_graph.md §4 — "the layer-05 composition root adapts the assembled
//   cell as an IAssembledCellView so the layer-04 CellAssemblyHandoff can publish it."
// spec: Docs/RE/specs/assembly_graph.md §1 — assembled cell carries the 9 slot model.

using MartialHeroes.Assets.Mapping;
using MartialHeroes.Client.Application.World;

namespace MartialHeroes.Client.Presentation.Adapters;

/// <summary>
///     Public layer-05 adapter that wraps the layer-03
///     <see cref="global::MartialHeroes.Assets.Mapping.AssembledCell" /> as the layer-04
///     <see cref="IAssembledCellView" /> abstraction.
///     Created by the composition root (<c>ClientContext.BuildApplicationGraph</c>) in the
///     <c>CellBake</c> delegate wired to <c>CellAssemblyHandoff</c>. May be safely down-cast by
///     any layer-05 consumer (e.g. <c>RealWorldRenderer.OnCellAssembled</c>) to reach the concrete
///     <see cref="ConcreteCell" /> for full 9-slot access without re-parsing.
///     spec: Docs/RE/specs/assembly_graph.md §4 — "the layer-05 composition root adapts the
///     assembled cell as an IAssembledCellView so the layer-04 CellAssemblyHandoff can publish it."
/// </summary>
public sealed class AssembledCellViewAdapter : IAssembledCellView
{
    /// <summary>
    ///     Creates the adapter around the given assembled cell.
    /// </summary>
    /// <param name="cell">The concrete layer-03 assembled cell to wrap. Must not be null.</param>
    public AssembledCellViewAdapter(AssembledCell cell)
    {
        ConcreteCell = cell ?? throw new ArgumentNullException(nameof(cell));
    }

    /// <summary>
    ///     The wrapped concrete <see cref="global::MartialHeroes.Assets.Mapping.AssembledCell" />.
    ///     Layer-05 consumers may use this for full slot access (9 slots + ResolvedTexturePaths).
    ///     spec: Docs/RE/specs/assembly_graph.md §1 — assembled cell carries the 9 sub-manager slots.
    /// </summary>
    public AssembledCell ConcreteCell { get; }

    /// <inheritdoc />
    public int MapX => ConcreteCell.MapX; // spec: assembly_graph.md §1 (cell key mapX part)

    /// <inheritdoc />
    public int MapZ => ConcreteCell.MapZ; // spec: assembly_graph.md §1 (cell key mapZ part)

    /// <inheritdoc />
    public bool IsResolved =>
        ConcreteCell.Slot0GroundTexGrid is not null || ConcreteCell.Slot1BuildingObjectGrid is not null;
}