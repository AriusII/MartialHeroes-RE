using System.Numerics;
using MartialHeroes.Client.Application.Diagnostics;
using MartialHeroes.Client.Application.Events;
using MartialHeroes.Client.Application.Handlers;
using MartialHeroes.Client.Application.Ingestion;
using MartialHeroes.Client.Application.Login;
using MartialHeroes.Client.Application.StateMachine;
using MartialHeroes.Client.Application.UseCases;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Client.Domain.Actors;
using MartialHeroes.Client.Domain.Stats;
using MartialHeroes.Network.Abstractions.Protocol;
using MartialHeroes.Network.Abstractions.Session;
using MartialHeroes.Network.Crypto;
using MartialHeroes.Shared.Kernel.Numerics;
using Xunit;

namespace MartialHeroes.Client.Application.Tests;

/// <summary>
/// Drives the new S2C packets (5/53, 5/1, 4/29, 5/32) and the 0/0 -> 1/4 login handshake through the
/// dispatcher into <see cref="GamePacketHandler"/>, asserting Domain mutation and published events.
/// </summary>
public sealed class InboundS2CHandlerTests
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

    [Fact]
    public void Vitals_5_53_updates_actor_hp_mp_and_publishes_event()
    {
        var bus = new ClientEventBus(ClientEventBus.Unbounded);
        var world = new ClientWorld();
        var fsm = new ClientStateMachine(bus, ClientState.World);
        var handler = new GamePacketHandler(world, bus, fsm, new CountingUnhandledOpcodeSink());
        var dispatcher = new InboundFrameDispatcher(handler);

        // Pre-register the actor with generous maxima so the new current values are not clamped.
        var key = new ActorKey(7, EntitySort.PlayerCharacter);
        world.Add(new Actor(key, 1, VitalStats.FromResolved(1000, 1000, 1000), 100, 100, 100, Vector3Fixed.Zero));

        Assert.False(dispatcher.RouteNow(SyntheticFrames.Vitals(
            sort: 1, actorId: 7, currentHp: 250, stamina: 60, vitalC: 80)));

        Assert.True(world.TryGet(key, out Actor actor));
        Assert.Equal(250u, actor.CurrentHp);
        Assert.Equal(80u, actor.CurrentMp);
        Assert.Equal(60u, actor.CurrentStamina);

        var vitals = Assert.IsType<ActorVitalsChangedEvent>(Assert.Single(Drain(bus)));
        Assert.Equal(key, vitals.Key);
        Assert.Equal(250u, vitals.CurrentHp);
        Assert.Equal(80u, vitals.CurrentMp);
        Assert.Equal(60u, vitals.CurrentStamina);
    }

    [Fact]
    public void SpawnExtended_5_1_registers_actor_with_descriptor_fields()
    {
        var bus = new ClientEventBus(ClientEventBus.Unbounded);
        var world = new ClientWorld();
        var fsm = new ClientStateMachine(bus, ClientState.World);
        var handler = new GamePacketHandler(world, bus, fsm, new CountingUnhandledOpcodeSink());
        var dispatcher = new InboundFrameDispatcher(handler);

        byte[] frame = SyntheticFrames.SpawnExtended(
            sort: 1, actorId: 55, name: "Sifu", level: 12,
            currentHp: 320, currentMp: 140, currentStamina: 90,
            worldX: 7.5f, worldZ: -2.0f, serverClass: 3);

        Assert.False(dispatcher.RouteNow(frame)); // dispatched via OnUnhandled path, returns false

        var key = new ActorKey(55, EntitySort.PlayerCharacter);
        Assert.True(world.TryGet(key, out Actor actor));
        Assert.Equal(12, actor.Level);
        Assert.Equal(320u, actor.CurrentHp); // not clamped (server-authoritative guard)
        Assert.Equal(140u, actor.CurrentMp);
        Assert.Equal(Vector3Fixed.FromFloat(7.5f, 0f, -2.0f), actor.Position);

        var spawned = Assert.IsType<ActorSpawnedEvent>(Assert.Single(Drain(bus)));
        Assert.Equal(key, spawned.Key);
        Assert.Equal("Sifu", spawned.Name);
        Assert.Equal((ushort)12, spawned.Level);
        Assert.Equal(320u, spawned.CurrentHp);
        Assert.Equal((ushort)3, spawned.ServerClass);
    }

    [Fact]
    public void StatUpdate_4_29_publishes_stats_when_result_ok()
    {
        var bus = new ClientEventBus(ClientEventBus.Unbounded);
        var world = new ClientWorld();
        var fsm = new ClientStateMachine(bus, ClientState.World);
        var handler = new GamePacketHandler(world, bus, fsm, new CountingUnhandledOpcodeSink());
        var dispatcher = new InboundFrameDispatcher(handler);

        dispatcher.RouteNow(SyntheticFrames.StatUpdate(
            handle: 7, resultOk: 1, stat0: 11, stat1: 12, stat2: 13, stat3: 14, stat4: 15,
            remainingStatPoints: 4));

        var stats = Assert.IsType<ActorStatsChangedEvent>(Assert.Single(Drain(bus)));
        Assert.Equal(11u, stats.Stat0);
        Assert.Equal(15u, stats.Stat4);
        Assert.Equal(4u, stats.RemainingStatPoints);
    }

    [Fact]
    public void StatUpdate_4_29_is_ignored_when_result_not_ok()
    {
        var bus = new ClientEventBus(ClientEventBus.Unbounded);
        var world = new ClientWorld();
        var fsm = new ClientStateMachine(bus, ClientState.World);
        var handler = new GamePacketHandler(world, bus, fsm, new CountingUnhandledOpcodeSink());
        var dispatcher = new InboundFrameDispatcher(handler);

        dispatcher.RouteNow(SyntheticFrames.StatUpdate(
            handle: 7, resultOk: 0, stat0: 11, stat1: 12, stat2: 13, stat3: 14, stat4: 15,
            remainingStatPoints: 4));

        Assert.Empty(Drain(bus));
    }

    [Fact]
    public void LevelUp_5_32_updates_level_and_publishes_event()
    {
        var bus = new ClientEventBus(ClientEventBus.Unbounded);
        var world = new ClientWorld();
        var fsm = new ClientStateMachine(bus, ClientState.World);
        var handler = new GamePacketHandler(world, bus, fsm, new CountingUnhandledOpcodeSink());
        var dispatcher = new InboundFrameDispatcher(handler);

        var key = new ActorKey(7, EntitySort.PlayerCharacter);
        world.Add(new Actor(key, 1, VitalStats.FromResolved(1000, 1000, 1000), 100, 100, 100, Vector3Fixed.Zero));

        dispatcher.RouteNow(SyntheticFrames.LevelUp(
            sort: 1, actorId: 7, newLevel: 5, currentHp: 500, currentMp: 300, stamina: 200,
            remainingStatPoints: 3));

        Assert.True(world.TryGet(key, out Actor actor));
        Assert.Equal(5, actor.Level);
        Assert.Equal(500u, actor.CurrentHp);
        Assert.Equal(300u, actor.CurrentMp);

        var leveled = Assert.IsType<ActorLeveledUpEvent>(Assert.Single(Drain(bus)));
        Assert.Equal((ushort)5, leveled.NewLevel);
        Assert.Equal(500u, leveled.CurrentHp);
        Assert.Equal(300u, leveled.CurrentMp);
        Assert.Equal(3, leveled.RemainingStatPoints);
    }

    [Fact]
    public void KeyExchange_0_0_builds_and_sends_a_1_4_auth_reply()
    {
        // Synthetic small RSA key (same construction as the Crypto tests): n EXACTLY 39 bytes, e 3 bytes
        // => L1 + L2 = 42 (the spec invariant). These are test primes, not recovered server values.
        BigInteger p = BigInteger.Parse("75377541258354731458810898159183352769326586247");
        BigInteger q = BigInteger.Parse("48710038997288231143179367274763024050866548859");
        BigInteger n = p * q;
        BigInteger e = 65537;
        byte[] nDigits = n.ToByteArray(isUnsigned: true, isBigEndian: true);
        byte[] eDigits = e.ToByteArray(isUnsigned: true, isBigEndian: true);

        var bus = new ClientEventBus(ClientEventBus.Unbounded);
        var world = new ClientWorld();
        var fsm = new ClientStateMachine(bus, ClientState.Login);
        var sink = new FakeOutboundSink();

        var credentials = new LoginCredentialStore();
        credentials.Stage("account", "hunter2"); // stage before 0/0, per crypto.md §6.1

        var loginDriver = new LoginHandshakeDriver(
            sink, credentials, new SessionId(1),
            paddingRandom: new SequentialPaddingRandom(start: 1), stateMachine: fsm);

        var handler = new GamePacketHandler(world, bus, fsm, new CountingUnhandledOpcodeSink(), loginDriver);
        var dispatcher = new InboundFrameDispatcher(handler);

        byte[] frame = SyntheticFrames.KeyExchange(nDigits, eDigits, scalar1: 0xDEADBEEF, scalar2: 0x0BADF00D);
        dispatcher.RouteNow(frame);

        var (major, minor, payload) = Assert.Single(sink.Sends);
        Assert.Equal(1, major);
        Assert.Equal(4, minor);
        Assert.True(payload.Length > 4, "1/4 reply must carry a length-prefixed cipher digit body.");

        // Credential is wiped after the reply is built (crypto.md §6.1).
        Assert.False(credentials.HasStagedCredential);

        // A handshake-completed event was published, and the FSM advanced out of Login.
        List<IClientEvent> events = Drain(bus);
        Assert.Contains(events, ev => ev is LoginHandshakeCompletedEvent);
        Assert.Equal(ClientState.CharacterSelection, fsm.Current);
    }

    /// <summary>Deterministic non-zero padding RNG for reproducible 1/4 replies (mirrors the Crypto tests).</summary>
    private sealed class SequentialPaddingRandom(byte start) : IPaddingRandom
    {
        private byte _next = start;

        public void Fill(Span<byte> destination)
        {
            for (int i = 0; i < destination.Length; i++)
            {
                if (_next == 0)
                {
                    _next = 1;
                }

                destination[i] = _next++;
            }
        }
    }
}