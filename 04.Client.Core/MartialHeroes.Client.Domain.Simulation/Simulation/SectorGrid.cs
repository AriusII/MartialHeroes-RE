namespace MartialHeroes.Client.Domain.Simulation.Simulation;

public enum StreamQuality
{
    Medium = 1,

    High = 2
}

public static class SectorGrid
{
    public const float SectorSizeWorldUnits = 1024.0f;

    public const int OriginBias = 10000;

    public const int EvictionRadius = 2;

    public static (int MapX, int MapZ) WorldToSector(float worldX, float worldZ)
    {
        var adjustedX = worldX < 0.0f ? worldX - SectorSizeWorldUnits : worldX;
        var adjustedZ = worldZ < 0.0f ? worldZ - SectorSizeWorldUnits : worldZ;

        var cellXRaw = (int)(adjustedX / SectorSizeWorldUnits);
        var cellZRaw = (int)(adjustedZ / SectorSizeWorldUnits);

        return (cellXRaw + OriginBias, cellZRaw + OriginBias);
    }

    public static int RingRadiusFor(StreamQuality quality)
    {
        return quality switch
        {
            StreamQuality.High => 2,
            _ => 1
        };
    }

    public static int RequiredSectorCount(int ringRadius)
    {
        if (ringRadius < 0)
            throw new ArgumentOutOfRangeException(nameof(ringRadius), "Ring radius must be non-negative.");

        var side = 2 * ringRadius + 1;
        return side * side;
    }

    public static int RequiredSectors(
        int centerX,
        int centerZ,
        int ringRadius,
        Span<(int MapX, int MapZ)> destination)
    {
        var count = RequiredSectorCount(ringRadius);
        if (destination.Length < count)
            throw new ArgumentException(
                $"Destination span too small: need {count}, have {destination.Length}.",
                nameof(destination));

        var index = 0;
        for (var dz = -ringRadius; dz <= ringRadius; dz++)
        for (var dx = -ringRadius; dx <= ringRadius; dx++)
            destination[index++] = (centerX + dx, centerZ + dz);

        return count;
    }

    public static bool ShouldEvict(int centerX, int centerZ, int cellX, int cellZ)
    {
        var distX = Math.Abs(cellX - centerX);
        var distZ = Math.Abs(cellZ - centerZ);
        var chebyshev = Math.Max(distX, distZ);
        return chebyshev > EvictionRadius;
    }
}