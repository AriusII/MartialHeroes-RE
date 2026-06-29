namespace MartialHeroes.Assets.Parsers.Texture.Models;

public readonly record struct LensFlareSpot(
    int TextureId,
    float Radius,
    float Position,
    byte ColorR,
    byte ColorG,
    byte ColorB,
    byte ColorA);

public sealed class LensFlareConfig
{
    private readonly LensFlareSpot[] _spots;

    internal LensFlareConfig(int spotCount, int textureCount, float intensityBorder, LensFlareSpot[] spots)
    {
        SpotCount = spotCount;
        TextureCount = textureCount;
        IntensityBorder = intensityBorder;
        _spots = spots;
    }

    public int SpotCount { get; }

    public int TextureCount { get; }

    public float IntensityBorder { get; }

    public IReadOnlyList<LensFlareSpot> Spots => _spots;
}