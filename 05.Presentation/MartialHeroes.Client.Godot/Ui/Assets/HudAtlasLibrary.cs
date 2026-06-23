using Godot;
using MartialHeroes.Assets.Parsers.Texture;
using MartialHeroes.Assets.Parsers.Texture.Models;
using MartialHeroes.Client.Godot.Composition;

namespace MartialHeroes.Client.Godot.Ui.Assets;

public sealed class HudAtlasLibrary : IDisposable
{
    private const string UiTexPath = "data/ui/UiTex.txt";

    private readonly RealClientAssets? _assets;

    private readonly Dictionary<int, Texture2D?> _texByIdCache = new();

    private readonly Dictionary<string, Texture2D?> _texByPathCache = new();

    private bool _disposed;

    private UiTexManifest? _manifest;
    private bool _manifestAttempted;


    public HudAtlasLibrary(RealClientAssets? assets)
    {
        _assets = assets;
    }


    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _texByIdCache.Clear();
        _texByPathCache.Clear();
    }


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
            _texByIdCache[texId] = null;
            return null;
        }

        var tex = LoadFromVfs(entry.VfsPath);
        _texByIdCache[texId] = tex;
        return tex;
    }


    public Texture2D? GetByPath(string vfsPath)
    {
        if (_texByPathCache.TryGetValue(vfsPath, out var cached))
            return cached;

        var tex = LoadFromVfs(vfsPath);
        _texByPathCache[vfsPath] = tex;
        return tex;
    }


    public AtlasTexture? SliceById(int texId, int srcX, int srcY, int w, int h)
    {
        var atlas = GetById(texId);
        return atlas is null ? null : BuildAtlasTexture(atlas, srcX, srcY, w, h);
    }

    public AtlasTexture? SliceByPath(string vfsPath, int srcX, int srcY, int w, int h)
    {
        var atlas = GetByPath(vfsPath);
        return atlas is null ? null : BuildAtlasTexture(atlas, srcX, srcY, w, h);
    }


    public void Preload(ReadOnlySpan<string> vfsPaths)
    {
        foreach (var path in vfsPaths)
            GetByPath(path);
    }


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


    private Texture2D? LoadFromVfs(string vfsPath)
    {
        if (_assets is null) return null;

        try
        {
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


    private static AtlasTexture BuildAtlasTexture(Texture2D atlas, int srcX, int srcY, int w, int h)
    {
        return new AtlasTexture
        {
            Atlas = atlas,
            Region = new Rect2(srcX, srcY, w, h),
            FilterClip = true
        };
    }
}