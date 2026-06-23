namespace MartialHeroes.Assets.Parsers.DataTables.Models;

public sealed record GameVerData
{
    public required uint Field0 { get; init; }

    public required uint Field1 { get; init; }

    public required uint Field2 { get; init; }

    public required uint Field3 { get; init; }

    public required uint Field4 { get; init; }

    public required uint VersionSourceField { get; init; }

    public required uint Field6 { get; init; }

    public uint EnterGameVersionToken => 10u * VersionSourceField + 9u;
}