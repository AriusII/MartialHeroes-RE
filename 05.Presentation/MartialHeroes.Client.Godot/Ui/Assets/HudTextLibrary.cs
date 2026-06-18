// Ui/Assets/HudTextLibrary.cs
//
// Text library for the shared HUD substrate.
//
// Responsibilities:
//   - Loads data/script/msg.xdb (516-byte records: i32 caption_id @+0, 512B CP949 @+4).
//   - Provides GetCaption(id) → already-decoded CP949 .NET string via MsgXdbParser.
//   - Binary-search lookup keyed by unsigned caption_id (ascending-order table).
//   - Offline / VFS-absent: returns the caller-supplied fallback string. Never throws.
//
// CP949 note: MsgXdbParser decodes the CP949 buffer to .NET strings; we never decode
// bytes in the UI layer. The string arrives ready to render in a Label/RichTextLabel.
//
// spec: Docs/RE/formats/msg_xdb.md — 516-byte records, i32 id @+0x000, 512B CP949 @+0x004.
// spec: Docs/RE/formats/msg_xdb.md — NUL-terminated within 512B field; 0xEE fill after NUL.
// spec: Docs/RE/formats/msg_xdb.md — ascending unsigned id order; binary-search lookup.
// spec: Docs/RE/formats/msg_xdb.md — 2,644 records (1,364,304 / 516 = 2,644): SAMPLE-VERIFIED.

using Godot;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Client.Godot.Dev;

namespace MartialHeroes.Client.Godot.Ui.Assets;

/// <summary>
/// Shared HUD text library — looks up CP949-decoded UI captions from msg.xdb by numeric id.
///
/// <para>One instance per session, created by the composition root (ClientContext).</para>
/// <para>All public methods return the caller-supplied fallback string when the VFS is
/// unavailable, the file is absent, or the id is not present.</para>
///
/// spec: Docs/RE/formats/msg_xdb.md — full format and lookup contract.
/// </summary>
public sealed class HudTextLibrary : IDisposable
{
    // VFS path of the message catalogue.
    // spec: Docs/RE/formats/msg_xdb.md — "data/script/msg.xdb": CONFIRMED.
    private const string MsgXdbPath = "data/script/msg.xdb";

    private readonly RealClientAssets? _assets;

    // Lazy-loaded msg.xdb catalogue.
    private MsgXdbCatalog? _catalog;
    private bool _catalogAttempted;

    private bool _disposed;

    // -------------------------------------------------------------------------
    // Construction
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates a HudTextLibrary backed by the supplied VFS assets handle.
    /// Pass <see langword="null"/> for offline / no-VFS mode; all lookups return the fallback.
    /// </summary>
    public HudTextLibrary(RealClientAssets? assets)
    {
        _assets = assets;
    }

    // -------------------------------------------------------------------------
    // Caption lookup
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the CP949-decoded UI caption for the given <paramref name="captionId"/>,
    /// or <paramref name="fallback"/> when the catalogue is unavailable or the id is absent.
    ///
    /// <para>The string is already decoded from CP949 by <see cref="MsgXdbParser"/>; no byte
    /// decoding happens in this layer. Pass it directly to a <see cref="Label.Text"/>.</para>
    ///
    /// <para>Key lookup uses unsigned comparison (ascending unsigned order on disk).
    /// ID space is SPARSE — do not assume id == record_index + 1.</para>
    ///
    /// spec: Docs/RE/formats/msg_xdb.md — "ordered-map lower-bound (binary search) on caption_id".
    /// spec: Docs/RE/formats/msg_xdb.md — "key comparison is UNSIGNED": CONFIRMED.
    /// spec: Docs/RE/formats/msg_xdb.md — caption IDs are SPARSE.
    /// </summary>
    /// <param name="captionId">
    /// Numeric caption identifier (e.g. 4001–4022 = EULA lines, 14001–14002 = char-select prompts).
    /// spec: Docs/RE/formats/msg_xdb.md (front-end caption-ID index).
    /// </param>
    /// <param name="fallback">Returned when the catalogue is offline or the id is absent.</param>
    public string GetCaption(int captionId, string fallback = "")
    {
        MsgXdbCatalog? cat = EnsureCatalog();
        if (cat is null) return fallback;

        // MsgXdbParser returns null/empty for absent ids.
        string? text = cat.GetText(captionId);
        return string.IsNullOrEmpty(text) ? fallback : text;
    }

    /// <summary>
    /// Convenience overload accepting an unsigned caption id.
    ///
    /// spec: Docs/RE/formats/msg_xdb.md — "key comparison is UNSIGNED": CONFIRMED.
    /// </summary>
    public string GetCaption(uint captionId, string fallback = "")
        => GetCaption((int)captionId, fallback);

    // -------------------------------------------------------------------------
    // Diagnostics
    // -------------------------------------------------------------------------

    /// <summary>Number of records in the loaded catalogue, or 0 when unloaded/offline.</summary>
    public int RecordCount
    {
        get
        {
            MsgXdbCatalog? c = EnsureCatalog();
            return c?.Count ?? 0;
        }
    }

    // -------------------------------------------------------------------------
    // Lazy loader
    // -------------------------------------------------------------------------

    private MsgXdbCatalog? EnsureCatalog()
    {
        if (_catalogAttempted) return _catalog;
        _catalogAttempted = true;

        if (_assets is null) return null;

        try
        {
            ReadOnlyMemory<byte> raw = _assets.GetRaw(MsgXdbPath);
            if (raw.IsEmpty)
            {
                GD.Print("[HudTextLibrary] data/script/msg.xdb absent from VFS — captions unavailable.");
                return null;
            }

            // spec: Docs/RE/formats/msg_xdb.md — "record_count = file_size / 516 = 2,644": SAMPLE-VERIFIED.
            // MsgXdbParser decodes CP949 text; 0xEE post-NUL fill is stripped automatically.
            _catalog = MsgXdbParser.Parse(raw);
            GD.Print($"[HudTextLibrary] msg.xdb loaded: {_catalog.Count} records " +
                     "(spec expects 2,644 — Docs/RE/formats/msg_xdb.md SAMPLE-VERIFIED).");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[HudTextLibrary] msg.xdb load/parse failed: {ex.Message}");
            _catalog = null;
        }

        return _catalog;
    }

    // -------------------------------------------------------------------------
    // IDisposable
    // -------------------------------------------------------------------------

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // _assets is owned by ClientContext; we do not dispose it here.
    }
}