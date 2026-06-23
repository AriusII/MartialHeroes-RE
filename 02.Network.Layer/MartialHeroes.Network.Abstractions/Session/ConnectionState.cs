namespace MartialHeroes.Network.Abstractions.Session;

public enum ConnectionState : byte
{
    Disconnected = 0,

    Connecting = 1,

    Handshaking = 2,

    Authenticated = 3,

    InWorld = 4,

    Faulted = 5
}