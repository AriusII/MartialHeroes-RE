// Adapters/UiCatalogs.cs
//
// Exposes the two runtime UI data catalogs to the presentation layer:
//   1. UiTex manifest  — data/ui/UiTex.txt        (texId → VFS DDS path → Godot ImageTexture)
//   2. Msg catalog     — data/script/msg.xdb       (id → CP949-decoded string)
//
// Both catalogs are loaded lazily on first use and cached for the session lifetime.
// When the VFS is unavailable (offline / no clientdata/) both degrade gracefully:
//   - GetTexture  → returns null (callers must handle null and show a placeholder)
//   - GetMessage  → returns the caller-supplied fallback string
//
// PASSIVE: zero game logic. Reads data through the existing RealClientAssets/UiTexManifestParser/
// MsgXdbParser stack — no second DDS decoder, no byte parsing in the UI layer.
//
// DDS loading reuses RealClientAssets.LoadTexture(), which already probes magic bytes and
// dispatches through Godot's built-in DDS/TGA/PNG/BMP loaders. The do.dds "TGA mislabelled
// as .dds" caveat (spec: Docs/RE/formats/ui_manifests.md §7) is handled there automatically.
//
// spec: Docs/RE/formats/ui_manifests.md §1   — uitex.txt grammar, 35 entries, id→path.
// spec: Docs/RE/formats/misc_data.md §6       — msg.xdb, 516-byte records, 2644 total.
// spec: Docs/RE/specs/ui_system.md §8.5       — HUD uitex integer binding contract.

using System.Runtime.InteropServices;
using Godot;
using MartialHeroes.Assets.Parsers.DataTables;
using MartialHeroes.Assets.Parsers.DataTables.Models;
using MartialHeroes.Assets.Parsers.Texture;
using MartialHeroes.Assets.Parsers.Texture.Models;
using MartialHeroes.Client.Godot.Composition;

namespace MartialHeroes.Client.Godot.Adapters;

/// <summary>
///     Singleton-style facade (created once in <see cref="MartialHeroes.Client.Godot.Autoload.ClientContext" />)
///     that exposes the two startup UI data catalogs to the presentation layer.
///     <list type="bullet">
///         <item>
///             <term>UiTex manifest</term>
///             <description>
///                 Lazy-loaded from <c>data/ui/UiTex.txt</c> via <see cref="UiTexManifestParser" />.
///                 <see cref="GetTexture" /> maps a <c>tex_id</c> integer (the handle used by every legacy
///                 widget bind call) to a Godot <see cref="ImageTexture" /> loaded from the VFS DDS path.
///                 spec: Docs/RE/formats/ui_manifests.md §1 — PARSER-CONFIRMED grammar; SAMPLE-VERIFIED content.
///                 spec: Docs/RE/specs/ui_system.md §8.5 — in-game panels bind atlas by integer tex_id.
///             </description>
///         </item>
///         <item>
///             <term>Msg catalog</term>
///             <description>
///                 Lazy-loaded from <c>data/script/msg.xdb</c> via <see cref="MsgXdbParser" />.
///                 <see cref="GetMessage" /> returns the CP949-decoded UI caption for a numeric id.
///                 Record count: 2,644 (SAMPLE-VERIFIED: 1,364,304 bytes / 516 = 2,644 records).
///                 spec: Docs/RE/formats/misc_data.md §6 — CODE-CONFIRMED loader + stride; SAMPLE-VERIFIED content.
///             </description>
///         </item>
///     </list>
///     Threading contract: all public methods must be called on the Godot main thread (same as every
///     other <see cref="ImageTexture" /> / <see cref="Image" /> operation).
/// </summary>
public sealed class UiCatalogs : IDisposable
{
    // VFS paths.
    // spec: Docs/RE/formats/ui_manifests.md §1 — "data/ui/UiTex.txt": PARSER-CONFIRMED.
    private const string UiTexPath = "data/ui/UiTex.txt";

    // spec: Docs/RE/formats/misc_data.md §6 — "data/script/msg.xdb": CODE-CONFIRMED.
    private const string MsgXdbPath = "data/script/msg.xdb";

    // The shared VFS asset handle.  Null when the VFS is unavailable (offline mode).
    private readonly RealClientAssets? _assets;

    // Texture cache keyed by tex_id — each atlas DDS is loaded at most once per session.
    // spec: Docs/RE/formats/ui_manifests.md §1.5 — non-contiguous id space; dict lookup required.
    private readonly Dictionary<int, ImageTexture?> _texCache = new();

    private bool _disposed;

    // Lazy-loaded msg catalog: parsed on first GetMessage() call.
    private MsgXdbCatalog? _msg;
    private bool _msgAttempted;

    // Lazy-loaded uitex manifest: parsed on first GetTexture() call.
    private UiTexManifest? _uitex;
    private bool _uitexAttempted;

    // -------------------------------------------------------------------------
    // Construction
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Creates a UiCatalogs backed by the supplied <paramref name="assets" /> handle.
    ///     Pass <see langword="null" /> for offline / no-VFS mode; both helpers return
    ///     their fallback values.
    /// </summary>
    public UiCatalogs(RealClientAssets? assets)
    {
        _assets = assets;
    }

    // -------------------------------------------------------------------------
    // Diagnostic helpers (for headless verify GD.Print)
    // -------------------------------------------------------------------------

    /// <summary>Number of DDS entries in the loaded uitex manifest, or 0 when unloaded/offline.</summary>
    public int UiTexEntryCount
    {
        get
        {
            var m = EnsureUiTex();
            return m?.DdsEntries.Count ?? 0;
        }
    }

    /// <summary>Number of records in the loaded msg catalog, or 0 when unloaded/offline.</summary>
    public int MsgRecordCount
    {
        get
        {
            var c = EnsureMsg();
            return c?.Count ?? 0;
        }
    }

    // -------------------------------------------------------------------------
    // IDisposable
    // -------------------------------------------------------------------------

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Godot ImageTexture objects are reference-counted by Godot's GC — no manual free needed.
        _texCache.Clear();
        // The _assets handle is owned by the caller (ClientContext); we do not dispose it here.
    }

    // -------------------------------------------------------------------------
    // UiTex — tex_id → ImageTexture
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Returns the Godot <see cref="ImageTexture" /> for the given <paramref name="texId" />,
    ///     or <see langword="null" /> when the VFS is offline, the id is absent from the manifest,
    ///     or the DDS file cannot be loaded.
    ///     The texture is cached after the first load; subsequent calls for the same id are O(1).
    ///     spec: Docs/RE/formats/ui_manifests.md §1.1 — "tex_id handle used by GUWindow/GUPanel bind".
    ///     spec: Docs/RE/formats/ui_manifests.md §1.3 — "id→path mapping": PARSER-CONFIRMED.
    ///     spec: Docs/RE/specs/ui_system.md §8.5 — in-game windows bind by integer tex_id.
    /// </summary>
    /// <param name="texId">
    ///     The 4-digit zero-padded ID from <c>UiTex.txt</c> (e.g. 1 for mainwindow.dds,
    ///     2 for inventwindow.dds, 8 for skillwindow.dds).
    ///     spec: Docs/RE/formats/ui_manifests.md §1.4 — confirmed id→path table (SAMPLE-VERIFIED).
    /// </param>
    public ImageTexture? GetTexture(int texId)
    {
        if (_texCache.TryGetValue(texId, out var cached))
            return cached;

        var manifest = EnsureUiTex();
        if (manifest is null)
        {
            _texCache[texId] = null;
            return null;
        }

        // Look up the VFS path for this tex_id.
        // spec: Docs/RE/formats/ui_manifests.md §1.5 — "must not assume a contiguous id space;
        //       build a dictionary and perform a direct-key lookup": PARSER-CONFIRMED.
        var entry = manifest.GetById(texId);
        if (entry is null)
        {
            // id not present in the manifest — gap in the id space is expected.
            // spec: Docs/RE/formats/ui_manifests.md §1.5 — large gaps confirmed (e.g. 0005–0007).
            _texCache[texId] = null;
            return null;
        }

        ImageTexture? tex = null;
        if (_assets is not null)
            try
            {
                // Delegate to RealClientAssets.LoadTexture which:
                //   (a) probes magic bytes for format (DDS/TGA/PNG/BMP),
                //   (b) handles the do.dds "TGA mislabelled as .dds" caveat,
                //   (c) returns an ImageTexture ready for use in Godot.
                // spec: Docs/RE/formats/ui_manifests.md §7 — do.dds TGA caveat (handled in loader).
                tex = _assets.LoadTexture(entry.VfsPath);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[UiCatalogs] GetTexture(texId={texId}, path='{entry.VfsPath}'): {ex.Message}");
            }

        _texCache[texId] = tex;
        return tex;
    }

    // -------------------------------------------------------------------------
    // Msg.xdb — id → CP949-decoded string
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Returns the CP949-decoded UI caption for the given <paramref name="id" />, or
    ///     <paramref name="fallback" /> when the catalog is unavailable or the id is absent.
    ///     The string is already decoded from CP949 by <see cref="MsgXdbParser" />; no byte decoding
    ///     happens in this layer.
    ///     spec: Docs/RE/formats/misc_data.md §6 — "all visible UI captions fetched by numeric id":
    ///     CODE-CONFIRMED load sequence.
    ///     spec: Docs/RE/specs/ui_system.md §10 — known id ranges (101–107 button labels, etc.).
    /// </summary>
    /// <param name="id">Numeric message id (e.g. 101 = OK, 102 = Cancel, 103 = Close).</param>
    /// <param name="fallback">Returned when the catalog is offline or the id is not present.</param>
    public string GetMessage(int id, string fallback)
    {
        var cat = EnsureMsg();
        if (cat is null) return fallback;

        // spec: Docs/RE/formats/misc_data.md §6 — "do NOT assume id == slot_index + 1;
        //       the id stored at record +0x000 is the authoritative identifier".
        var text = cat.GetText(id);
        return string.IsNullOrEmpty(text) ? fallback : text;
    }

    // -------------------------------------------------------------------------
    // Lazy loaders
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Loads and caches the uitex manifest on first use.
    ///     Returns null when the VFS is offline or the file is absent/malformed.
    /// </summary>
    private UiTexManifest? EnsureUiTex()
    {
        if (_uitexAttempted) return _uitex;
        _uitexAttempted = true;

        if (_assets is null) return null;

        try
        {
            var raw = _assets.GetRaw(UiTexPath);
            if (raw.IsEmpty)
            {
                GD.Print("[UiCatalogs] data/ui/UiTex.txt absent from VFS — texture catalog unavailable.");
                return null;
            }

            // spec: Docs/RE/formats/ui_manifests.md §1.2 — braced-block grammar, '#' comments,
            //       UI_TEXTURE { DDS { id "path" … } MSK { } }: PARSER-CONFIRMED.
            _uitex = UiTexManifestParser.Parse(raw);
            GD.Print($"[UiCatalogs] UiTex.txt loaded: {_uitex.DdsEntries.Count} DDS entries " +
                     $"(spec expects 35 in the observed VFS copy — " +
                     $"Docs/RE/formats/ui_manifests.md §1.4).");
        }
        catch (IOException ex)
        {
            // IO failure reading VFS entry — degrade gracefully (finding 7).
            GD.PrintErr($"[UiCatalogs] UiTex.txt IO error: {ex.Message}");
            _uitex = null;
        }
        catch (InvalidDataException ex)
        {
            // Malformed file data — degrade gracefully (finding 7).
            GD.PrintErr($"[UiCatalogs] UiTex.txt parse failed (InvalidData): {ex.Message}");
            _uitex = null;
        }
        catch (Exception ex) when (ex is ExternalException
                                       or ObjectDisposedException
                                       or InvalidOperationException)
        {
            // Godot-load / native failures — degrade gracefully (finding 7).
            GD.PrintErr($"[UiCatalogs] UiTex.txt Godot-load error: {ex.Message}");
            _uitex = null;
        }

        return _uitex;
    }

    /// <summary>
    ///     Loads and caches the msg catalog on first use.
    ///     Returns null when the VFS is offline or the file is absent/malformed.
    /// </summary>
    private MsgXdbCatalog? EnsureMsg()
    {
        if (_msgAttempted) return _msg;
        _msgAttempted = true;

        if (_assets is null) return null;

        try
        {
            var raw = _assets.GetRaw(MsgXdbPath);
            if (raw.IsEmpty)
            {
                GD.Print("[UiCatalogs] data/script/msg.xdb absent from VFS — message catalog unavailable.");
                return null;
            }

            // spec: Docs/RE/formats/misc_data.md §6 — "record_count = file_size / 516 = 2,644
            //       records exactly": SAMPLE-VERIFIED.
            _msg = MsgXdbParser.Parse(raw);
            GD.Print($"[UiCatalogs] msg.xdb loaded: {_msg.Count} records " +
                     $"(spec expects 2,644 filled + empty slots — " +
                     $"Docs/RE/formats/misc_data.md §6).");
        }
        catch (IOException ex)
        {
            // IO failure reading VFS entry — degrade gracefully (finding 7).
            GD.PrintErr($"[UiCatalogs] msg.xdb IO error: {ex.Message}");
            _msg = null;
        }
        catch (InvalidDataException ex)
        {
            // Malformed file data — degrade gracefully (finding 7).
            GD.PrintErr($"[UiCatalogs] msg.xdb parse failed (InvalidData): {ex.Message}");
            _msg = null;
        }
        catch (Exception ex) when (ex is ExternalException
                                       or ObjectDisposedException
                                       or InvalidOperationException)
        {
            // Godot-load / native failures — degrade gracefully (finding 7).
            GD.PrintErr($"[UiCatalogs] msg.xdb Godot-load error: {ex.Message}");
            _msg = null;
        }

        return _msg;
    }
}