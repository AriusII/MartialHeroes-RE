using System;
using System.Collections.Generic;
using MartialHeroes.Assets.Parsers.DataTables;
using MartialHeroes.Explorer.Files.Models;

namespace MartialHeroes.Explorer.Files.Services.Decoders;

public sealed class MsgXdbDecoder : IFormatDecoder
{
    private static readonly HexDumpDecoder Fallback = new();

    public DecodedDocument Decode(VfsFileNode node, ReadOnlyMemory<byte> bytes)
    {
        try
        {
            var catalog = MsgXdbParser.Parse(bytes);
            var rows = new List<TableRow>(catalog.Records.Count);
            for (var i = 0; i < catalog.Records.Count; i++)
            {
                var record = catalog.Records[i];
                rows.Add(new TableRow
                {
                    Cells = [(i + 1).ToString(), ((uint)record.CaptionId).ToString(), record.Text]
                });
            }

            return new TableDocument
            {
                Title = node.Name,
                Summary = $"{rows.Count:N0} message records · 516-byte stride · CP949",
                Columns = ["#", "caption id", "text"],
                Rows = rows
            };
        }
        catch (Exception ex)
        {
            return DecoderFallback.AsText(node, bytes, ex, Fallback);
        }
    }
}