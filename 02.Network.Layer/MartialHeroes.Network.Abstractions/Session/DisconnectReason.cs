namespace MartialHeroes.Network.Abstractions.Session;

public enum DisconnectReason : byte
{
    None = 0,

    LocalClose = 1,

    RemoteClose = 2,

    ServerKick = 3,

    NetworkError = 4,

    FramingError = 5,

    CryptoError = 6,

    Timeout = 7
}