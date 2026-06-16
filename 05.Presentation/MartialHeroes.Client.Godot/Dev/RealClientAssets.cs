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

    // NOTE: LoadTed(areaId, mapX, mapZ) and LoadManifestKeys(areaId) were removed —
    // both had zero call sites. Callers use GetRaw / Contains directly (e.g. RealWorldRenderer,
    // CharSelectScene3D). VfsTerrainSectorSource handles manifest keys via vfs.Contains.
    // spec: Docs/RE/formats/terrain.md §1.2–1.3.

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
    // Texture helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Loads a texture from the VFS and converts it to a Godot <see cref="ImageTexture"/>.
    /// Supports PNG, BMP, DDS (via Godot's built-in DDS importer), and TGA.
    /// Returns null when the file is absent or the format is not recognised.
    ///
    /// DXT2 premultiplied-alpha note (P5 fix):
    ///   <c>loginwindow_02.dds</c> and <c>login_slice1.dds</c> are DXT2 (premultiplied alpha) —
    ///   FourCC DXT2, spec: Docs/RE/specs/frontend_scenes.md §11.1a.
    ///   Godot 4 loads DXT2 as BC2 (same layout as DXT3) but treats RGB as straight.
    ///   The RGB channels are actually RGB*A (premultiplied), so transparent edges composite
    ///   with a dark fringe unless we unpremultiply: R'=R/A, G'=G/A, B'=B/A.
    ///   We detect DXT2 by reading bytes 84–87 of the DDS header (the FourCC field).
    ///   After unpremultiplication the image is straight-alpha RGBA8 and composites correctly.
    ///   spec: Docs/RE/specs/frontend_scenes.md §11.1a "DXT2 premultiplied-alpha flag". CODE-CONFIRMED.
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

            // Detect DXT2 (premultiplied alpha) before decoding.
            // DDS header FourCC is at bytes 84–87 (little-endian).
            // DXT2 FourCC = 0x32545844 ('D','X','T','2'). CODE-CONFIRMED by byte-verified header.
            // spec: Docs/RE/specs/frontend_scenes.md §11.1a "DXT2 (premultiplied alpha)". CODE-CONFIRMED.
            bool isDxt2Premultiplied = false;
            if (result.Format == ImageFormat.Dds && raw.Length >= 88)
            {
                ReadOnlySpan<byte> header = raw.Span;
                // FourCC offset 84 in the DDS binary layout (4-byte magic "DDS " at 0, then header).
                // DDS header: magic(4) + DDSURFACEDESC2(124): pfFourCC is at offset 80 within DDSURFACEDESC2
                // = byte offset 84 from file start. DXT2 = ASCII "DXT2" = 0x44 0x58 0x54 0x32.
                isDxt2Premultiplied =
                    header[84] == 0x44 && // 'D'
                    header[85] == 0x58 && // 'X'
                    header[86] == 0x54 && // 'T'
                    header[87] == 0x32; // '2'
            }

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

            // DXT2 premultiplied-alpha unpremultiplication (P5 fix).
            // Godot decodes DXT2 as BC2 (same data as DXT3) — the block format is identical,
            // only the semantic differs: DXT2 RGB channels are premultiplied by alpha.
            // We must DECOMPRESS first (Image.Decompress()) — block-compressed formats stay
            // compressed after Image.Convert() and GetPixel() will throw on compressed images.
            // Then divide R/G/B by A to recover straight-alpha RGBA8.
            // spec: Docs/RE/specs/frontend_scenes.md §11.1a "DXT2 premultiplied alpha". CODE-CONFIRMED.
            if (isDxt2Premultiplied)
            {
                // Decompress block-compressed DXT2/BC2 to uncompressed RGBA8 for pixel access.
                if (img.IsCompressed())
                {
                    Error decompErr = img.Decompress();
                    if (decompErr != Error.Ok)
                    {
                        GD.PrintErr(
                            $"[RealClientAssets] DXT2 decompress failed for '{virtualPath}': {decompErr}. Skipping unpremultiply.");
                    }
                    else
                    {
                        if (img.GetFormat() != Image.Format.Rgba8)
                            img.Convert(Image.Format.Rgba8);
                        UnpremultiplyAlpha(img);
                        GD.Print($"[RealClientAssets] DXT2 decompressed+unpremultiplied: '{virtualPath}'.");
                    }
                }
                else
                {
                    // Already uncompressed (safe path, unlikely for DDS).
                    if (img.GetFormat() != Image.Format.Rgba8)
                        img.Convert(Image.Format.Rgba8);
                    UnpremultiplyAlpha(img);
                    GD.Print($"[RealClientAssets] DXT2 unpremultiplied (pre-decompressed): '{virtualPath}'.");
                }
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
            // them. Guard to Rgba8 only (character skin PNGs convert to Rgba8 above; DXT2 images
            // are also converted to Rgba8 in the unpremultiply path above).
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

    /// <summary>
    /// In-place straight-alpha conversion for a DXT2 (premultiplied-alpha) image decoded to RGBA8.
    /// For each pixel: if A &gt; 0, R' = R*255/A, G' = G*255/A, B' = B*255/A.
    /// Fully transparent pixels (A==0) are left as-is (any RGB value is valid, we zero them).
    ///
    /// spec: Docs/RE/specs/frontend_scenes.md §11.1a "DXT2 premultiplied-alpha flag". CODE-CONFIRMED.
    /// </summary>
    private static void UnpremultiplyAlpha(Image img)
    {
        // Operate pixel-by-pixel via GetPixel/SetPixel.
        // This is intentionally simple: UI atlases are loaded once at startup, not on hot paths.
        int w = img.GetWidth();
        int h = img.GetHeight();
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                Color c = img.GetPixel(x, y);
                float a = c.A;
                if (a > 0f)
                {
                    // Un-premultiply: divide colour channels by alpha.
                    img.SetPixel(x, y, new Color(c.R / a, c.G / a, c.B / a, a));
                }
                else
                {
                    // Fully transparent: zero the colour channels to avoid artefacts.
                    img.SetPixel(x, y, new Color(0f, 0f, 0f, 0f));
                }
            }
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
            // Use ReadOnlySpan<char> to avoid per-entry Substring allocation.
            int inner = name.Length - prefix.Length - tedSuffix.Length;
            if (inner <= 0) continue;

            ReadOnlySpan<char> middle = name.AsSpan(prefix.Length, inner);
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