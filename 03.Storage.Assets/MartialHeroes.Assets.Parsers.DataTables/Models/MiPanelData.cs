namespace MartialHeroes.Assets.Parsers.DataTables.Models;

public sealed class MiWidgetRecord
{
    public required uint WidgetId { get; init; }

    public required uint FieldA0 { get; init; }

    public required uint FieldA1 { get; init; }

    public required uint FieldKind { get; init; }

    public required uint FieldB0 { get; init; }

    public required uint FieldB1 { get; init; }

    public required uint FieldLink { get; init; }
}

public sealed class MiPanelData
{
    public const int RecordStride = 28;

    public const int FieldsPerRecord = 7;

    public required uint RecordCount { get; init; }

    public required MiWidgetRecord[] Records { get; init; }
}