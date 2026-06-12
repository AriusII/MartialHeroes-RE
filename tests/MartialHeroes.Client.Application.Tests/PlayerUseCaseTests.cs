using System.Buffers.Binary;
using MartialHeroes.Client.Application.Events;
using MartialHeroes.Client.Application.StateMachine;
using MartialHeroes.Client.Application.UseCases;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Client.Domain.Actors;
using MartialHeroes.Client.Domain.Inventory;
using MartialHeroes.Client.Domain.Skills;
using MartialHeroes.Client.Domain.Stats;
using MartialHeroes.Network.Abstractions.Protocol;
using MartialHeroes.Network.Abstractions.Session;
using MartialHeroes.Shared.Kernel.Ids;
using MartialHeroes.Shared.Kernel.Numerics;
using Xunit;

namespace MartialHeroes.Client.Application.Tests;

/// <summary>
/// Drives the new player use cases (CastSkill, EquipItem, MoveItem, TradeRequest, PartyInvite) through a
/// fake outbound sink, asserting that a validated request sends the right C2S frame and a blocked request
/// sends nothing.
/// </summary>
public sealed class PlayerUseCaseTests
{
    private sealed class FakeOutboundSink : IOutboundPacketSink
    {
        public List<(ushort Major, ushort Minor, byte[] Payload)> Sends { get; } = new();

        public ValueTask SendAsync(
            SessionId sessionId, ushort majorOpcode, ushort minorOpcode,
            ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
        {
            Sends.Add((majorOpcode, minorOpcode, payload.ToArray()));
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>A targeting query that always passes range / LoS / target-state. spec: skills.md §2.2.</summary>
    private sealed class PassingTargetingQuery : ISkillTargetingQuery
    {
        public float CasterBodyRadius => 0f;
        public float BuffRangeBonus => 0f;
        public float SquaredPlanarDistanceToAim(in Vector3Fixed aimPoint) => 0f;
        public bool HasLineOfSight(in Vector3Fixed aimPoint) => true;
        public bool IsTargetStateValid(bool isReviveSkill) => true;
    }

    private static ApplicationUseCases NewUseCases(
        FakeOutboundSink sink, out ClientWorld world, out LocalPlayerState local,
        ClientState initial = ClientState.World)
    {
        var bus = new ClientEventBus(ClientEventBus.Unbounded);
        var fsm = new ClientStateMachine(bus, initial);
        world = new ClientWorld();
        local = new LocalPlayerState();
        var credentials = new LoginCredentialStore();
        return new ApplicationUseCases(sink, fsm, world, credentials, new SessionId(1), localPlayer: local);
    }

    private static SkillDefinition NewSkill(uint id, ushort category = 3) => new()
    {
        Id = new SkillId(id),
        Category = category,
        TargetMode = SkillTargetMode.SingleTarget,
        BaseRange = 100f,
        MpCostFactor = 0,
        CooldownCentiseconds = 50,
    };

    // -------------------------------------------------------------------------
    // CastSkill
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CastSkill_valid_sends_2_52_and_advances_cast_state()
    {
        var sink = new FakeOutboundSink();
        ApplicationUseCases useCases = NewUseCases(sink, out _, out LocalPlayerState local);

        SkillCastResult result = await useCases.CastSkillAsync(
            slot: 1, NewSkill(4242), CasterState.AllClear, new PassingTargetingQuery(),
            aimPoint: Vector3Fixed.Zero, nowMs: 0, targetsA: new uint[] { 11 });

        Assert.Equal(SkillCastResult.Ok, result);
        var (major, minor, _) = Assert.Single(sink.Sends);
        Assert.Equal(2, major);
        Assert.Equal(52, minor);
        Assert.Equal(SkillCastPhase.Casting, local.CastState.Phase);
    }

    [Fact]
    public async Task CastSkill_blocked_gate_sends_nothing()
    {
        var sink = new FakeOutboundSink();
        ApplicationUseCases useCases = NewUseCases(sink, out _, out LocalPlayerState local);

        // Not alive -> gate 7 blocks (code 2). spec: skills.md §2.1 gate 7.
        CasterState dead = CasterState.AllClear with { IsAlive = false };
        SkillCastResult result = await useCases.CastSkillAsync(
            slot: 1, NewSkill(4242), dead, new PassingTargetingQuery(),
            aimPoint: Vector3Fixed.Zero, nowMs: 0);

        Assert.Equal(SkillCastResult.NotAlive, result);
        Assert.Empty(sink.Sends);
        Assert.Equal(SkillCastPhase.Idle, local.CastState.Phase); // no advance
    }

    [Fact]
    public async Task CastSkill_blocked_by_cooldown_sends_nothing()
    {
        var sink = new FakeOutboundSink();
        ApplicationUseCases useCases = NewUseCases(sink, out _, out LocalPlayerState local);

        // Put the skill on the hotbar and arm its cooldown so the recast gate blocks. spec: skills.md §4.
        var skill = NewSkill(4242);
        local.SetHotbarSlot(0, skill.Id, points: 1, cooldownDurationMs: 10_000);
        local.Cooldowns.Arm(skill.Id, now: 0);

        SkillCastResult result = await useCases.CastSkillAsync(
            slot: 0, skill, CasterState.AllClear, new PassingTargetingQuery(),
            aimPoint: Vector3Fixed.Zero, nowMs: 100, targetsA: new uint[] { 11 });

        Assert.Equal(SkillCastResult.OnCooldown, result);
        Assert.Empty(sink.Sends);
    }

    // -------------------------------------------------------------------------
    // EquipItem
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EquipItem_allowed_sends_2_16_with_payload()
    {
        var sink = new FakeOutboundSink();
        ApplicationUseCases useCases = NewUseCases(sink, out _, out _);

        EquipCheckResult result = await useCases.EquipItemAsync(
            mode: 1, slot: 2, fromSub: 3, toSlot: 4, sub: 0, itemIndex: 17,
            state: EquipStateGates.AllClear);

        Assert.Equal(EquipCheckResult.Allowed, result);
        var (major, minor, payload) = Assert.Single(sink.Sends);
        Assert.Equal(2, major);
        Assert.Equal(16, minor);
        Assert.Equal(12, payload.Length);
        Assert.Equal(1, payload[0x00]); // mode
        Assert.Equal(4, payload[0x03]); // to
        Assert.Equal(17, BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(0x08, 4))); // item_index
    }

    [Fact]
    public async Task EquipItem_blocked_state_sends_nothing()
    {
        var sink = new FakeOutboundSink();
        ApplicationUseCases useCases = NewUseCases(sink, out _, out _);

        // Dead/busy state gate fails. spec: inventory_trade.md §4.2 gate 2.
        EquipCheckResult result = await useCases.EquipItemAsync(
            mode: 1, slot: 2, fromSub: 3, toSlot: 4, sub: 0, itemIndex: 17,
            state: EquipStateGates.AllClear with { NotDead = false });

        Assert.Equal(EquipCheckResult.StateBlocked, result);
        Assert.Empty(sink.Sends);
    }

    [Fact]
    public async Task EquipItem_invalid_index_sends_nothing()
    {
        var sink = new FakeOutboundSink();
        ApplicationUseCases useCases = NewUseCases(sink, out _, out _);

        EquipCheckResult result = await useCases.EquipItemAsync(
            mode: 1, slot: 2, fromSub: 3, toSlot: 4, sub: 0, itemIndex: -1,
            state: EquipStateGates.AllClear);

        Assert.Equal(EquipCheckResult.InvalidIndex, result);
        Assert.Empty(sink.Sends);
    }

    // -------------------------------------------------------------------------
    // MoveItem
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MoveItem_full_move_applies_grid_and_sends_2_16()
    {
        var sink = new FakeOutboundSink();
        ApplicationUseCases useCases = NewUseCases(sink, out _, out _);

        var grid = new InventoryGrid(4, 4, maxStackSize: 99);
        grid.SetSlot(0, new InventorySlot(new ItemId(5), 10));

        bool moved = await useCases.MoveItemAsync(grid, fromIndex: 0, toIndex: 1);

        Assert.True(moved);
        Assert.True(grid.IsEmpty(0));
        Assert.Equal(new ItemId(5), grid[1].Item);
        var (major, minor, _) = Assert.Single(sink.Sends);
        Assert.Equal(2, major);
        Assert.Equal(16, minor);
    }

    [Fact]
    public async Task MoveItem_rejected_move_sends_nothing()
    {
        var sink = new FakeOutboundSink();
        ApplicationUseCases useCases = NewUseCases(sink, out _, out _);

        var grid = new InventoryGrid(4, 4, maxStackSize: 99); // both slots empty -> move is a no-op
        bool moved = await useCases.MoveItemAsync(grid, fromIndex: 0, toIndex: 1);

        Assert.False(moved);
        Assert.Empty(sink.Sends);
    }

    // -------------------------------------------------------------------------
    // TradeRequest
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TradeRequest_idle_sends_2_23_and_advances()
    {
        var sink = new FakeOutboundSink();
        ApplicationUseCases useCases = NewUseCases(sink, out ClientWorld world, out _);
        world.LocalActorKey = new ActorKey(7, EntitySort.PlayerCharacter);

        var (next, accepted) = await useCases.TradeRequestAsync(TradeSession.Idle, partnerActorId: 42);

        Assert.True(accepted);
        Assert.Equal(TradePhase.Requested, next.Phase);
        Assert.Equal(42u, next.PartnerActorId);
        var (major, minor, payload) = Assert.Single(sink.Sends);
        Assert.Equal(2, major);
        Assert.Equal(23, minor);
        Assert.Equal(42u, BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(0x04, 4)));
    }

    [Fact]
    public async Task TradeRequest_self_target_sends_nothing()
    {
        var sink = new FakeOutboundSink();
        ApplicationUseCases useCases = NewUseCases(sink, out ClientWorld world, out _);
        world.LocalActorKey = new ActorKey(7, EntitySort.PlayerCharacter);

        var (_, accepted) = await useCases.TradeRequestAsync(TradeSession.Idle, partnerActorId: 7);

        Assert.False(accepted);
        Assert.Empty(sink.Sends);
    }

    [Fact]
    public async Task TradeRequest_out_of_phase_sends_nothing()
    {
        var sink = new FakeOutboundSink();
        ApplicationUseCases useCases = NewUseCases(sink, out ClientWorld world, out _);
        world.LocalActorKey = new ActorKey(7, EntitySort.PlayerCharacter);

        var session = new TradeSession { Phase = TradePhase.WindowOpen, PartnerActorId = 9 };
        var (_, accepted) = await useCases.TradeRequestAsync(session, partnerActorId: 42);

        Assert.False(accepted);
        Assert.Empty(sink.Sends);
    }

    // -------------------------------------------------------------------------
    // PartyInvite
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PartyInvite_sends_2_35_with_target()
    {
        var sink = new FakeOutboundSink();
        ApplicationUseCases useCases = NewUseCases(sink, out ClientWorld world, out _);
        world.LocalActorKey = new ActorKey(7, EntitySort.PlayerCharacter);

        bool sent = await useCases.PartyInviteAsync(targetActorId: 88, subOp: 1);

        Assert.True(sent);
        var (major, minor, payload) = Assert.Single(sink.Sends);
        Assert.Equal(2, major);
        Assert.Equal(35, minor);
        Assert.Equal(1, payload[0x00]); // sub-op
        Assert.Equal(88u, BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(0x04, 4)));
    }

    [Fact]
    public async Task PartyInvite_self_target_sends_nothing()
    {
        var sink = new FakeOutboundSink();
        ApplicationUseCases useCases = NewUseCases(sink, out ClientWorld world, out _);
        world.LocalActorKey = new ActorKey(7, EntitySort.PlayerCharacter);

        bool sent = await useCases.PartyInviteAsync(targetActorId: 7);

        Assert.False(sent);
        Assert.Empty(sink.Sends);
    }
}
