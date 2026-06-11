using MartialHeroes.Client.Application.Events;
using MartialHeroes.Client.Application.StateMachine;
using MartialHeroes.Network.Abstractions.Session;
using Xunit;

namespace MartialHeroes.Client.Application.Tests;

public sealed class ClientStateMachineTests
{
    [Fact]
    public void Full_happy_path_walks_login_to_world()
    {
        var bus = new ClientEventBus(ClientEventBus.Unbounded);
        var fsm = new ClientStateMachine(bus, ClientState.Login);

        Assert.True(fsm.OnAuthenticated());
        Assert.Equal(ClientState.CharacterSelection, fsm.Current);

        Assert.True(fsm.OnCharacterSelected());
        Assert.Equal(ClientState.Loading, fsm.Current);

        Assert.True(fsm.OnEnterWorld());
        Assert.Equal(ClientState.World, fsm.Current);
    }

    [Fact]
    public void Illegal_transition_is_rejected_without_event()
    {
        var bus = new ClientEventBus(ClientEventBus.Unbounded);
        var fsm = new ClientStateMachine(bus, ClientState.Login);

        // EnterWorld is not valid from Login.
        Assert.False(fsm.OnEnterWorld());
        Assert.Equal(ClientState.Login, fsm.Current);
        Assert.False(bus.Reader.TryRead(out _));
    }

    [Fact]
    public void Disconnect_collapses_from_any_state_and_is_idempotent()
    {
        var bus = new ClientEventBus(ClientEventBus.Unbounded);
        var fsm = new ClientStateMachine(bus, ClientState.World);

        Assert.True(fsm.OnDisconnected());
        Assert.Equal(ClientState.Disconnected, fsm.Current);
        Assert.False(fsm.OnDisconnected()); // idempotent, no second event
    }

    [Theory]
    [InlineData(ClientState.Login, ConnectionState.Handshaking)]
    [InlineData(ClientState.CharacterSelection, ConnectionState.Authenticated)]
    [InlineData(ClientState.Loading, ConnectionState.Authenticated)]
    [InlineData(ClientState.World, ConnectionState.InWorld)]
    [InlineData(ClientState.Disconnected, ConnectionState.Disconnected)]
    public void Maps_to_transport_connection_state(ClientState state, ConnectionState expected) =>
        Assert.Equal(expected, ClientStateMachine.ToConnectionState(state));
}