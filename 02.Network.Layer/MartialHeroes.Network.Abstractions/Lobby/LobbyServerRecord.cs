namespace MartialHeroes.Network.Abstractions.Lobby;

public readonly record struct LobbyServerRecord(
    short ServerId,
    short StatusCode,
    short Load,
    short OpenTime)
{
    public bool IsSelectable =>
        StatusCode == 0 && Load < 2400;
}