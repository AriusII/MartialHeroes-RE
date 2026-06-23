#nullable enable
namespace MartialHeroes.Assets.Mapping;

public static class CharSkinTextureResolver
{
    private static readonly string[] BucketDirs =
    [
        "data/char/tex10241024/",
        "data/char/tex512512/",
        "data/char/tex256512/",
        "data/char/tex256256/",
    ];

    private static readonly string[] ProbeExtensions =
    [
        ".png",
        ".dds",
        ".bmp",
    ];

    public static string? Resolve(
        int texId,
        Func<string, bool> vfsContains,
        Action<string>? onMissing = null)
    {
        ArgumentNullException.ThrowIfNull(vfsContains);

        var name = texId.ToString(System.Globalization.CultureInfo.InvariantCulture);

        foreach (var bucket in BucketDirs)
        {
            foreach (var ext in ProbeExtensions)
            {
                var candidate = string.Concat(bucket, name, ext);
                if (vfsContains(candidate))
                    return candidate;
            }
        }

        onMissing?.Invoke(
            $"CharSkinTextureResolver: no texture found for tex_id={texId} in any char tex bucket");

        return null;
    }
}
