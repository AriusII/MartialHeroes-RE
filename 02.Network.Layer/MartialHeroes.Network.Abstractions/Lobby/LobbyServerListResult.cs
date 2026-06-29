namespace MartialHeroes.Network.Abstractions.Lobby;

public enum LobbyServerListOutcome
{
    Empty,

    Failed,

    Populated
}

public readonly record struct LobbyServerListResult(
    LobbyServerListOutcome Outcome,
    IReadOnlyList<LobbyServerRecord> Records)
{
    public static LobbyServerListResult Empty { get; } =
        new(LobbyServerListOutcome.Empty, []);

    public static LobbyServerListResult Failed { get; } =
        new(LobbyServerListOutcome.Failed, []);

    public static LobbyServerListResult Populated(IReadOnlyList<LobbyServerRecord> records)
    {
        return new LobbyServerListResult(LobbyServerListOutcome.Populated, records);
    }
}