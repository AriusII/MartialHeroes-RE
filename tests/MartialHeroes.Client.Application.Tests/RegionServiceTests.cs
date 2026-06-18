using System.Threading.Channels;
using MartialHeroes.Client.Application.Hud;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Client.Domain.Simulation;
using MartialHeroes.Shared.Kernel.Enums;
using Xunit;

namespace MartialHeroes.Client.Application.Tests;

// ────────────────────────────────────────────────────────────────────────────────
// RegionServiceTests
//
// Verifies that RegionService:
//   - publishes exactly ONE ZoneChangedEvent when the player crosses a region boundary.
//   - publishes ZoneType.Safe (the no-catalog default) when region files are absent (null catalog).
//   - does NOT publish when the zone is unchanged between consecutive UpdatePosition calls.
//   - resets and re-fires on a new LoadAreaAsync.
//
// spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1–§16.3.
// ────────────────────────────────────────────────────────────────────────────────

public sealed class RegionServiceTests
{
    // ── Synthetic catalog builder ────────────────────────────────────────────

    /// <summary>
    /// Builds a <see cref="RegionCatalog"/> from a minimal synthetic grid.
    ///
    /// Grid: 2×1 cells (width=2, height=1), cell size=256.
    /// Cell [0,0] = region id 0 → ZoneType.Safe   (rawZoneType=0)
    /// Cell [1,0] = region id 1 → ZoneType.OpenPvp (rawZoneType=1)
    /// World origin: (0, 0).
    ///
    /// So world X in [0, 255] → region 0 (Safe), X in [256, 511] → region 1 (OpenPvp).
    /// spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1 — "col = (worldX - originX) / 256": CONFIRMED.
    /// </summary>
    private static RegionCatalog BuildTwoZoneCatalog()
    {
        // 2 cells wide × 1 cell tall.
        byte[] cells = [0, 1]; // region id 0 (col=0), region id 1 (col=1)

        // 32 raw zone-type values: region 0 = Safe(0), region 1 = OpenPvp(1), rest = Safe.
        var rawZoneTypes = new uint[32];
        rawZoneTypes[0] = 0; // Safe   // spec: world_systems.md Ch. 16 §16.3 — value 0: PLAUSIBLE (Safe).
        rawZoneTypes[1] = 1; // OpenPvp // spec: world_systems.md Ch. 16 §16.3 — value 1: CONFIRMED.

        return new RegionCatalog(
            width: 2,
            height: 1,
            cells: cells,
            originX: 0,
            originZ: 0,
            rawZoneTypes: rawZoneTypes);
    }

    // ── Fakes ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fake <see cref="IRegionSource"/> that returns a pre-built catalog or null (absent files).
    /// </summary>
    private sealed class FakeRegionSource(RegionCatalog? catalog) : IRegionSource
    {
        public ValueTask<RegionCatalog?> LoadRegionCatalogAsync(
            int areaId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(catalog);
    }

    /// <summary>
    /// Minimal <see cref="IHudEventHub"/> stub that captures every published
    /// <see cref="ZoneChangedEvent"/> in a list for assertions.
    /// All other publish/subscribe members are no-ops.
    /// </summary>
    private sealed class CapturingHudHub : IHudEventHub
    {
        private readonly List<ZoneChangedEvent> _captured = [];

        public IReadOnlyList<ZoneChangedEvent> ZoneCaptured => _captured;

        public bool PublishZoneChanged(ZoneChangedEvent z)
        {
            _captured.Add(z);
            return true;
        }

        // ---- Stubs (unused in these tests) ----
        public bool PublishChatLine(ChatLineEvent _) => false;
        public bool PublishBuffState(BuffStateEvent _) => false;
        public bool PublishCombatText(CombatTextEvent _) => false;
        public bool PublishTargetChanged(TargetChangedEvent _) => false;
        public bool PublishExpLevel(ExpLevelEvent _) => false;
        public bool PublishStatAllocation(StatAllocationView _) => false;
        public bool PublishVitals(HudVitalsEvent _) => false;

        private static ChannelReader<T> EmptyReader<T>()
        {
            var ch = Channel.CreateBounded<T>(1);
            ch.Writer.TryComplete();
            return ch.Reader;
        }

        public ChannelReader<ChatLineEvent> ChatLines => EmptyReader<ChatLineEvent>();
        public ChannelReader<BuffStateEvent> BuffStates => EmptyReader<BuffStateEvent>();
        public ChannelReader<CombatTextEvent> CombatTexts => EmptyReader<CombatTextEvent>();
        public ChannelReader<TargetChangedEvent> TargetChanges => EmptyReader<TargetChangedEvent>();
        public ChannelReader<ExpLevelEvent> ExpLevels => EmptyReader<ExpLevelEvent>();
        public ChannelReader<StatAllocationView> StatAllocations => EmptyReader<StatAllocationView>();
        public ChannelReader<ZoneChangedEvent> ZoneChanges => EmptyReader<ZoneChangedEvent>();
        public ChannelReader<HudVitalsEvent> Vitals => EmptyReader<HudVitalsEvent>();

        public void Complete()
        {
        }
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// When no region source is available (null catalog), the first UpdatePosition call should
    /// publish exactly one ZoneChangedEvent with ZoneType.Safe (the no-catalog default).
    /// spec: Docs/RE/specs/world_systems.md Ch. 16 §16.3 — graceful degradation → Safe ("anything else = safe").
    /// </summary>
    [Fact]
    public async Task UpdatePosition_NullCatalog_PublishesSafe()
    {
        var hub = new CapturingHudHub();
        var service = new RegionService(new FakeRegionSource(null), hub);

        await service.LoadAreaAsync(areaId: 0);
        service.UpdatePosition(0f, 0f);

        Assert.Single(hub.ZoneCaptured);
        Assert.Equal(ZoneType.Safe, hub.ZoneCaptured[0].Zone);
    }

    /// <summary>
    /// Player starts in the Safe zone (X=128), moves into OpenPvp (X=300):
    /// exactly TWO ZoneChangedEvents should fire (one per distinct zone).
    /// spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1–§16.3.
    /// </summary>
    [Fact]
    public async Task UpdatePosition_CrossesBoundary_FiresExactlyOneEventPerZoneChange()
    {
        var hub = new CapturingHudHub();
        var catalog = BuildTwoZoneCatalog();
        var service = new RegionService(new FakeRegionSource(catalog), hub);

        await service.LoadAreaAsync(areaId: 0);

        // First position: X=128 → col=0 → region 0 → Safe.
        // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1 — "col = (128-0)/256 = 0".
        service.UpdatePosition(128f, 0f);

        // Same position again: should NOT fire another event.
        service.UpdatePosition(128f, 0f);
        service.UpdatePosition(200f, 0f); // still col=0, still Safe

        Assert.Single(hub.ZoneCaptured);
        Assert.Equal(ZoneType.Safe, hub.ZoneCaptured[0].Zone);

        // Cross the boundary: X=300 → col=1 → region 1 → OpenPvp.
        // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.3 — value 1: CONFIRMED (OpenPvp).
        service.UpdatePosition(300f, 0f);

        Assert.Equal(2, hub.ZoneCaptured.Count);
        Assert.Equal(ZoneType.OpenPvp, hub.ZoneCaptured[1].Zone);

        // Staying in OpenPvp: no more events.
        service.UpdatePosition(400f, 0f);
        service.UpdatePosition(511f, 0f);

        Assert.Equal(2, hub.ZoneCaptured.Count);
    }

    /// <summary>
    /// After <see cref="RegionService.LoadAreaAsync"/> is called a second time, the first
    /// UpdatePosition call should fire a ZoneChangedEvent even if the position (and resulting zone)
    /// is the same as before — because LoadAreaAsync resets the last-published sentinel.
    /// spec: Docs/RE/specs/world_systems.md Ch. 16 — LoadAreaAsync resets state.
    /// </summary>
    [Fact]
    public async Task LoadAreaAsync_Resets_AlwaysFiresFirstUpdateAfterReload()
    {
        var hub = new CapturingHudHub();
        var catalog = BuildTwoZoneCatalog();
        var service = new RegionService(new FakeRegionSource(catalog), hub);

        // First load + update.
        await service.LoadAreaAsync(areaId: 0);
        service.UpdatePosition(128f, 0f); // → Safe (event #1)

        // Reload same area.
        await service.LoadAreaAsync(areaId: 0);
        service.UpdatePosition(128f, 0f); // → Safe again, but first-after-reload → event #2

        Assert.Equal(2, hub.ZoneCaptured.Count);
        Assert.All(hub.ZoneCaptured, e => Assert.Equal(ZoneType.Safe, e.Zone));
    }

    /// <summary>
    /// <see cref="RegionService.CurrentZone"/> reflects the most-recently-resolved zone.
    /// Before first UpdatePosition after LoadAreaAsync it returns Safe (the default).
    /// spec: Docs/RE/specs/world_systems.md Ch. 16 §16.3.
    /// </summary>
    [Fact]
    public async Task CurrentZone_ReflectsLastResolved()
    {
        var hub = new CapturingHudHub();
        var catalog = BuildTwoZoneCatalog();
        var service = new RegionService(new FakeRegionSource(catalog), hub);

        await service.LoadAreaAsync(areaId: 0);
        // Before any UpdatePosition: Safe default.
        Assert.Equal(ZoneType.Safe, service.CurrentZone);

        service.UpdatePosition(128f, 0f);
        Assert.Equal(ZoneType.Safe, service.CurrentZone);

        service.UpdatePosition(300f, 0f);
        Assert.Equal(ZoneType.OpenPvp, service.CurrentZone);
    }

    /// <summary>
    /// LoadAreaAsync with a faulting source (throws unexpectedly) must not propagate the
    /// exception to the caller — it degrades gracefully to null catalog → Safe zone (the default).
    /// spec: Docs/RE/specs/world_systems.md Ch. 16 §16.3 — graceful degradation on parse error → Safe.
    /// </summary>
    [Fact]
    public async Task LoadAreaAsync_FaultingSource_DegradesToSafe()
    {
        var hub = new CapturingHudHub();
        var service = new RegionService(new ThrowingRegionSource(), hub);

        // Must not throw.
        await service.LoadAreaAsync(areaId: 0);

        // First UpdatePosition → Safe (no-catalog default).
        service.UpdatePosition(128f, 0f);
        Assert.Single(hub.ZoneCaptured);
        Assert.Equal(ZoneType.Safe, hub.ZoneCaptured[0].Zone);
    }

    /// <summary>A source that always throws to simulate a corrupt VFS.</summary>
    private sealed class ThrowingRegionSource : IRegionSource
    {
        public ValueTask<RegionCatalog?> LoadRegionCatalogAsync(
            int areaId, CancellationToken cancellationToken = default)
        {
            throw new InvalidDataException("Simulated corrupt region file.");
        }
    }
}