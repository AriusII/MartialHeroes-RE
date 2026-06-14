// Dev/RealClientAssets.cs
//
// HOW TO ACTIVATE REAL-ASSET RENDERING:
//
//   Option A — fichier de config (recommandé, aucune variable d'environnement requise) :
//     Edite client_dir.cfg à côté de project.godot :
//       client_dir=D:\MartialHeroesClient
//       real_assets=true
//
//   Option B — variable d'environnement (override optionnel) :
//     Windows PowerShell:  $env:MH_CLIENT_DIR = "D:\MartialHeroesClient"
//     Windows CMD:         set MH_CLIENT_DIR=D:\MartialHeroesClient
//
//   Option C — auto-détection : si le client est dans D:\MartialHeroesClient ou
//     C:\MartialHeroesClient ou LegacyClient/, il est détecté automatiquement.
//
//   Layout attendu sous le répertoire client :
//     <client_dir>/data.inf          — index VFS
//     <client_dir>/data/data.vfs     — archive VFS
//
//   Si le répertoire ou les fichiers sont absents, le système bascule sur
//   SyntheticWorldFeeder — aucun crash, aucune exception propagée à l'utilisateur.
//
// NOTE: This file is dev-only. It MUST NOT be referenced from production code paths.
//       It lives under Dev/ to make that intent clear.
//       It never commits game files; it opens them at runtime from a user-supplied path.
//
// spec: PRESERVATION_AND_ARCHITECTURE.md §Non-distribution rules — user must supply originals.

using Godot;
using MartialHeroes.Assets.Mapping;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Assets.Vfs;

namespace MartialHeroes.Client.Godot.Dev;

/// <summary>
/// Dev-only helper that opens a real Martial Heroes VFS archive at runtime and exposes
/// typed asset-loading helpers consumed by the real-asset rendering scene.
///
/// Path resolution is fully delegated to <see cref="ClientPathResolver.ResolveClientDir"/>
/// (env override → client_dir.cfg → auto-detection). No hard-coded path here.
///
/// Threading: all public methods are called from the Godot main thread (via GameLoop._Ready
/// or the deferred real-world spawner). They are NOT designed for concurrent access.
///
/// spec: PRESERVATION_AND_ARCHITECTURE.md §Non-distribution rules.
/// spec: Docs/RE/formats/pak.md — MappedVfsArchive.Open(infPath, vfsPath).
/// spec: Docs/RE/formats/terrain.md §1.1–1.3 (area tag, manifest, ted path).
/// spec: Docs/RE/formats/mesh.md (skn/bnd).
/// spec: Docs/RE/formats/animation.md (mot).
/// spec: Docs/RE/formats/texture.md (png/bmp/dds/tga via AssetPassthrough).
/// </summary>
public sealed class RealClientAssets : IDisposable
{
    // -------------------------------------------------------------------------
    // Internal state
    // -------------------------------------------------------------------------

    private readonly MappedVfsArchive _vfs;
    private bool _disposed;

    private RealClientAssets(MappedVfsArchive vfs)
    {
        _vfs = vfs;
    }

    // -------------------------------------------------------------------------
    // Factory
    // -------------------------------------------------------------------------

    /// <summary>
    /// Attempts to open the VFS archive from the resolved client directory.
    /// Returns <see langword="null"/> and logs a diagnostic when the directory or archive
    /// files are absent — no exception is propagated to the caller.
    ///
    /// Path resolution is delegated to <see cref="ClientPathResolver.ResolveClientDir"/>:
    /// MH_CLIENT_DIR env override → client_dir.cfg → auto-detection → null (offline).
    ///
    /// spec: Docs/RE/formats/pak.md — two-file scheme (data.inf + data/data.vfs).
    /// </summary>
    public static RealClientAssets? TryOpen()
    {
        // Delegate resolution to the shared resolver — no direct env-var read here.
        // spec: PRESERVATION_AND_ARCHITECTURE.md §Non-distribution rules.
        string? clientDir = ClientPathResolver.ResolveClientDir();
        if (clientDir is null)
        {
            GD.Print("[RealClientAssets] Aucun répertoire client résolu — mode synthétique.");
            return null;
        }

        string infPath = Path.Combine(clientDir, "data.inf");
        string vfsPath = Path.Combine(clientDir, "data", "data.vfs");

        try
        {
            MappedVfsArchive vfs = MappedVfsArchive.Open(infPath, vfsPath);
            GD.Print($"[RealClientAssets] VFS opened: {vfs.EntryCount} entries from '{clientDir}'.");
            return new RealClientAssets(vfs);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealClientAssets] VFS open failed from '{clientDir}': {ex.Message}. " +
                        "Falling back to synthetic feeder.");
            return null;
        }
    }

    // -------------------------------------------------------------------------
    // Terrain helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Loads the raw .ted bytes for a specific map cell.
    ///
    /// spec: Docs/RE/formats/terrain.md §1.3 per-cell path pattern.
    /// spec: Docs/RE/formats/terrain.md §5.2 — total 46987 bytes, five blocks.
    /// </summary>
    /// <param name="areaId">Area identifier (e.g. 0 for map000).</param>
    /// <param name="mapX">Cell X coordinate (world origin = 10000).</param>
    /// <param name="mapZ">Cell Z coordinate (world origin = 10000).</param>
    /// <returns>Raw .ted bytes, or empty when absent.</returns>
    public ReadOnlyMemory<byte> LoadTed(int areaId, int mapX, int mapZ)
    {
        string path = BuildTedPath(areaId, mapX, mapZ);
        return TryGetContent(path);
    }

    /// <summary>
    /// Loads and parses the .lst manifest for an area, returning the set of valid cell keys.
    /// Returns an empty set when the manifest is absent or unparseable.
    ///
    /// spec: Docs/RE/formats/terrain.md §1.2 — manifest path + key formula.
    /// </summary>
    public HashSet<uint> LoadManifestKeys(int areaId)
    {
        string path = BuildLstPath(areaId);
        ReadOnlyMemory<byte> data = TryGetContent(path);
        if (data.IsEmpty)
        {
            return [];
        }

        try
        {
            LstManifest manifest = LstManifestParser.Parse(data);
            var keys = new HashSet<uint>(manifest.Entries.Length);
            foreach (LstCellEntry entry in manifest.Entries)
            {
                keys.Add(entry.Key);
            }

            return keys;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealClientAssets] LST parse failed for area {areaId}: {ex.Message}");
            return [];
        }
    }

    /// <summary>
    /// Loads the .map scene descriptor for a specific map cell and returns the paths of the
    /// TERRAIN and BUILDING DATAFILE entries, or null when the .map is absent.
    ///
    /// spec: Docs/RE/formats/terrain.md §3.2 — DATAFILE directive per section keyword.
    /// </summary>
    public (string? TedPath, string? BudPath) LoadMapDatafilePaths(int areaId, int mapX, int mapZ)
    {
        // .map path follows the same base pattern.
        // spec: Docs/RE/formats/terrain.md §1.3 — per-cell base path.
        string mapPath = BuildCellBasePath(areaId, mapX, mapZ) + ".map";
        ReadOnlyMemory<byte> data = TryGetContent(mapPath);
        if (data.IsEmpty)
        {
            return (null, null);
        }

        try
        {
            MapDescriptor descriptor = MapDescriptorParser.Parse(data);
            string? tedPath = null;
            string? budPath = null;

            foreach (MapSection section in descriptor.Sections)
            {
                if (section.Keyword.Equals("TERRAIN", StringComparison.OrdinalIgnoreCase) &&
                    section.DataFile is not null)
                {
                    tedPath = section.DataFile;
                }
                else if (section.Keyword.Equals("BUILDING", StringComparison.OrdinalIgnoreCase) &&
                         section.DataFile is not null)
                {
                    budPath = section.DataFile;
                }
            }

            return (tedPath, budPath);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealClientAssets] .map parse failed ({mapPath}): {ex.Message}");
            return (null, null);
        }
    }

    /// <summary>
    /// Loads and parses a .bud building scene file.
    /// Returns null when absent or parse fails.
    ///
    /// spec: Docs/RE/formats/terrain_scene.md — BudScene layout.
    /// </summary>
    public BudScene? LoadBud(string virtualPath)
    {
        ReadOnlyMemory<byte> data = TryGetContent(virtualPath);
        if (data.IsEmpty) return null;

        try
        {
            return TerrainSceneParser.Parse(data);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealClientAssets] .bud parse failed ({virtualPath}): {ex.Message}");
            return null;
        }
    }

    // -------------------------------------------------------------------------
    // Skinned mesh helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Loads a skinned mesh from .skn + optional .bnd + optional .mot files.
    /// Writes a GLB into <paramref name="glbOutput"/> via <see cref="GltfConverter.WriteGlb"/>.
    /// Returns false when required files are absent.
    ///
    /// spec: Docs/RE/formats/mesh.md §.skn / §.bnd.
    /// spec: Docs/RE/formats/animation.md §.mot.
    /// </summary>
    public bool LoadSkinned(
        string sknPath,
        string? bndPath,
        string[]? motPaths,
        Stream glbOutput)
    {
        ReadOnlyMemory<byte> sknData = TryGetContent(sknPath);
        if (sknData.IsEmpty)
        {
            GD.PrintErr($"[RealClientAssets] .skn not found: {sknPath}");
            return false;
        }

        try
        {
            SkinnedMesh mesh = SknParser.Parse(sknData);

            Skeleton? skeleton = null;
            if (bndPath is not null)
            {
                ReadOnlyMemory<byte> bndData = TryGetContent(bndPath);
                if (!bndData.IsEmpty)
                {
                    skeleton = BndParser.Parse(bndData);
                }
            }

            AnimationClip[]? clips = null;
            if (motPaths is { Length: > 0 })
            {
                var clipList = new List<AnimationClip>(motPaths.Length);
                foreach (string motPath in motPaths)
                {
                    ReadOnlyMemory<byte> motData = TryGetContent(motPath);
                    if (!motData.IsEmpty)
                    {
                        try
                        {
                            clipList.Add(AnimationParser.Parse(motData));
                        }
                        catch (Exception ex)
                        {
                            GD.PrintErr($"[RealClientAssets] .mot parse failed ({motPath}): {ex.Message}");
                        }
                    }
                }

                if (clipList.Count > 0) clips = clipList.ToArray();
            }

            GltfConverter.WriteGlb(mesh, skeleton, glbOutput, clips);
            return true;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealClientAssets] skinned load failed ({sknPath}): {ex.Message}");
            return false;
        }
    }

    // -------------------------------------------------------------------------
    // Texture helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Loads a texture from the VFS and converts it to a Godot <see cref="ImageTexture"/>.
    /// Supports PNG, BMP, DDS (via Godot's built-in DDS importer), and TGA.
    /// Returns null when the file is absent or the format is not recognised.
    ///
    /// spec: Docs/RE/formats/texture.md §PNG / §BMP / §DDS / §TGA.
    /// spec: Docs/RE/formats/texture.md §There is no proprietary texture format — standard
    ///       D3DX9 format auto-detection: CONFIRMED.
    /// </summary>
    public ImageTexture? LoadTexture(string virtualPath)
    {
        ReadOnlyMemory<byte> raw = TryGetContent(virtualPath);
        if (raw.IsEmpty) return null;

        try
        {
            string ext = Path.GetExtension(virtualPath).ToLowerInvariant();
            ImagePassthroughResult result = AssetPassthrough.PassthroughImage(raw, ext);

            // Build a Godot Image from the raw bytes.
            // Godot 4 Image.LoadFromBuffer handles PNG and BMP natively.
            // DDS and TGA are loaded via Image.LoadDdsFromBuffer / Image.LoadTgaFromBuffer.
            // spec: Docs/RE/formats/texture.md §PNG — standard ISO 15948: SAMPLE-VERIFIED.
            // spec: Docs/RE/formats/texture.md §BMP — standard Windows DIB: SAMPLE-VERIFIED.
            // spec: Docs/RE/formats/texture.md §DDS — DXT1/DXT5: CONFIRMED-from-routine.
            // spec: Docs/RE/formats/texture.md §TGA — uncompressed 32bpp: SAMPLE-VERIFIED.
            byte[] bytes = raw.ToArray();
            var img = new Image();
            Error err;

            switch (result.Format)
            {
                case ImageFormat.Png:
                    err = img.LoadPngFromBuffer(bytes);
                    break;
                case ImageFormat.Bmp:
                    err = img.LoadBmpFromBuffer(bytes);
                    break;
                case ImageFormat.Dds:
                    err = img.LoadDdsFromBuffer(bytes);
                    break;
                case ImageFormat.Tga:
                    err = img.LoadTgaFromBuffer(bytes);
                    break;
                default:
                    GD.PrintErr($"[RealClientAssets] Unsupported texture format for: {virtualPath}");
                    return null;
            }

            if (err != Error.Ok)
            {
                GD.PrintErr($"[RealClientAssets] Image load failed for '{virtualPath}': {err}");
                return null;
            }

            if (img.GetWidth() == 0 || img.GetHeight() == 0)
            {
                GD.PrintErr(
                    $"[RealClientAssets] Image has zero size after load (paletted/unsupported format?): '{virtualPath}'");
                return null;
            }

            // Character skin PNGs are RGB8 (no alpha channel). Some GPU backends (D3D12 Forward+)
            // require RGBA8 for reliable GPU texture creation; convert before upload.
            // spec: Docs/RE/formats/texture.md §PNG — standard ISO 15948 RGB, no alpha channel.
            if (img.GetFormat() == Image.Format.Rgb8)
            {
                img.Convert(Image.Format.Rgba8);
            }

            // Generate mipmaps so LinearWithMipmaps filter works correctly.
            // GenerateMipmaps() only works on uncompressed images — DDS (DXT1/DXT3/DXT5) images
            // are already compressed and Godot will log an error if we try to generate mips from
            // them. Guard to Rgba8 only (character skin PNGs convert to Rgba8 above; DDS stays
            // in its compressed format and Godot handles the mip chain from the DDS header).
            // spec: Docs/RE/formats/texture.md §DDS — DXT1/DXT5 already contain a mip chain.
            if (img.GetFormat() == Image.Format.Rgba8)
            {
                img.GenerateMipmaps();
            }

            return ImageTexture.CreateFromImage(img);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealClientAssets] Texture load failed ({virtualPath}): {ex.Message}");
            return null;
        }
    }

    // -------------------------------------------------------------------------
    // Raw VFS access
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the raw bytes for a VFS-relative path, or empty when absent.
    /// </summary>
    public ReadOnlyMemory<byte> GetRaw(string virtualPath)
        => TryGetContent(virtualPath);

    /// <summary>True when the VFS contains the given path.</summary>
    public bool Contains(string virtualPath) => _vfs.Contains(virtualPath);

    // -------------------------------------------------------------------------
    // Path construction helpers
    // spec: Docs/RE/formats/terrain.md §1.1 (digit decomposition).
    // spec: Docs/RE/formats/terrain.md §1.2 (manifest path).
    // spec: Docs/RE/formats/terrain.md §1.3 (per-cell base path).
    // -------------------------------------------------------------------------

    private static string AreaTag(int areaId)
    {
        // spec: Docs/RE/formats/terrain.md §1.1 — d0 = areaId/100, d1 = (areaId/10)%10, d2 = areaId%10. CONFIRMED.
        int d0 = areaId / 100;
        int d1 = (areaId / 10) % 10;
        int d2 = areaId % 10;
        return $"{d0}{d1}{d2}";
    }

    private static string BuildCellBasePath(int areaId, int mapX, int mapZ)
    {
        // spec: Docs/RE/formats/terrain.md §1.3 — "data/map<d0d1d2>/dat/d<d0d1d2>x<mapX>z<mapZ>". CONFIRMED.
        string tag = AreaTag(areaId);
        return $"data/map{tag}/dat/d{tag}x{mapX}z{mapZ}";
    }

    private static string BuildTedPath(int areaId, int mapX, int mapZ)
        => BuildCellBasePath(areaId, mapX, mapZ) + ".ted";

    private static string BuildLstPath(int areaId)
    {
        // spec: Docs/RE/formats/terrain.md §1.2 — "data/map<d0d1d2>/dat/d<d0d1d2>.lst". CONFIRMED.
        string tag = AreaTag(areaId);
        return $"data/map{tag}/dat/d{tag}.lst";
    }

    private ReadOnlyMemory<byte> TryGetContent(string path)
    {
        try
        {
            if (!_vfs.Contains(path)) return ReadOnlyMemory<byte>.Empty;
            return _vfs.GetFileContent(path);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealClientAssets] VFS read error ({path}): {ex.Message}");
            return ReadOnlyMemory<byte>.Empty;
        }
    }

    // -------------------------------------------------------------------------
    // VFS cell enumeration
    // -------------------------------------------------------------------------

    /// <summary>
    /// Enumerates all terrain cell coordinates available in the VFS for the given area.
    ///
    /// Scans VFS entry names matching the pattern:
    ///   data/map{tag}/dat/d{tag}x{mapX}z{mapZ}.ted
    /// and parses (mapX, mapZ) from each matched name.
    ///
    /// Returns cells in the order they appear in the (sorted) TOC.
    /// Returns an empty list when no cells are found or the VFS is unavailable.
    ///
    /// spec: Docs/RE/formats/terrain.md §1.3 — per-cell base path pattern. CONFIRMED.
    /// </summary>
    /// <param name="areaId">Area identifier (e.g. 0 for map000).</param>
    public List<(int MapX, int MapZ)> EnumerateTerrainCells(int areaId)
    {
        var results = new List<(int, int)>();

        string tag = AreaTag(areaId);
        // Expected prefix: "data/map000/dat/d000x"
        // Expected suffix: "z<mapZ>.ted"
        // spec: Docs/RE/formats/terrain.md §1.3 — "data/map<d0d1d2>/dat/d<d0d1d2>x<mapX>z<mapZ>". CONFIRMED.
        string prefix = $"data/map{tag}/dat/d{tag}x";
        const string tedSuffix = ".ted";

        ReadOnlySpan<VfsEntry> entries = _vfs.GetEntries();
        foreach (VfsEntry entry in entries)
        {
            string name = entry.Name; // already lower-case, spec: pak.md §TOC names. CONFIRMED.
            if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (!name.EndsWith(tedSuffix, StringComparison.Ordinal)) continue;

            // Name between prefix and suffix: "<mapX>z<mapZ>"
            // e.g. "10000z9990" → mapX=10000, mapZ=9990
            int inner = name.Length - prefix.Length - tedSuffix.Length;
            if (inner <= 0) continue;

            string middle = name.Substring(prefix.Length, inner);
            int zIndex = middle.IndexOf('z');
            if (zIndex <= 0) continue;

            if (int.TryParse(middle[..zIndex], out int mapX) &&
                int.TryParse(middle[(zIndex + 1)..], out int mapZ))
            {
                results.Add((mapX, mapZ));
            }
        }

        return results;
    }

    // -------------------------------------------------------------------------
    // IDisposable
    // -------------------------------------------------------------------------

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _vfs.Dispose();
    }
}