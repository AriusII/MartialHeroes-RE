using System.Buffers.Binary;
using System.Collections.Immutable;
using MartialHeroes.Client.Application.Diagnostics;
using MartialHeroes.Client.Application.Events;
using MartialHeroes.Client.Application.Handlers;
using MartialHeroes.Client.Application.Ingestion;
using MartialHeroes.Client.Application.StateMachine;
using MartialHeroes.Client.Application.UseCases;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Network.Abstractions.Lobby;
using MartialHeroes.Network.Abstractions.Protocol;
using MartialHeroes.Network.Abstractions.Session;
using Xunit;

namespace MartialHeroes.Client.Application.Tests;

/// <summary>
/// Covers E4-c sub-wave 2: lobby orchestration use-cases (against a fake <see cref="ILobbyClient"/>),
/// the 3/4 / 3/6 / 3/23 character-management result handlers (synthetic frames -&gt; events), and the
/// feature-flagged 1/6 login-blob emit (flag on -&gt; right bytes to a fake sink).
/// </summary>
public sealed class LobbyAndCharManagementTests
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

    private sealed class FakeLobbyClient : ILobbyClient
    {
        public IReadOnlyList<LobbyServerRecord> ServerList { get; init; } = [];
        public LobbyChannelEndpoint Endpoint { get; init; } = new("0.0.0.0", 0);
        public ushort? LastRequestedServerId { get; private set; }

        public Task<IReadOnlyList<LobbyServerRecord>> FetchServerListAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(ServerList);

        public Task<LobbyChannelEndpoint> FetchChannelEndpointAsync(
            ushort serverId, CancellationToken cancellationToken = default)
        {
            LastRequestedServerId = serverId;
            return Task.FromResult(Endpoint);
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

    // =====================================================================================================
    // Lobby orchestration
    // =====================================================================================================

    [Fact]
    public async Task FetchServerList_publishes_view_with_load_and_status_hints()
    {
        var sink = new FakeOutboundSink();
        var bus = new ClientEventBus(ClientEventBus.Unbounded);
        var fsm = new ClientStateMachine(bus, ClientState.Login);
        var world = new ClientWorld();
        var lobby = new FakeLobbyClient
        {
            ServerList =
            [
                new LobbyServerRecord(ServerId: 1, Status: 1, Load: 1300, OpenTime: 0), // Full, Normal (status 1 = in-range)
                new LobbyServerRecord(ServerId: 2, Status: 3, Load: 900, OpenTime: 30), // Busy, ScheduledOpen
                new LobbyServerRecord(ServerId: 3, Status: 24, Load: 600, OpenTime: 0), // Moderate, Preparing
                new LobbyServerRecord(ServerId: 4, Status: 100, Load: 100, OpenTime: 0), // Light, CurrentSelection
                new LobbyServerRecord(ServerId: 5, Status: -1, Load: 0, OpenTime: 0), // Light, Invalid
            ],
        };
        var useCases = new ApplicationUseCases(
            sink, fsm, world, new LoginCredentialStore(), new SessionId(1),
            eventBus: bus, lobbyClient: lobby);

        IReadOnlyList<LobbyServerRecord> raw = await useCases.FetchServerListAsync();

        Assert.Equal(5, raw.Count);
        var evt = Assert.IsType<ServerListReceivedEvent>(Assert.Single(Drain(bus)));
        ImmutableArray<ServerListEntryView> v = evt.Servers;
        Assert.Equal(5, v.Length);

        Assert.Equal(ServerLoadBand.Full, v[0].LoadHint);
        Assert.Equal(ServerStatusHint.Normal, v[0].StatusHint);

        Assert.Equal(ServerLoadBand.Busy, v[1].LoadHint);
        Assert.Equal(ServerStatusHint.ScheduledOpen, v[1].StatusHint);
        Assert.Equal((short)30, v[1].OpenTime);

        Assert.Equal(ServerLoadBand.Moderate, v[2].LoadHint);
        Assert.Equal(ServerStatusHint.Preparing, v[2].StatusHint);

        Assert.Equal(ServerLoadBand.Light, v[3].LoadHint);
        Assert.Equal(ServerStatusHint.CurrentSelection, v[3].StatusHint);

        Assert.Equal(ServerStatusHint.Invalid, v[4].StatusHint);
    }

    [Fact]
    public async Task SelectServer_resolves_endpoint_and_publishes_event()
    {
        var sink = new FakeOutboundSink();
        var bus = new ClientEventBus(ClientEventBus.Unbounded);
        var fsm = new ClientStateMachine(bus, ClientState.Login);
        var world = new ClientWorld();
        var lobby = new FakeLobbyClient { Endpoint = new LobbyChannelEndpoint("203.0.113.7", 20801) };
        var useCases = new ApplicationUseCases(
            sink, fsm, world, new LoginCredentialStore(), new SessionId(1),
            eventBus: bus, lobbyClient: lobby);

        LobbyChannelEndpoint endpoint = await useCases.SelectServerAsync(serverId: 4);

        Assert.Equal("203.0.113.7", endpoint.Host);
        Assert.Equal(20801, endpoint.Port);
        Assert.Equal((ushort)4, lobby.LastRequestedServerId);

        var evt = Assert.IsType<ChannelEndpointResolvedEvent>(Assert.Single(Drain(bus)));
        Assert.Equal((ushort)4, evt.ServerId);
        Assert.Equal("203.0.113.7", evt.Host);
        Assert.Equal(20801, evt.Port);
    }

    [Fact]
    public async Task Lobby_usecases_throw_when_no_lobby_client_wired()
    {
        var sink = new FakeOutboundSink();
        var bus = new ClientEventBus(ClientEventBus.Unbounded);
        var fsm = new ClientStateMachine(bus, ClientState.Login);
        var useCases = new ApplicationUseCases(
            sink, fsm, new ClientWorld(), new LoginCredentialStore(), new SessionId(1), eventBus: bus);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await useCases.FetchServerListAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await useCases.SelectServerAsync(1));
    }

    // =====================================================================================================
    // 3/4 — char manage / delete result
    // =====================================================================================================

    [Fact]
    public void CharManage_3_4_delete_confirm_decrements_account_count_and_publishes()
    {
        var bus = new ClientEventBus(ClientEventBus.Unbounded);
        var world = new ClientWorld();
        var fsm = new ClientStateMachine(bus, ClientState.CharacterSelection);
        var account = new AccountCharacterState(initialCount: 3);
        var handler = new GamePacketHandler(
            world, bus, fsm, new CountingUnhandledOpcodeSink(),
            accountCharacters: account);
        var dispatcher = new InboundFrameDispatcher(handler);

        dispatcher.RouteNow(SyntheticFrames.CharManageResult(result: 1, subtype: 2, readyTime: 0));

        Assert.Equal(2, account.CharacterCount);
        var evt = Assert.IsType<CharManageResultEvent>(Assert.Single(Drain(bus)));
        Assert.True(evt.Success);
        Assert.Equal(CharManageSubtype.DeleteConfirm, evt.Subtype);
        Assert.Equal((byte)2, evt.RawSubtype);
        Assert.Equal(2, evt.AccountCharacterCount);
    }

    [Fact]
    public void CharManage_3_4_blocked_carries_ready_time_and_does_not_decrement()
    {
        var bus = new ClientEventBus(ClientEventBus.Unbounded);
        var world = new ClientWorld();
        var fsm = new ClientStateMachine(bus, ClientState.CharacterSelection);
        var account = new AccountCharacterState(initialCount: 3);
        var handler = new GamePacketHandler(
            world, bus, fsm, new CountingUnhandledOpcodeSink(), accountCharacters: account);
        var dispatcher = new InboundFrameDispatcher(handler);

        // Result 0 = blocked (cooldown); ReadyTime drives the wait message; no decrement.
        dispatcher.RouteNow(SyntheticFrames.CharManageResult(result: 0, subtype: 2, readyTime: 0xDEADBEEF));

        Assert.Equal(3, account.CharacterCount); // unchanged
        var evt = Assert.IsType<CharManageResultEvent>(Assert.Single(Drain(bus)));
        Assert.False(evt.Success);
        Assert.Equal(0xDEADBEEFu, evt.ReadyTime);
    }

    [Fact]
    public void CharManage_3_4_subtype_zero_classifies_generic_refresh()
    {
        var bus = new ClientEventBus(ClientEventBus.Unbounded);
        var world = new ClientWorld();
        var fsm = new ClientStateMachine(bus, ClientState.CharacterSelection);
        var handler = new GamePacketHandler(world, bus, fsm, new CountingUnhandledOpcodeSink());
        var dispatcher = new InboundFrameDispatcher(handler);

        dispatcher.RouteNow(SyntheticFrames.CharManageResult(result: 1, subtype: 0, readyTime: 0));

        var evt = Assert.IsType<CharManageResultEvent>(Assert.Single(Drain(bus)));
        Assert.Equal(CharManageSubtype.GenericRefresh, evt.Subtype);
    }

    // =====================================================================================================
    // 3/6 — rename result
    // =====================================================================================================

    [Fact]
    public void Rename_3_6_success_decodes_new_name()
    {
        var bus = new ClientEventBus(ClientEventBus.Unbounded);
        var world = new ClientWorld();
        var fsm = new ClientStateMachine(bus, ClientState.CharacterSelection);
        var handler = new GamePacketHandler(world, bus, fsm, new CountingUnhandledOpcodeSink());
        var dispatcher = new InboundFrameDispatcher(handler);

        dispatcher.RouteNow(SyntheticFrames.RenameCharResult(result: 1, name: "newhero"));

        var evt = Assert.IsType<CharRenameResultEvent>(Assert.Single(Drain(bus)));
        Assert.True(evt.Success);
        Assert.Equal("newhero", evt.NewName);
        Assert.Equal((byte)0, evt.ErrorCode);
    }

    [Fact]
    public void Rename_3_6_failure_carries_error_code()
    {
        var bus = new ClientEventBus(ClientEventBus.Unbounded);
        var world = new ClientWorld();
        var fsm = new ClientStateMachine(bus, ClientState.CharacterSelection);
        var handler = new GamePacketHandler(world, bus, fsm, new CountingUnhandledOpcodeSink());
        var dispatcher = new InboundFrameDispatcher(handler);

        // Error code in the documented 0xC8..0xD4 range.
        dispatcher.RouteNow(SyntheticFrames.RenameCharResult(result: 0, errorCode: 0xCA));

        var evt = Assert.IsType<CharRenameResultEvent>(Assert.Single(Drain(bus)));
        Assert.False(evt.Success);
        Assert.Equal(string.Empty, evt.NewName);
        Assert.Equal((byte)0xCA, evt.ErrorCode);
    }

    // =====================================================================================================
    // 3/23 — character-create result
    // =====================================================================================================

    [Fact]
    public void Create_3_23_success_increments_account_count_and_reports_slot()
    {
        var bus = new ClientEventBus(ClientEventBus.Unbounded);
        var world = new ClientWorld();
        var fsm = new ClientStateMachine(bus, ClientState.CharacterSelection);
        var account = new AccountCharacterState(initialCount: 1);
        var handler = new GamePacketHandler(
            world, bus, fsm, new CountingUnhandledOpcodeSink(), accountCharacters: account);
        var dispatcher = new InboundFrameDispatcher(handler);

        dispatcher.RouteNow(SyntheticFrames.CharCreateResult(
            result: 1, code: 2, value1: 111, value2: 222));

        Assert.Equal(2, account.CharacterCount);
        var evt = Assert.IsType<CharCreateResultEvent>(Assert.Single(Drain(bus)));
        Assert.True(evt.Success);
        Assert.Equal((byte)2, evt.AssignedSlotId);
        Assert.Equal((byte)0, evt.ErrorCode);
        Assert.Equal(111u, evt.Value1);
        Assert.Equal(222u, evt.Value2);
        Assert.Equal(2, evt.AccountCharacterCount);
    }

    [Fact]
    public void Create_3_23_failure_reports_error_code_and_does_not_increment()
    {
        var bus = new ClientEventBus(ClientEventBus.Unbounded);
        var world = new ClientWorld();
        var fsm = new ClientStateMachine(bus, ClientState.CharacterSelection);
        var account = new AccountCharacterState(initialCount: 1);
        var handler = new GamePacketHandler(
            world, bus, fsm, new CountingUnhandledOpcodeSink(), accountCharacters: account);
        var dispatcher = new InboundFrameDispatcher(handler);

        dispatcher.RouteNow(SyntheticFrames.CharCreateResult(result: 0, code: 0xD4));

        Assert.Equal(1, account.CharacterCount); // unchanged
        var evt = Assert.IsType<CharCreateResultEvent>(Assert.Single(Drain(bus)));
        Assert.False(evt.Success);
        Assert.Equal((byte)0, evt.AssignedSlotId);
        Assert.Equal((byte)0xD4, evt.ErrorCode);
    }

    // =====================================================================================================
    // 1/6 login-blob emit (feature-flagged)
    // =====================================================================================================

    [Fact]
    public async Task Login_with_flag_off_emits_no_1_6_frame()
    {
        var sink = new FakeOutboundSink();
        var bus = new ClientEventBus(ClientEventBus.Unbounded);
        var fsm = new ClientStateMachine(bus, ClientState.Login);
        var credentials = new LoginCredentialStore();
        var useCases = new ApplicationUseCases(
            sink, fsm, new ClientWorld(), credentials, new SessionId(1));

        await useCases.LoginAsync("account", "secret", pin: "1234");

        Assert.True(credentials.HasStagedCredential);
        Assert.Empty(sink.Sends); // default flag off: no 1/6
    }

    [Fact]
    public async Task Login_with_flag_on_emits_1_6_blob_with_subopcode_and_length_prefixes()
    {
        var sink = new FakeOutboundSink();
        var bus = new ClientEventBus(ClientEventBus.Unbounded);
        var fsm = new ClientStateMachine(bus, ClientState.Login);
        var credentials = new LoginCredentialStore();
        var useCases = new ApplicationUseCases(
            sink, fsm, new ClientWorld(), credentials, new SessionId(1),
            emitLoginBlob16: true);

        await useCases.LoginAsync("account", "secret", pin: "1234");

        // Password still goes via the RSA 1/4 reply only — never in the blob.
        Assert.True(credentials.HasStagedCredential);

        var (major, minor, payload) = Assert.Single(sink.Sends);
        Assert.Equal(1, major);
        Assert.Equal(6, minor);

        // [0x2B][u32len account\0]["account"][\0][u32len PIN\0]["1234"][\0]
        Assert.Equal(0x2B, payload[0]);

        int cursor = 1;
        uint accountLen = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(cursor, 4));
        Assert.Equal((uint)("account".Length + 1), accountLen); // length INCLUDES the NUL
        cursor += 4;
        string account = System.Text.Encoding.ASCII.GetString(payload, cursor, "account".Length);
        Assert.Equal("account", account);
        Assert.Equal(0, payload[cursor + "account".Length]); // trailing NUL
        cursor += (int)accountLen;

        uint pinLen = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(cursor, 4));
        Assert.Equal((uint)("1234".Length + 1), pinLen);
        cursor += 4;
        string pin = System.Text.Encoding.ASCII.GetString(payload, cursor, "1234".Length);
        Assert.Equal("1234", pin);
        Assert.Equal(0, payload[cursor + "1234".Length]); // trailing NUL
        cursor += (int)pinLen;

        // The blob is exactly [sub-op][account field][pin field] with no trailing bytes.
        Assert.Equal(cursor, payload.Length);

        // The password ("secret") must NOT appear anywhere in the blob.
        string blobText = System.Text.Encoding.ASCII.GetString(payload);
        Assert.DoesNotContain("secret", blobText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_with_flag_on_and_no_pin_omits_the_optional_pin_field()
    {
        var sink = new FakeOutboundSink();
        var bus = new ClientEventBus(ClientEventBus.Unbounded);
        var fsm = new ClientStateMachine(bus, ClientState.Login);
        var useCases = new ApplicationUseCases(
            sink, fsm, new ClientWorld(), new LoginCredentialStore(), new SessionId(1),
            emitLoginBlob16: true);

        await useCases.LoginAsync("account", "secret", pin: null);

        var (_, _, payload) = Assert.Single(sink.Sends);
        // [0x2B] + [u32 len][account][NUL] only — no PIN field.
        int expected = 1 + 4 + "account".Length + 1;
        Assert.Equal(expected, payload.Length);
    }
}
