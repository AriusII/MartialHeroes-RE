using Godot;
using MartialHeroes.Client.Domain.Simulation.Simulation;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Composition;
using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class RealWorldRenderer
{
    public void Initialise(ClientContext ctx, TerrainNode terrainNode)
    {
        GD.Print("[RealWorldRenderer] Initialise: start");

        _ctx = ctx;
        _terrainNode = terrainNode;

        _composeRender = ReadComposeRenderFlag();
        GD.Print($"[RealWorldRenderer] compose_render={_composeRender} " +
                 "(set compose_render=1 in client_dir.cfg or MH_COMPOSE_RENDER=1 to enable composer path).");

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

        GD.Print("[RealWorldRenderer] Initialise: loading cel toon ramp");
        CelShadeMaterialFactory.InitSession(Assets);


        GD.Print("[RealWorldRenderer] Initialise: SpawnCamera start");
        try
        {
            SpawnCamera();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealWorldRenderer] SpawnCamera failed: {ex.Message}");
        }

        GD.Print("[RealWorldRenderer] Initialise: renderer ready — world build deferred to server 4/1 " +
                 "(OnWorldEntered). spec: world_entry.md §2.3/§3.1.");
    }


    public void OnWorldEntered(int areaId, Vector3Fixed position)
    {
        if (areaId <= 0)
        {
            GD.Print($"[RealWorldRenderer] OnWorldEntered: areaId={areaId} ≤ 0 — no-op. " +
                     "spec: Docs/RE/specs/world_entry.md §2.3.");
            return;
        }

        if (Assets is null)
        {
            GD.Print($"[RealWorldRenderer] OnWorldEntered: areaId={areaId} — VFS unavailable; renders nothing. " +
                     "spec: Docs/RE/specs/world_entry.md §2.3.");
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

            TargetAreaId = areaId;

            var spawnCellApplied = TryApplySpawnCell(areaId, position);

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

            try
            {
                LoadTextureResolutionInputs();
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[RealWorldRenderer] OnWorldEntered: LoadTextureResolutionInputs failed: {ex.Message}");
            }

            try
            {
                WireEnvironmentAndWater();
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[RealWorldRenderer] OnWorldEntered: WireEnvironmentAndWater failed: {ex.Message}");
            }

            if (_mapXEffectScheduler is not null && IsInstanceValid(_mapXEffectScheduler))
            {
                var effectRenderer = GetParent()?.GetNodeOrNull<EffectRenderer>("EffectRenderer");
                if (effectRenderer is not null)
                {
                    _mapXEffectScheduler.Bind(effectRenderer, Assets);
                    GD.Print($"[RealWorldRenderer] MapXEffectScheduler.Bind() called for area {areaId}. " +
                             "spec: Docs/RE/specs/effect-scheduling.md (bind before the per-frame ambient gate runs).");
                }
                else
                {
                    GD.Print("[RealWorldRenderer] MapXEffectScheduler.Bind(): EffectRenderer not found in " +
                             "parent tree — ambient effects deferred until renderer exists.");
                }
            }

            if (_terrainNode is not null)
                try
                {
                    WireTerrainTextureResolver(_terrainNode);
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[RealWorldRenderer] OnWorldEntered: WireTerrainTextureResolver failed: {ex.Message}");
                }

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

            if (!_composeRender && !_areaContentBuilt)
            {
                BuildLegacyAreaContent();
                _areaContentBuilt = true;
            }

            GD.Print($"[RealWorldRenderer] OnWorldEntered: cold-start complete for area {areaId}. " +
                     "spec: Docs/RE/specs/world_entry.md §2.3.");
        }
        finally
        {
            _worldEntryInProgress = false;
        }
    }

    private void BuildLegacyAreaContent()
    {
        if (Assets is null) return;

        GD.Print("[RealWorldRenderer] OnWorldEntered: LoadAndSpawnBudScene start");
        try
        {
            LoadAndSpawnBudScene();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealWorldRenderer] LoadAndSpawnBudScene failed: {ex.Message}");
        }

        GD.Print("[RealWorldRenderer] OnWorldEntered: NpcRenderer.PopulateFromArea start");
        try
        {
            var npcRenderer = new NpcRenderer { Name = "NpcRenderer" };
            npcRenderer.GroundYFunc = (lx, lz) => _terrainNode?.GetGroundHeight(lx, lz, 26f) ?? 26f;
            if (_terrainNode is not null)
            {
                var terrainCapture = _terrainNode;
                npcRenderer.TryGroundYFunc = (lx, lz, out hy) =>
                    terrainCapture.TryGetGroundHeight(lx, lz, out hy);
            }

            AddChild(npcRenderer);
            npcRenderer.PopulateFromArea(Assets, TargetAreaId);

            if (_terrainNode is not null)
                _terrainNode.SectorBecameResident += npcRenderer.OnSectorBecameResident;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealWorldRenderer] NpcRenderer.PopulateFromArea failed: {ex.Message}");
        }
    }

    private bool TryApplySpawnCell(int areaId, Vector3Fixed position)
    {
        try
        {
            var (spawnLegacyX, _, spawnLegacyZ) = position.ToVector3Float();

            var (spawnCellX, spawnCellZ) = SectorGrid.WorldToSector(spawnLegacyX, spawnLegacyZ);

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
            const int DensityRadius = 1;
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
            GD.PrintErr($"[RealWorldRenderer] Server area {serverAreaId} has no .ted cells in VFS. " +
                        $"Keeping previous cell ({TargetMapX},{TargetMapZ}). " +
                        "spec: Docs/RE/specs/world_entry.md §3.1.");
        }
    }
}