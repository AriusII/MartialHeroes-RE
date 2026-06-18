using System.Collections.Immutable;
using System.Text;
using MartialHeroes.Client.Application.Hud;
using MartialHeroes.Client.Domain.Actors;
using Xunit;

namespace MartialHeroes.Client.Application.Tests;

/// <summary>
/// Headless coverage of the HUD event hub: publish -> subscribe round-trips for every event family,
/// the per-family backpressure policy (latest-wins drop), and the DTO invariants / derived helpers.
/// No real network, no Godot — a stub publisher writes, an in-memory channel reader asserts.
/// </summary>
public sealed class HudEventHubTests
{
    private static ActorKey Key(uint id) => new(id, EntitySort.Monster);

    [Fact]
    public async Task ChatLine_round_trips_through_the_chat_stream()
    {
        var hub = new HudEventHub();
        var line = new ChatLineEvent(ChatLineEvent.ChannelSay, "hello", ChatLineEvent.SayColorArgb, "Wanderer");

        Assert.True(hub.PublishChatLine(line));
        hub.Complete();

        var received = await ReadAll(hub.ChatLines);
        Assert.Equal(line, Assert.Single(received));
    }

    [Fact]
    public async Task CombatText_round_trips_through_the_combat_stream()
    {
        var hub = new HudEventHub();
        var hit = new CombatTextEvent(Key(7), Value: 1234, Kind: 3, IsCrit: true);

        Assert.True(hub.PublishCombatText(hit));
        hub.Complete();

        var received = await ReadAll(hub.CombatTexts);
        Assert.Equal(hit, Assert.Single(received));
    }

    [Fact]
    public async Task TargetChanged_round_trips_and_None_reports_cleared()
    {
        var hub = new HudEventHub();
        var target = new TargetChangedEvent(Key(42), "Boss", HpRatio: 0.5f, MpRatio: 0.25f);

        Assert.True(hub.PublishTargetChanged(target));
        hub.Complete();

        var received = await ReadAll(hub.TargetChanges);
        Assert.Equal(target, Assert.Single(received));
        Assert.False(target.IsCleared);
        Assert.True(TargetChangedEvent.None.IsCleared);
    }

    [Fact]
    public async Task ExpLevel_round_trips_through_the_exp_stream()
    {
        var hub = new HudEventHub();
        var exp = new ExpLevelEvent(CurrentXp: 250, XpForLevel: 1000, Level: 12);

        Assert.True(hub.PublishExpLevel(exp));
        hub.Complete();

        var received = await ReadAll(hub.ExpLevels);
        Assert.Equal(exp, Assert.Single(received));
    }

    [Fact]
    public async Task StatAllocation_round_trips_through_the_stat_stream()
    {
        var hub = new HudEventHub();
        var view = new StatAllocationView(10, 11, 12, 13, 14, 1, 0, 0, 2, 0, RemainingStatPoints: 5);

        Assert.True(hub.PublishStatAllocation(view));
        hub.Complete();

        var received = await ReadAll(hub.StatAllocations);
        Assert.Equal(view, Assert.Single(received));
    }

    [Fact]
    public async Task BuffState_round_trips_with_thirty_slots()
    {
        var hub = new HudEventHub();
        var slots = ImmutableArray.CreateBuilder<BuffSlot>(BuffStateEvent.SlotCount);
        for (int i = 0; i < BuffStateEvent.SlotCount; i++)
        {
            slots.Add(i == 0 ? new BuffSlot(101, 30_000u) : new BuffSlot(BuffSlot.EmptyBuffId, null));
        }

        var evt = BuffStateEvent.FromSlots(slots.MoveToImmutable());
        Assert.True(hub.PublishBuffState(evt));
        hub.Complete();

        var received = Assert.Single(await ReadAll(hub.BuffStates));
        Assert.Equal(BuffStateEvent.SlotCount, received.Slots.Length);
        Assert.Equal((ushort)101, received.Slots[0].BuffId);
        Assert.False(received.Slots[0].IsEmpty);
        Assert.True(received.Slots[1].IsEmpty);
    }

    [Fact]
    public void Streams_are_independent_a_publish_does_not_cross_families()
    {
        var hub = new HudEventHub();
        hub.PublishChatLine(new ChatLineEvent(0, "x", ChatLineEvent.SayColorArgb));

        Assert.True(hub.ChatLines.TryRead(out _));
        Assert.False(hub.CombatTexts.TryRead(out _));
        Assert.False(hub.ExpLevels.TryRead(out _));
    }

    [Fact]
    public async Task LatestWins_stream_keeps_only_the_freshest_snapshot_under_a_burst()
    {
        var hub = new HudEventHub();

        // Three consecutive XP snapshots with no reader draining between them. Capacity 1 + DropOldest
        // collapses the burst to the freshest value so the HUD never repaints stale intermediate state.
        hub.PublishExpLevel(new ExpLevelEvent(100, 1000, 10));
        hub.PublishExpLevel(new ExpLevelEvent(400, 1000, 10));
        hub.PublishExpLevel(new ExpLevelEvent(900, 1000, 10));
        hub.Complete();

        var received = Assert.Single(await ReadAll(hub.ExpLevels));
        Assert.Equal(900, received.CurrentXp);
    }

    [Fact]
    public void BuffState_rejects_a_wrong_slot_count()
    {
        var tooFew = ImmutableArray.Create(new BuffSlot(1, null));
        Assert.Throws<ArgumentException>(() => BuffStateEvent.FromSlots(tooFew));
    }

    [Theory]
    [InlineData(0, 1000, 0f)]
    [InlineData(250, 1000, 0.25f)]
    [InlineData(1000, 1000, 1f)]
    [InlineData(5000, 1000, 1f)] // overflow clamps to full
    [InlineData(100, 0, 0f)] // unknown/max level -> no divide-by-zero
    public void ExpLevel_ratio_is_clamped(long current, long forLevel, float expected)
    {
        var exp = new ExpLevelEvent(current, forLevel, 1);
        Assert.Equal(expected, exp.Ratio, precision: 4);
    }

    [Fact]
    public void StatAllocation_recomputes_available_points_and_absolutes()
    {
        var view = new StatAllocationView(
            BaseStr: 20, BaseInt: 10, BaseAgi: 10, BaseDex: 10, BaseCon: 10,
            DeltaStr: 3, DeltaInt: 0, DeltaAgi: 0, DeltaDex: 2, DeltaCon: 0,
            RemainingStatPoints: 10);

        Assert.Equal(5, view.PendingTotal);
        Assert.Equal(5, view.PointsAvailable); // 10 remaining - 5 staged
        Assert.True(view.HasPendingAllocation);
        Assert.Equal(23u, view.AbsoluteStr); // base + delta
        Assert.Equal(12u, view.AbsoluteDex);
        Assert.Equal(10u, view.AbsoluteInt); // no delta
    }

    [Fact]
    public void StatAllocation_available_floors_at_zero_when_overspent()
    {
        var view = StatAllocationView.Empty with { DeltaStr = 5, RemainingStatPoints = 2 };
        Assert.Equal(0, view.PointsAvailable);
    }

    [Fact]
    public void Cp949_decode_trims_at_first_nul()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        byte[] padded = [.. Encoding.GetEncoding(949).GetBytes("name"), 0, 0, 0];

        Assert.Equal("name", Cp949Text.Decode(padded));
        Assert.Equal(string.Empty, Cp949Text.Decode(ReadOnlySpan<byte>.Empty));
    }

    // ---- HudVitalsEvent channel tests -----------------------------------------------------------

    [Fact]
    public async Task Vitals_round_trips_through_the_vitals_stream()
    {
        // spec: Docs/RE/specs/combat.md §12.2; Docs/RE/packets/5-53_actor_vitals_and_pair_state.yaml
        var hub = new HudEventHub();
        var evt = new HudVitalsEvent(
            CurrentHp: 800u, MaxHp: 1000u,
            CurrentMp: 300u, MaxMp: 500u,
            CurrentStamina: 120u, MaxStamina: 200u);

        Assert.True(hub.PublishVitals(evt));
        hub.Complete();

        var received = Assert.Single(await ReadAll(hub.Vitals));
        Assert.Equal(evt, received);
    }

    [Fact]
    public async Task Vitals_latest_wins_collapses_burst_to_freshest_snapshot()
    {
        // spec: Docs/RE/specs/combat.md §12.2 — absolute current HP; only the latest value matters for the gauge.
        var hub = new HudEventHub();

        hub.PublishVitals(new HudVitalsEvent(200u, 1000u, 100u, 500u, 50u, 200u));
        hub.PublishVitals(new HudVitalsEvent(500u, 1000u, 250u, 500u, 100u, 200u));
        hub.PublishVitals(new HudVitalsEvent(900u, 1000u, 490u, 500u, 195u, 200u));
        hub.Complete();

        var received = Assert.Single(await ReadAll(hub.Vitals));
        Assert.Equal(900u, received.CurrentHp); // only the freshest survives
    }

    [Fact]
    public void Vitals_does_not_cross_into_other_streams()
    {
        var hub = new HudEventHub();
        hub.PublishVitals(new HudVitalsEvent(500u, 1000u, 0u, 0u, 0u, 0u));

        Assert.True(hub.Vitals.TryRead(out _));
        Assert.False(hub.ChatLines.TryRead(out _));
        Assert.False(hub.ExpLevels.TryRead(out _));
        Assert.False(hub.TargetChanges.TryRead(out _));
    }

    [Fact]
    public void Vitals_None_sentinel_is_all_zero_and_IsEmpty()
    {
        // spec: Docs/RE/specs/combat.md §12.2 (HP reaching 0 = death; gauge handles zero max gracefully).
        var none = HudVitalsEvent.None;
        Assert.True(none.IsEmpty);
        Assert.Equal(0u, none.CurrentHp);
        Assert.Equal(0u, none.MaxHp);
        Assert.Equal(0u, none.CurrentMp);
        Assert.Equal(0u, none.MaxMp);
        Assert.Equal(0u, none.CurrentStamina);
        Assert.Equal(0u, none.MaxStamina);
    }

    [Theory]
    [InlineData(0u, 1000u, 0f)] // 0 HP → 0 ratio
    [InlineData(500u, 1000u, 0.5f)] // half
    [InlineData(1000u, 1000u, 1f)] // full
    [InlineData(1200u, 1000u, 1f)] // over-cap clamps to 1
    [InlineData(0u, 0u, 0f)] // zero max → no divide-by-zero
    public void Vitals_HpRatio_clamps_correctly(uint current, uint max, float expected)
    {
        // spec: Docs/RE/specs/combat.md §12.2 (HP-bar ratio source; clamp on zero max).
        var evt = new HudVitalsEvent(current, max, 0u, 1u, 0u, 1u);
        Assert.Equal(expected, evt.HpRatio, precision: 4);
    }

    [Theory]
    [InlineData(0u, 500u, 0f)]
    [InlineData(250u, 500u, 0.5f)]
    [InlineData(500u, 500u, 1f)]
    [InlineData(600u, 500u, 1f)] // over-cap clamps
    [InlineData(0u, 0u, 0f)] // zero max → 0, no divide-by-zero
    public void Vitals_MpRatio_clamps_correctly(uint current, uint max, float expected)
    {
        // spec: Docs/RE/packets/5-53_actor_vitals_and_pair_state.yaml (CurrentMp@0x14).
        var evt = new HudVitalsEvent(0u, 1u, current, max, 0u, 1u);
        Assert.Equal(expected, evt.MpRatio, precision: 4);
    }

    [Theory]
    [InlineData(0u, 200u, 0f)]
    [InlineData(100u, 200u, 0.5f)]
    [InlineData(200u, 200u, 1f)]
    [InlineData(250u, 200u, 1f)] // over-cap clamps
    [InlineData(0u, 0u, 0f)] // zero max → 0
    public void Vitals_StaminaRatio_clamps_correctly(uint current, uint max, float expected)
    {
        // spec: Docs/RE/packets/5-53_actor_vitals_and_pair_state.yaml (Stamina@0x18).
        var evt = new HudVitalsEvent(0u, 1u, 0u, 1u, current, max);
        Assert.Equal(expected, evt.StaminaRatio, precision: 4);
    }

    private static async Task<List<T>> ReadAll<T>(System.Threading.Channels.ChannelReader<T> reader)
    {
        var items = new List<T>();
        await foreach (T item in reader.ReadAllAsync())
        {
            items.Add(item);
        }

        return items;
    }
}