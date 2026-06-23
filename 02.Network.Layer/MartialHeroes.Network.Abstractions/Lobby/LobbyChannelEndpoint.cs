namespace MartialHeroes.Network.Abstractions.Lobby;

public sealed record LobbyChannelEndpoint(string Host, int Port)
{
    public override string ToString()
    {
        return $"{Host}:{Port}";
    }
}