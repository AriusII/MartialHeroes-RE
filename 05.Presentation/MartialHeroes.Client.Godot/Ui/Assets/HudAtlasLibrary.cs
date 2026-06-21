// Ui/Assets/HudAtlasLibrary.cs
//
// Atlas library for the shared HUD substrate.
//
// Responsibilities:
//   - Maps uitex.txt integer tex_id → loaded DDS Texture2D (cached per id).
//   - Slices an AtlasTexture from a loaded atlas at a (srcX, srcY, w, h) sub-rect
//     with FilterClip=true to prevent atlas bleed.
//   - Provides LoadByPath() for atlases referenced by hard-coded VFS path rather
//     than by uitex.txt id (e.g. login_slice1.dds, loginwindow.dds).
//
// Offline / VFS-absent: every method returns null; never throws or substitutes a
// placeholder asset.
//
// Cache note (port choice): the legacy client had NO per-session atlas cache; it
// called the ID3DXSprite bind each frame. We cache per tex_id as a port choice —
// each DDS is loaded at most once.
//
// spec: Docs/RE/formats/ui_manifests.md §1  — uitex.txt grammar, id→path mapping.
// spec: Docs/RE/formats/ui_manifests.md §1.4 — confirmed tex_id→path table (SAMPLE-VERIFIED).
// spec: Docs/RE/formats/ui_manifests.md §1.5 — non-contiguous id space; dict lookup required.
// spec: Docs/RE/specs/ui_system.md §1.3 — "atlas pixels map 1:1 to screen pixels on 1024×768".

using Godot;
using MartialHeroes.Assets.Parsers.Texture;
using MartialHeroes.Assets.Parsers.Texture.Models;
using MartialHeroes.Client.Godot.Composition;

namespace MartialHeroes.Client.Godot.Ui.Assets;

/// <summary>
///     Shared HUD atlas library — maps uitex.txt tex_ids and hard-coded VFS paths to
///     Godot <see cref="Texture2D" /> objects and slices <see cref="AtlasTexture" /> sub-rects.
///     <para>
///         Pass to every HUD widget/window that needs atlas access. One instance per session,
///         created by the composition root (ClientContext) and passed down by constructor.
///     </para>
///     <para>
///         All public methods return <see langword="null" /> when the VFS is unavailable or an
///         id/path is absent. Callers MUST handle null and render nothing.
///     </para>
///     spec: Docs/RE/formats/ui_manifests.md §1 — uitex.txt grammar (PARSER-CONFIRMED).
/// </summary>
public sealed class HudAtlasLibrary : IDisposable
{
    // VFS path of the uitex manifest.
    // spec: Docs/RE/formats/ui_manifests.md §1.1 — "data/ui/UiTex.txt": PARSER-CONFIRMED.
    private const string UiTexPath = "data/ui/UiTex.txt";

    private readonly RealClientAssets? _assets;

    // Texture cache keyed by tex_id — each atlas is loaded at most once per session.
    // spec: Docs/RE/formats/ui_manifests.md §1.5 — non-contiguous id space requires dict lookup.
    private readonly Dictionary<int, Texture2D?> _texByIdCache = new();

    // Texture cache keyed by VFS path — for atlases loaded by path (not by uitex id).
    private readonly Dictionary<string, Texture2D?> _texByPathCache = new();

    private bool _disposed;

    // Lazy-loaded uitex manifest.
    private UiTexManifest? _manifest;
    private bool _manifestAttempted;

    // -------------------------------------------------------------------------
    // Construction
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Creates a HudAtlasLibrary backed by the supplied VFS assets handle.
    ///     Pass <see langword="null" /> for offline / no-VFS mode; all methods return null.
    /// </summary>
    public HudAtlasLibrary(RealClientAssets? assets)
    {
        _assets = assets;
    }

    // -------------------------------------------------------------------------
    // IDisposable
    // -------------------------------------------------------------------------

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _texByIdCache.Clear();
        _texByPathCache.Clear();
        // Godot Texture2D objects are reference-counted by Godot's GC; no manual free needed.
        // _assets is owned by ClientContext; we do not dispose it here.
    }

    // -------------------------------------------------------------------------
    // Atlas by tex_id (uitex.txt)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Returns the Godot <see cref="Texture2D" /> for the given uitex.txt
    ///     <paramref name="texId" />, or <see langword="null" /> when the VFS is offline,
    ///     the id is absent from the manifest, or the DDS file cannot be loaded.
    ///     <para>Cached after the first load; O(1) on subsequent calls for the same id.</para>
    ///     spec: Docs/RE/formats/ui_manifests.md §1.4 — confirmed id→path (SAMPLE-VERIFIED):
    ///     id 1=mainwindow.dds, 2=inventwindow.dds, 8=skillwindow.dds,
    ///     9=messagewindow.dds, 10=skillpipe.dds, 11=skillpipe_02.dds,
    ///     14=blacksheet.dds.
    ///     spec: Docs/RE/formats/ui_manifests.md §1.5 — non-contiguous id space.
    /// </summary>
    public Texture2D? GetById(int texId)
    {
        if (_texByIdCache.TryGetValue(texId, out var cached))
            return cached;

        var manifest = EnsureManifest();
        if (manifest is null)
        {
            _texByIdCache[texId] = null;
            return null;
        }

        var entry = manifest.GetById(texId);
        if (entry is null)
        {
            // Gap in the non-contiguous id space — expected and not an error.
            // spec: Docs/RE/formats/ui_manifests.md §1.5 — large gaps confirmed.
            _texByIdCache[texId] = null;
            return null;
        }

        var tex = LoadFromVfs(entry.VfsPath);
        _texByIdCache[texId] = tex;
        return tex;
    }

    // -------------------------------------------------------------------------
    // Atlas by VFS path (for hard-coded paths not in uitex.txt)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Returns the Godot <see cref="Texture2D" /> for the given VFS
    ///     <paramref name="vfsPath" />, or <see langword="null" /> when the VFS is offline
    ///     or the file cannot be loaded. Cached per path.
    ///     <para>
    ///         Used for atlases referenced by hard-coded path in screen build routines
    ///         (login_slice1.dds, loginwindow.dds, etc.) rather than through uitex.txt.
    ///     </para>
    ///     spec: Docs/RE/specs/ui_system.md §8.1 — login atlases loaded by hard-coded path.
    /// </summary>
    public Texture2D? GetByPath(string vfsPath)
    {
        if (_texByPathCache.TryGetValue(vfsPath, out var cached))
            return cached;

        var tex = LoadFromVfs(vfsPath);
        _texByPathCache[vfsPath] = tex;
        return tex;
    }

    // -------------------------------------------------------------------------
    // AtlasTexture slicing
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Returns an <see cref="AtlasTexture" /> for the sub-rect at
    ///     <c>(srcX, srcY, w, h)</c> within the atlas identified by uitex.txt
    ///     <paramref name="texId" />, or <see langword="null" /> when offline.
    ///     <para>
    ///         <c>FilterClip = true</c> is set to prevent atlas bleed at non-integer
    ///         scale factors (a Godot port choice — not a legacy spec literal).
    ///     </para>
    ///     spec: Docs/RE/specs/ui_system.md §1.3 — "pSrcRect = {srcX, srcY, srcX+w, srcY+h}
    ///     passed to ID3DXSprite::Draw; atlas pixels 1:1 on 1024×768 canvas".
    /// </summary>
    public AtlasTexture? SliceById(int texId, int srcX, int srcY, int w, int h)
    {
        var atlas = GetById(texId);
        return atlas is null ? null : BuildAtlasTexture(atlas, srcX, srcY, w, h);
    }

    /// <summary>
    ///     Returns an <see cref="AtlasTexture" /> for the sub-rect at
    ///     <c>(srcX, srcY, w, h)</c> within the atlas at the given VFS path,
    ///     or <see langword="null" /> when offline.
    ///     spec: Docs/RE/specs/ui_system.md §1.3 — atlas pixel map 1:1 on 1024×768 canvas.
    /// </summary>
    public AtlasTexture? SliceByPath(string vfsPath, int srcX, int srcY, int w, int h)
    {
        var atlas = GetByPath(vfsPath);
        return atlas is null ? null : BuildAtlasTexture(atlas, srcX, srcY, w, h);
    }

    // -------------------------------------------------------------------------
    // Preload helpers (eager-loads a set of atlases into the path cache)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Eagerly loads a set of VFS atlas paths into the internal cache so subsequent
    ///     <see cref="GetByPath" /> / <see cref="SliceByPath" /> calls are O(1).
    ///     <para>Call this at screen-build time before constructing any widgets.</para>
    /// </summary>
    public void Preload(ReadOnlySpan<string> vfsPaths)
    {
        foreach (var path in vfsPaths)
            GetByPath(path); // populates the cache, result discarded
    }

    // -------------------------------------------------------------------------
    // Lazy manifest loader
    // -------------------------------------------------------------------------

    private UiTexManifest? EnsureManifest()
    {
        if (_manifestAttempted) return _manifest;
        _manifestAttempted = true;

        if (_assets is null) return null;

        try
        {
            var raw = _assets.GetRaw(UiTexPath);
            if (raw.IsEmpty)
            {
                GD.Print("[HudAtlasLibrary] data/ui/UiTex.txt absent from VFS — atlas catalog unavailable.");
                return null;
            }

            // spec: Docs/RE/formats/ui_manifests.md §1.2 — braced-block grammar, UI_TEXTURE { DDS { } }.
            _manifest = UiTexManifestParser.Parse(raw);
            GD.Print($"[HudAtlasLibrary] UiTex.txt loaded: {_manifest.DdsEntries.Count} DDS entries " +
                     $"(spec expects 37 EOF-driven — Docs/RE/formats/ui_manifests.md §1.4).");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[HudAtlasLibrary] UiTex.txt load/parse failed: {ex.Message}");
            _manifest = null;
        }

        return _manifest;
    }

    // -------------------------------------------------------------------------
    // VFS load helper
    // -------------------------------------------------------------------------

    private Texture2D? LoadFromVfs(string vfsPath)
    {
        if (_assets is null) return null;

        try
        {
            // RealClientAssets.LoadTexture probes the file magic bytes internally and
            // handles the DDS/TGA/PNG/BMP variants (including .dds-named TGA files).
            var tex = _assets.LoadTexture(vfsPath);
            if (tex is null)
                GD.PrintErr($"[HudAtlasLibrary] LoadTexture returned null for '{vfsPath}'.");
            return tex;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[HudAtlasLibrary] LoadFromVfs('{vfsPath}') failed: {ex.Message}");
            return null;
        }
    }

    // -------------------------------------------------------------------------
    // AtlasTexture builder
    // -------------------------------------------------------------------------

    private static AtlasTexture BuildAtlasTexture(Texture2D atlas, int srcX, int srcY, int w, int h)
    {
        return new AtlasTexture
        {
            Atlas = atlas,
            Region = new Rect2(srcX, srcY, w, h),
            // FilterClip prevents texture bleed at non-integer scale factors.
            // This is a Godot port-side mitigation, not a legacy spec value.
            FilterClip = true
        };
    }
}