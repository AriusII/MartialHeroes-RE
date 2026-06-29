using System.Collections.Generic;

namespace MartialHeroes.Explorer.Files.Models;

public abstract class DecodedDocument
{
    public required string Title { get; init; }

    public required string Summary { get; init; }
}

public sealed class TextDocument : DecodedDocument
{
    public required string Text { get; init; }
}

public sealed class TableDocument : DecodedDocument
{
    public required IReadOnlyList<string> Columns { get; init; }

    public required IReadOnlyList<TableRow> Rows { get; init; }
}

public sealed class TableRow
{
    public required IReadOnlyList<string> Cells { get; init; }

    public string this[int index] => index >= 0 && index < Cells.Count ? Cells[index] : string.Empty;
}