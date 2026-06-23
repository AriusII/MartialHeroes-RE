namespace MartialHeroes.Assets.Parsers.DataTables.Models;


public sealed class ChatFilterEntry
{
    public required string BadWord { get; init; }

    public required string Replacement { get; init; }
}