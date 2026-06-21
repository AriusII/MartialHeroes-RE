namespace MartialHeroes.Client.Domain.Simulation.Simulation;

/// <summary>
///     Streaming quality selector for the terrain sector ring.
/// </summary>
/// <remarks>
///     The legacy client picks the ring shape at startup from a stored stream radius compared against
///     1000.0: greater than 1000 selects the 5×5 dispatcher, otherwise the 3×3 dispatcher
///     (spec: Docs/RE/formats/terrain.md §9.2). We model the three documented quality levels and map
///     them to a Chebyshev ring radius: High → 2 (5×5), Medium/Low → 1 (3×3).
/// </remarks>
public enum StreamQuality
{
    /// <summary>Low quality (radius 600.0). 3×3 ring (ring radius 1). spec: terrain.md §9.2.</summary>
    Low = 0,

    /// <summary>Medium quality (radius 1000.0). 3×3 ring (ring radius 1). spec: terrain.md §9.2.</summary>
    Medium = 1,

    /// <summary>High quality (radius 1800.0). 5×5 ring (ring radius 2). spec: terrain.md §9.2.</summary>
    High = 2
}

/// <summary>
///     Pure, deterministic math for the terrain streaming sector grid: world-position to sector
///     mapping, the loaded ring of sectors around a centre, and the eviction predicate. No I/O, no
///     platform state — the actual cell loading lives in Assets.Parsers / Application; this type only
///     computes which sectors should be resident.
/// </summary>
/// <remarks>
///     <para>
///         The world is a uniform grid of cells of exactly 1024×1024 world units, identified by a
///         <c>(mapX, mapZ)</c> integer pair biased around the world origin (10000, 10000)
///         (spec: Docs/RE/formats/terrain.md §Overview, §1.4). All values here are our deterministic
///         re-derivation of the documented formulas; no sample data is encoded.
///     </para>
/// </remarks>
public static class SectorGrid
{
    /// <summary>
    ///     Edge length of one terrain cell in world units. One cell covers exactly 1024×1024 units.
    ///     spec: Docs/RE/formats/terrain.md §Overview / §1.4 ("1024 × 1024 world units").
    /// </summary>
    public const float SectorSizeWorldUnits = 1024.0f; // spec: Docs/RE/formats/terrain.md §streaming

    /// <summary>
    ///     World-origin bias added to the raw cell index to form the <c>(mapX, mapZ)</c> coordinate.
    ///     The cell whose south-west corner is world (0,0) has coordinate (10000, 10000).
    ///     spec: Docs/RE/formats/terrain.md §Overview / §2.
    /// </summary>
    public const int OriginBias = 10000; // spec: Docs/RE/formats/terrain.md §streaming

    /// <summary>
    ///     Chebyshev distance beyond which a loaded cell is evicted. Cells strictly farther than this
    ///     from the centre (in either axis) are marked evictable; exactly-2-away cells are retained.
    ///     spec: Docs/RE/formats/terrain.md §9.3 ("strictly &gt; 2").
    /// </summary>
    public const int EvictionRadius = 2; // spec: Docs/RE/formats/terrain.md §9.3

    /// <summary>
    ///     Maps a world-space position to its terrain sector coordinate <c>(mapX, mapZ)</c>.
    /// </summary>
    /// <remarks>
    ///     Implements the documented negative-axis correction: for a negative world coordinate, 1024 is
    ///     subtracted before truncation so the division floors correctly (truncation toward zero would
    ///     otherwise round the wrong way for negatives). spec: Docs/RE/formats/terrain.md §2.
    /// </remarks>
    /// <param name="worldX">World-space X coordinate.</param>
    /// <param name="worldZ">World-space Z coordinate.</param>
    /// <returns>The biased sector coordinate pair.</returns>
    public static (int MapX, int MapZ) WorldToSector(float worldX, float worldZ)
    {
        // Negative-axis correction: subtract one cell before truncation so negative coordinates
        // floor toward negative infinity. spec: Docs/RE/formats/terrain.md §2.
        var adjustedX = worldX < 0.0f ? worldX - SectorSizeWorldUnits : worldX;
        var adjustedZ = worldZ < 0.0f ? worldZ - SectorSizeWorldUnits : worldZ;

        // Truncation toward zero of the adjusted value reproduces floor-division semantics for both
        // signs. The multiplier 1/1024 is documented; we divide for clarity (same result).
        var cellXRaw = (int)(adjustedX / SectorSizeWorldUnits);
        var cellZRaw = (int)(adjustedZ / SectorSizeWorldUnits);

        return (cellXRaw + OriginBias, cellZRaw + OriginBias);
    }

    /// <summary>
    ///     Selects the Chebyshev ring radius for a streaming quality level.
    /// </summary>
    /// <remarks>
    ///     Low and Medium use a 3×3 ring (radius 1); High uses a 5×5 ring (radius 2).
    ///     spec: Docs/RE/formats/terrain.md §9.2.
    /// </remarks>
    public static int RingRadiusFor(StreamQuality quality)
    {
        return quality switch
        {
            StreamQuality.High => 2, // 5×5. spec: terrain.md §9.2
            StreamQuality.Medium => 1, // 3×3. spec: terrain.md §9.2
            StreamQuality.Low => 1, // 3×3. spec: terrain.md §9.2
            _ => 1 // defensive default: smallest documented ring.
        };
    }

    /// <summary>
    ///     Number of sectors in a ring of the given radius: <c>(2r + 1)²</c> (9 for r=1, 25 for r=2).
    /// </summary>
    public static int RequiredSectorCount(int ringRadius)
    {
        if (ringRadius < 0)
            throw new ArgumentOutOfRangeException(nameof(ringRadius), "Ring radius must be non-negative.");

        var side = 2 * ringRadius + 1;
        return side * side;
    }

    /// <summary>
    ///     Fills <paramref name="destination" /> with every sector coordinate in the square ring of the
    ///     given Chebyshev radius around the centre, in deterministic row-major order
    ///     (Z outer, X inner, both ascending). Zero-allocation: writes into the caller's buffer.
    /// </summary>
    /// <param name="centerX">Centre sector X (biased <c>mapX</c>).</param>
    /// <param name="centerZ">Centre sector Z (biased <c>mapZ</c>).</param>
    /// <param name="ringRadius">Chebyshev radius (1 = 3×3, 2 = 5×5). spec: terrain.md §9.2.</param>
    /// <param name="destination">
    ///     Span to fill. Must be at least <see cref="RequiredSectorCount(int)" /> long.
    /// </param>
    /// <returns>The number of sectors written (always <see cref="RequiredSectorCount(int)" />).</returns>
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

    /// <summary>
    ///     Convenience overload that selects the ring radius from a <see cref="StreamQuality" />.
    /// </summary>
    public static int RequiredSectors(
        int centerX,
        int centerZ,
        StreamQuality quality,
        Span<(int MapX, int MapZ)> destination)
    {
        return RequiredSectors(centerX, centerZ, RingRadiusFor(quality), destination);
    }

    /// <summary>
    ///     Decides whether a loaded cell should be evicted given a new centre cell. A cell is evictable
    ///     when its Chebyshev distance from the centre exceeds <see cref="EvictionRadius" /> (2) in
    ///     either axis. Cells exactly 2 away are retained. spec: Docs/RE/formats/terrain.md §9.3.
    /// </summary>
    /// <param name="centerX">New centre sector X.</param>
    /// <param name="centerZ">New centre sector Z.</param>
    /// <param name="cellX">Loaded cell sector X.</param>
    /// <param name="cellZ">Loaded cell sector Z.</param>
    /// <returns><c>true</c> if the cell should be marked evictable.</returns>
    public static bool ShouldEvict(int centerX, int centerZ, int cellX, int cellZ)
    {
        var distX = Math.Abs(cellX - centerX);
        var distZ = Math.Abs(cellZ - centerZ);
        var chebyshev = Math.Max(distX, distZ);
        return chebyshev > EvictionRadius; // strictly greater than 2. spec: terrain.md §9.3
    }
}