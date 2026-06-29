using System.Globalization;
using MartialHeroes.Assets.Parsers.Character.Models;

namespace MartialHeroes.Client.Infrastructure.Catalog;

public sealed class CharacterVisualCatalogue
{
    private readonly Dictionary<int, CharacterTexEntry> _byTexId;

    public CharacterVisualCatalogue(
        CharFilenameManifest itemSkins,
        CharFilenameManifest charSkins,
        IReadOnlyList<CharacterTexBucket> texBuckets)
    {
        ArgumentNullException.ThrowIfNull(itemSkins);
        ArgumentNullException.ThrowIfNull(charSkins);
        ArgumentNullException.ThrowIfNull(texBuckets);

        ItemSkinFilenames = itemSkins.Entries;
        CharSkinFilenames = charSkins.Entries;

        _byTexId = new Dictionary<int, CharacterTexEntry>();
        foreach (var bucket in texBuckets)
        foreach (var fileName in bucket.Manifest.Entries)
        {
            if (!TryParseTexId(fileName, out var texId))
                continue;

            var entry = new CharacterTexEntry(texId, bucket.BucketDir, fileName, bucket.BucketDir + fileName);
            _byTexId.TryAdd(texId, entry);
        }
    }

    public IReadOnlyList<string> ItemSkinFilenames { get; }

    public IReadOnlyList<string> CharSkinFilenames { get; }

    public int TexCount => _byTexId.Count;

    public int SkinCount => ItemSkinFilenames.Count + CharSkinFilenames.Count;

    public CharacterTexEntry? GetTexById(int texId)
    {
        return _byTexId.GetValueOrDefault(texId);
    }

    public static CharacterVisualCatalogue FromLoader(VfsCatalogueLoader loader)
    {
        ArgumentNullException.ThrowIfNull(loader);

        var buckets = new CharacterTexBucket[]
        {
            new(VfsCatalogueLoader.Tex10241024Dir, loader.LoadCharTex10241024List()),
            new(VfsCatalogueLoader.Tex512512Dir, loader.LoadCharTex512512List()),
            new(VfsCatalogueLoader.Tex256256Dir, loader.LoadCharTex256256List()),
            new(VfsCatalogueLoader.Tex256512Dir, loader.LoadCharTex256512List())
        };

        return new CharacterVisualCatalogue(loader.LoadItemSkinlist(), loader.LoadCharSkinlist(), buckets);
    }

    private static bool TryParseTexId(string fileName, out int texId)
    {
        texId = 0;
        var dot = fileName.LastIndexOf('.');
        var stem = dot >= 0 ? fileName.AsSpan(0, dot) : fileName.AsSpan();
        var end = 0;
        while (end < stem.Length && char.IsAsciiDigit(stem[end]))
            end++;

        if (end == 0)
            return false;

        return int.TryParse(stem[..end], NumberStyles.Integer, CultureInfo.InvariantCulture, out texId);
    }
}

public sealed record CharacterTexBucket(string BucketDir, CharFilenameManifest Manifest);

public sealed record CharacterTexEntry(int TexId, string BucketDir, string FileName, string VfsPath);