
using Godot;
using MartialHeroes.Assets.Parsers.DataTables;
using MartialHeroes.Assets.Parsers.DataTables.Models;
using MartialHeroes.Client.Godot.Composition;

namespace MartialHeroes.Client.Godot.Ui.Assets;

public sealed class HudTextLibrary : IDisposable
{
    private const string MsgXdbPath = "data/script/msg.xdb";

    private readonly RealClientAssets? _assets;

    private MsgXdbCatalog? _catalog;
    private bool _catalogAttempted;

    private bool _disposed;


    public HudTextLibrary(RealClientAssets? assets)
    {
        _assets = assets;
    }


    public int RecordCount
    {
        get
        {
            var c = EnsureCatalog();
            return c?.Count ?? 0;
        }
    }


    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }


    public string GetCaption(int captionId, string fallback = "")
    {
        var cat = EnsureCatalog();
        if (cat is null) return fallback;

        var text = cat.GetText(captionId);
        return string.IsNullOrEmpty(text) ? fallback : text;
    }

    public string GetCaption(uint captionId, string fallback = "")
    {
        return GetCaption((int)captionId, fallback);
    }


    private MsgXdbCatalog? EnsureCatalog()
    {
        if (_catalogAttempted) return _catalog;
        _catalogAttempted = true;

        if (_assets is null) return null;

        try
        {
            var raw = _assets.GetRaw(MsgXdbPath);
            if (raw.IsEmpty)
            {
                GD.Print("[HudTextLibrary] data/script/msg.xdb absent from VFS — captions unavailable.");
                return null;
            }

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
}