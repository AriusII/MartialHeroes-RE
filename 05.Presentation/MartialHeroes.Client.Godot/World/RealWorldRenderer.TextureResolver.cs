using Godot;
using MartialHeroes.Assets.Mapping;
using MartialHeroes.Assets.Parsers.Terrain;
using MartialHeroes.Assets.Parsers.Terrain.Models;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class RealWorldRenderer
{
    private readonly Dictionary<(int MapX, int MapZ), MapDescriptor?> _cellMapCache = new();

    private void LoadTextureResolutionInputs()
    {
        if (Assets is null) return;
        var tag = AreaTag(TargetAreaId);

        try
        {
            const string lstPath = "data/map000/texture/bgtexture.lst";

            if (Assets.Contains(lstPath))
            {
                _bgTextures = BgTextureCatalog.FromLst(Assets.GetRaw(lstPath));
                GD.Print(
                    $"[RealWorldRenderer] bgtexture pool loaded from bgtexture.lst: {_bgTextures.SlotCount} slots.");
            }
            else
            {
                GD.PrintErr(
                    $"[RealWorldRenderer] bgtexture.lst absent ({lstPath}) — terrain/buildings stay untextured.");
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealWorldRenderer] bgtexture pool load failed: {ex.Message}");
        }

        try
        {
            var mapPath = $"data/map{tag}/dat/d{tag}x{TargetMapX}z{TargetMapZ}.map";
            if (Assets.Contains(mapPath))
            {
                _cellMap = MapDescriptorParser.Parse(Assets.GetRaw(mapPath));
                GD.Print($"[RealWorldRenderer] cell .map loaded: {_cellMap.Sections.Length} sections.");
            }
            else
            {
                GD.Print($"[RealWorldRenderer] cell .map absent ({mapPath}).");
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealWorldRenderer] cell .map load failed: {ex.Message}");
        }
    }

    private ImageTexture? ResolveSectionTexture(string sectionKeyword, int oneBasedIndex)
    {
        if (Assets is null || _bgTextures is null || _cellMap is null) return null;

        var list = GetSectionTextures(sectionKeyword);
        if (list is null || list.Length == 0) return null;

        var b = oneBasedIndex < 1 || oneBasedIndex > list.Length ? 1 : oneBasedIndex;
        var intTexId = list[b - 1].TexId;

        var ddsPath = _bgTextures.ResolveTexturePath(intTexId);
        if (ddsPath is null) return null;

        return Assets.Contains(ddsPath) ? Assets.LoadTexture(ddsPath) : null;
    }

    private (int Flag, int TexId)[]? GetSectionTextures(string keyword)
    {
        if (_cellMap is null) return null;
        foreach (var section in _cellMap.Sections)
            if (string.Equals(section.Keyword, keyword, StringComparison.OrdinalIgnoreCase))
                return section.Textures;

        return null;
    }

    private MapDescriptor? GetOrLoadCellMap(int mapX, int mapZ)
    {
        if (Assets is null) return null;

        var key = (mapX, mapZ);
        if (_cellMapCache.TryGetValue(key, out var cached)) return cached;

        MapDescriptor? map = null;
        try
        {
            var tag = AreaTag(TargetAreaId);
            var mapPath = $"data/map{tag}/dat/d{tag}x{mapX}z{mapZ}.map";
            if (Assets.Contains(mapPath))
                map = MapDescriptorParser.Parse(Assets.GetRaw(mapPath));
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealWorldRenderer] per-cell .map load failed for ({mapX},{mapZ}): {ex.Message}");
        }

        _cellMapCache[key] = map;
        return map;
    }

    private ImageTexture? ResolveSectionTextureForCell(MapDescriptor cellMap, string sectionKeyword, int oneBasedIndex)
    {
        if (Assets is null || _bgTextures is null) return null;

        (int Flag, int TexId)[]? list = null;
        foreach (var section in cellMap.Sections)
            if (string.Equals(section.Keyword, sectionKeyword, StringComparison.OrdinalIgnoreCase))
            {
                list = section.Textures;
                break;
            }

        if (list is null || list.Length == 0) return null;

        var b = oneBasedIndex < 1 || oneBasedIndex > list.Length ? 1 : oneBasedIndex;
        var intTexId = list[b - 1].TexId;

        var ddsPath = _bgTextures.ResolveTexturePath(intTexId);
        if (ddsPath is null) return null;

        return Assets.Contains(ddsPath) ? Assets.LoadTexture(ddsPath) : null;
    }

    private void WireTerrainTextureResolver(TerrainNode terrainNode)
    {
        if (Assets is null) return;

        var texCache = new Dictionary<(int, int, int), ImageTexture?>();
        var loggedOnce = false;

        terrainNode.TextureResolver = (texByte, mapX, mapZ) =>
        {
            var key = (mapX, mapZ, texByte);
            if (texCache.TryGetValue(key, out var cached)) return cached;

            var cellMap = GetOrLoadCellMap(mapX, mapZ);
            var tex = cellMap is null ? null : ResolveSectionTextureForCell(cellMap, "TERRAIN", texByte);
            if (tex is not null && !loggedOnce)
            {
                GD.Print(
                    $"[RealWorldRenderer] Terrain texture resolved for byte {texByte} at cell ({mapX},{mapZ}) (area {TargetAreaId}).");
                loggedOnce = true;
            }

            texCache[key] = tex;
            return tex;
        };

        GD.Print(
            $"[RealWorldRenderer] Terrain TextureResolver wired (per-cell .map, 2-hop bgtexture chain) for area {TargetAreaId}.");
    }
}