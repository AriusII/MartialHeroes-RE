using System.Buffers.Binary;
using MartialHeroes.Client.Application.Events;
using MartialHeroes.Client.Application.StateMachine;
using MartialHeroes.Client.Application.UseCases;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Client.Domain.Actors;
using MartialHeroes.Client.Domain.Stats;
using MartialHeroes.Network.Abstractions.Protocol;
using MartialHeroes.Network.Abstractions.Session;
using MartialHeroes.Network.Protocol.Packets;
using MartialHeroes.Shared.Kernel.Numerics;
using Xunit;

namespace MartialHeroes.Client.Application.Tests;

public sealed class ApplicationUseCasesTests
{
    /// <summary>A fake outbound sink that records every send for assertions.</summary>
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

    private static ApplicationUseCases NewUseCases(
        FakeOutboundSink sink, out ClientWorld world, out ClientStateMachine fsm,
        ClientState initial = ClientState.World)
    {
        var bus = new ClientEventBus(ClientEventBus.Unbounded);
        fsm = new ClientStateMachine(bus, initial);
        world = new ClientWorld();
        var credentials = new LoginCredentialStore();
        return new ApplicationUseCases(sink, fsm, world, credentials, new SessionId(1));
    }

    [Fact]
    public async Task SelectCharacter_drives_fsm_to_loading_and_sends_1_9()
    {
        var sink = new FakeOutboundSink();
        var useCases = NewUseCases(sink, out _, out ClientStateMachine fsm, ClientState.CharacterSelection);

        await useCases.SelectCharacterAsync(slotIndex: 2);

        Assert.Equal(ClientState.Loading, fsm.Current);
        var (major, minor, payload) = Assert.Single(sink.Sends);
        Assert.Equal(1, major);
        Assert.Equal(9, minor);
        Assert.Equal(CmsgEnterGameRequest.WireSize, payload.Length);
        Assert.Equal(2, payload[0x00]); // SlotIndex
    }

    [Fact]
    public async Task RequestMove_sends_2_13_with_expected_target_bytes()
    {
        var sink = new FakeOutboundSink();
        var useCases = NewUseCases(sink, out ClientWorld world, out _);

        // Local player at origin; move toward (3, _, 4).
        var key = new ActorKey(7, EntitySort.PlayerCharacter);
        world.Add(new Actor(key, 1, VitalStats.FromResolved(100, 0, 0), 100, 0, 0, Vector3Fixed.Zero));
        world.LocalActorKey = key;

        Vector3Fixed target = Vector3Fixed.FromWholeUnits(3, 0, 4);
        await useCases.RequestMoveAsync(target, running: true);

        var (major, minor, payload) = Assert.Single(sink.Sends);
        Assert.Equal(2, major);
        Assert.Equal(13, minor);
        Assert.Equal(CmsgMoveRequest.WireSize, payload.Length);

        // TargetX @0x04, TargetZ @0x08 are the FromFloat-roundtripped wire floats.
        var (tx, _, tz) = target.ToVector3Float();
        Assert.Equal(tx, BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(0x04, 4)));
        Assert.Equal(tz, BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(0x08, 4)));

        // Heading @0x00 = atan2(tz - 0, tx - 0).
        float expectedHeading = MathF.Atan2(tz, tx);
        Assert.Equal(expectedHeading, BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(0x00, 4)), 5);

        // ModeFlags @0x0c: mode 1 + run bit 0x100.
        uint modeFlags = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(0x0c, 4));
        Assert.Equal(1u, modeFlags & 0xFFu);     // click-to-move mode
        Assert.Equal(0x100u, modeFlags & 0x100u); // run bit set
    }

    [Fact]
    public async Task UseSkill_sends_2_52_header_plus_target_arrays()
    {
        var sink = new FakeOutboundSink();
        var useCases = NewUseCases(sink, out _, out _);

        uint[] a = [101u, 202u];
        uint[] b = [303u];
        await useCases.UseSkillAsync(slot: 0x05, targetsA: a, targetsB: b);

        var (major, minor, payload) = Assert.Single(sink.Sends);
        Assert.Equal(2, major);
        Assert.Equal(52, minor);
        Assert.Equal(CmsgUseSkillHeader.HeaderSize + (a.Length + b.Length) * 4, payload.Length);
        Assert.Equal(0x05, payload[0x00]); // SkillSlot
        Assert.Equal((ushort)a.Length, BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(0x14, 2))); // CountA
        Assert.Equal((ushort)b.Length, BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(0x16, 2))); // CountB

        int cursor = CmsgUseSkillHeader.HeaderSize;
        Assert.Equal(101u, BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(cursor, 4)));
        Assert.Equal(202u, BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(cursor + 4, 4)));
        Assert.Equal(303u, BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(cursor + 8, 4)));
    }

    [Fact]
    public async Task SendChat_whisper_builds_2_7_with_name_and_length_prefixed_text()
    {
        var sink = new FakeOutboundSink();
        var useCases = NewUseCases(sink, out _, out _);

        await useCases.SendChatAsync(channel: 0, text: "hi", recipientName: "Master");

        var (major, minor, payload) = Assert.Single(sink.Sends);
        Assert.Equal(2, major);
        Assert.Equal(7, minor);
        // TargetName @0x02 (16 bytes, NUL-padded).
        string name = System.Text.Encoding.ASCII.GetString(payload, 0x02, 6);
        Assert.Equal("Master", name);
        // Body: [u32 len incl NUL]["hi"][0x00].
        uint len = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(CmsgWhisperHeader.HeaderSize, 4));
        Assert.Equal(3u, len); // "hi" + NUL
    }

    [Fact]
    public async Task SendChat_channel_builds_3_21_with_selector_and_text()
    {
        var sink = new FakeOutboundSink();
        var useCases = NewUseCases(sink, out _, out _);

        await useCases.SendChatAsync(channel: 42, text: "hello");

        var (major, minor, payload) = Assert.Single(sink.Sends);
        Assert.Equal(3, major);
        Assert.Equal(21, minor);
        Assert.Equal(42u, BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(0x04, 4))); // ChannelSelector
        uint len = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(CmsgChatChannelHeader.HeaderSize, 4));
        Assert.Equal(6u, len); // "hello" + NUL
    }

    [Fact]
    public async Task Login_stages_credential_and_advances_fsm_without_sending()
    {
        var sink = new FakeOutboundSink();
        var bus = new ClientEventBus(ClientEventBus.Unbounded);
        var fsm = new ClientStateMachine(bus, ClientState.Login);
        var world = new ClientWorld();
        var credentials = new LoginCredentialStore();
        var useCases = new ApplicationUseCases(sink, fsm, world, credentials, new SessionId(1));

        await useCases.LoginAsync("account", "secret");

        Assert.True(credentials.HasStagedCredential);
        Assert.Equal("account", credentials.Username);
        // Pre-0/0 username send (1/6) is provisional and not emitted.
        Assert.Empty(sink.Sends);
        Assert.Equal(ClientState.CharacterSelection, fsm.Current);
    }
}
