namespace MartialHeroes.Assets.Parsers.Texture.Models;

public sealed class MapOptionBin
{
    public const int FixedSize = 40;

    public required uint IsDungeon { get; init; }

    public required uint SightDistance { get; init; }

    public required uint LensFlareEnable { get; init; }

    public required uint StarDomeEnable { get; init; }

    public required uint CloudDomeEnable { get; init; }

    public required uint SunEnable { get; init; }

    public required uint MoonEnable { get; init; }

    public required uint SkyboxEnable { get; init; }

    public required uint IndoorFlag { get; init; }

    public required uint Reserved { get; init; }
}

public readonly record struct BgraColor(byte B, byte G, byte R, byte A);

public sealed class FogBin
{
    public const int FixedSize = 204;

    public const int KeyframeCount = 48;

    public required float StartDist { get; init; }

    public required float EndDist { get; init; }

    public required uint DataLoadFlag { get; init; }

    public required BgraColor[] FogColors { get; init; }
}

public sealed class MaterialBin
{
    public const int FixedSize = 9792;

    public const int KeyframeCount = 48;

    public const int ValuesPerKeyframe = 51;

    public required float[][] ColorTable { get; init; }
}

public sealed class LightingKeyframe
{
    public required float[] ColorA { get; init; }

    public required float[] ColorB { get; init; }

    public required float[] ColorC { get; init; }
}

public sealed class LightBin
{
    public const int FixedSize = 5312;

    public const int KeyframeCount = 48;

    public required LightingKeyframe[] DirectionalKeyframes { get; init; }

    public required LightingKeyframe[] AmbientKeyframes { get; init; }

    public required float[] FogDistanceScalars { get; init; }

    public required float[] SecondaryFogScalars { get; init; }

    public required ReadOnlyMemory<byte> RawSectionE { get; init; }

    public required float FallbackScale { get; init; }

    public required float FallbackDirX { get; init; }

    public required float FallbackDirY { get; init; }

    public required float FallbackDirZ { get; init; }

    public required ReadOnlyMemory<byte> RawBytes { get; init; }
}

public sealed class StarDomeBin
{
    public const int FixedSize = 9216;

    public const int KeyframeCount = 12;

    public const int StarsPerKeyframe = 192;

    public required BgraColor[][] StarColors { get; init; }
}

public sealed class CloudDomeBin
{
    public const int FixedSize = 23040;

    public const int KeyframeCount = 12;

    public const int VerticesPerKeyframe = 240;

    public required BgraColor[][] Layer1Colors { get; init; }

    public required BgraColor[][] Layer2Colors { get; init; }
}

public readonly record struct CloudCycleRow(
    byte Speed,
    byte Cloud1Id0To12H,
    byte Cloud1Id12To24H,
    byte Cloud2Id0To6H,
    byte Cloud2Id6To12H,
    byte Cloud2Id12To18H,
    byte Cloud2Id18To24H);

public sealed class CloudCycleBin
{
    public const int FixedSize = 70;

    public const int RowCount = 10;

    public const int BytesPerRow = 7;

    public required CloudCycleRow[] Rows { get; init; }
}