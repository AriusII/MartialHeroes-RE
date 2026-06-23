using Godot;
using MartialHeroes.Assets.Mapping;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Client.Presentation.Adapters;
using Environment = System.Environment;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class RealWorldRenderer
{
    public void OnCellAssembled(IAssembledCellView cellView)
    {
        var cellForCollision = (cellView as AssembledCellViewAdapter)?.ConcreteCell;
        _cellCollisionManager?.RegisterCell(
            cellView.MapX, cellView.MapZ,
            cellForCollision?.Collision);

        if (!_composeRender) return;

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

        if (cell is not null)
        {
            _composedCells[(cellView.MapX, cellView.MapZ)] = cell;
            GD.Print($"[RealWorldRenderer][ComposeRender] Cell ({cellView.MapX},{cellView.MapZ}) cached " +
                     $"({_composedCells.Count} total). spec: assembly_graph.md §1.");

            if (_terrainNode is not null && cell.ResolvedTexturePaths is not null
                                         && cell.Slot0GroundTexGrid is not null)
                WireComposerTerrainResolver(cell);

            {
                var cellLegacyX = (cellView.MapX - 10000) * 1024f;
                var cellLegacyZ = (cellView.MapZ - 10000) * 1024f;
                var cellOriginGodot = new Vector3(cellLegacyX, 0f, -cellLegacyZ);

                SlotRenderer.RenderFxSlots(
                    this,
                    cell,
                    cellOriginGodot,
                    (cellView.MapX, cellView.MapZ),
                    (channel, texIdx) => ResolveSectionTexture("FX" + channel, (int)texIdx));
            }

            if (cell.Slot1BuildingObjectGrid is not null)
            {
                var cellKey = (cellView.MapX, cellView.MapZ);
                if (!_composedBuildingsSpawned.Contains(cellKey))
                {
                    _composedBuildingsSpawned.Add(cellKey);

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

    private void WireComposerTerrainResolver(
        AssembledCell cell)
    {
        if (Assets is null || _terrainNode is null) return;
        if (cell.ResolvedTexturePaths is null || cell.Slot0GroundTexGrid is null) return;

        var resolvedPaths = cell.ResolvedTexturePaths;
        var textureIndexGrid = cell.Slot0GroundTexGrid.TextureIndexGrid;

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

            var gridLen = textureIndexGrid.Length;
            var clamped = texByte < 1 ? (byte)1 : (byte)Math.Min(texByte, 255);

            if (!byteToFirstPatchIndex.TryGetValue(clamped, out var patchIdx))
            {
                _composerTexCache[$"tb:{texByte}"] = null;
                return null;
            }

            if (patchIdx < 0 || patchIdx >= resolvedPaths.Length)
            {
                _composerTexCache[$"tb:{texByte}"] = null;
                return null;
            }

            var ddsPath = resolvedPaths[patchIdx];
            ImageTexture? tex = null;
            if (ddsPath is not null)
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

    public void OnAreaAssembled(IAssembledAreaView areaView)
    {
        if (!_composeRender) return;

        GD.Print($"[RealWorldRenderer][ComposeRender] AreaAssembled: area={areaView.AreaId} " +
                 $"cellCount={areaView.CellKeyCount} spawns={areaView.Spawns.Count}. " +
                 "spec: assembly_graph.md §1.");

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

        try
        {
            var npcRenderer = new NpcRenderer { Name = "NpcRendererComposer" };

            npcRenderer.GroundYFunc = (lx, lz) => _terrainNode?.GetGroundHeight(lx, lz, 26f) ?? 26f;
            if (_terrainNode is not null)
            {
                var terrainCapture = _terrainNode;
                npcRenderer.TryGroundYFunc = (lx, lz, out hy) =>
                    terrainCapture.TryGetGroundHeight(lx, lz, out hy);
            }

            AddChild(npcRenderer);

            npcRenderer.PopulateFromSpawns(Assets, areaView.Spawns);

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
        }

        return false;
    }
}