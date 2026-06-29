using System;
using System.Collections.Generic;
using MartialHeroes.Assets.Parsers.DataTables;
using MartialHeroes.Assets.Parsers.DataTables.Models;
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
                        r.StanceFilter.ToString(),
                        r.SlotIndex.ToString(),
                        r.ClassStanceRef.ToString(),
                        $"{r.WidgetPosX},{r.WidgetPosYRaw}",
                        $"{r.IconSrcX},{r.IconSrcY}",
                        $"{r.LevelBarSrcX},{r.LevelBarSrcY}",
                        DescribeOverlays(r.Overlay0, r.Overlay1, r.Overlay2)
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
                    "#", "instance key", "filter", "slot", "class ref",
                    "widget x,y", "icon src x,y", "levelbar x,y", "overlays"
                ],
                Rows = rows
            };
        }
        catch (Exception ex)
        {
            return DecoderFallback.AsText(node, bytes, ex, Fallback);
        }
    }

    private static string DescribeOverlays(
        DoStanceOverlay o0, DoStanceOverlay o1, DoStanceOverlay o2)
    {
        var parts = new List<string>(3);
        Append(parts, 0, o0);
        Append(parts, 1, o1);
        Append(parts, 2, o2);
        return parts.Count == 0 ? "-" : string.Join("  ", parts);
    }

    private static void Append(List<string> parts, int index, DoStanceOverlay overlay)
    {
        if (!overlay.Present)
            return;
        parts.Add(
            $"#{index} +{overlay.Dx},{overlay.Dy} src {overlay.SrcX},{overlay.SrcY} {overlay.Width}×{overlay.Height}");
    }
}