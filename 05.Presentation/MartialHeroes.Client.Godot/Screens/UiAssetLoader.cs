// Screens/UiAssetLoader.cs
//
// Loads the legacy UI atlas DDS sheets and the msg.xdb string catalogue from the real
// client VFS, for the login / character-select screens.
//
// REUSE NOTE: this delegates entirely to the existing Dev/RealClientAssets loader (the same
// loader the terrain pipeline uses for DDS textures). RealClientAssets.LoadTexture already
// probes the format and routes DDS/TGA/PNG/BMP through AssetPassthrough, so the do.dds
// "TGA mislabelled as .dds" caveat (spec: Docs/RE/formats/ui_manifests.md §5 — probe magic
// bytes) is handled there, not duplicated here.
//
// PASSIVE: this is a pure view-asset reader. Zero game logic. CP949 strings come back already
// decoded from MsgXdbParser (Assets.Parsers); we never decode bytes in the UI layer.
//
// spec: Docs/RE/specs/ui_system.md §3 (per-screen DDS manifests), §5 (msg.xdb string DB).
// spec: Docs/RE/formats/ui_manifests.md §5 (do.dds TGA caveat), §6 (DXT3 atlas format).
// spec: Docs/RE/formats/misc_data.md §6 (msg.xdb 516-byte records).

using Godot;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Client.Godot.Dev;

namespace MartialHeroes.Client.Godot.Screens;

/// <summary>
/// Thin presentation-side facade over the real client VFS for UI assets.
///
/// Opens the VFS once (via <see cref="RealClientAssets.TryOpen"/>), then serves:
///   - UI atlas DDS sheets as Godot <see cref="Texture2D"/> (cached by VFS path),
///   - sprite sub-rects as <see cref="AtlasTexture"/> (spec §8.1 / §8.3 atlas mapping),
///   - the <c>msg.xdb</c> string catalogue (spec §5) for CP949 UI captions.
///
/// All public methods return null / fallback gracefully when the VFS is offline so the screens
/// always build (they fall back to solid-colour panels + English placeholder text).
/// </summary>
public sealed class UiAssetLoader : IDisposable
{
    private readonly RealClientAssets? _assets;
    private MsgXdbCatalog? _msg;
    private bool _msgAttempted;

    // Texture cache keyed by VFS path — the login/select atlases are reused across many widgets,
    // so we load each DDS sheet once and slice many AtlasTextures from it.
    private readonly Dictionary<string, Texture2D?> _atlasCache = new();

    private bool _disposed;

    private UiAssetLoader(RealClientAssets? assets)
    {
        _assets = assets;
    }

    /// <summary>True when a real VFS was opened and atlases/strings are available.</summary>
    public bool HasVfs => _assets is not null;

    /// <summary>
    /// Opens the VFS (best-effort). Never throws — returns a loader whose <see cref="HasVfs"/>
    /// is false when no client directory is resolved (offline mode).
    /// </summary>
    public static UiAssetLoader Open()
    {
        RealClientAssets? assets = null;
        try
        {
            assets = RealClientAssets.TryOpen();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[UiAssetLoader] VFS open failed: {ex.Message} — UI runs without real atlases.");
        }

        return new UiAssetLoader(assets);
    }

    // -------------------------------------------------------------------------
    // Atlas textures
    // -------------------------------------------------------------------------

    /// <summary>
    /// Loads a full UI atlas sheet from the VFS as a Godot texture, cached by path.
    /// Returns null when the VFS is offline or the file is absent.
    ///
    /// spec: Docs/RE/formats/ui_manifests.md §6 — large UI sheets are DXT3 DDS, 1024×1024.
    /// spec: Docs/RE/formats/ui_manifests.md §5 — format probed by magic bytes (do.dds caveat).
    /// </summary>
    public Texture2D? LoadAtlas(string vfsPath)
    {
        if (_atlasCache.TryGetValue(vfsPath, out Texture2D? cached))
            return cached;

        Texture2D? tex = null;
        if (_assets is not null)
        {
            try
            {
                // RealClientAssets.LoadTexture probes the format internally (DDS/TGA/PNG/BMP),
                // so the .dds-named-TGA caveat is handled there.
                tex = _assets.LoadTexture(vfsPath);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[UiAssetLoader] LoadAtlas('{vfsPath}') failed: {ex.Message}");
            }
        }

        _atlasCache[vfsPath] = tex;
        return tex;
    }

    /// <summary>
    /// Returns an <see cref="AtlasTexture"/> for the sprite sub-rect at <c>(srcX, srcY, w, h)</c>
    /// in the named atlas, or null when the atlas is unavailable.
    ///
    /// spec: Docs/RE/specs/ui_system.md §8.3 — "the sprite is the sub-rect Rect2(srcX, srcY, w, h)
    ///       of the atlas"; atlas pixels map 1:1 to screen pixels on the 1024×768 reference canvas.
    /// </summary>
    public AtlasTexture? Slice(string vfsPath, int srcX, int srcY, int w, int h)
    {
        Texture2D? atlas = LoadAtlas(vfsPath);
        if (atlas is null) return null;

        return new AtlasTexture
        {
            Atlas = atlas,
            Region = new Rect2(srcX, srcY, w, h),
            FilterClip = true,
        };
    }

    // -------------------------------------------------------------------------
    // String catalogue (msg.xdb)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the CP949-decoded UI caption for a msg.xdb id, or <paramref name="fallback"/> when
    /// the catalogue is offline or the id is absent.
    ///
    /// spec: Docs/RE/specs/ui_system.md §5 — UI captions fetched by numeric id from msg.xdb.
    /// spec: Docs/RE/formats/misc_data.md §6 — 516-byte records, u32 id + u8[512] CP949 text.
    /// </summary>
    public string Text(uint id, string fallback)
    {
        MsgXdbCatalog? cat = EnsureMsg();
        if (cat is null) return fallback;

        string? s = cat.GetText((int)id);
        return string.IsNullOrEmpty(s) ? fallback : s;
    }

    /// <summary>Loads and caches the msg.xdb catalogue on first use. Null when offline/absent.</summary>
    private MsgXdbCatalog? EnsureMsg()
    {
        if (_msgAttempted) return _msg;
        _msgAttempted = true;

        if (_assets is null) return null;

        // spec: Docs/RE/specs/ui_system.md §5 — "data/script/msg.xdb".
        const string MsgPath = "data/script/msg.xdb";
        try
        {
            ReadOnlyMemory<byte> raw = _assets.GetRaw(MsgPath);
            if (raw.IsEmpty)
            {
                GD.Print("[UiAssetLoader] msg.xdb absent — UI uses English placeholders.");
                return null;
            }

            _msg = MsgXdbParser.Parse(raw);
            GD.Print($"[UiAssetLoader] msg.xdb loaded: {_msg.Count} records.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[UiAssetLoader] msg.xdb parse failed: {ex.Message} — using placeholders.");
            _msg = null;
        }

        return _msg;
    }

    // -------------------------------------------------------------------------
    // IDisposable
    // -------------------------------------------------------------------------

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _assets?.Dispose();
        _atlasCache.Clear();
    }
}