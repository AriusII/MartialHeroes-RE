namespace MartialHeroes.Network.Abstractions.Lobby;

public interface ILobbyClient
{
    Task<IReadOnlyList<LobbyServerRecord>> FetchServerListAsync(
        CancellationToken cancellationToken = default);

    Task<LobbyChannelEndpoint> FetchChannelEndpointAsync(
        ushort serverId,
        CancellationToken cancellationToken = default);
}