using Godot;
using MartialHeroes.Assets.Mapping;
using MartialHeroes.Assets.Parsers.Character;
using MartialHeroes.Assets.Parsers.Character.Models;
using MartialHeroes.Assets.Parsers.Mesh.Models;
using MartialHeroes.Assets.Parsers.Texture;
using MartialHeroes.Assets.Parsers.Texture.Models;
using MartialHeroes.Assets.Vfs;

namespace MartialHeroes.Explorer.Viewer;

public static class ViewerTextures
{
    private const string SkinTxtPath = "data/char/skin.txt";
    private const string BgTexturePath = "data/map000/texture/bgtexture.lst";

    private const int CacheCapacity = 256;
    private static readonly Dictionary<string, ImageTexture?> _cache = new(260);
    private static readonly LinkedList<string> _lru = new();
    private static readonly Dictionary<string, LinkedListNode<string>> _lruNodes = new(260);
    private static readonly HashSet<string> _sessionPaths = new();
    private static bool _trackingActive;

    private static ImageTexture? DecodeCached(MappedVfsArchive archive, string path)
    {
        if (_cache.TryGetValue(path, out var cached))
        {
            if (_lruNodes.TryGetValue(path, out var existing))
            {
                _lru.Remove(existing);
                _lruNodes[path] = _lru.AddLast(path);
            }

            if (_trackingActive) _sessionPaths.Add(path);
            return cached;
        }

        if (_cache.Count >= CacheCapacity)
            EvictOldest();
        var tex = Decode(archive.GetFileContent(path));
        _cache[path] = tex;
        _lruNodes[path] = _lru.AddLast(path);
        if (_trackingActive) _sessionPaths.Add(path);
        return tex;
    }

    internal static void BeginSession()
    {
        _sessionPaths.Clear();
        _trackingActive = true;
    }

    internal static IReadOnlySet<string> EndSession()
    {
        _trackingActive = false;
        var result = _sessionPaths.ToHashSet();
        _sessionPaths.Clear();
        return result;
    }

    internal static void EvictAll()
    {
        var count = _cache.Count;
        _cache.Clear();
        _lru.Clear();
        _lruNodes.Clear();
        GD.Print($"[ViewerTextures] EvictAll: freed {count} cached textures.");
    }

    internal static void EvictPaths(IReadOnlySet<string> paths)
    {
        var freed = 0;
        foreach (var path in paths)
        {
            if (!_cache.Remove(path)) continue;
            if (_lruNodes.Remove(path, out var node))
                _lru.Remove(node);
            freed++;
        }

        if (freed > 0)
            GD.Print($"[ViewerTextures] EvictPaths: freed {freed} targeted textures.");
    }

    private static void EvictOldest()
    {
        var node = _lru.First;
        if (node is null) return;
        _lru.Remove(node);
        _cache.Remove(node.Value);
        _lruNodes.Remove(node.Value);
    }

    public static ImageTexture? Decode(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            var desc = TextureDetector.Detect(bytes);
            var img = new Image();
            Error err;

            switch (desc.Format)
            {
                case TextureFormat.Png:
                    err = img.LoadPngFromBuffer(bytes.ToArray());
                    break;
                case TextureFormat.Dds:
                    using (var ms = new MemoryStream())
                    {
                        PngConverter.WritePng(desc, ms);
                        err = img.LoadPngFromBuffer(ms.ToArray());
                    }

                    break;
                case TextureFormat.Bmp:
                    err = img.LoadBmpFromBuffer(bytes.ToArray());
                    break;
                case TextureFormat.Tga:
                    err = img.LoadTgaFromBuffer(bytes.ToArray());
                    break;
                default:
                    return null;
            }

            if (err != Error.Ok) return null;

            if (img.GetFormat() == Image.Format.Rgb8)
                img.Convert(Image.Format.Rgba8);
            img.GenerateMipmaps();
            return ImageTexture.CreateFromImage(img);
        }
        catch
        {
            return null;
        }
    }

    public static Resolved ResolveSkn(MappedVfsArchive archive, SkinnedMesh mesh)
    {
        if (!archive.Contains(SkinTxtPath))
            return new Resolved(null, null, "skin.txt absent from VFS");

        SkinTxtCatalog catalog;
        try
        {
            catalog = SkinTxtParser.Parse(archive.GetFileContent(SkinTxtPath));
        }
        catch
        {
            return new Resolved(null, null, "skin.txt parse failed");
        }

        var texId = catalog.GetByMeshGid((int)mesh.IdA)?.TextureId;
        if (texId is null || texId.Value == 0)
            return new Resolved(null, null, $"no skin.txt row for IdA={mesh.IdA}");

        var path = CharSkinTextureResolver.Resolve(texId.Value, archive.Contains);
        if (path is null)
            return new Resolved(null, null, $"texId {texId.Value} has no texture file");

        var tex = DecodeCached(archive, path);
        return tex is null
            ? new Resolved(null, path, $"texId {texId.Value} decode failed")
            : new Resolved(tex, path, "ok");
    }

    public static ImageTexture? ResolveTexId(MappedVfsArchive archive, int texId)
    {
        if (texId <= 0) return null;
        var path = CharSkinTextureResolver.Resolve(texId, archive.Contains);
        if (path is null || !archive.Contains(path)) return null;
        return DecodeCached(archive, path);
    }

    public static BgTextureCatalog? LoadBgCatalog(MappedVfsArchive archive)
    {
        if (!archive.Contains(BgTexturePath)) return null;
        try
        {
            return BgTextureCatalog.FromLst(archive.GetFileContent(BgTexturePath));
        }
        catch
        {
            return null;
        }
    }

    public static Resolved ResolveBgSlot(MappedVfsArchive archive, BgTextureCatalog? catalog, int texId)
    {
        if (catalog is null)
            return new Resolved(null, null, "no bgtexture pool");

        var path = catalog.ResolveTexturePath(texId);
        if (path is null || !archive.Contains(path))
            return new Resolved(null, path, $"slot {texId} unresolved");

        var tex = DecodeCached(archive, path);
        return tex is null
            ? new Resolved(null, path, $"slot {texId} decode failed")
            : new Resolved(tex, path, "ok");
    }

    public sealed record Resolved(ImageTexture? Texture, string? Path, string Note);
}