using MartialHeroes.Client.Domain.Simulation;
using Xunit;

namespace MartialHeroes.Client.Domain.Tests;

/// <summary>
/// Tests for the deterministic terrain sector grid math.
/// spec: Docs/RE/formats/terrain.md §Overview, §2, §9.2, §9.3.
/// </summary>
public sealed class SectorGridTests
{
    // -------------------------------------------------------------------------
    // WorldToSector — constants and origin bias.
    // -------------------------------------------------------------------------

    [Fact]
    public void Constants_MatchSpec()
    {
        Assert.Equal(1024.0f, SectorGrid.SectorSizeWorldUnits);
        Assert.Equal(10000, SectorGrid.OriginBias);
        Assert.Equal(2, SectorGrid.EvictionRadius);
    }

    [Fact]
    public void WorldToSector_OriginCell_IsBias()
    {
        // The cell whose SW corner is world (0,0) is (10000, 10000). spec: terrain.md §Overview.
        Assert.Equal((10000, 10000), SectorGrid.WorldToSector(0f, 0f));
    }

    [Fact]
    public void WorldToSector_InsideFirstCell_StaysAtBias()
    {
        // Anywhere in [0,1024) maps to the same cell.
        Assert.Equal((10000, 10000), SectorGrid.WorldToSector(1023.9f, 500f));
    }

    [Fact]
    public void WorldToSector_SecondCell_Increments()
    {
        Assert.Equal((10001, 10000), SectorGrid.WorldToSector(1024.0f, 0f));
        Assert.Equal((10000, 10002), SectorGrid.WorldToSector(0f, 2048.0f));
    }

    [Fact]
    public void WorldToSector_NegativeAxis_FloorsTowardNegative()
    {
        // spec: terrain.md §2 negative-axis correction: subtract 1024 before truncation.
        // worldX = -1: adjusted = -1025, /1024 -> -1 (truncate toward zero) -> map 9999.
        Assert.Equal((9999, 10000), SectorGrid.WorldToSector(-1f, 0f));

        // worldX = -1024: adjusted = -2048, /1024 = -2 -> map 9998.
        Assert.Equal((9998, 10000), SectorGrid.WorldToSector(-1024f, 0f));

        // worldX = -1025: adjusted = -2049, /1024 = -2.0009.. -> trunc -2 -> map 9998.
        Assert.Equal((9998, 10000), SectorGrid.WorldToSector(-1025f, 0f));

        // Both axes negative.
        Assert.Equal((9999, 9999), SectorGrid.WorldToSector(-1f, -1f));
    }

    [Fact]
    public void WorldToSector_IsDeterministic()
    {
        var first = SectorGrid.WorldToSector(-3333.25f, 5120.5f);
        for (int i = 0; i < 1000; i++)
        {
            Assert.Equal(first, SectorGrid.WorldToSector(-3333.25f, 5120.5f));
        }
    }

    // -------------------------------------------------------------------------
    // Ring radius / quality mapping.
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(StreamQuality.Low, 1)]
    [InlineData(StreamQuality.Medium, 1)]
    [InlineData(StreamQuality.High, 2)]
    public void RingRadiusFor_MatchesSpec(StreamQuality quality, int expected)
    {
        // spec: terrain.md §9.2 — Low/Medium 3×3 (r=1), High 5×5 (r=2).
        Assert.Equal(expected, SectorGrid.RingRadiusFor(quality));
    }

    [Theory]
    [InlineData(1, 9)] // 3×3
    [InlineData(2, 25)] // 5×5
    [InlineData(0, 1)]
    public void RequiredSectorCount_IsSquareOfSide(int radius, int expected)
    {
        Assert.Equal(expected, SectorGrid.RequiredSectorCount(radius));
    }

    // -------------------------------------------------------------------------
    // RequiredSectors — buffer fill, counts and contents.
    // -------------------------------------------------------------------------

    [Fact]
    public void RequiredSectors_Ring1_Fills3x3()
    {
        Span<(int, int)> buffer = stackalloc (int, int)[9];
        int n = SectorGrid.RequiredSectors(10000, 10000, ringRadius: 1, buffer);
        Assert.Equal(9, n);

        var set = new HashSet<(int, int)>();
        for (int i = 0; i < n; i++)
        {
            set.Add(buffer[i]);
        }

        Assert.Equal(9, set.Count); // all distinct
        Assert.Contains((10000, 10000), set); // centre present
        Assert.Contains((9999, 9999), set);
        Assert.Contains((10001, 10001), set);
        Assert.DoesNotContain((10002, 10000), set); // outside ring 1
    }

    [Fact]
    public void RequiredSectors_Ring2_Fills5x5()
    {
        Span<(int, int)> buffer = stackalloc (int, int)[25];
        int n = SectorGrid.RequiredSectors(10000, 10000, ringRadius: 2, buffer);
        Assert.Equal(25, n);

        var set = new HashSet<(int, int)>();
        for (int i = 0; i < n; i++)
        {
            set.Add(buffer[i]);
        }

        Assert.Equal(25, set.Count);
        Assert.Contains((9998, 9998), set);
        Assert.Contains((10002, 10002), set);
        Assert.DoesNotContain((10003, 10000), set);
    }

    [Fact]
    public void RequiredSectors_QualityOverload_HighIs25()
    {
        Span<(int, int)> buffer = stackalloc (int, int)[25];
        int n = SectorGrid.RequiredSectors(0, 0, StreamQuality.High, buffer);
        Assert.Equal(25, n);
    }

    [Fact]
    public void RequiredSectors_QualityOverload_MediumIs9()
    {
        Span<(int, int)> buffer = stackalloc (int, int)[9];
        int n = SectorGrid.RequiredSectors(0, 0, StreamQuality.Medium, buffer);
        Assert.Equal(9, n);
    }

    [Fact]
    public void RequiredSectors_BufferTooSmall_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            // Cannot use Span across the lambda boundary directly; allocate inside.
            var buffer = new (int, int)[4];
            SectorGrid.RequiredSectors(0, 0, ringRadius: 2, buffer);
        });
    }

    [Fact]
    public void RequiredSectors_Order_IsDeterministicRowMajor()
    {
        var a = new (int, int)[9];
        var b = new (int, int)[9];
        SectorGrid.RequiredSectors(5, 7, 1, a);
        SectorGrid.RequiredSectors(5, 7, 1, b);
        Assert.Equal(a, b);

        // First entry is the SW corner (centre - r, centre - r); last is NE corner.
        Assert.Equal((4, 6), a[0]);
        Assert.Equal((6, 8), a[8]);
    }

    // -------------------------------------------------------------------------
    // ShouldEvict — Chebyshev > 2.
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(10000, 10000, false)] // centre
    [InlineData(10002, 10000, false)] // exactly 2 in X — retained
    [InlineData(10000, 10002, false)] // exactly 2 in Z — retained
    [InlineData(10002, 10002, false)] // exactly 2 diagonally — retained
    [InlineData(10003, 10000, true)] // 3 in X — evict
    [InlineData(10000, 9997, true)] // 3 in Z (negative direction) — evict
    [InlineData(10002, 10003, true)] // mixed, max is 3 — evict
    public void ShouldEvict_StrictlyGreaterThanTwo(int cellX, int cellZ, bool expected)
    {
        // spec: terrain.md §9.3 — evict when Chebyshev distance > 2 (exactly 2 retained).
        Assert.Equal(expected, SectorGrid.ShouldEvict(10000, 10000, cellX, cellZ));
    }
}