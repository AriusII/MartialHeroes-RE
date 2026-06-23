
using System.Runtime.InteropServices;
using Godot;
using MartialHeroes.Assets.Parsers.DataTables;
using MartialHeroes.Assets.Parsers.DataTables.Models;
using MartialHeroes.Assets.Parsers.Texture;
using MartialHeroes.Assets.Parsers.Texture.Models;
using MartialHeroes.Client.Godot.Composition;

namespace MartialHeroes.Client.Godot.Adapters;

public sealed class UiCatalogs : IDisposable
{
    private const string UiTexPath = "data/ui/UiTex.txt";

    private const string MsgXdbPath = "data/script/msg.xdb";

    private readonly RealClientAssets? _assets;

    private readonly Dictionary<int, ImageTexture?> _texCache = new();

    private bool _disposed;

    private MsgXdbCatalog? _msg;
    private bool _msgAttempted;

    private UiTexManifest? _uitex;
    private bool _uitexAttempted;


    public UiCatalogs(RealClientAssets? assets)
    {
        _assets = assets;
    }


    public int UiTexEntryCount
    {
        get
        {
            var m = EnsureUiTex();
            return m?.DdsEntries.Count ?? 0;
        }
    }

    public int MsgRecordCount
    {
        get
        {
            var c = EnsureMsg();
            return c?.Count ?? 0;
        }
    }


    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _texCache.Clear();
    }


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

        var entry = manifest.GetById(texId);
        if (entry is null)
        {
            _texCache[texId] = null;
            return null;
        }

        ImageTexture? tex = null;
        if (_assets is not null)
            try
            {
                tex = _assets.LoadTexture(entry.VfsPath);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[UiCatalogs] GetTexture(texId={texId}, path='{entry.VfsPath}'): {ex.Message}");
            }

        _texCache[texId] = tex;
        return tex;
    }


    public string GetMessage(int id, string fallback)
    {
        var cat = EnsureMsg();
        if (cat is null) return fallback;

        var text = cat.GetText(id);
        return string.IsNullOrEmpty(text) ? fallback : text;
    }


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

            _uitex = UiTexManifestParser.Parse(raw);
            GD.Print($"[UiCatalogs] UiTex.txt loaded: {_uitex.DdsEntries.Count} DDS entries " +
                     $"(spec expects 35 in the observed VFS copy — " +
                     $"Docs/RE/formats/ui_manifests.md §1.4).");
        }
        catch (IOException ex)
        {
            GD.PrintErr($"[UiCatalogs] UiTex.txt IO error: {ex.Message}");
            _uitex = null;
        }
        catch (InvalidDataException ex)
        {
            GD.PrintErr($"[UiCatalogs] UiTex.txt parse failed (InvalidData): {ex.Message}");
            _uitex = null;
        }
        catch (Exception ex) when (ex is ExternalException
                                       or ObjectDisposedException
                                       or InvalidOperationException)
        {
            GD.PrintErr($"[UiCatalogs] UiTex.txt Godot-load error: {ex.Message}");
            _uitex = null;
        }

        return _uitex;
    }

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

            _msg = MsgXdbParser.Parse(raw);
            GD.Print($"[UiCatalogs] msg.xdb loaded: {_msg.Count} records " +
                     $"(spec expects 2,644 filled + empty slots — " +
                     $"Docs/RE/formats/misc_data.md §6).");
        }
        catch (IOException ex)
        {
            GD.PrintErr($"[UiCatalogs] msg.xdb IO error: {ex.Message}");
            _msg = null;
        }
        catch (InvalidDataException ex)
        {
            GD.PrintErr($"[UiCatalogs] msg.xdb parse failed (InvalidData): {ex.Message}");
            _msg = null;
        }
        catch (Exception ex) when (ex is ExternalException
                                       or ObjectDisposedException
                                       or InvalidOperationException)
        {
            GD.PrintErr($"[UiCatalogs] msg.xdb Godot-load error: {ex.Message}");
            _msg = null;
        }

        return _msg;
    }
}