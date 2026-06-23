using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.Texture.Models;

namespace MartialHeroes.Assets.Parsers.Texture;

public static class EnvironmentBinParsers
{
    private const int LightSectionAOffset = 0x0000;
    private const int LightSectionBOffset = 0x0930;
    private const int LightSectionCOffset = 0x1260;
    private const int LightSectionCCount = 48;
    private const int LightSectionDOffset = 0x1320;
    private const int LightSectionDCount = 48;
    private const int LightSectionEOffset = 0x13E0;
    private const int LightSectionESize = 200;
    private const int LightFallbackOffset = 0x14B0;
    private const int LightKeyframeStride = 48;

    public static MapOptionBin ParseMapOption(ReadOnlyMemory<byte> data)
    {
        return ParseMapOption(data.Span);
    }

    public static MapOptionBin ParseMapOption(ReadOnlySpan<byte> span)
    {
        if (span.Length != MapOptionBin.FixedSize)
            throw new InvalidDataException(
                $"map_option*.bin parse error: expected {MapOptionBin.FixedSize} bytes, " +
                $"got {span.Length}. " +
                "spec: Docs/RE/formats/environment_bins.md §1.");


        var isDungeon = BinaryPrimitives.ReadUInt32LittleEndian(span[..]);
        var sightDistance = BinaryPrimitives.ReadUInt32LittleEndian(span[0x04..]);
        var lensFlareEnable = BinaryPrimitives.ReadUInt32LittleEndian(span[0x08..]);
        var starDomeEnable = BinaryPrimitives.ReadUInt32LittleEndian(span[0x0C..]);
        var cloudDomeEnable = BinaryPrimitives.ReadUInt32LittleEndian(span[0x10..]);
        var sunEnable = BinaryPrimitives.ReadUInt32LittleEndian(span[0x14..]);
        var moonEnable = BinaryPrimitives.ReadUInt32LittleEndian(span[0x18..]);
        var skyboxEnable = BinaryPrimitives.ReadUInt32LittleEndian(span[0x1C..]);
        var indoorFlag = BinaryPrimitives.ReadUInt32LittleEndian(span[0x20..]);
        var reserved = BinaryPrimitives.ReadUInt32LittleEndian(span[0x24..]);

        return new MapOptionBin
        {
            IsDungeon = isDungeon,
            SightDistance = sightDistance,
            LensFlareEnable = lensFlareEnable,
            StarDomeEnable = starDomeEnable,
            CloudDomeEnable = cloudDomeEnable,
            SunEnable = sunEnable,
            MoonEnable = moonEnable,
            SkyboxEnable = skyboxEnable,
            IndoorFlag = indoorFlag,
            Reserved = reserved
        };
    }

    public static FogBin ParseFog(ReadOnlyMemory<byte> data)
    {
        return ParseFog(data.Span);
    }

    public static FogBin ParseFog(ReadOnlySpan<byte> span)
    {
        if (span.Length != FogBin.FixedSize)
            throw new InvalidDataException(
                $"fog*.bin parse error: expected {FogBin.FixedSize} bytes, " +
                $"got {span.Length}. " +
                "spec: Docs/RE/formats/environment_bins.md §2.");

        var startDist = BinaryPrimitives.ReadSingleLittleEndian(span[..]);
        var endDist = BinaryPrimitives.ReadSingleLittleEndian(span[0x04..]);
        var dataLoadFlag = BinaryPrimitives.ReadUInt32LittleEndian(span[0x08..]);

        var colors = new BgraColor[FogBin.KeyframeCount];
        var colorBase = 0x0C;
        for (var i = 0; i < FogBin.KeyframeCount; i++)
        {
            var off = colorBase + i * 4;
            colors[i] = new BgraColor(
                span[off],
                span[off + 1],
                span[off + 2],
                span[off + 3]);
        }

        return new FogBin
        {
            StartDist = startDist,
            EndDist = endDist,
            DataLoadFlag = dataLoadFlag,
            FogColors = colors
        };
    }


    public static MaterialBin ParseMaterial(ReadOnlyMemory<byte> data)
    {
        return ParseMaterial(data.Span);
    }

    public static MaterialBin ParseMaterial(ReadOnlySpan<byte> span)
    {
        if (span.Length != MaterialBin.FixedSize)
            throw new InvalidDataException(
                $"material*.bin parse error: expected {MaterialBin.FixedSize} bytes, " +
                $"got {span.Length}. " +
                "spec: Docs/RE/formats/environment_bins.md §3.");

        const int rowStride = MaterialBin.ValuesPerKeyframe * 4;
        var table = new float[MaterialBin.KeyframeCount][];
        for (var k = 0; k < MaterialBin.KeyframeCount; k++)
        {
            var rowOffset = k * rowStride;
            var row = new float[MaterialBin.ValuesPerKeyframe];
            for (var j = 0; j < MaterialBin.ValuesPerKeyframe; j++)
                row[j] = BinaryPrimitives.ReadSingleLittleEndian(span[(rowOffset + j * 4)..]);

            table[k] = row;
        }

        return new MaterialBin { ColorTable = table };
    }

    public static LightBin ParseLight(ReadOnlyMemory<byte> data)
    {
        return ParseLight(data.Span, data);
    }

    private static LightBin ParseLight(ReadOnlySpan<byte> span, ReadOnlyMemory<byte> backing)
    {
        if (span.Length != LightBin.FixedSize)
            throw new InvalidDataException(
                $"light*.bin parse error: expected {LightBin.FixedSize} bytes, " +
                $"got {span.Length}. " +
                "spec: Docs/RE/formats/environment_bins.md §9.0.");

        var dirKf = ReadLightKeyframes(span, LightSectionAOffset, LightBin.KeyframeCount);

        var ambKf = ReadLightKeyframes(span, LightSectionBOffset, LightBin.KeyframeCount);

        var fogScalars = new float[LightSectionCCount];
        for (var i = 0; i < LightSectionCCount; i++)
            fogScalars[i] = BinaryPrimitives.ReadSingleLittleEndian(
                span[(LightSectionCOffset + i * 4)..]);

        var secScalars = new float[LightSectionDCount];
        for (var i = 0; i < LightSectionDCount; i++)
            secScalars[i] = BinaryPrimitives.ReadSingleLittleEndian(
                span[(LightSectionDOffset + i * 4)..]);

        var rawSectionE = backing.IsEmpty
            ? span.Slice(LightSectionEOffset, LightSectionESize).ToArray()
            : backing.Slice(LightSectionEOffset, LightSectionESize);

        var fallbackScale = BinaryPrimitives.ReadSingleLittleEndian(span[LightFallbackOffset..]);
        var fallbackDirX = BinaryPrimitives.ReadSingleLittleEndian(span[(LightFallbackOffset + 4)..]);
        var fallbackDirY = BinaryPrimitives.ReadSingleLittleEndian(span[(LightFallbackOffset + 8)..]);
        var fallbackDirZ = BinaryPrimitives.ReadSingleLittleEndian(span[(LightFallbackOffset + 12)..]);

        var rawBytes = backing.IsEmpty
            ? span.ToArray()
            : backing;

        return new LightBin
        {
            DirectionalKeyframes = dirKf,
            AmbientKeyframes = ambKf,
            FogDistanceScalars = fogScalars,
            SecondaryFogScalars = secScalars,
            RawSectionE = rawSectionE,
            FallbackScale = fallbackScale,
            FallbackDirX = fallbackDirX,
            FallbackDirY = fallbackDirY,
            FallbackDirZ = fallbackDirZ,
            RawBytes = rawBytes
        };
    }

    private static LightingKeyframe[] ReadLightKeyframes(
        ReadOnlySpan<byte> span, int sectionOffset, int count)
    {
        var kf = new LightingKeyframe[count];
        for (var i = 0; i < count; i++)
        {
            var slotBase = sectionOffset + i * LightKeyframeStride;

            var colorA = new float[4];
            colorA[0] = BinaryPrimitives.ReadSingleLittleEndian(span[(slotBase + 0x00)..]);
            colorA[1] = BinaryPrimitives.ReadSingleLittleEndian(span[(slotBase + 0x04)..]);
            colorA[2] = BinaryPrimitives.ReadSingleLittleEndian(span[(slotBase + 0x08)..]);
            colorA[3] = BinaryPrimitives.ReadSingleLittleEndian(span[(slotBase + 0x0C)..]);

            var colorB = new float[4];
            colorB[0] = BinaryPrimitives.ReadSingleLittleEndian(span[(slotBase + 0x10)..]);
            colorB[1] = BinaryPrimitives.ReadSingleLittleEndian(span[(slotBase + 0x14)..]);
            colorB[2] = BinaryPrimitives.ReadSingleLittleEndian(span[(slotBase + 0x18)..]);
            colorB[3] = BinaryPrimitives.ReadSingleLittleEndian(span[(slotBase + 0x1C)..]);

            var colorC = new float[4];
            colorC[0] = BinaryPrimitives.ReadSingleLittleEndian(span[(slotBase + 0x20)..]);
            colorC[1] = BinaryPrimitives.ReadSingleLittleEndian(span[(slotBase + 0x24)..]);
            colorC[2] = BinaryPrimitives.ReadSingleLittleEndian(span[(slotBase + 0x28)..]);
            colorC[3] = BinaryPrimitives.ReadSingleLittleEndian(span[(slotBase + 0x2C)..]);

            kf[i] = new LightingKeyframe
            {
                ColorA = colorA,
                ColorB = colorB,
                ColorC = colorC
            };
        }

        return kf;
    }


    public static StarDomeBin ParseStarDome(ReadOnlyMemory<byte> data)
    {
        return ParseStarDome(data.Span);
    }

    public static StarDomeBin ParseStarDome(ReadOnlySpan<byte> span)
    {
        if (span.Length != StarDomeBin.FixedSize)
            throw new InvalidDataException(
                $"stardome*.bin parse error: expected {StarDomeBin.FixedSize} bytes, " +
                $"got {span.Length}. " +
                "spec: Docs/RE/formats/environment_bins.md §4.");

        var starColors = new BgraColor[StarDomeBin.KeyframeCount][];
        var offset = 0;
        for (var k = 0; k < StarDomeBin.KeyframeCount; k++)
        {
            var frame = new BgraColor[StarDomeBin.StarsPerKeyframe];
            for (var s = 0; s < StarDomeBin.StarsPerKeyframe; s++)
            {
                frame[s] = new BgraColor(
                    span[offset],
                    span[offset + 1],
                    span[offset + 2],
                    span[offset + 3]);
                offset += 4;
            }

            starColors[k] = frame;
        }

        return new StarDomeBin { StarColors = starColors };
    }

    public static CloudDomeBin ParseCloudDome(ReadOnlyMemory<byte> data)
    {
        return ParseCloudDome(data.Span);
    }

    public static CloudDomeBin ParseCloudDome(ReadOnlySpan<byte> span)
    {
        if (span.Length != CloudDomeBin.FixedSize)
            throw new InvalidDataException(
                $"clouddome*.bin parse error: expected {CloudDomeBin.FixedSize} bytes, " +
                $"got {span.Length}. " +
                "spec: Docs/RE/formats/environment_bins.md §5.");

        var layer1 = ReadCloudDomeLayer(span, 0x0000);

        var layer2 = ReadCloudDomeLayer(span, 0x2D00);

        return new CloudDomeBin
        {
            Layer1Colors = layer1,
            Layer2Colors = layer2
        };
    }

    private static BgraColor[][] ReadCloudDomeLayer(ReadOnlySpan<byte> span, int offset)
    {
        var layer = new BgraColor[CloudDomeBin.KeyframeCount][];
        for (var k = 0; k < CloudDomeBin.KeyframeCount; k++)
        {
            var frame = new BgraColor[CloudDomeBin.VerticesPerKeyframe];
            for (var v = 0; v < CloudDomeBin.VerticesPerKeyframe; v++)
            {
                frame[v] = new BgraColor(
                    span[offset],
                    span[offset + 1],
                    span[offset + 2],
                    span[offset + 3]);
                offset += 4;
            }

            layer[k] = frame;
        }

        return layer;
    }


    public static CloudCycleBin ParseCloudCycle(ReadOnlyMemory<byte> data)
    {
        return ParseCloudCycle(data.Span);
    }

    public static CloudCycleBin ParseCloudCycle(ReadOnlySpan<byte> span)
    {
        if (span.Length != CloudCycleBin.FixedSize)
            throw new InvalidDataException(
                $"cloud_cycle*.bin parse error: expected {CloudCycleBin.FixedSize} bytes, " +
                $"got {span.Length}. " +
                "spec: Docs/RE/formats/environment_bins.md §6.");

        var rows = new CloudCycleRow[CloudCycleBin.RowCount];
        for (var r = 0; r < CloudCycleBin.RowCount; r++)
        {
            var rowBase = r * CloudCycleBin.BytesPerRow;
            rows[r] = new CloudCycleRow(
                span[rowBase + 0],
                span[rowBase + 1],
                span[rowBase + 2],
                span[rowBase + 3],
                span[rowBase + 4],
                span[rowBase + 5],
                span[rowBase + 6]);
        }

        return new CloudCycleBin { Rows = rows };
    }


    public static PointLightBin ParsePointLight(ReadOnlyMemory<byte> data)
    {
        return ParsePointLight(data.Span, data);
    }

    private static PointLightBin ParsePointLight(ReadOnlySpan<byte> span, ReadOnlyMemory<byte> backing)
    {
        if (span.Length < PointLightBin.HeaderSize)
            throw new InvalidDataException(
                $"point_light*.bin parse error: need at least {PointLightBin.HeaderSize} bytes for header, " +
                $"got {span.Length}.");

        var intensityScale = BinaryPrimitives.ReadSingleLittleEndian(span[..]);
        var recordCount = BinaryPrimitives.ReadInt32LittleEndian(span[0x04..]);

        if (recordCount < 0)
            throw new InvalidDataException(
                $"point_light*.bin parse error: negative record_count {recordCount}.");

        var expectedSize = PointLightBin.HeaderSize + recordCount * PointLightRecord.Stride;
        if (span.Length < expectedSize)
            throw new InvalidDataException(
                $"point_light*.bin parse error: declared count={recordCount} requires {expectedSize} bytes, " +
                $"got {span.Length}.");

        var records = new PointLightRecord[recordCount];
        for (var i = 0; i < recordCount; i++)
        {
            var recBase = PointLightBin.HeaderSize + i * PointLightRecord.Stride;

            var colorDiffuseR = BinaryPrimitives.ReadSingleLittleEndian(span[(recBase + 0x00)..]);
            var colorDiffuseG = BinaryPrimitives.ReadSingleLittleEndian(span[(recBase + 0x04)..]);
            var colorDiffuseB = BinaryPrimitives.ReadSingleLittleEndian(span[(recBase + 0x08)..]);

            var colorBR = BinaryPrimitives.ReadSingleLittleEndian(span[(recBase + 0x0C)..]);
            var colorBG = BinaryPrimitives.ReadSingleLittleEndian(span[(recBase + 0x10)..]);
            var colorBB = BinaryPrimitives.ReadSingleLittleEndian(span[(recBase + 0x14)..]);

            var colorCR = BinaryPrimitives.ReadSingleLittleEndian(span[(recBase + 0x18)..]);
            var colorCG = BinaryPrimitives.ReadSingleLittleEndian(span[(recBase + 0x1C)..]);
            var colorCB = BinaryPrimitives.ReadSingleLittleEndian(span[(recBase + 0x20)..]);

            var posX = BinaryPrimitives.ReadSingleLittleEndian(span[(recBase + 0x24)..]);
            var posY = BinaryPrimitives.ReadSingleLittleEndian(span[(recBase + 0x28)..]);
            var posZ = BinaryPrimitives.ReadSingleLittleEndian(span[(recBase + 0x2C)..]);

            var range = BinaryPrimitives.ReadSingleLittleEndian(span[(recBase + 0x30)..]);

            var rawAt34 = BinaryPrimitives.ReadUInt32LittleEndian(span[(recBase + 0x34)..]);

            var typeFlag = BinaryPrimitives.ReadInt32LittleEndian(span[(recBase + 0x38)..]);

            records[i] = new PointLightRecord
            {
                ColorDiffuseR = colorDiffuseR,
                ColorDiffuseG = colorDiffuseG,
                ColorDiffuseB = colorDiffuseB,
                ColorBR = colorBR,
                ColorBG = colorBG,
                ColorBB = colorBB,
                ColorCR = colorCR,
                ColorCG = colorCG,
                ColorCB = colorCB,
                PositionX = posX,
                PositionY = posY,
                PositionZ = posZ,
                Range = range,
                RawU32At0x34 = rawAt34,
                TypeFlag = typeFlag
            };
        }

        return new PointLightBin
        {
            ProximityRadius = intensityScale,
            RecordCount = recordCount,
            Records = records
        };
    }

    public static WeatherBin ParseWeather(ReadOnlyMemory<byte> data)
    {
        return ParseWeather(data.Span);
    }

    public static WeatherBin ParseWeather(ReadOnlySpan<byte> span)
    {
        if (span.Length != WeatherBin.FixedSize)
            throw new InvalidDataException(
                $"weather*.bin parse error: expected {WeatherBin.FixedSize} bytes, " +
                $"got {span.Length}.");

        var grid = span.ToArray();

        return new WeatherBin { Grid = grid };
    }
}