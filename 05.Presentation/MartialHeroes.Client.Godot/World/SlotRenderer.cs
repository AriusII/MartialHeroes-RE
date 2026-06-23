// World/SlotRenderer.cs
//
// CYCLE 2 Phase 2-A: Slot-specific renderers for composer-driven world rendering.
//
// Renders individual cell slots FROM the AreaComposer's AssembledCell model rather than the
// legacy direct-from-VFS path. Each public method handles one 9-slot range:
//   RenderSlot1Buildings: slot 1 — mass/building-object placement grid (.bud)
//   RenderFxSlots: slots 2-8 — FX overlay texture layers (.fx1-.fx7)
//
// All methods are strictly passive: no game logic, no domain mutation. They consume pre-assembled
// data from AssembledCell and emit Godot Node3D/MeshInstance3D children onto a parent node.
//
// Threading: all Godot node creation happens on the main thread (called from OnCellAssembled →
// GameLoop.DispatchEvent → GameLoop._Process). No background thread touches these nodes.
//
// spec: Docs/RE/structs/terrain-manager.md §"The 9 per-cell render sub-manager slots" — CONFIRMED.
// spec: Docs/RE/specs/assembly_graph.md §1 — assembled cell carries 9 slots.
// spec: Docs/RE/formats/terrain_scene.md — BUD scene coordinate system (absolute world-space).

using Godot;
using MartialHeroes.Assets.Mapping;

namespace MartialHeroes.Client.Godot.World;

/// <summary>
///     Static helpers that render individual cell slots from a pre-assembled
///     <see cref="global::MartialHeroes.Assets.Mapping.AssembledCell" /> onto a target <see cref="Node3D" />.
///     Used exclusively by <see cref="RealWorldRenderer" /> when the <c>compose_render</c> flag is on.
///     spec: Docs/RE/structs/terrain-manager.md §"The 9 per-cell render sub-manager slots" — CONFIRMED.
///     spec: Docs/RE/specs/assembly_graph.md §1 — assembled cell carries 9 slots.
/// </summary>
internal static class SlotRenderer
{
    // ─── Slot 1 — Buildings ───────────────────────────────────────────────────

    /// <summary>
    ///     Renders slot 1 (mass/building-object placement grid) from the assembled cell's
    ///     <see cref="global::MartialHeroes.Assets.Mapping.AssembledCell.Slot1BuildingObjectGrid" />
    ///     (<c>.bud</c> scene) via <see cref="BudMeshBuilder" />. The resulting <see cref="Node3D" />
    ///     subtree is added as a child of <paramref name="parent" />.
    ///     Building coordinates are pre-baked into absolute world-space (no cell-relative offset needed).
    ///     spec: Docs/RE/formats/terrain_scene.md §Coordinate system — "positions are pre-baked into
    ///     absolute world-space": CONFIRMED.
    ///     The building texture resolver uses the same two-hop chain as the legacy path
    ///     (<c>.map</c> BUILDING TEXTURES → bgtexture.lst → DDS) via <paramref name="budTexResolver" />.
    ///     Returns true when buildings were spawned, false when the slot was absent or empty.
    ///     spec: Docs/RE/structs/terrain-manager.md slot 1 — "mass/building-object placement grid (.bud)": CONFIRMED.
    ///     spec: Docs/RE/formats/area_inventory.md §3.2 — absent .bud is NOT an error.
    /// </summary>
    /// <param name="parent">The parent node to attach the building subtree to.</param>
    /// <param name="cell">The fully assembled cell (slot 1 may be null — handled gracefully).</param>
    /// <param name="budTexResolver">
    ///     Texture resolver: given a 1-based .bud tex_id, returns a Godot ImageTexture or null.
    ///     Uses the cell's .map BUILDING TEXTURES section (same chain as the legacy BUD path).
    /// </param>
    /// <param name="cellMapXZ">Biased cell coordinates for log messages.</param>
    public static bool RenderSlot1Buildings(
        Node3D parent,
        AssembledCell cell,
        Func<uint, ImageTexture?> budTexResolver,
        (int MapX, int MapZ) cellMapXZ)
    {
        var budScene = cell.Slot1BuildingObjectGrid;
        if (budScene is null)
        {
            GD.Print($"[SlotRenderer] Cell ({cellMapXZ.MapX},{cellMapXZ.MapZ}) slot 1: absent (terrain-only cell). " +
                     "spec: area_inventory.md §3.2 — absent .bud is not an error.");
            return false;
        }

        if (budScene.Objects.Length == 0)
        {
            GD.Print($"[SlotRenderer] Cell ({cellMapXZ.MapX},{cellMapXZ.MapZ}) slot 1: BUD scene empty.");
            return false;
        }

        Node3D budRoot;
        try
        {
            // BudMeshBuilder.Build constructs a Node3D subtree (one MeshInstance3D per BUD object)
            // with ArrayMesh geometry. NO GltfDocument is used — the mesh is built directly.
            // spec: Docs/RE/formats/terrain_scene.md — BUD objects have absolute world-space positions.
            // spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — no GltfDocument.AppendFromBuffer.
            budRoot = BudMeshBuilder.Build(budScene, id => budTexResolver(id));
        }
        catch (Exception ex)
        {
            GD.PrintErr(
                $"[SlotRenderer] Cell ({cellMapXZ.MapX},{cellMapXZ.MapZ}) slot 1: BudMeshBuilder.Build failed: {ex.Message}");
            return false;
        }

        budRoot.Name = $"ComposedBudSlot1_({cellMapXZ.MapX},{cellMapXZ.MapZ})";
        parent.AddChild(budRoot);

        GD.Print($"[SlotRenderer][ComposeRender] Cell ({cellMapXZ.MapX},{cellMapXZ.MapZ}) slot 1: " +
                 $"{budScene.Objects.Length} buildings spawned from assembled cell. " +
                 "spec: assembly_graph.md §1 — slot 1 mass/building grid.");
        return true;
    }

    // ─── Slots 2-8 — FX Overlays ─────────────────────────────────────────────

    /// <summary>
    ///     Renders slots 2-8 (FX overlay texture layers .fx1-.fx7) from the assembled cell by building
    ///     real overlay geometry via <see cref="FxMeshBuilder" /> (one Godot ArrayMesh per
    ///     group / tile / section / sub-chunk). Missing FX slots are silently skipped — absent FX is
    ///     not an error. Groups whose texture does not resolve are logged + skipped by the builder
    ///     (never substituted with a placeholder).
    ///     <para>
    ///         FX overlay vertices are ABSOLUTE world-space (spec: terrain_layers.md §4.2 — XZ within
    ///         the cell world AABB <c>[(cellX-10000)*1024 .. +1024]</c>), so each per-channel subtree is
    ///         parented at <see cref="Vector3.Zero" /> and the vertices carry their own world position;
    ///         the handedness flip is the world convention (negate Z), identical to
    ///         <see cref="BudMeshBuilder" />. The <paramref name="cellWorldOriginGodot" /> is retained
    ///         for logging/diagnostics only (FX verts are NOT cell-local).
    ///     </para>
    ///     spec: Docs/RE/structs/terrain-manager.md slots 2-8 — FX overlay texture layers: CONFIRMED.
    ///     spec: Docs/RE/specs/assembly_graph.md §1 — slots 2..8 = fx1..fx7 overlays.
    ///     spec: Docs/RE/formats/terrain_layers.md §4.2 — FX verts absolute world-space: CONFIRMED.
    ///     spec: WorldCoordinates.ToGodot — world geometry negates Z (port-side): CONFIRMED.
    /// </summary>
    /// <param name="parent">Parent node (same RealWorldRenderer node).</param>
    /// <param name="cell">Assembled cell whose FX slots may carry overlay data.</param>
    /// <param name="cellWorldOriginGodot">
    ///     The Godot-space world origin of this cell (already with Z negated). Retained for logging
    ///     only — FX vertices are absolute world-space and are NOT offset by this value.
    ///     spec: WorldCoordinates.ToGodot — (x,y,z) → (x,y,-z): CONFIRMED.
    /// </param>
    /// <param name="cellMapXZ">Biased cell coordinates for log messages.</param>
    /// <param name="fxTexResolver">
    ///     Optional FX texture resolver: <c>(channel 1..7, texIndex1Based) → ImageTexture?</c>, delegating
    ///     to the cell <c>.map</c> FX{N} TEXTURES two-hop chain. When <see langword="null" /> no FX group
    ///     resolves a texture and every group is logged + skipped (no placeholder). The wiring of a real
    ///     resolver from the renderer is tracked as a coordination item (the call site is in
    ///     RealWorldRenderer.Composer.cs, outside this cluster).
    ///     spec: Docs/RE/formats/terrain_layers.md §1.4b — texture_index is 1-based into the per-channel
    ///     FX{N} register (clamp [1,count], idx-1): CONFIRMED.
    /// </param>
    /// <returns>Number of FX slots rendered (0 if all absent).</returns>
    public static int RenderFxSlots(
        Node3D parent,
        AssembledCell cell,
        Vector3 cellWorldOriginGodot,
        (int MapX, int MapZ) cellMapXZ,
        FxMeshBuilder.FxTextureResolver? fxTexResolver = null)
    {
        var rendered = 0;

        // Slots 2-8 carry FX overlay layers — each an indexed triangle-list mesh (universal
        // group-array model §1.1a). Missing FX is not an error; most cells have no FX.
        // spec: Docs/RE/structs/terrain-manager.md slots 2-8: CONFIRMED.
        // spec: Docs/RE/formats/area_inventory.md §3.2 — absent FX is not an error.
        //
        // FX vertices are ABSOLUTE world-space (terrain_layers.md §4.2) so each subtree is parented at
        // Vector3.Zero with raw negate-Z (FxMeshBuilder), exactly like BudMeshBuilder for .bud.

        if (cell.Slot2Fx1 is not null)
            rendered += AttachFxSubtree(parent, FxMeshBuilder.BuildFx1(cell.Slot2Fx1, fxTexResolver), 2, cellMapXZ);
        if (cell.Slot3Fx2 is not null)
            rendered += AttachFxSubtree(parent, FxMeshBuilder.BuildFx2(cell.Slot3Fx2, fxTexResolver), 3, cellMapXZ);
        if (cell.Slot4Fx3 is not null)
            rendered += AttachFxSubtree(parent, FxMeshBuilder.BuildFx3(cell.Slot4Fx3, fxTexResolver), 4, cellMapXZ);
        if (cell.Slot5Fx4 is not null)
            rendered += AttachFxSubtree(parent, FxMeshBuilder.BuildFx4(cell.Slot5Fx4, fxTexResolver), 5, cellMapXZ);
        if (cell.Slot6Fx5 is not null)
            rendered += AttachFxSubtree(parent, FxMeshBuilder.BuildFx5(cell.Slot6Fx5, fxTexResolver), 6, cellMapXZ);
        if (cell.Slot7Fx6 is not null)
            rendered += AttachFxSubtree(parent, FxMeshBuilder.BuildFx6(cell.Slot7Fx6, fxTexResolver), 7, cellMapXZ);
        if (cell.Slot8Fx7 is not null)
            rendered += AttachFxSubtree(parent, FxMeshBuilder.BuildFx7(cell.Slot8Fx7, fxTexResolver), 8, cellMapXZ);

        if (rendered > 0)
            GD.Print($"[SlotRenderer][ComposeRender] Cell ({cellMapXZ.MapX},{cellMapXZ.MapZ}) " +
                     $"slots 2-8: {rendered} FX overlay layer(s) built (origin {cellWorldOriginGodot}). " +
                     "spec: assembly_graph.md §1; terrain_layers.md §4.2.");
        else
            GD.Print($"[SlotRenderer][ComposeRender] Cell ({cellMapXZ.MapX},{cellMapXZ.MapZ}) " +
                     "slots 2-8: all absent (no FX overlays for this cell). spec: area_inventory.md §3.2.");

        return rendered;
    }

    // ─── FX subtree attach helper ──────────────────────────────────────────────

    /// <summary>
    ///     Parents one FX layer subtree (built by <see cref="FxMeshBuilder" />) under
    ///     <paramref name="parent" /> at the world origin (FX verts are absolute world-space, so no
    ///     positional offset is applied). Returns 1 if the subtree has at least one drawn child mesh,
    ///     else 0 (every group was skipped because no texture resolved — logged by the builder, no
    ///     placeholder). spec: Docs/RE/formats/terrain_layers.md §4.2 — absolute world-space: CONFIRMED.
    /// </summary>
    private static int AttachFxSubtree(Node3D parent, Node3D fxRoot, int slotIndex, (int MapX, int MapZ) cellMapXZ)
    {
        if (fxRoot.GetChildCount() == 0)
        {
            // No drawable groups (all skipped for missing textures, or empty layer). Discard the
            // empty root rather than attaching an inert node. spec: ABSOLUTE RULE — no placeholder.
            fxRoot.QueueFree();
            return 0;
        }

        // FX vertices are absolute world-space — parent at the origin (Position defaults to zero).
        // spec: Docs/RE/formats/terrain_layers.md §4.2 — XZ within cell world AABB (absolute): CONFIRMED.
        fxRoot.Name = $"ComposedFxSlot{slotIndex}_({cellMapXZ.MapX},{cellMapXZ.MapZ})";
        parent.AddChild(fxRoot);
        return 1;
    }
}