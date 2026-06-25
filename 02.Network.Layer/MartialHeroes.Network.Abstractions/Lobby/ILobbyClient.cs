namespace MartialHeroes.Network.Abstractions.Lobby;

public interface ILobbyClient
{
    Task<LobbyServerListResult> FetchServerListAsync(
        CancellationToken cancellationToken = default);

    Task<LobbyChannelEndpoint> FetchChannelEndpointAsync(
        ushort serverId,
        CancellationToken cancellationToken = default);
}