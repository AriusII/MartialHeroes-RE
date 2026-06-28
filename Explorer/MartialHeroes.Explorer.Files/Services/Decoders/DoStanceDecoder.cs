using System;
using System.Collections.Generic;
using MartialHeroes.Assets.Parsers.DataTables;
using MartialHeroes.Explorer.Files.Models;

namespace MartialHeroes.Explorer.Files.Services.Decoders;

public sealed class DoStanceDecoder : IFormatDecoder
{
    private static readonly HexDumpDecoder Fallback = new();

    public DecodedDocument Decode(VfsFileNode node, ReadOnlyMemory<byte> bytes)
    {
        try
        {
            var table = DoStanceParser.Parse(bytes);
            var rows = new List<TableRow>(table.Records.Count);
            for (var i = 0; i < table.Records.Count; i++)
            {
                var r = table.Records[i];
                rows.Add(new TableRow
                {
                    Cells =
                    [
                        (i + 1).ToString(),
                        r.InstanceKey.ToString(),
                        r.GroupId.ToString(),
                        r.GroupSubIndex.ToString(),
                        r.SlotIndex.ToString(),
                        r.ClassStanceRef.ToString(),
                        $"{r.IconSrcX},{r.IconSrcY}",
                        $"{r.SecondarySpriteX},{r.SecondarySpriteY}"
                    ]
                });
            }

            return new TableDocument
            {
                Title = node.Name,
                Summary =
                    $"{table.TotalRecordCount:N0} stance records · 116-byte stride · trailing {table.TrailingByteCount} B",
                Columns =
                [
                    "#", "instance key", "group", "sub", "slot",
                    "class ref", "icon src x,y", "sprite x,y"
                ],
                Rows = rows
            };
        }
        catch (Exception ex)
        {
            return DecoderFallback.AsText(node, bytes, ex, Fallback);
        }
    }
}