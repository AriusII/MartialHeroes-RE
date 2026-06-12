using System.Collections.Concurrent;
using MartialHeroes.Client.Application.Events;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Client.Domain.Simulation;
using Xunit;

namespace MartialHeroes.Client.Application.Tests;

public sealed class SectorStreamingServiceTests
{
    /// <summary>Records every (mapX, mapZ) load request so we can assert no double-loading.</summary>
    private sealed class RecordingSectorSource : ITerrainSectorSource
    {
        public ConcurrentBag<(int MapX, int MapZ)> Requests { get; } = new();
        public List<int> AreaCalls { get; } = new();

        public ValueTask<ReadOnlyMemory<byte>> LoadSectorAsync(
            int mapX, int mapZ, CancellationToken cancellationToken = default)
        {
            Requests.Add((mapX, mapZ));
            return ValueTask.FromResult<ReadOnlyMemory<byte>>(new byte[] { (byte)mapX, (byte)mapZ });
        }

        public void SetArea(int areaId) => AreaCalls.Add(areaId);
    }

    private static List<IClientEvent> Drain(ClientEventBus bus)
    {
        var events = new List<IClientEvent>();
        while (bus.Reader.TryRead(out IClientEvent? e))
        {
            events.Add(e);
        }

        return events;
    }

    [Fact]
    public async Task Medium_quality_loads_a_3x3_ring_of_9_sectors()
    {
        var bus = new ClientEventBus(ClientEventBus.Unbounded);
        var source = new RecordingSectorSource();
        var service = new SectorStreamingService(source, bus, StreamQuality.Medium);

        await service.UpdateCenterAsync(10000, 10000);

        Assert.Equal(9, service.ResidentCount); // 3×3. spec: terrain.md §9.2
        Assert.Equal(9, source.Requests.Count);

        var loaded = Drain(bus).OfType<SectorLoadedEvent>().ToList();
        Assert.Equal(9, loaded.Count);
        // Centre and a corner are present in the ring.
        Assert.Contains(loaded, e => e.MapX == 10000 && e.MapZ == 10000);
        Assert.Contains(loaded, e => e.MapX == 9999 && e.MapZ == 9999);
    }

    [Fact]
    public async Task High_quality_loads_a_5x5_ring_of_25_sectors()
    {
        var bus = new ClientEventBus(ClientEventBus.Unbounded);
        var source = new RecordingSectorSource();
        var service = new SectorStreamingService(source, bus, StreamQuality.High);

        await service.UpdateCenterAsync(10000, 10000);

        Assert.Equal(25, service.ResidentCount); // 5×5. spec: terrain.md §9.2
        Assert.Equal(25, source.Requests.Count);
    }

    [Fact]
    public async Task Moving_one_cell_loads_new_column_without_reloading_shared_cells()
    {
        var bus = new ClientEventBus(ClientEventBus.Unbounded);
        var source = new RecordingSectorSource();
        var service = new SectorStreamingService(source, bus, StreamQuality.Medium);

        await service.UpdateCenterAsync(10000, 10000);
        Drain(bus);
        int afterFirst = source.Requests.Count; // 9

        // Move one cell east. The load ring is 3×3 (radius 1) but eviction is radius > 2, so the cells
        // one step behind are RETAINED (their distance is only 2). Only the new leading column (3 cells)
        // is loaded; nothing is evicted yet. spec: terrain.md §9.2 (3×3 load) vs §9.3 (evict > 2).
        await service.UpdateCenterAsync(10001, 10000);

        int newLoads = source.Requests.Count - afterFirst;
        Assert.Equal(3, newLoads); // only the new column loaded; no double-loading of shared cells.
        Assert.Equal(12, service.ResidentCount); // 9 retained + 3 new (eviction band is wider than the load ring).

        // No (mapX, mapZ) appears twice across all requests -> zero double-loading.
        Assert.Equal(source.Requests.Count, source.Requests.Distinct().Count());
    }

    [Fact]
    public async Task Distant_jump_evicts_all_old_cells()
    {
        var bus = new ClientEventBus(ClientEventBus.Unbounded);
        var source = new RecordingSectorSource();
        var service = new SectorStreamingService(source, bus, StreamQuality.Medium);

        await service.UpdateCenterAsync(10000, 10000);
        Drain(bus);

        // Jump far away (Chebyshev distance > 2 for every old cell -> all evicted). spec: terrain.md §9.3
        await service.UpdateCenterAsync(10100, 10100);

        var events = Drain(bus);
        Assert.Equal(9, events.OfType<SectorUnloadedEvent>().Count()); // all 9 old cells evicted
        Assert.Equal(9, events.OfType<SectorLoadedEvent>().Count()); // a fresh 3×3 ring loaded
        Assert.Equal(9, service.ResidentCount);
    }

    [Fact]
    public async Task SetArea_rebinds_source_and_unloads_old_ring_so_next_update_streams_fresh()
    {
        var bus = new ClientEventBus(ClientEventBus.Unbounded);
        var source = new RecordingSectorSource();
        var service = new SectorStreamingService(source, bus, StreamQuality.Medium);

        // Stream area 0's ring at a centre, then switch to area 5 at the SAME biased coordinate.
        await service.UpdateCenterAsync(10000, 10000);
        Drain(bus);
        int afterFirst = source.Requests.Count; // 9

        service.SetArea(5);

        // The source was told to rebind; the old ring is fully unloaded and the resident set cleared.
        Assert.Equal(new[] { 5 }, source.AreaCalls);
        Assert.Equal(0, service.ResidentCount);
        Assert.Equal(9, Drain(bus).OfType<SectorUnloadedEvent>().Count());

        // Even at the identical centre, the new area re-streams the full ring (no stale residency).
        await service.UpdateCenterAsync(10000, 10000);
        Assert.Equal(9, service.ResidentCount);
        Assert.Equal(afterFirst + 9, source.Requests.Count);
        Assert.Equal(9, Drain(bus).OfType<SectorLoadedEvent>().Count());
    }

    [Fact]
    public async Task Re_updating_same_center_does_not_reload()
    {
        var bus = new ClientEventBus(ClientEventBus.Unbounded);
        var source = new RecordingSectorSource();
        var service = new SectorStreamingService(source, bus, StreamQuality.Medium);

        await service.UpdateCenterAsync(10000, 10000);
        int afterFirst = source.Requests.Count;

        await service.UpdateCenterAsync(10000, 10000); // identical centre

        Assert.Equal(afterFirst, source.Requests.Count); // no new loads — already resident.
    }
}