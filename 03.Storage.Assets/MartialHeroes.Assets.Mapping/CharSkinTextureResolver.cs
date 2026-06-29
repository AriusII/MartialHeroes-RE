using System.Globalization;
using MartialHeroes.Assets.Parsers.Character.Models;

namespace MartialHeroes.Assets.Mapping;

public sealed class CharSkinTextureResolver
{
    public static readonly IReadOnlyList<Bucket> Buckets =
    [
        new("data/char/tex10241024list.txt", "data/char/tex10241024/"),
        new("data/char/tex512512list.txt", "data/char/tex512512/"),
        new("data/char/tex256512list.txt", "data/char/tex256512/"),
        new("data/char/tex256256list.txt", "data/char/tex256256/")
    ];

    private readonly Dictionary<int, string> _index;

    public CharSkinTextureResolver(IReadOnlyDictionary<string, CharFilenameManifest> manifestsByDirectory)
    {
        ArgumentNullException.ThrowIfNull(manifestsByDirectory);

        var index = new Dictionary<int, string>();

        foreach (var pair in manifestsByDirectory)
        {
            var directory = pair.Key;
            var manifest = pair.Value;
            if (manifest is null)
                continue;

            foreach (var entry in manifest.Entries)
            {
                var fileName = FileNameOf(entry);
                var stem = StemOf(fileName);
                if (!int.TryParse(stem, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                    continue;

                index.TryAdd(id, string.Concat(directory, fileName));
            }
        }

        _index = index;
    }

    public int Count => _index.Count;

    public string? Resolve(int texId)
    {
        return _index.TryGetValue(texId, out var path) ? path : null;
    }

    private static string FileNameOf(string entry)
    {
        var slash = entry.LastIndexOfAny(['/', '\\']);
        return slash < 0 ? entry : entry[(slash + 1)..];
    }

    private static string StemOf(string fileName)
    {
        var dot = fileName.LastIndexOf('.');
        return dot < 0 ? fileName : fileName[..dot];
    }

    public readonly record struct Bucket(string ManifestPath, string Directory);
}