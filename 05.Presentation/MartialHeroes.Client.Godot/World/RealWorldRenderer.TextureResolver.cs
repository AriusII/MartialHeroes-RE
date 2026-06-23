using Godot;
using MartialHeroes.Assets.Mapping;
using MartialHeroes.Assets.Parsers.Terrain;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class RealWorldRenderer
{
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

    private void WireTerrainTextureResolver(TerrainNode terrainNode)
    {
        if (Assets is null) return;

        var texCache = new Dictionary<int, ImageTexture?>();
        var loggedOnce = false;

        terrainNode.TextureResolver = texByte =>
        {
            if (texCache.TryGetValue(texByte, out var cached)) return cached;

            var tex = ResolveSectionTexture("TERRAIN", texByte);
            if (tex is not null && !loggedOnce)
            {
                GD.Print($"[RealWorldRenderer] Terrain texture resolved for byte {texByte} (area {TargetAreaId}).");
                loggedOnce = true;
            }

            texCache[texByte] = tex;
            return tex;
        };

        GD.Print($"[RealWorldRenderer] Terrain TextureResolver wired (2-hop bgtexture chain) for area {TargetAreaId}.");
    }
}