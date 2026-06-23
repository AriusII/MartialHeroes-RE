namespace MartialHeroes.Assets.Parsers.DataTables.Models;


public sealed class EventsScrRecord
{

    public required uint EventId { get; init; }

    public required ushort ModeFlag { get; init; }

    public required IReadOnlyList<uint> RateArray { get; init; }

    public required IReadOnlyList<uint> ActorArray { get; init; }


    public required ushort EventType { get; init; }

    public required ushort DayCount { get; init; }

    public required ReadOnlyMemory<byte> Raw { get; init; }
}