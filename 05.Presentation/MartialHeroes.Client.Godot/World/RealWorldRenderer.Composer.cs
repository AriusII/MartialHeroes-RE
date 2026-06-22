// World/RealWorldRenderer.Composer.cs
//
// AreaComposer / AssembledArea wiring: CellAssembledEvent + AreaAssembledEvent handlers
// and composer-driven terrain texture resolver (CYCLE 2 Phase 2-A).
// Part of the RealWorldRenderer partial class split.

using Godot;
using MartialHeroes.Assets.Mapping;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Client.Presentation.Adapters;
using Environment = System.Environment;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class RealWorldRenderer
{
    // -------------------------------------------------------------------------
    // CYCLE 2 Phase 2-A — composer-driven event handlers
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Called by <see cref="GameLoop.DispatchEvent" /> on the main thread each frame when a
    ///     <see cref="CellAssembledEvent" /> arrives. When <see cref="_composeRender" /> is off this
    ///     is a no-op (zero regression). When on, logs the cell key and slot counts for headless
    ///     verification (A.1); future increments extend to actual rendering (A.2+).
    ///     The concrete <see cref="global::MartialHeroes.Assets.Mapping.AssembledCell" /> is reached by
    ///     casting to <see cref="AssembledCellViewAdapter" />
    ///     (the public adapter the composition root uses). Layer 05 owns Assets.Mapping and may use
    ///     the concrete type directly.
    ///     spec: Docs/RE/specs/assembly_graph.md §1 — assembled cell carries the 9 slot model.
    /// </summary>
    public void OnCellAssembled(IAssembledCellView cellView)
    {
        if (!_composeRender) return;

        // Reach the concrete AssembledCell via the layer-05 public adapter.
        // AssembledCellViewAdapter is in Adapters/ (same layer-05 project) and is not file-local.
        var cell =
            (cellView as AssembledCellViewAdapter)?.ConcreteCell;

        var slot1Count = cell?.Slot1BuildingObjectGrid?.Objects.Length ?? -1;
        var hasSlot0 = cell?.Slot0GroundTexGrid is not null ? 1 : 0;
        var fxSlots = 0;
        if (cell is not null)
        {
            if (cell.Slot2Fx1 is not null) fxSlots++;
            if (cell.Slot3Fx2 is not null) fxSlots++;
            if (cell.Slot4Fx3 is not null) fxSlots++;
            if (cell.Slot5Fx4 is not null) fxSlots++;
            if (cell.Slot6Fx5 is not null) fxSlots++;
            if (cell.Slot7Fx6 is not null) fxSlots++;
            if (cell.Slot8Fx7 is not null) fxSlots++;
        }

        GD.Print($"[RealWorldRenderer][ComposeRender] CellAssembled: cell=({cellView.MapX},{cellView.MapZ}) " +
                 $"resolved={cellView.IsResolved} slot0={hasSlot0} slot1Buildings={slot1Count} fxSlots={fxSlots}. " +
                 "spec: assembly_graph.md §1.");

        // A.2: store the assembled cell and wire the composer-driven terrain texture resolver.
        if (cell is not null)
        {
            _composedCells[(cellView.MapX, cellView.MapZ)] = cell;
            GD.Print($"[RealWorldRenderer][ComposeRender] Cell ({cellView.MapX},{cellView.MapZ}) cached " +
                     $"({_composedCells.Count} total). spec: assembly_graph.md §1.");

            // A.2: wire the terrain texture resolver from the assembled cell's pre-baked path array.
            // Only wire for the target cell (the one that matches the loaded streaming anchor) — the
            // TerrainNode resolver is shared across all sectors, so we use the cell whose texByte→path
            // mapping covers the visible streaming ring. The pre-baked ResolvedTexturePaths array
            // is indexed by patch position (0..255), where each entry is the resolved DDS path for that
            // patch. Since all patches sharing the same texByte resolve to the same path, we scan the
            // array to find the canonical path for each distinct texByte value.
            // spec: Docs/RE/specs/assembly_graph.md §1 — ResolvedTexturePaths baked by AreaComposer.
            // spec: Docs/RE/formats/terrain.md §5.6 — texByte is 1-based, clamp [1,count].
            if (_terrainNode is not null && cell.ResolvedTexturePaths is not null
                                         && cell.Slot0GroundTexGrid is not null)
                WireComposerTerrainResolver(cell);

            // A.4: render slots 2-8 (FX overlays) from the assembled cell.
            // Missing FX slots are silently skipped (no crash). Effect sub-offsets negate Z port-side.
            // spec: Docs/RE/structs/terrain-manager.md slots 2-8 — FX overlays: CONFIRMED.
            // spec: WorldCoordinates.ToGodot — world geometry negates Z (port-side): CONFIRMED.
            {
                // SW corner of the cell in Godot space (Z negated from legacy).
                // spec: terrain.md §1.4 — worldX_min = (mapX-10000)×1024; cell size 1024 wu: CONFIRMED.
                var cellLegacyX = (cellView.MapX - 10000) * 1024f; // spec: terrain.md §1.4
                var cellLegacyZ = (cellView.MapZ - 10000) * 1024f; // spec: terrain.md §1.4
                // spec: WorldCoordinates.ToGodot — (x,y,z) → (x,y,-z): CONFIRMED.
                var cellOriginGodot = new Vector3(cellLegacyX, 0f, -cellLegacyZ);

                SlotRenderer.RenderFxSlots(
                    this,
                    cell,
                    cellOriginGodot,
                    (cellView.MapX, cellView.MapZ));
            }

            // A.3: render slot 1 buildings from the assembled cell using SlotRenderer.
            // The building texture resolver uses the legacy _cellMap+_bgTextures two-hop chain
            // (same as LoadAndSpawnBudScene) so buildings get their correct textures.
            // For non-target cells (streaming ring), _cellMap may belong to the target cell;
            // this is acceptable for A.3 (single-cell demo parity).
            //
            // TODO (C12-D13 deferred merge — COMPOSER GAP #1): Per-cell .map cache required.
            // Currently _cellMap is the target-cell's .map (loaded once in LoadTextureResolutionInputs
            // for TargetMapX/Z). When OnCellAssembled fires for a NON-target cell in the streaming ring,
            // this resolver silently returns textures keyed from the WRONG cell's BUILDING TEXTURES list.
            // Fix: load and cache each cell's .map lazily inside OnCellAssembled, keyed by (MapX,MapZ),
            // and pass the correct per-cell MapDescriptor to ResolveSectionTexture. This must pass a
            // windowed visual-oracle check before this composer path can replace the legacy path.
            // See ROADMAP.md §C12-D13 and Initializer.cs BuildLegacyAreaContent gate comment.
            // spec: Docs/RE/structs/terrain-manager.md slot 1 — "mass/building-object placement grid (.bud)": CONFIRMED.
            if (cell.Slot1BuildingObjectGrid is not null)
            {
                // Guard against duplicate spawning (the same cell may arrive multiple times if the
                // streaming ring reloads it; track by cell key).
                var cellKey = (cellView.MapX, cellView.MapZ);
                if (!_composedBuildingsSpawned.Contains(cellKey))
                {
                    _composedBuildingsSpawned.Add(cellKey);

                    // Use cached building texture resolver (same lambda as legacy path).
                    var budTexCache = new Dictionary<uint, ImageTexture?>();
                    Func<uint, ImageTexture?> budTexResolver = texId =>
                    {
                        if (budTexCache.TryGetValue(texId, out var cached)) return cached;
                        var tex = ResolveSectionTexture("BUILDING", (int)texId);
                        budTexCache[texId] = tex;
                        return tex;
                    };

                    SlotRenderer.RenderSlot1Buildings(
                        this,
                        cell,
                        budTexResolver,
                        (cellView.MapX, cellView.MapZ));
                }
                else
                {
                    GD.Print($"[RealWorldRenderer][ComposeRender] Cell ({cellView.MapX},{cellView.MapZ}) " +
                             "slot 1 buildings already spawned — skipping duplicate.");
                }
            }
        }
    }

    /// <summary>
    ///     Wires <see cref="TerrainNode.TextureResolver" /> using the assembled cell's pre-baked
    ///     <see cref="global::MartialHeroes.Assets.Mapping.AssembledCell.ResolvedTexturePaths" /> array.
    ///     For each distinct raw texByte value in the <c>.ted</c> TextureIndexGrid, the resolver
    ///     scans the pre-baked path array to find the canonical resolved DDS path (the path baked by
    ///     <see cref="global::MartialHeroes.Assets.Mapping.AreaComposer.ComposeCell" /> via the same
    ///     idx-1 finalize + bgtexture.lst chain), then loads it as a Godot ImageTexture.
    ///     All patches with the same texByte value share the same resolved path (the resolution is a
    ///     function of texByte only, not position), so the scan terminates on the first match.
    ///     spec: Docs/RE/specs/assembly_graph.md §1 — ResolvedTexturePaths[256] baked by AreaComposer.
    ///     spec: Docs/RE/formats/terrain.md §CORRECTED CYCLE 1 — idx-1 finalize on texByte only: CONFIRMED.
    ///     spec: Docs/RE/formats/bgtexture_lst.md §Cross-file join — intTexId IS the 0-based pool slot, NO −1: CONFIRMED.
    /// </summary>
    private void WireComposerTerrainResolver(
        AssembledCell cell)
    {
        if (Assets is null || _terrainNode is null) return;
        if (cell.ResolvedTexturePaths is null || cell.Slot0GroundTexGrid is null) return;

        var resolvedPaths = cell.ResolvedTexturePaths;
        var textureIndexGrid = cell.Slot0GroundTexGrid.TextureIndexGrid;

        // Build a texByte → (patchIndex of first patch with that byte) map to allow O(1) lookup.
        // spec: terrain.md §5.6 — texByte 1-based; all patches with same byte share same resolved path.
        var byteToFirstPatchIndex = new Dictionary<byte, int>(256);
        for (var i = 0; i < textureIndexGrid.Length; i++)
        {
            var b = textureIndexGrid[i];
            if (!byteToFirstPatchIndex.ContainsKey(b))
                byteToFirstPatchIndex[b] = i;
        }

        var assetsCapture = Assets;

        _terrainNode.TextureResolver = texByte =>
        {
            if (_composerTexCache.TryGetValue($"tb:{texByte}", out var cached))
                return cached;

            // Clamp texByte to [1, count] then apply the idx-1 finalize.
            // spec: terrain.md §5.6 — clamp <1 to 1; >count to count: CONFIRMED.
            var gridLen = textureIndexGrid.Length;
            var clamped = texByte < 1 ? (byte)1 : (byte)Math.Min(texByte, 255);

            // Find the first patch index whose texByte (after clamping) matches.
            // Since all patches with the same byte resolve to the same path, any match works.
            if (!byteToFirstPatchIndex.TryGetValue(clamped, out var patchIdx))
            {
                // Byte not found in this cell — fall back to legacy resolver if available.
                _composerTexCache[$"tb:{texByte}"] = null;
                return null;
            }

            // Guard patch index bounds.
            if (patchIdx < 0 || patchIdx >= resolvedPaths.Length)
            {
                _composerTexCache[$"tb:{texByte}"] = null;
                return null;
            }

            var ddsPath = resolvedPaths[patchIdx];
            ImageTexture? tex = null;
            if (ddsPath is not null)
                // Cache by VFS path to share instances across texByte values that resolve to the same DDS.
                if (!_composerTexCache.TryGetValue(ddsPath, out tex))
                {
                    tex = assetsCapture.Contains(ddsPath) ? assetsCapture.LoadTexture(ddsPath) : null;
                    _composerTexCache[ddsPath] = tex;
                }

            _composerTexCache[$"tb:{texByte}"] = tex;
            return tex;
        };

        GD.Print($"[RealWorldRenderer][ComposeRender] Terrain TextureResolver wired from assembled cell " +
                 $"({cell.MapX},{cell.MapZ}) pre-baked paths ({resolvedPaths.Length} slots). " +
                 "spec: assembly_graph.md §1 — ResolvedTexturePaths baked by AreaComposer.");
    }

    /// <summary>
    ///     Called by <see cref="GameLoop.DispatchEvent" /> on the main thread each frame when an
    ///     <see cref="AreaAssembledEvent" /> arrives. No-op when <see cref="_composeRender" /> is off.
    ///     When <see cref="_composeRender" /> is ON: places actors from <paramref name="areaView" />.Spawns
    ///     via <see cref="NpcRenderer.PopulateFromSpawns" />. No-double-spawn: the composer path was
    ///     already guarded in <see cref="Initialise" /> so <see cref="NpcRenderer.PopulateFromArea" />
    ///     was skipped; this handler places actors exactly once.
    ///     Actor Y is snapped to terrain after each sector loads (TerrainNode.SectorBecameResident → the
    ///     NpcRenderer's pending-snap mechanism) — same deferred grounding as the legacy path.
    ///     spec: Docs/RE/specs/assembly_graph.md §1 (Phase A — area load → spawns).
    ///     spec: Helpers/WorldCoordinates.cs — ToGodot negate Z. CONFIRMED.
    /// </summary>
    public void OnAreaAssembled(IAssembledAreaView areaView)
    {
        if (!_composeRender) return;

        GD.Print($"[RealWorldRenderer][ComposeRender] AreaAssembled: area={areaView.AreaId} " +
                 $"cellCount={areaView.CellKeyCount} spawns={areaView.Spawns.Count}. " +
                 "spec: assembly_graph.md §1.");

        // Avoid double-spawn: guard with a flag so a re-fire of AreaAssembledEvent for the same
        // area is a no-op. (AreaAssemblyHandoff guarantees exactly-once publish per distinct area id,
        // but an additional defence is cheap.) spec: assembly_graph.md §1 (idempotent).
        if (_composerActorsAreaId == areaView.AreaId)
        {
            GD.Print($"[RealWorldRenderer][ComposeRender] AreaAssembled: area={areaView.AreaId} " +
                     "actors already placed — skipping duplicate.");
            return;
        }

        _composerActorsAreaId = areaView.AreaId;

        if (Assets is null)
        {
            GD.Print("[RealWorldRenderer][ComposeRender] AreaAssembled: assets null — cannot place actors.");
            return;
        }

        // Build and add a NpcRenderer to the scene, then drive it from the composer spawn list.
        // This reuses all of NpcRenderer's skin-lookup, TryBuildFromMobId, and pending-snap machinery.
        // NpcRenderer.PopulateFromArea is suppressed in Initialise when _composeRender is ON, so
        // this is the only placement call — no double-spawn possible.
        // spec: Docs/RE/specs/assembly_graph.md §1 — spawns placed via PopulateFromSpawns.
        try
        {
            var npcRenderer = new NpcRenderer { Name = "NpcRendererComposer" };

            // Inject the same ground-Y delegates as the legacy path (fallback 26 until terrain loads).
            npcRenderer.GroundYFunc = (lx, lz) => _terrainNode?.GetGroundHeight(lx, lz, 26f) ?? 26f;
            if (_terrainNode is not null)
            {
                var terrainCapture = _terrainNode;
                npcRenderer.TryGroundYFunc = (lx, lz, out hy) =>
                    terrainCapture.TryGetGroundHeight(lx, lz, out hy);
            }

            AddChild(npcRenderer);

            // Drive placement from the composer spawn list (not from the VFS .arr files directly).
            // spec: assembly_graph.md §1 — "spawns: npc.arr / mob.arr (position/facing/static metadata)".
            npcRenderer.PopulateFromSpawns(Assets, areaView.Spawns);

            // Wire pending-snap so actors snap to real terrain as cells stream in.
            // spec: TerrainNode.SectorBecameResident — fired on the main thread after heightmap cached.
            if (_terrainNode is not null)
                _terrainNode.SectorBecameResident += npcRenderer.OnSectorBecameResident;

            GD.Print($"[RealWorldRenderer][ComposeRender] AreaAssembled: NpcRendererComposer placed " +
                     $"from {areaView.Spawns.Count} composer spawns for area={areaView.AreaId}. " +
                     "spec: assembly_graph.md §1/§4.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealWorldRenderer][ComposeRender] AreaAssembled actor placement failed: {ex.Message}");
        }
    }

    /// <summary>
    ///     Reads the "compose_render" bool flag from <c>client_dir.cfg</c> or the env
    ///     variable <c>MH_COMPOSE_RENDER</c>. Default OFF so the legacy path is unchanged.
    /// </summary>
    private static bool ReadComposeRenderFlag()
    {
        var envVal = Environment.GetEnvironmentVariable("MH_COMPOSE_RENDER");
        if (envVal is "1" or "true" or "yes")
            return true;

        try
        {
            var absPath = ProjectSettings.GlobalizePath("res://client_dir.cfg");
            if (!File.Exists(absPath)) return false;

            foreach (var rawLine in File.ReadLines(absPath))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                var eq = line.IndexOf('=');
                if (eq < 0) continue;
                var k = line[..eq].Trim();
                var v = line[(eq + 1)..].Trim();
                if (k.Equals("compose_render", StringComparison.OrdinalIgnoreCase))
                    return v is "1" or "true" or "yes";
            }
        }
        catch
        {
            // I/O error → default false.
        }

        return false;
    }
}