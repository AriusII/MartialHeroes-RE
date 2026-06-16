using Xunit;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Tests for <see cref="ItemsCsvParser"/>, focusing on robustness against the real shipped
/// items.csv which contains occasional ragged rows (a 138-column row was observed at ~29 MB into
/// the real 89,712-row catalogue). A single ragged row must never discard the whole catalogue.
/// spec: Docs/RE/formats/config_tables.md §4.1.
/// </summary>
public sealed class ItemsCsvParserTests
{
    private const int Columns = 139;

    private static string Row(int columnCount, uint id)
    {
        // Build a CSV row with `columnCount` comma-separated fields. col1 = id.
        var parts = new string[columnCount];
        for (int i = 0; i < columnCount; i++) parts[i] = "0";
        parts[0] = $"item{id}";
        if (columnCount > 1) parts[1] = id.ToString();
        return string.Join(',', parts);
    }

    [Fact]
    public void Exact_139_column_row_parses()
    {
        ItemCsvRow[] rows = ItemsCsvParser.ParseText(Row(Columns, 100) + "\n");
        Assert.Single(rows);
        Assert.Equal(100u, rows[0].ItemId);
    }

    [Fact]
    public void Ragged_short_row_is_padded_not_discarded()
    {
        // A 138-column row (one short) must still parse — padded with an empty trailing field.
        // spec: Docs/RE/formats/config_tables.md §4.1 — real data is ragged; never throw.
        ItemCsvRow[] rows = ItemsCsvParser.ParseText(Row(138, 200) + "\n");
        Assert.Single(rows);
        Assert.Equal(200u, rows[0].ItemId);
        Assert.Equal(Columns, rows[0].RawColumns.Length); // padded up to 139
    }

    [Fact]
    public void One_ragged_row_does_not_discard_the_others()
    {
        // The critical regression: previously a single 138-column row threw and the whole
        // catalogue (every other valid row) was lost. Now all three rows load.
        string csv = Row(Columns, 1) + "\n" + Row(138, 2) + "\n" + Row(Columns, 3) + "\n";
        ItemCsvRow[] rows = ItemsCsvParser.ParseText(csv);
        Assert.Equal(3, rows.Length);
        Assert.Equal(1u, rows[0].ItemId);
        Assert.Equal(2u, rows[1].ItemId);
        Assert.Equal(3u, rows[2].ItemId);
    }

    [Fact]
    public void Long_row_keeps_all_columns_and_still_decodes_typed_fields()
    {
        // A row with MORE than 139 columns keeps every column in RawColumns and the typed
        // decode (highest index read is 131) still succeeds.
        // RawColumns layout: [name, id, desc, tail...] = 3 + tailCount.
        // For a 145-column row: col0=name, col1=id, col2..144=numeric tail (143 entries, desc="").
        // rawColumns.Length = 3 + 143 = 146.
        // spec: Docs/RE/formats/items_csv.md §3 -- numeric-anchor split: name+id+desc = first 3 slots.
        ItemCsvRow[] rows = ItemsCsvParser.ParseText(Row(145, 300) + "\n");
        Assert.Single(rows);
        Assert.Equal(300u, rows[0].ItemId);
        Assert.Equal(146, rows[0].RawColumns.Length); // 3 (name+id+empty-desc) + 143 tail = 146
    }
}