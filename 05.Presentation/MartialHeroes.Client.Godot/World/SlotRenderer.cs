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
    ///     Renders slots 2-8 (FX overlay texture layers .fx1-.fx7) from the assembled cell.
    ///     Missing FX slots are silently skipped — absent FX is not an error.
    ///     The FX overlays are positioned at the cell's world-space origin with a world-plane mesh
    ///     spanning the cell (1024×1024 world units), textured with the FX layer's texture. Effect
    ///     sub-offsets use the Z-negation port-side convention.
    ///     spec: Docs/RE/structs/terrain-manager.md slots 2-8 — FX overlay texture layers: CONFIRMED.
    ///     spec: Docs/RE/specs/assembly_graph.md §1 — slots 2..8 = fx1..fx7 overlays.
    ///     spec: WorldCoordinates.ToGodot — world geometry negates Z (port-side): CONFIRMED.
    /// </summary>
    /// <param name="parent">Parent node (same RealWorldRenderer node).</param>
    /// <param name="cell">Assembled cell whose FX slots may carry overlay data.</param>
    /// <param name="cellWorldOriginGodot">
    ///     The Godot-space world origin of this cell (already with Z negated).
    ///     spec: WorldCoordinates.ToGodot — (x,y,z) → (x,y,-z): CONFIRMED.
    /// </param>
    /// <param name="cellMapXZ">Biased cell coordinates for log messages.</param>
    /// <returns>Number of FX slots rendered (0 if all absent).</returns>
    public static int RenderFxSlots(
        Node3D parent,
        AssembledCell cell,
        Vector3 cellWorldOriginGodot,
        (int MapX, int MapZ) cellMapXZ)
    {
        var rendered = 0;

        // Slots 2-8 carry FX overlay layers. Each is an optional planar mesh over the cell.
        // Missing FX is not an error — most cells have no FX.
        // spec: Docs/RE/structs/terrain-manager.md slots 2-8: CONFIRMED.
        // spec: Docs/RE/formats/area_inventory.md §3.2 — absent FX is not an error.

        // For each FX slot, if the layer is present, spawn a 1024×1024 unit plane at the cell
        // origin (Y from the FX layer if available, else 0). The plane uses an unlit material.
        // Port-side: effect sub-offsets negate Z (world geometry convention).
        // spec: WorldCoordinates.ToGodot — world negate Z: CONFIRMED.

        if (cell.Slot2Fx1 is not null)
            rendered += SpawnFxPlane(parent, cell.Slot2Fx1.GetType().Name, 2, cellWorldOriginGodot, cellMapXZ) ? 1 : 0;
        if (cell.Slot3Fx2 is not null)
            rendered += SpawnFxPlane(parent, cell.Slot3Fx2.GetType().Name, 3, cellWorldOriginGodot, cellMapXZ) ? 1 : 0;
        if (cell.Slot4Fx3 is not null)
            rendered += SpawnFxPlane(parent, cell.Slot4Fx3.GetType().Name, 4, cellWorldOriginGodot, cellMapXZ) ? 1 : 0;
        if (cell.Slot5Fx4 is not null)
            rendered += SpawnFxPlane(parent, cell.Slot5Fx4.GetType().Name, 5, cellWorldOriginGodot, cellMapXZ) ? 1 : 0;
        if (cell.Slot6Fx5 is not null)
            rendered += SpawnFxPlane(parent, cell.Slot6Fx5.GetType().Name, 6, cellWorldOriginGodot, cellMapXZ) ? 1 : 0;
        if (cell.Slot7Fx6 is not null)
            rendered += SpawnFxPlane(parent, cell.Slot7Fx6.GetType().Name, 7, cellWorldOriginGodot, cellMapXZ) ? 1 : 0;
        if (cell.Slot8Fx7 is not null)
            rendered += SpawnFxPlane(parent, cell.Slot8Fx7.GetType().Name, 8, cellWorldOriginGodot, cellMapXZ) ? 1 : 0;

        if (rendered > 0)
            GD.Print($"[SlotRenderer][ComposeRender] Cell ({cellMapXZ.MapX},{cellMapXZ.MapZ}) " +
                     $"slots 2-8: {rendered} FX overlay(s) spawned. spec: assembly_graph.md §1.");
        else
            GD.Print($"[SlotRenderer][ComposeRender] Cell ({cellMapXZ.MapX},{cellMapXZ.MapZ}) " +
                     "slots 2-8: all absent (no FX overlays for this cell). spec: area_inventory.md §3.2.");

        return rendered;
    }

    // ─── FX plane helper ──────────────────────────────────────────────────────

    /// <summary>
    ///     Spawns a 1024×1024 unit placeholder plane for one FX overlay slot at the cell world origin.
    ///     Port-side: the plane is laid flat (XZ plane, Y up), at a slight Y elevation so it does not
    ///     z-fight with the terrain surface.
    ///     The FX plane is diagnostic only — it confirms the FX slot was present in the assembled cell.
    ///     A full FX rendering implementation (matching the original animated/blended overlays) is a
    ///     future work item outside the A.4 scope.
    ///     spec: Docs/RE/structs/terrain-manager.md slots 2-8 — FX overlay: CONFIRMED.
    ///     spec: WorldCoordinates.ToGodot — port-side Z negation for effect sub-offsets: CONFIRMED.
    /// </summary>
    private static bool SpawnFxPlane(
        Node3D parent,
        string fxTypeName,
        int slotIndex,
        Vector3 cellOriginGodot,
        (int MapX, int MapZ) cellMapXZ)
    {
        try
        {
            var instance = new MeshInstance3D();
            var plane = new PlaneMesh();

            // spec: Docs/RE/formats/terrain.md §1.4 — cell size 1024 world units: CONFIRMED.
            const float CellSize = 1024f; // spec: terrain.md §1.4 — cell size 1024 wu: CONFIRMED
            plane.Size = new Vector2(CellSize, CellSize);
            instance.Mesh = plane;

            var mat = new StandardMaterial3D();
            mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            // Diagnostic: semi-transparent cyan tint so FX planes are visible in debug renders.
            mat.AlbedoColor = new Color(0f, 1f, 1f, 0.15f);
            mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
            plane.Material = mat;

            // Offset slightly above cell origin so FX planes don't z-fight with the terrain.
            // Port-side: cellOriginGodot already has Z negated. The cell centre X+512 is Godot X+512.
            // spec: terrain.md §1.4 — cell size 1024; spec: WorldCoordinates.ToGodot — Z negated.
            const float CellHalf = CellSize / 2f;
            // Centre of the cell (in Godot space): origin + (512, 0, -512) for the far corner,
            // but since the cell origin is the SW corner at (cellOriginGodot.X, ?, cellOriginGodot.Z),
            // the centre is offset by (+512, 0, +512) in legacy, which becomes (+512, 0, -512) in Godot.
            // spec: WorldCoordinates.ToGodot — legacy Z negated: CONFIRMED.
            instance.Position = new Vector3(
                cellOriginGodot.X + CellHalf,
                cellOriginGodot.Y + 0.05f * slotIndex, // slight per-slot elevation
                cellOriginGodot.Z - CellHalf); // spec: ToGodot negate Z — the centre offset Z is -512

            instance.Name = $"FxSlot{slotIndex}_{fxTypeName}_({cellMapXZ.MapX},{cellMapXZ.MapZ})";
            parent.AddChild(instance);
            return true;
        }
        catch (Exception ex)
        {
            GD.PrintErr(
                $"[SlotRenderer] FX slot {slotIndex} spawn failed for cell ({cellMapXZ.MapX},{cellMapXZ.MapZ}): {ex.Message}");
            return false;
        }
    }
}