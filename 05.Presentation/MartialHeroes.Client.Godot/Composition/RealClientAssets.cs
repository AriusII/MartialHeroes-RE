
using Godot;
using MartialHeroes.Assets.Mapping;
using MartialHeroes.Assets.Parsers.Terrain;
using MartialHeroes.Assets.Parsers.Terrain.Models;
using MartialHeroes.Assets.Vfs;

namespace MartialHeroes.Client.Godot.Composition;

public sealed class RealClientAssets : IDisposable
{

    private readonly MappedVfsArchive _vfs;
    private bool _disposed;

    private RealClientAssets(MappedVfsArchive vfs)
    {
        _vfs = vfs;
    }


    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _vfs.Dispose();
    }


    public static RealClientAssets? TryOpen()
    {
        var clientDir = ClientPathResolver.ResolveClientDir();
        if (clientDir is null)
        {
            GD.Print("[RealClientAssets] No client directory resolved — VFS unavailable.");
            return null;
        }

        var infPath = Path.Combine(clientDir, "data.inf");
        var vfsPath = Path.Combine(clientDir, "data", "data.vfs");

        try
        {
            var vfs = MappedVfsArchive.Open(infPath, vfsPath);
            GD.Print($"[RealClientAssets] VFS opened: {vfs.EntryCount} entries from '{clientDir}'.");
            return new RealClientAssets(vfs);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealClientAssets] VFS open failed from '{clientDir}': {ex.Message}. " +
                        "World will render nothing until VFS is available.");
            return null;
        }
    }



    public (string? TedPath, string? BudPath) LoadMapDatafilePaths(int areaId, int mapX, int mapZ)
    {
        var mapPath = BuildCellBasePath(areaId, mapX, mapZ) + ".map";
        var data = TryGetContent(mapPath);
        if (data.IsEmpty) return (null, null);

        try
        {
            var descriptor = MapDescriptorParser.Parse(data);
            string? tedPath = null;
            string? budPath = null;

            foreach (var section in descriptor.Sections)
                if (section.Keyword.Equals("TERRAIN", StringComparison.OrdinalIgnoreCase) &&
                    section.DataFile is not null)
                    tedPath = section.DataFile;
                else if (section.Keyword.Equals("BUILDING", StringComparison.OrdinalIgnoreCase) &&
                         section.DataFile is not null)
                    budPath = section.DataFile;

            return (tedPath, budPath);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealClientAssets] .map parse failed ({mapPath}): {ex.Message}");
            return (null, null);
        }
    }

    public BudScene? LoadBud(string virtualPath)
    {
        var data = TryGetContent(virtualPath);
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


    public ImageTexture? LoadTexture(string virtualPath)
    {
        var raw = TryGetContent(virtualPath);
        if (raw.IsEmpty) return null;

        try
        {
            var ext = Path.GetExtension(virtualPath).ToLowerInvariant();
            var result = AssetPassthrough.PassthroughImage(raw, ext);

            var isDxt2Premultiplied = false;
            if (result.Format == ImageFormat.Dds && raw.Length >= 88)
            {
                var header = raw.Span;
                isDxt2Premultiplied =
                    header[84] == 0x44 &&
                    header[85] == 0x58 &&
                    header[86] == 0x54 &&
                    header[87] == 0x32;
            }

            var bytes = raw.ToArray();
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

            if (isDxt2Premultiplied)
            {
                if (img.IsCompressed())
                {
                    var decompErr = img.Decompress();
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
                    if (img.GetFormat() != Image.Format.Rgba8)
                        img.Convert(Image.Format.Rgba8);
                    UnpremultiplyAlpha(img);
                    GD.Print($"[RealClientAssets] DXT2 unpremultiplied (pre-decompressed): '{virtualPath}'.");
                }
            }

            if (img.GetFormat() == Image.Format.Rgb8) img.Convert(Image.Format.Rgba8);

            if (img.GetFormat() == Image.Format.Rgba8) img.GenerateMipmaps();

            return ImageTexture.CreateFromImage(img);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealClientAssets] Texture load failed ({virtualPath}): {ex.Message}");
            return null;
        }
    }

    private static void UnpremultiplyAlpha(Image img)
    {
        var w = img.GetWidth();
        var h = img.GetHeight();
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var c = img.GetPixel(x, y);
            var a = c.A;
            if (a > 0f)
                img.SetPixel(x, y, new Color(c.R / a, c.G / a, c.B / a, a));
            else
                img.SetPixel(x, y, new Color(0f, 0f, 0f, 0f));
        }
    }


    public ReadOnlyMemory<byte> GetRaw(string virtualPath)
    {
        return TryGetContent(virtualPath);
    }

    public bool Contains(string virtualPath)
    {
        return _vfs.Contains(virtualPath);
    }


    private static string AreaTag(int areaId)
    {
        var d0 = areaId / 100;
        var d1 = areaId / 10 % 10;
        var d2 = areaId % 10;
        return $"{d0}{d1}{d2}";
    }

    private static string BuildCellBasePath(int areaId, int mapX, int mapZ)
    {
        var tag = AreaTag(areaId);
        return $"data/map{tag}/dat/d{tag}x{mapX}z{mapZ}";
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


    public List<(int MapX, int MapZ)> EnumerateTerrainCells(int areaId)
    {
        var results = new List<(int, int)>();

        var tag = AreaTag(areaId);
        var prefix = $"data/map{tag}/dat/d{tag}x";
        const string tedSuffix = ".ted";

        var entries = _vfs.GetEntries();
        foreach (var entry in entries)
        {
            var name = entry.Name;
            if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (!name.EndsWith(tedSuffix, StringComparison.Ordinal)) continue;

            var inner = name.Length - prefix.Length - tedSuffix.Length;
            if (inner <= 0) continue;

            var middle = name.AsSpan(prefix.Length, inner);
            var zIndex = middle.IndexOf('z');
            if (zIndex <= 0) continue;

            if (int.TryParse(middle[..zIndex], out var mapX) &&
                int.TryParse(middle[(zIndex + 1)..], out var mapZ))
                results.Add((mapX, mapZ));
        }

        return results;
    }
}