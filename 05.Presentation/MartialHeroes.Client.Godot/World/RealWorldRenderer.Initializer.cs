// World/RealWorldRenderer.Initializer.cs
//
// Area/scene initialization and server-driven world-entry cold-start.
// Part of the RealWorldRenderer partial class split.

using Godot;
using MartialHeroes.Client.Domain.Simulation.Simulation;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Composition;
using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class RealWorldRenderer
{
    /// <summary>
    ///     Called by GameLoop._Ready. Performs synchronous BUD/character loading and node creation;
    ///     kicks off the async 3×3 terrain-ring streaming in a fire-and-forget task.
    ///     Each step is individually guarded: a failure in one step is logged and skipped;
    ///     subsequent steps still run. This ensures the window always opens even if asset
    ///     loading partially fails on real data.
    ///     IMPORTANT: GltfDocument.AppendFromBuffer is NOT called anywhere in this method or
    ///     its callees. All geometry is built as ArrayMesh via BudMeshBuilder.
    /// </summary>
    public void Initialise(ClientContext ctx, TerrainNode terrainNode)
    {
        GD.Print("[RealWorldRenderer] Initialise: start");

        _ctx = ctx;
        _terrainNode = terrainNode;

        // Read the compose_render flag BEFORE any asset loading so derived paths can gate on it.
        // Default OFF: the legacy direct-from-VFS path runs unchanged (zero regression).
        _composeRender = ReadComposeRenderFlag();
        GD.Print($"[RealWorldRenderer] compose_render={_composeRender} " +
                 "(set compose_render=1 in client_dir.cfg or MH_COMPOSE_RENDER=1 to enable composer path).");

        // Open the VFS — falls back gracefully to null if absent.
        GD.Print("[RealWorldRenderer] Initialise: opening VFS");
        try
        {
            Assets = RealClientAssets.TryOpen();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealWorldRenderer] RealClientAssets.TryOpen threw: {ex.Message}");
            Assets = null;
        }

        if (Assets is null)
        {
            GD.Print("[RealWorldRenderer] No real assets available — skipping real-asset render.");
            return;
        }

        // Initialise the cel-shading session: loads toonramp.bmp from the VFS and caches it.
        // All subsequent SkinnedCharacterBuilder / SkinnedCharacterNode builds pick up the ramp.
        // spec: Docs/RE/formats/shaders.md §C5.3 — toonramp.bmp 1-D N·L ramp, stage 1.
        // spec: Docs/RE/specs/rendering.md §5.2 — dotoonshading path = skinned character only.
        GD.Print("[RealWorldRenderer] Initialise: loading cel toon ramp");
        CelShadeMaterialFactory.InitSession(Assets);

        // Resolve the target area and cell.
        // The area id is read from client_dir.cfg (key "area="), defaulting to 0.
        // The cell is discovered by enumerating real VFS entries for that area.
        // If the configured area has no cells, we auto-select the first area that does.
        // This ensures we NEVER target a non-existent cell (fixing the (10000,10000) bug).
        GD.Print("[RealWorldRenderer] Initialise: resolving target cell");
        try
        {
            ResolveTargetCell();
        }
        catch (Exception ex)
        {
            GD.PrintErr(
                $"[RealWorldRenderer] ResolveTargetCell failed: {ex.Message} — keeping default ({TargetMapX},{TargetMapZ}).");
        }

        GD.Print($"[RealWorldRenderer] Target cell resolved to ({TargetMapX},{TargetMapZ}) for area {TargetAreaId}.");

        // Load the texture-resolution inputs once: the global bgtexture pool (runtime bgtexture.lst,
        // with the .txt mirror as dev/loose-tree fallback) and the cell's .map (per-section TEXTURES
        // lists). spec: asset_pipeline.md §3 chain B + terrain.md §3.5. CONFIRMED.
        LoadTextureResolutionInputs();

        // Atmosphere (EnvironmentNode): assemble the area's sky/fog/light from the parsed per-area
        // environment bins (map_option/fog/light/material). RECONCILED Campaign 5: map_option carries
        // no water field, so no map_option-driven water plane is placed (per-cell water presence is
        // detected separately from .map FX texture names).
        // spec: Docs/RE/specs/environment.md §3 (assembly) + §4 (water RESOLVED-NEGATIVE).
        GD.Print("[RealWorldRenderer] Initialise: wiring environment + water");
        try
        {
            WireEnvironmentAndWater();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealWorldRenderer] WireEnvironmentAndWater failed: {ex.Message}");
        }

        // Wire the texture resolver into TerrainNode so each sector can get a real texture.
        // spec: Docs/RE/formats/terrain.md §5.6 Block 3 — 1-based TextureIndexGrid → texture path.
        GD.Print("[RealWorldRenderer] Initialise: wiring terrain texture resolver");
        try
        {
            WireTerrainTextureResolver(terrainNode);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealWorldRenderer] WireTerrainTextureResolver failed: {ex.Message}");
        }

        // Kick off 5×5 terrain streaming via SectorStreamingService.
        // spec: Docs/RE/formats/terrain.md §12.2 (5×5 ring, StreamQuality.High).
        GD.Print("[RealWorldRenderer] Initialise: triggering terrain streaming");
        try
        {
            TriggerTerrainStreaming(ctx);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealWorldRenderer] TriggerTerrainStreaming failed: {ex.Message}");
        }

        // Check load_models flag before loading .bud and .skn.
        // Set load_models=false in client_dir.cfg to render terrain only (safe fallback).
        var loadModels = ClientPathResolver.LoadModelsEnabled();
        GD.Print($"[RealWorldRenderer] Initialise: load_models={loadModels}");

        if (loadModels)
        {
            // Load BUD scene and create MeshInstance3D children via ArrayMesh (no GltfDocument).
            // FLAG-GATE (CYCLE 2 Phase 2-A): when compose_render is ON, buildings are placed from
            // CellAssembledEvent → OnCellAssembled → SlotRenderer.RenderSlot1Buildings for each
            // streamed cell. Suppress the legacy single-cell LoadAndSpawnBudScene to avoid double-
            // spawning the target cell's buildings (legacy once + compose once = 2×).
            // The legacy path runs unchanged when compose_render is OFF (zero regression).
            // spec: Docs/RE/specs/assembly_graph.md §1 — slot 1 buildings come from the assembled cell.
            if (_composeRender)
            {
                GD.Print("[RealWorldRenderer] Initialise: compose_render=ON — LoadAndSpawnBudScene " +
                         "SUPPRESSED (buildings placed per-cell from CellAssembledEvent via OnCellAssembled). " +
                         "spec: assembly_graph.md §1.");
            }
            else
            {
                GD.Print("[RealWorldRenderer] Initialise: LoadAndSpawnBudScene start");
                try
                {
                    LoadAndSpawnBudScene();
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[RealWorldRenderer] LoadAndSpawnBudScene failed: {ex.Message}");
                }

                GD.Print("[RealWorldRenderer] Initialise: LoadAndSpawnBudScene done");
            }

            // Spawn skinned character static pose (if available) via ArrayMesh (no GltfDocument).
            GD.Print("[RealWorldRenderer] Initialise: LoadAndSpawnCharacter start");
            try
            {
                LoadAndSpawnCharacter();
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[RealWorldRenderer] LoadAndSpawnCharacter failed: {ex.Message}");
            }

            GD.Print("[RealWorldRenderer] Initialise: LoadAndSpawnCharacter done");

            // Populate the area with monsters/NPCs from mob*.arr + npc*.arr (static characters,
            // resolved via the mob_id -> actormotion -> skin chain). Areas with no spawns are no-ops.
            //
            // FLAG-GATE (CYCLE 2 Phase 2-A.5): when compose_render is ON, actor placement is driven
            // by the AreaAssembledEvent → OnAreaAssembled composer path (see below). In that case,
            // NpcRenderer.PopulateFromArea is SUPPRESSED to avoid double-spawning the same actors.
            // When compose_render is OFF, the legacy NpcRenderer path runs unchanged (zero regression).
            // spec: Docs/RE/specs/assembly_graph.md §1 (Phase A — spawns reach layer-05 via event bus).
            if (_composeRender)
            {
                GD.Print("[RealWorldRenderer] Initialise: compose_render=ON — NpcRenderer.PopulateFromArea " +
                         "SUPPRESSED (actors placed from AreaAssembledEvent.Spawns via OnAreaAssembled). " +
                         "spec: assembly_graph.md §1/§4.");
            }
            else
            {
                GD.Print("[RealWorldRenderer] Initialise: NpcRenderer.PopulateFromArea start");
                try
                {
                    var npcRenderer = new NpcRenderer { Name = "NpcRenderer" };
                    // Sample terrain height (legacy worldX/worldZ); falls back to 26 until sectors load.
                    npcRenderer.GroundYFunc = (lx, lz) => _terrainNode?.GetGroundHeight(lx, lz, 26f) ?? 26f;
                    // TryGroundYFunc returns false (not just a fallback constant) when the sector is absent —
                    // used by the pending-snap mechanism to snap only when real data is available.
                    // spec: TerrainNode.TryGetGroundHeight — returns false when cell absent: CONFIRMED.
                    if (_terrainNode is not null)
                    {
                        var terrainCapture = _terrainNode;
                        npcRenderer.TryGroundYFunc = (lx, lz, out hy) =>
                            terrainCapture.TryGetGroundHeight(lx, lz, out hy);
                    }

                    AddChild(npcRenderer);
                    npcRenderer.PopulateFromArea(Assets, TargetAreaId);

                    // Wire the sector-resident notification so actors are re-grounded as soon as each
                    // cell's heightmap arrives — eliminates the fallback-Y race (D2).
                    // TerrainNode fires SectorBecameResident on the Godot main thread (GameLoop._Process)
                    // after the cell enters its height-lookup cache.
                    // spec: TerrainNode.SectorBecameResident — fired after _cellCache updated: CONFIRMED.
                    if (_terrainNode is not null)
                        _terrainNode.SectorBecameResident += npcRenderer.OnSectorBecameResident;
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[RealWorldRenderer] NpcRenderer.PopulateFromArea failed: {ex.Message}");
                }

                GD.Print("[RealWorldRenderer] Initialise: NpcRenderer.PopulateFromArea done");
            }
        }
        else
        {
            GD.Print("[RealWorldRenderer] Initialise: load_models=false — skipping BUD and character.");
        }

        // Position a camera above the origin cell centre.
        // spec: Docs/RE/formats/terrain.md §1.4 — worldX_min = (mapX-10000)×1024, cell size 1024. CONFIRMED.
        GD.Print("[RealWorldRenderer] Initialise: SpawnCamera start");
        try
        {
            SpawnCamera();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealWorldRenderer] SpawnCamera failed: {ex.Message}");
        }

        GD.Print($"[RealWorldRenderer] Initialise: complete for cell ({TargetMapX},{TargetMapZ}).");
    }

    // -------------------------------------------------------------------------
    // Server-driven area cold-start (called from GameLoop on InGameWorldBootstrappedEvent)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Re-targets the renderer to <paramref name="areaId" /> and re-runs the area cold-start
    ///     (texture resolver, environment, terrain streaming) for the new area.
    ///     Called by <see cref="GameLoop" /> when <c>InGameWorldBootstrappedEvent</c> arrives (the 4/1
    ///     world-entry carrier). This makes the area cold-start <b>server-driven</b>: the AreaId at
    ///     4/1 body offset 12 selects the on-disk area, and the config "area=" key becomes an
    ///     OFFLINE-DEMO fallback only (Initialise still uses it when no live 4/1 supplies a different
    ///     id — preserving the demo path exactly as before).
    ///     Behaviour:
    ///     <list type="bullet">
    ///         <item>
    ///             <see cref="IClientEvent">areaId</see> ≤ 0 → no-op (spec: 4/1 AreaId checked ≠ 0).
    ///             spec: Docs/RE/specs/world_entry.md §2.3.
    ///         </item>
    ///         <item>
    ///             <c>_assets</c> null (offline, Initialise bailed) → log and return; the offline
    ///             demo area is preserved exactly as before.
    ///         </item>
    ///         <item>
    ///             <c>areaId == TargetAreaId</c> AND already initialised → no-op (avoid double-init /
    ///             races; logs "already on area N").
    ///         </item>
    ///         <item>
    ///             Otherwise → retarget, re-resolve the spawn-density cell for the new area, re-run
    ///             texture resolver + environment + streaming for the new area. The camera is NOT
    ///             re-spawned (it is already in the scene tree from Initialise). Actor placement for the
    ///             new area's NPCs/mobs comes through the 4/4 ActorSpawnedEvent stream handled by
    ///             ActorRegistry — NpcRenderer is NOT re-run here.
    ///         </item>
    ///     </list>
    ///     spec: Docs/RE/specs/world_entry.md §2.3 — 4/1 reads AreaId, cold-starts the area by its
    ///     3-digit decimal directory; area cold-start happens INSIDE the 4/1 handler.
    ///     spec: Docs/RE/specs/world_entry.md §3.1 — AreaId → zero-padded 3-digit dir → &lt;id&gt;.lst.
    /// </summary>
    public void OnWorldEntered(int areaId, Vector3Fixed position)
    {
        // spec: Docs/RE/specs/world_entry.md §2.3 — AreaId is checked != 0 before use.
        if (areaId <= 0)
        {
            GD.Print($"[RealWorldRenderer] OnWorldEntered: areaId={areaId} ≤ 0 — no-op. " +
                     "spec: Docs/RE/specs/world_entry.md §2.3.");
            return;
        }

        if (Assets is null)
        {
            // Offline path (Initialise bailed with no VFS): the offline demo area is preserved.
            // spec: Docs/RE/specs/world_entry.md §2.3 (server AreaId drives cold-start; config is fallback).
            GD.Print($"[RealWorldRenderer] OnWorldEntered: areaId={areaId} — no real assets (offline demo); " +
                     "offline demo area kept. spec: Docs/RE/specs/world_entry.md §2.3.");
            return;
        }

        if (areaId == TargetAreaId && _followAnchorArmed)
        {
            GD.Print($"[RealWorldRenderer] OnWorldEntered: already on area {areaId} — no-op.");
            return;
        }

        if (_worldEntryInProgress)
        {
            GD.Print($"[RealWorldRenderer] OnWorldEntered: area={areaId} — re-entry while in-progress; skipped.");
            return;
        }

        _worldEntryInProgress = true;
        try
        {
            GD.Print($"[RealWorldRenderer] OnWorldEntered: retargeting to server area {areaId}. " +
                     "spec: Docs/RE/specs/world_entry.md §2.3/§3.1.");

            // Override TargetAreaId with the server-supplied value BEFORE re-resolving the cell,
            // so ResolveTargetCell reads the correct area instead of the config area.
            // spec: Docs/RE/specs/world_entry.md §3.1 — AreaId → 3-digit dir → cold-start.
            TargetAreaId = areaId;

            // Derive the streaming anchor from the 4/1 spawn position.
            // spec: Docs/RE/specs/world_entry.md §2.3 — SpawnX/SpawnZ read from the 4/1 body.
            // spec: Docs/RE/specs/world_entry.md §3.1 — first-ring load centred on the spawn position.
            var spawnCellApplied = TryApplySpawnCell(areaId, position);

            // Re-resolve the spawn-density cell for the new area (fallback when spawn cell is absent).
            // When the spawn cell was valid and already applied above, this is still called to
            // confirm/refresh the area's cell list state, but TargetMapX/Z has already been set.
            // spec: Docs/RE/specs/world_entry.md §3.1 — AreaId → 3-digit dir; on-disk area must have cells.
            if (!spawnCellApplied)
                try
                {
                    ResolveTargetCellForServerArea(areaId);
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[RealWorldRenderer] OnWorldEntered: ResolveTargetCellForServerArea failed: " +
                                $"{ex.Message} — keeping defaults ({TargetMapX},{TargetMapZ}).");
                }

            GD.Print($"[RealWorldRenderer] OnWorldEntered: area={areaId} cell=({TargetMapX},{TargetMapZ}).");

            // Re-load texture-resolution inputs for the new area.
            // spec: Docs/RE/specs/asset_pipeline.md §3 chain B (runtime = bgtexture.lst).
            try
            {
                LoadTextureResolutionInputs();
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[RealWorldRenderer] OnWorldEntered: LoadTextureResolutionInputs failed: {ex.Message}");
            }

            // Re-wire the environment (sky/fog/light) for the new area.
            // spec: Docs/RE/specs/environment.md §3 (assembly).
            try
            {
                WireEnvironmentAndWater();
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[RealWorldRenderer] OnWorldEntered: WireEnvironmentAndWater failed: {ex.Message}");
            }

            // Re-wire terrain texture resolver for the new area.
            // spec: Docs/RE/formats/terrain.md §5.6 — 1-based TextureIndexGrid → texture path.
            if (_terrainNode is not null)
                try
                {
                    WireTerrainTextureResolver(_terrainNode);
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[RealWorldRenderer] OnWorldEntered: WireTerrainTextureResolver failed: {ex.Message}");
                }

            // Kick off terrain streaming centred on the new area's spawn-density cell.
            // spec: Docs/RE/formats/terrain.md §12.2 (5×5 ring streaming).
            // spec: Docs/RE/specs/world_entry.md §3.1 — terrain first-ring load centred on spawn.
            if (_ctx is not null)
                try
                {
                    TriggerTerrainStreaming(_ctx);
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[RealWorldRenderer] OnWorldEntered: TriggerTerrainStreaming failed: {ex.Message}");
                }
            else
                GD.PrintErr("[RealWorldRenderer] OnWorldEntered: _ctx is null — terrain streaming not triggered.");

            GD.Print($"[RealWorldRenderer] OnWorldEntered: cold-start complete for area {areaId}. " +
                     "spec: Docs/RE/specs/world_entry.md §2.3.");
        }
        finally
        {
            _worldEntryInProgress = false;
        }
    }

    /// <summary>
    ///     Like <see cref="ResolveTargetCell" /> but uses the given <paramref name="serverAreaId" />
    ///     directly instead of reading "area=" from config. Called by <see cref="OnWorldEntered" />
    ///     so the server-supplied AreaId drives cell discovery rather than the config fallback.
    ///     Resolution:
    ///     1. Enumerate .ted entries for <paramref name="serverAreaId" />.
    ///     2. If cells found, compute spawn-density anchor + pick ring centre (same as Initialise).
    ///     3. If no cells for that area, fall back to terrain centroid or default coordinates.
    ///     spec: Docs/RE/specs/world_entry.md §3.1 — AreaId → 3-digit dir; on-disk area must have cells.
    ///     spec: Docs/RE/formats/terrain.md §1.3 — per-cell path pattern. CONFIRMED.
    /// </summary>
    /// <summary>
    ///     Derives the spawn-position cell from the 4/1 <paramref name="position" /> and validates it
    ///     against the VFS cell list for <paramref name="areaId" />. When valid, writes
    ///     <see cref="TargetMapX" /> / <see cref="TargetMapZ" /> and returns <c>true</c>.
    ///     When absent or invalid, returns <c>false</c> without mutating target state.
    ///     Convention: <paramref name="position" /> is LEGACY WORLD SPACE — no Z-negation here; that
    ///     negation applies only when converting TO Godot-space for node placement.
    ///     spec: Helpers/WorldCoordinates.ToGodot — legacy Z → Godot -Z.
    ///     spec: Docs/RE/specs/world_entry.md §2.3 — spawn position seeds terrain anchor. CONFIRMED.
    ///     spec: Docs/RE/formats/terrain.md §1.4 — cell = floor(coord/1024)+10000. CONFIRMED.
    /// </summary>
    private bool TryApplySpawnCell(int areaId, Vector3Fixed position)
    {
        try
        {
            var (spawnLegacyX, _, spawnLegacyZ) = position.ToVector3Float();

            // Biased cell from the server spawn position.
            // spec: Docs/RE/formats/terrain.md §1.4 — origin bias 10000, cell size 1024 wu.
            var (spawnCellX, spawnCellZ) = SectorGrid.WorldToSector(spawnLegacyX, spawnLegacyZ);

            // Validate against the VFS cell list for this area.
            var areaCells = Assets?.EnumerateTerrainCells(areaId);
            var spawnCellValid = areaCells is not null &&
                                 areaCells.Any(c => c.MapX == spawnCellX && c.MapZ == spawnCellZ);

            if (spawnCellValid)
            {
                TargetMapX = spawnCellX;
                TargetMapZ = spawnCellZ;
                GD.Print(
                    $"[RealWorldRenderer] OnWorldEntered area={areaId} spawn-cell=({TargetMapX},{TargetMapZ}) " +
                    $"from 4/1 spawn (legacy XZ=({spawnLegacyX:F0},{spawnLegacyZ:F0})). " +
                    "spec: world_entry.md §2.3/§3.1.");
                return true;
            }

            GD.Print($"[RealWorldRenderer] OnWorldEntered: spawn cell ({spawnCellX},{spawnCellZ}) " +
                     $"not in VFS for area {areaId} (legacy XZ=({spawnLegacyX:F0},{spawnLegacyZ:F0})) " +
                     "— falling back to VFS-enumeration anchor. spec: world_entry.md §2.3/§3.1.");
            return false;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealWorldRenderer] TryApplySpawnCell: derivation failed: {ex.Message} " +
                        "— falling back to VFS-enumeration anchor.");
            return false;
        }
    }

    private void ResolveTargetCellForServerArea(int serverAreaId)
    {
        if (Assets is null) return;

        var ringRadius = ReadRingRadiusFromConfig();
        var cells = Assets.EnumerateTerrainCells(serverAreaId);
        if (cells.Count > 0)
        {
            const int DensityRadius = 1; // always 3×3 neighbourhood for anchor detection.
            var (anchorX, anchorZ) = ComputeSpawnAnchor(Assets, serverAreaId, cells, DensityRadius);
            var (mx, mz, fullRing) = PickRingCenter(cells, anchorX, anchorZ, ringRadius);
            TargetAreaId = serverAreaId;
            TargetMapX = mx;
            TargetMapZ = mz;
            GD.Print($"[RealWorldRenderer] Server area {serverAreaId}: {cells.Count} cells → " +
                     $"cell ({TargetMapX},{TargetMapZ}) " +
                     $"(anchor=({anchorX:F1},{anchorZ:F1}), fullRing={fullRing}). " +
                     "spec: Docs/RE/specs/world_entry.md §3.1.");
        }
        else
        {
            // No cells on disk for this area — keep whatever Initialise resolved (or defaults).
            // spec: Docs/RE/formats/terrain.md §12.3 — absent cells load empty without crash. CONFIRMED.
            GD.PrintErr($"[RealWorldRenderer] Server area {serverAreaId} has no .ted cells in VFS. " +
                        $"Keeping previous cell ({TargetMapX},{TargetMapZ}). " +
                        "spec: Docs/RE/specs/world_entry.md §3.1.");
        }
    }
}