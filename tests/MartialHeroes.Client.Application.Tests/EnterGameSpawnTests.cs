using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Client.Application.Diagnostics;
using MartialHeroes.Client.Application.Events;
using MartialHeroes.Client.Application.Handlers;
using MartialHeroes.Client.Application.Ingestion;
using MartialHeroes.Client.Application.StateMachine;
using MartialHeroes.Client.Application.UseCases;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Client.Domain.Actors;
using MartialHeroes.Network.Abstractions.Protocol;
using MartialHeroes.Network.Abstractions.Session;
using MartialHeroes.Network.Protocol.Packets;
using Xunit;

namespace MartialHeroes.Client.Application.Tests;

/// <summary>
/// Phase E4-b — the enter-game → local-player SPAWN critical path: the 3/7 SmsgCharSpawnResult handler,
/// the descriptor cache filled by 3/1 and consumed on 3/7, the SelectCharacterAsync @BLANK@ /
/// slot-guard routing, and the 1/9 version-token derivation. spec: Docs/RE/specs/login_flow.md
/// §3.3 / §3.5 / §5.3 / §7.
/// </summary>
public sealed class EnterGameSpawnTests
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

    private static List<IClientEvent> Drain(ClientEventBus bus)
    {
        var events = new List<IClientEvent>();
        while (bus.Reader.TryRead(out IClientEvent? e))
        {
            events.Add(e);
        }

        return events;
    }

    // -------------------------------------------------------------------------
    // 3/7 handler — spawn on Result != 0, fail on Result == 0
    // -------------------------------------------------------------------------

    [Fact]
    public void CharSpawnResult_3_7_materializes_local_player_from_cached_descriptor()
    {
        var bus = new ClientEventBus(ClientEventBus.Unbounded);
        var world = new ClientWorld();
        var fsm = new ClientStateMachine(bus, ClientState.World);
        var store = new CharacterSelectionStore();
        var handler = new GamePacketHandler(
            world, bus, fsm, new CountingUnhandledOpcodeSink(), characterSelection: store);
        var dispatcher = new InboundFrameDispatcher(handler);

        // Pre-cache the chosen slot descriptor (as the 3/1 handler + SelectCharacterAsync would).
        store.Retain(new CharacterSlotRecord(
            slotIndex: 2,
            rawDescriptorAndStats: BuildDescriptor("Wuxia", level: 7, hp: 250, mp: 100, stamina: 80,
                worldX: 12.5f, worldZ: -3.25f, serverClass: 9),
            slotFlag: 0));
        Assert.Equal(CharacterSelectionStore.SelectOutcome.Confirmed, store.Confirm(2));

        // 3/7 with Result != 0 -> spawn.
        Assert.False(dispatcher.RouteNow(SyntheticFrames.CharSpawnResult(result: 1, slot: 2)));

        // The local player is registered and marked as the controlled actor.
        Assert.NotNull(world.LocalActorKey);
        ActorKey key = world.LocalActorKey!.Value;
        Assert.Equal(EntitySort.PlayerCharacter, key.Sort);
        Assert.True(world.TryGet(key, out Actor actor));
        Assert.Equal(7, actor.Level);
        Assert.Equal(250u, actor.CurrentHp);

        var spawned = Assert.IsType<LocalPlayerSpawnedEvent>(Assert.Single(Drain(bus)));
        Assert.Equal(2, spawned.SlotIndex);
        Assert.Equal("Wuxia", spawned.Name);
        Assert.Equal((ushort)7, spawned.Level);
        Assert.Equal(250u, spawned.CurrentHp);
        Assert.Equal((ushort)9, spawned.ServerClass);
    }

    [Fact]
    public void CharSpawnResult_3_7_failure_publishes_failure_event_and_does_not_spawn()
    {
        var bus = new ClientEventBus(ClientEventBus.Unbounded);
        var world = new ClientWorld();
        var fsm = new ClientStateMachine(bus, ClientState.World);
        var store = new CharacterSelectionStore();
        var handler = new GamePacketHandler(
            world, bus, fsm, new CountingUnhandledOpcodeSink(), characterSelection: store);
        var dispatcher = new InboundFrameDispatcher(handler);

        store.Retain(new CharacterSlotRecord(
            slotIndex: 0,
            rawDescriptorAndStats: BuildDescriptor("Sifu", level: 3, hp: 90, mp: 40, stamina: 30,
                worldX: 0f, worldZ: 0f, serverClass: 1),
            slotFlag: 0));
        store.Confirm(0);

        // Result == 0 -> failure (no spawn). spec: login_flow.md §5.3.
        Assert.False(dispatcher.RouteNow(SyntheticFrames.CharSpawnResult(result: 0, slot: 3)));

        Assert.Null(world.LocalActorKey);
        Assert.Equal(0, world.Count);

        var failed = Assert.IsType<LocalPlayerSpawnFailedEvent>(Assert.Single(Drain(bus)));
        Assert.Equal((byte)3, failed.SlotIndex);
    }

    [Fact]
    public void CharSpawnResult_3_7_without_cached_descriptor_does_not_spawn()
    {
        var bus = new ClientEventBus(ClientEventBus.Unbounded);
        var world = new ClientWorld();
        var fsm = new ClientStateMachine(bus, ClientState.World);
        var store = new CharacterSelectionStore(); // nothing confirmed
        var unhandled = new CountingUnhandledOpcodeSink();
        var handler = new GamePacketHandler(world, bus, fsm, unhandled, characterSelection: store);
        var dispatcher = new InboundFrameDispatcher(handler);

        Assert.False(dispatcher.RouteNow(SyntheticFrames.CharSpawnResult(result: 1, slot: 0)));

        Assert.Null(world.LocalActorKey);
        Assert.Equal(0, world.Count);
        Assert.Empty(Drain(bus));
        Assert.Equal(1, unhandled.Count); // recorded, not spawned
    }

    // -------------------------------------------------------------------------
    // 3/1 cache + SelectCharacterAsync routing
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SelectCharacter_caches_descriptor_and_sends_1_9_for_a_real_slot()
    {
        var (useCases, _, store, bus, sink, fsm) = NewSelectionHarness(ClientState.CharacterSelection);

        // Feed a 3/1 list with a real character at slot 1 (caches the raw descriptor).
        DispatchCharacterList(store, bus, fsm, slots: (1, "Wuxia", 7, 250u, (ushort)9));

        await useCases.SelectCharacterAsync(slotIndex: 1);

        // The chosen slot's descriptor was cached for the 3/7 spawn.
        Assert.NotNull(store.Chosen);
        Assert.Equal("Wuxia", store.Chosen!.Name);

        // The 1/9 enter-game request was sent and the FSM advanced.
        Assert.Equal(ClientState.Loading, fsm.Current);
        var (major, minor, payload) = Assert.Single(sink.Sends);
        Assert.Equal(1, major);
        Assert.Equal(9, minor);
        Assert.Equal(CmsgEnterGameRequest.WireSize, payload.Length);
        Assert.Equal(1, payload[0x00]); // slot index
    }

    [Fact]
    public async Task SelectCharacter_blank_slot_routes_to_create_and_sends_no_1_9()
    {
        var (useCases, _, store, bus, sink, _) = NewSelectionHarness(ClientState.CharacterSelection);

        // Slot 0 holds the literal "@BLANK@" sentinel = empty slot. spec: login_flow.md §3.3.
        DispatchCharacterList(store, bus, fsm: null,
            slots: (0, CharacterSelectionStore.BlankSlotSentinel, 0, 0u, (ushort)0));
        Drain(bus); // discard the CharacterListEvent / state change

        await useCases.SelectCharacterAsync(slotIndex: 0);

        // No 1/9 frame; a CreateCharacterRequested event was published instead.
        Assert.Empty(sink.Sends);
        Assert.Null(store.Chosen);
        var requested = Assert.IsType<CreateCharacterRequestedEvent>(Assert.Single(Drain(bus)));
        Assert.Equal(0, requested.SlotIndex);
    }

    [Fact]
    public async Task SelectCharacter_slot_above_4_is_rejected()
    {
        var (useCases, _, _, _, sink, _) = NewSelectionHarness(ClientState.CharacterSelection);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await useCases.SelectCharacterAsync(slotIndex: 5));

        Assert.Empty(sink.Sends); // nothing sent for an out-of-range slot
    }

    // -------------------------------------------------------------------------
    // 1/9 version-token derivation: 2114 -> 21149
    // -------------------------------------------------------------------------

    [Fact]
    public void VersionToken_derivation_2114_yields_21149()
    {
        Assert.Equal(21149u, ClientVersionToken.Derive(2114));
        Assert.Equal(21149u, ClientVersionToken.SampledToken);
        Assert.Equal(2114u, ClientVersionToken.SampledVersionField);
        Assert.Equal(21149u, ClientVersionToken.Derive(DefaultClientVersionSource.Instance.VersionField));
    }

    [Fact]
    public async Task SelectCharacter_stamps_the_derived_21149_token_into_the_1_9_body()
    {
        var (useCases, _, store, bus, sink, _) = NewSelectionHarness(ClientState.CharacterSelection);
        DispatchCharacterList(store, bus, fsm: null, slots: (0, "Hero", 1, 10u, (ushort)1));

        await useCases.SelectCharacterAsync(slotIndex: 0);

        var (_, _, payload) = Assert.Single(sink.Sends);
        // The token region (0x01..) holds the NUL-terminated decimal ASCII "21149". spec: login_flow.md §3.3.
        ReadOnlySpan<byte> tokenRegion = payload.AsSpan(0x01, ApplicationUseCases.VersionTokenLength);
        int nul = tokenRegion.IndexOf((byte)0);
        string decoded = Encoding.ASCII.GetString(nul >= 0 ? tokenRegion[..nul] : tokenRegion);
        Assert.Equal("21149", decoded);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static (ApplicationUseCases UseCases, ClientWorld World, CharacterSelectionStore Store,
        ClientEventBus Bus, FakeOutboundSink Sink, ClientStateMachine Fsm) NewSelectionHarness(
            ClientState initial)
    {
        var bus = new ClientEventBus(ClientEventBus.Unbounded);
        var fsm = new ClientStateMachine(bus, initial);
        var world = new ClientWorld();
        var store = new CharacterSelectionStore();
        var sink = new FakeOutboundSink();
        var credentials = new LoginCredentialStore();
        var useCases = new ApplicationUseCases(
            sink, fsm, world, credentials, new SessionId(1),
            characterSelection: store, eventBus: bus);
        return (useCases, world, store, bus, sink, fsm);
    }

    /// <summary>Routes a synthetic 3/1 list through the handler so the store retains the raw descriptors.</summary>
    private static void DispatchCharacterList(
        CharacterSelectionStore store, ClientEventBus bus, ClientStateMachine? fsm,
        params (int Slot, string Name, ushort Level, uint Hp, ushort Class)[] slots)
    {
        var listFsm = fsm ?? new ClientStateMachine(bus, ClientState.Login);
        var handler = new GamePacketHandler(
            new ClientWorld(), bus, listFsm, new CountingUnhandledOpcodeSink(), characterSelection: store);
        var dispatcher = new InboundFrameDispatcher(handler);
        dispatcher.RouteNow(SyntheticFrames.CharacterList(serverId: 1, channelId: 1, slots));
    }

    /// <summary>Builds an 880 + 96 = 976-byte descriptor+stats blob with the cited sub-offsets.</summary>
    private static byte[] BuildDescriptor(
        string name, ushort level, uint hp, uint mp, uint stamina, float worldX, float worldZ,
        ushort serverClass)
    {
        var d = new byte[976]; // 880 descriptor + 96 stats. spec: login_flow.md §3.2.
        byte[] nameBytes = Encoding.ASCII.GetBytes(name);
        int nameLen = Math.Min(nameBytes.Length, 16);
        Array.Copy(nameBytes, 0, d, 0x00, nameLen); // +0x00 name
        BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(0x3A, 2), level); // +0x3A
        BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(0x3C, 4), hp); // +0x3C
        BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(0x40, 4), mp); // +0x40
        BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(0x44, 4), stamina); // +0x44
        BinaryPrimitives.WriteSingleLittleEndian(d.AsSpan(0x4C, 4), worldX); // +0x4C
        BinaryPrimitives.WriteSingleLittleEndian(d.AsSpan(0x50, 4), worldZ); // +0x50
        BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(0x74, 2), serverClass); // +0x74
        return d;
    }
}