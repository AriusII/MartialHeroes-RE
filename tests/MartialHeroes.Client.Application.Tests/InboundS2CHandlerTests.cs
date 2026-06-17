using System.Numerics;
using System.Text;
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

        // --- Verify the 0x2B pre-image is present in the whitened payload ---
        // The whole payload is per-dword XOR 0x29 whitened (spec: crypto.md §6.4).
        // First byte of the un-whitened payload = sub-opcode 0x2B; after whitening = 0x2B ^ 0x29 = 0x02.
        // spec: packets/login.yaml (SubOpcode = 0x2B, DEBUGGER-VERIFIED); crypto.md §6.4.
        const byte ExpectedFirstByteWhitened = 0x2B ^ 0x29; // 0x02
        Assert.Equal(ExpectedFirstByteWhitened, payload[0]);

        // The account "account" is 7 bytes; the pre-image is [0x2B][u32 LE 8][7 account bytes][0x00].
        // De-whiten the first 12 bytes (3 dwords) to verify the account-length prefix.
        // XOR each 4-byte word with 0x29 (whitening involution). spec: crypto.md §6.4.
        byte[] dewhitenedHead = new byte[12];
        payload.AsSpan(0, 12).CopyTo(dewhitenedHead);
        for (int dw = 0; dw < 3; dw++)
        {
            dewhitenedHead[dw * 4] ^= 0x29;
            // upper 3 bytes of each dword XOR with 0x00 0x00 0x00 (key = 0x00000029 LE). spec §6.4.
        }

        Assert.Equal(0x2B, dewhitenedHead[0]); // sub-opcode. spec login.yaml off 0x00.
        uint accountLen = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(dewhitenedHead.AsSpan(1, 4));
        Assert.Equal(8u, accountLen); // strlen("account") + 1 = 8. spec login.yaml AccountLength = strlen+1.

        // Credential is wiped after the reply is built (crypto.md §6.1).
        Assert.False(credentials.HasStagedCredential);

        // A handshake-completed event was published EXACTLY ONCE (the handler is the single publisher;
        // the driver no longer publishes, so there is no double-fire), and the FSM advanced out of Login.
        List<IClientEvent> events = Drain(bus);
        Assert.Single(events, ev => ev is LoginHandshakeCompletedEvent);
        Assert.Equal(ClientState.CharacterSelection, fsm.Current);
    }

    [Fact]
    public void KeyExchange_0_0_publishes_handshake_completed_exactly_once()
    {
        // Regression guard for the former double-publish: the handler and the driver both used to be able
        // to publish LoginHandshakeCompletedEvent. The driver no longer publishes — the handler is the
        // single owner — so even with the same bus reachable from both, the event fires exactly once.
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
        credentials.Stage("account", "hunter2");

        var loginDriver = new LoginHandshakeDriver(
            sink, credentials, new SessionId(1),
            paddingRandom: new SequentialPaddingRandom(start: 1), stateMachine: fsm);
        var handler = new GamePacketHandler(world, bus, fsm, new CountingUnhandledOpcodeSink(), loginDriver);
        var dispatcher = new InboundFrameDispatcher(handler);

        dispatcher.RouteNow(SyntheticFrames.KeyExchange(nDigits, eDigits, scalar1: 0, scalar2: 0));

        int handshakeEvents = Drain(bus).Count(ev => ev is LoginHandshakeCompletedEvent);
        Assert.Equal(1, handshakeEvents);
    }

    [Fact]
    public void KeyExchange_0_0_with_pin_includes_pin_region_in_1_4_pre_image()
    {
        // Synthetic RSA key: same test primes, L1 + L2 = 42. spec: crypto.md §6.2.1.
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
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding cp949 = Encoding.GetEncoding(949);
        // Stage with PIN — triggers the a7-gate; PIN region must appear in the pre-image.
        // spec: login.yaml PIN GATE; crypto.md §6.1, §6.6.
        credentials.Stage("abc", "pw", pin: "1234");

        var loginDriver = new LoginHandshakeDriver(
            sink, credentials, new SessionId(1),
            paddingRandom: new SequentialPaddingRandom(start: 0x55), stateMachine: fsm);

        var handler = new GamePacketHandler(world, bus, fsm, new CountingUnhandledOpcodeSink(), loginDriver);
        var dispatcher = new InboundFrameDispatcher(handler);

        byte[] frame = SyntheticFrames.KeyExchange(nDigits, eDigits, scalar1: 0, scalar2: 0);
        dispatcher.RouteNow(frame);

        var (major, minor, payload) = Assert.Single(sink.Sends);
        Assert.Equal(1, major);
        Assert.Equal(4, minor);

        // De-whiten per-dword XOR 0x29 to inspect the pre-image. spec: crypto.md §6.4 (involution).
        byte[] plain = (byte[])payload.Clone();
        for (int dw = 0; dw < plain.Length / 4; dw++)
        {
            plain[dw * 4] ^= 0x29; // XOR key 0x00000029 LE: only lowest byte of each dword is 0x29.
        }

        int i = 0;
        Assert.Equal(0x2B, plain[i++]); // sub-opcode. spec login.yaml off 0x00.

        uint accLen = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(plain.AsSpan(i, 4));
        i += 4;
        Assert.Equal(4u, accLen); // strlen("abc") + 1 = 4. spec login.yaml AccountLength = strlen+1.
        Assert.True(plain.AsSpan(i, 3).SequenceEqual("abc"u8)); // account bytes
        i += 3;
        Assert.Equal(0x00, plain[i++]); // trailing NUL (counted in accLen)

        // PIN region must be present (a7-gate active). spec: login.yaml PIN GATE.
        uint pinLen = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(plain.AsSpan(i, 4));
        i += 4;
        Assert.Equal(5u, pinLen); // strlen("1234") + 1 = 5. spec login.yaml PinLength = strlen+1.
        Assert.True(plain.AsSpan(i, 4).SequenceEqual(cp949.GetBytes("1234"))); // PIN bytes
        i += 4;
        Assert.Equal(0x00, plain[i++]); // trailing NUL

        // The ciphertext region follows: [u32 LE len][BE RSA digits]. spec: crypto.md §6.3.
        uint cipherLen = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(plain.AsSpan(i, 4));
        Assert.True(cipherLen > 0);
        Assert.Equal((uint)(plain.Length - i - 4), cipherLen);

        Assert.False(credentials.HasStagedCredential);
        Assert.Contains(Drain(bus), ev => ev is LoginHandshakeCompletedEvent);
    }

    [Fact]
    public void LoginCredentialStore_stages_17_byte_M_and_exposes_account_bytes()
    {
        var store = new LoginCredentialStore();

        store.Stage("myUser", "myPass");

        // HasStagedCredential reflects the 17-byte M being present.
        Assert.True(store.HasStagedCredential);
        Assert.Equal("myUser", store.Username);

        // AccountBytes encodes the account as CP949 WITHOUT trailing NUL. spec: login_flow.md §7;
        // login.yaml AccountLength = strlen+1.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding cp949 = Encoding.GetEncoding(949);
        Assert.True(store.AccountBytes.SequenceEqual(cp949.GetBytes("myUser")));

        // StagedPasswordM is always 17 bytes, zero-padded. spec: crypto.md §6.1, §6.6, §6b
        // (DEBUGGER-VERIFIED: the server expects a fixed-width 17-byte password field).
        Assert.Equal(17, store.StagedPasswordM.Length);
        Assert.True(store.StagedPasswordM[..6].SequenceEqual(cp949.GetBytes("myPass")));
        for (int i = 6; i < 17; i++)
        {
            Assert.Equal(0, store.StagedPasswordM[i]); // trailing zero-padding. spec: crypto.md §6.1.
        }

        // No PIN staged — IncludePin must be false. spec: login.yaml PIN GATE.
        Assert.False(store.IncludePin);
        Assert.Equal(0, store.PinBytes.Length);

        // Clear zeroes everything. spec: crypto.md §6.1 (staged M zeroed and freed).
        store.Clear();
        Assert.False(store.HasStagedCredential);
        Assert.Equal(string.Empty, store.Username);
        Assert.Equal(0, store.AccountBytes.Length);
        Assert.Equal(0, store.PinBytes.Length);
    }

    [Fact]
    public void LoginCredentialStore_stages_17_byte_M_with_pin_and_sets_include_flag()
    {
        var store = new LoginCredentialStore();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding cp949 = Encoding.GetEncoding(949);

        store.Stage("user", "pass", pin: "9876");

        Assert.True(store.HasStagedCredential);
        Assert.True(store.IncludePin); // a7-gate active. spec: login.yaml PIN GATE.
        Assert.True(store.PinBytes.SequenceEqual(cp949.GetBytes("9876")));

        store.Clear();
        Assert.False(store.IncludePin);
    }

    [Fact]
    public void LoginCredentialStore_encodes_korean_login_text_as_CP949_and_rejects_5_digit_pin()
    {
        var store = new LoginCredentialStore();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding cp949 = Encoding.GetEncoding(949);

        store.Stage("무사", "비번", pin: "1234");

        Assert.True(store.AccountBytes.SequenceEqual(cp949.GetBytes("무사")));
        Assert.True(store.StagedPasswordM[..cp949.GetByteCount("비번")].SequenceEqual(cp949.GetBytes("비번")));
        Assert.True(store.PinBytes.SequenceEqual(cp949.GetBytes("1234")));

        Assert.Throws<ArgumentException>(() => store.Stage("user", "pass", pin: "12345"));
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