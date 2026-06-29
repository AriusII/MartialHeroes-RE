using System.Globalization;

namespace MartialHeroes.Assets.Mapping;

public static class ItemModelPathResolver
{
    public const string SkinDir = "data/char/skin/";

    private const string SknExtension = ".skn";

    public static string ResolveSkinPath(uint modelRefKey)
    {
        return string.Concat(SkinDir, "g", modelRefKey.ToString(CultureInfo.InvariantCulture), SknExtension);
    }
}