// World/RealWorldRenderer.TextureResolver.cs
//
// Terrain/building texture resolution chain:
//   .ted byte → .map TEXTURES[idx-1].intTexId → bgtexture.lst pool[intTexId] → data/map000/texture/<rel>.dds
// Part of the RealWorldRenderer partial class split.

using Godot;
using MartialHeroes.Assets.Mapping;
using MartialHeroes.Assets.Parsers.Terrain;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class RealWorldRenderer
{
    /// <summary>
    ///     Loads the inputs for the two-hop terrain/building texture resolution: the global
    ///     background-texture pool and the target cell's <c>.map</c> descriptor.
    ///     The pool is loaded from the BINARY <c>data/map000/texture/bgtexture.lst</c> — the runtime
    ///     form the original client actually consumes — via
    ///     <see cref="global::MartialHeroes.Assets.Mapping.BgTextureCatalog.FromLst" />.
    ///     The human-readable <c>bgtexture.txt</c> mirror is ABSENT from a real packed <c>data.vfs</c>,
    ///     so it is used only as a dev / loose-tree fallback when the <c>.lst</c> is missing. The pool +
    ///     the texture <c>.dds</c> are GLOBAL under <c>map000</c> for ALL areas (there is no per-area pool).
    ///     spec: Docs/RE/specs/asset_pipeline.md §3 chain B — runtime opens <c>bgtexture.lst</c>;
    ///     <c>bgtexture.txt</c> absent from the image (the <c>.lst</c> wins). CONFIRMED.
    ///     spec: Docs/RE/formats/bgtexture_lst.md — u32 count, 48-byte records → index-keyed pool. CONFIRMED.
    ///     spec: Docs/RE/formats/terrain.md §3.5 (.map TEXTURES). CONFIRMED.
    /// </summary>
    private void LoadTextureResolutionInputs()
    {
        if (Assets is null) return;
        var tag = AreaTag(TargetAreaId);

        try
        {
            // Runtime form: the binary bgtexture.lst. spec: asset_pipeline.md §3 chain B —
            // the loader opens data/map000/texture/bgtexture.lst (global map000 pool). CONFIRMED.
            // bgtexture.txt is absent from a real packed data.vfs; FromTxt is not called here.
            // spec: Docs/RE/formats/bgtexture_lst.md — packed VFS-only source. CONFIRMED.
            const string lstPath = "data/map000/texture/bgtexture.lst";

            if (Assets.Contains(lstPath))
            {
                // spec: Docs/RE/formats/bgtexture_lst.md — binary index-keyed pool. CONFIRMED.
                _bgTextures = BgTextureCatalog.FromLst(Assets.GetRaw(lstPath));
                GD.Print(
                    $"[RealWorldRenderer] bgtexture pool loaded from bgtexture.lst: {_bgTextures.SlotCount} slots.");
            }
            else
            {
                // Hard failure path: bgtexture.lst is the ONLY real source; .txt is not a fallback.
                // spec: Docs/RE/specs/asset_pipeline.md §3 chain B — runtime opens bgtexture.lst (packed VFS). CONFIRMED.
                // spec: Docs/RE/formats/bgtexture_lst.md — absent means no texture pool; terrain/buildings stay untextured.
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
            // spec: Docs/RE/formats/terrain.md §1.3 per-cell path; §3.5 .map TEXTURES. CONFIRMED.
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

    /// <summary>
    ///     Resolves a 1-based <c>.ted</c> per-cell byte (from <see cref="TerrainNode" />) or a BUD object to
    ///     a Godot <see cref="ImageTexture" /> via the IDA-confirmed chain (263bd994): the byte is clamped to
    ///     <c>[1, count]</c> (both <c>&lt;1</c> and <c>&gt;count</c> → 1), then <c>TEXTURES[byte-1].intTexId</c>
    ///     (the ONLY <c>-1</c>, on the byte — IDA 0x44b296), then the <c>bgtexture.lst</c> pool indexed
    ///     DIRECTLY by <c>intTexId</c> (0-based, NO <c>-1</c> — IDA 0x445833 / 0x44a46d) →
    ///     <c>data/map000/texture/&lt;rel&gt;.dds</c>.
    ///     spec: Docs/RE/formats/terrain.md §3.5 + §5.6; Docs/RE/formats/bgtexture_lst.md §Cross-file join. CONFIRMED.
    /// </summary>
    /// <param name="sectionKeyword">The .map section to read the TEXTURES list from (e.g. "TERRAIN", "BUILDING").</param>
    /// <param name="oneBasedIndex">The 1-based index into that section's TEXTURES list.</param>
    private ImageTexture? ResolveSectionTexture(string sectionKeyword, int oneBasedIndex)
    {
        if (Assets is null || _bgTextures is null || _cellMap is null) return null;

        var list = GetSectionTextures(sectionKeyword);
        if (list is null || list.Length == 0) return null;

        // The .ted per-cell byte is 1-based into the cell texture list and clamped to [1, count]:
        // BOTH a byte < 1 and a byte > count resolve to slot 1 (perCellTexList[0]) — there is NO
        // no-texture sentinel. This clamp + the byte-1 below are the ONE legitimate -1 in the chain
        // (it is on the .ted byte, NOT on the intTexId).
        // spec: Docs/RE/formats/terrain.md §5.6 — Ted_ResolvePatchTextures (IDA 0x44b296: <1→1, >count→1).
        var b = oneBasedIndex < 1 || oneBasedIndex > list.Length ? 1 : oneBasedIndex;
        var intTexId = list[b - 1].TexId;

        // The .map intTexId is the 0-based bgtexture pool slot, used DIRECTLY (NO -1): the pool accessor
        // reads pool[0]+stride*intTexId. spec: Docs/RE/formats/bgtexture_lst.md §Cross-file join
        // (IDA-corrected 263bd994: 0x445833 / 0x44a46d / store 0x44b267). Textures live under the GLOBAL
        // map000 pool for all areas. spec: terrain.md §3.5. CONFIRMED.
        var ddsPath = _bgTextures.ResolveTexturePath(intTexId);
        if (ddsPath is null) return null;

        return Assets.Contains(ddsPath) ? Assets.LoadTexture(ddsPath) : null;
    }

    /// <summary>Returns the TEXTURES list of the named <c>.map</c> section, or null if absent.</summary>
    private (int Flag, int TexId)[]? GetSectionTextures(string keyword)
    {
        if (_cellMap is null) return null;
        foreach (var section in _cellMap.Sections)
            if (string.Equals(section.Keyword, keyword, StringComparison.OrdinalIgnoreCase))
                return section.Textures;

        return null;
    }

    /// <summary>
    ///     Wires a <see cref="TerrainNode.TextureResolver" /> delegate that maps a 1-based cell texture
    ///     byte (from TextureIndexGrid) to a real Godot ImageTexture via <see cref="ResolveSectionTexture" />
    ///     reading the cell <c>.map</c> <c>TERRAIN</c> section.
    ///     spec: Docs/RE/formats/terrain.md §5.6 Block 3 + §3.5 + §4.2. CONFIRMED.
    /// </summary>
    private void WireTerrainTextureResolver(TerrainNode terrainNode)
    {
        if (Assets is null) return;

        var texCache = new Dictionary<int, ImageTexture?>();
        var loggedOnce = false;

        terrainNode.TextureResolver = texByte =>
        {
            if (texCache.TryGetValue(texByte, out var cached)) return cached;

            // spec: Docs/RE/formats/terrain.md §3.5 — terrain patches index the .map TERRAIN TEXTURES list.
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