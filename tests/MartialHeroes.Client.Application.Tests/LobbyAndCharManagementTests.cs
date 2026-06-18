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
/// the 3/7 / 3/6 / 3/23 character-management result handlers (synthetic frames -&gt; events), and login
/// credential staging (no outbound frame — the credential rides the secure 1/4 reply, 1/6 is
/// character-create only; spec: login_flow.md §4.2).
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
    public async Task FetchServerList_publishes_view_with_load_and_caption_hints()
    {
        // Corrected lobby record model (spec: Docs/RE/packets/lobby.yaml Record Shape A):
        //   +0 ServerId (1..40; == 100 is display-only, NOT a gate), +2 StatusCode (caption/branch
        //   selector, == 0 active), +4 Load (thresholds 500/800/1200), +6 OpenTime (scheduled-open
        //   minute, a time value — NOT a flag). Selectability gate = StatusCode == 0 AND Load < 2400.
        var sink = new FakeOutboundSink();
        var bus = new ClientEventBus(ClientEventBus.Unbounded);
        var fsm = new ClientStateMachine(bus, ClientState.Login);
        var world = new ClientWorld();
        var lobby = new FakeLobbyClient
        {
            ServerList =
            [
                // StatusCode 0 (active): Load 1300 > 1200 -> Full; branch Normal.
                new LobbyServerRecord(ServerId: 1, StatusCode: 0, Load: 1300, OpenTime: 1),
                // StatusCode 0 (active): Load 600 > 500 -> Moderate; branch Normal.
                new LobbyServerRecord(ServerId: 2, StatusCode: 0, Load: 600, OpenTime: 0),
                // StatusCode 3: scheduled-open branch. Load = hour, OpenTime = minute.
                new LobbyServerRecord(ServerId: 3, StatusCode: 3, Load: 14, OpenTime: 30),
                // ServerId == 100: display-only label, NOT a gate. StatusCode 0 -> Normal.
                new LobbyServerRecord(ServerId: 100, StatusCode: 0, Load: 100, OpenTime: 1),
                // StatusCode 5 (in 1..39): caption-array branch.
                new LobbyServerRecord(ServerId: 5, StatusCode: 5, Load: 0, OpenTime: 0),
                // StatusCode -1 (< 1): fallback 5901 -> Invalid.
                new LobbyServerRecord(ServerId: 6, StatusCode: -1, Load: 0, OpenTime: 0),
            ],
        };
        var useCases = new ApplicationUseCases(
            sink, fsm, world, new LoginCredentialStore(), new SessionId(1),
            eventBus: bus, lobbyClient: lobby);

        IReadOnlyList<LobbyServerRecord> raw = await useCases.FetchServerListAsync();

        Assert.Equal(6, raw.Count);
        var evt = Assert.IsType<ServerListReceivedEvent>(Assert.Single(Drain(bus)));
        ImmutableArray<ServerListEntryView> v = evt.Servers;
        Assert.Equal(6, v.Length);

        // Load band (> 1200 -> Full); StatusCode 0 -> Normal.
        Assert.Equal(ServerLoadBand.Full, v[0].LoadHint);
        Assert.Equal(ServerStatusHint.Normal, v[0].StatusHint);

        // Load band (> 500 -> Moderate); StatusCode 0 -> Normal.
        Assert.Equal(ServerLoadBand.Moderate, v[1].LoadHint);
        Assert.Equal(ServerStatusHint.Normal, v[1].StatusHint);

        // StatusCode == 3 -> Special (scheduled-open); the wire Load/OpenTime are forwarded verbatim.
        Assert.Equal(ServerStatusHint.Special, v[2].StatusHint);
        Assert.Equal((short)14, v[2].Load);
        Assert.Equal((short)30, v[2].OpenTime);

        // ServerId == 100 is NOT a gate -> StatusCode 0 still classifies as Normal.
        Assert.Equal(ServerStatusHint.Normal, v[3].StatusHint);

        // StatusCode in 1..39 -> Caption branch.
        Assert.Equal(ServerStatusHint.Caption, v[4].StatusHint);

        // StatusCode < 1 -> Invalid (fallback 5901).
        Assert.Equal(ServerStatusHint.Invalid, v[5].StatusHint);
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

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await useCases.FetchServerListAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await useCases.SelectServerAsync(1));
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
    // Login credential staging (no outbound frame — the credential rides the secure 1/4 reply)
    // =====================================================================================================

    [Fact]
    public async Task Login_stages_credential_and_emits_no_frame()
    {
        // The former feature-flagged 1/6 "login blob" was removed: the 1/6-vs-credential collision is
        // RESOLVED — the credential (0x2B pre-image + RSA password) rides the secure 1/4 reply built by
        // the login handshake driver, and 1/6 is character-create only. LoginAsync only STAGES the
        // credential and emits nothing on the wire. spec: Docs/RE/specs/login_flow.md §4.2.
        var sink = new FakeOutboundSink();
        var bus = new ClientEventBus(ClientEventBus.Unbounded);
        var fsm = new ClientStateMachine(bus, ClientState.Login);
        var credentials = new LoginCredentialStore();
        var useCases = new ApplicationUseCases(
            sink, fsm, new ClientWorld(), credentials, new SessionId(1));

        await useCases.LoginAsync("account", "secret", pin: "1234");

        Assert.True(credentials.HasStagedCredential);
        Assert.Empty(sink.Sends); // no outbound frame: the credential rides the later 1/4 reply
    }
}