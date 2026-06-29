using System;
using System.Collections.Generic;
using MartialHeroes.Assets.Parsers.DataTables;
using MartialHeroes.Explorer.Files.Models;

namespace MartialHeroes.Explorer.Files.Services.Decoders;

public sealed class DoTableDecoder : IFormatDecoder
{
    private const int MaxRows = 100_000;

    private static readonly HexDumpDecoder Fallback = new();
    private static readonly DoStanceDecoder Stance = new();

    private static readonly HashSet<string> StanceFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "musajung.do", "musasa.do", "musama.do",
        "assasinjung.do", "assasinsa.do", "assasinma.do",
        "wizardjung.do", "wizardsa.do", "wizardma.do",
        "monkjung.do", "monksa.do", "monkma.do"
    };

    public DecodedDocument Decode(VfsFileNode node, ReadOnlyMemory<byte> bytes)
    {
        if (StanceFiles.Contains(node.Name))
            return Stance.Decode(node, bytes);

        try
        {
            if (Is(node, "textcommand.do"))
                return TextCommand(node, bytes);
            if (Is(node, "emoticon.do"))
                return Emoticon(node, bytes);
            if (Is(node, "msginfo.do"))
                return MsgInfo(node, bytes);
            if (Is(node, "items_extra.do"))
                return ItemsExtra(node, bytes);

            return Fallback.Decode(node, bytes);
        }
        catch (Exception ex)
        {
            return DecoderFallback.AsText(node, bytes, ex, Fallback);
        }
    }

    private static bool Is(VfsFileNode node, string name)
    {
        return string.Equals(node.Name, name, StringComparison.OrdinalIgnoreCase);
    }

    private static DecodedDocument TextCommand(VfsFileNode node, ReadOnlyMemory<byte> bytes)
    {
        var records = DoTableParser.ParseTextCommandDo(bytes);
        var rows = new List<TableRow>(records.Length);
        for (var i = 0; i < records.Length && i < MaxRows; i++)
        {
            var r = records[i];
            rows.Add(new TableRow
            {
                Cells =
                [
                    (i + 1).ToString(),
                    r.CommandId.ToString(),
                    r.CommandName,
                    r.ArgumentFlag.ToString(),
                    r.SubCommandId.ToString()
                ]
            });
        }

        return new TableDocument
        {
            Title = node.Name,
            Summary = $"{records.Length:N0} chat-command records · 52-byte stride · CP949",
            Columns = ["#", "command id", "name", "arg flag", "sub command"],
            Rows = rows
        };
    }

    private static DecodedDocument Emoticon(VfsFileNode node, ReadOnlyMemory<byte> bytes)
    {
        var records = DoTableParser.ParseEmoticonDo(bytes);
        var rows = new List<TableRow>(records.Length);
        for (var i = 0; i < records.Length && i < MaxRows; i++)
        {
            var r = records[i];
            rows.Add(new TableRow
            {
                Cells =
                [
                    (i + 1).ToString(),
                    r.EmoteId.ToString(),
                    r.CategoryFlag.ToString(),
                    r.SecondaryKey.ToString(),
                    r.ActionLink.ToString(),
                    $"{r.DstX},{r.DstY}",
                    $"{r.GlyphSrcX},{r.GlyphSrcY}",
                    $"{r.LabelSrcX},{r.LabelSrcY}"
                ]
            });
        }

        return new TableDocument
        {
            Title = node.Name,
            Summary = $"{records.Length:N0} emoticon records · 40-byte stride",
            Columns =
            [
                "#", "emote id", "category", "secondary key", "action link",
                "dst x,y", "glyph src x,y", "label src x,y"
            ],
            Rows = rows
        };
    }

    private static DecodedDocument MsgInfo(VfsFileNode node, ReadOnlyMemory<byte> bytes)
    {
        var records = DoTableParser.ParseMsgInfoDo(bytes);
        var rows = new List<TableRow>(records.Length);
        for (var i = 0; i < records.Length && i < MaxRows; i++)
        {
            var r = records[i];
            rows.Add(new TableRow
            {
                Cells =
                [
                    (i + 1).ToString(),
                    r.MessageId.ToString(),
                    r.DialogFlag.ToString(),
                    r.TextLine1,
                    r.TextLine2
                ]
            });
        }

        return new TableDocument
        {
            Title = node.Name,
            Summary = $"{records.Length:N0} popup-message records · 128-byte stride · CP949",
            Columns = ["#", "message id", "dialog flag", "line 1", "line 2"],
            Rows = rows
        };
    }

    private static DecodedDocument ItemsExtra(VfsFileNode node, ReadOnlyMemory<byte> bytes)
    {
        var records = DoTableParser.ParseItemsExtraDo(bytes);
        var shown = Math.Min(records.Length, MaxRows);
        var rows = new List<TableRow>(shown);
        for (var i = 0; i < shown; i++)
        {
            var r = records[i];
            rows.Add(new TableRow
            {
                Cells =
                [
                    (i + 1).ToString(),
                    r.ItemId.ToString(),
                    r.IsSentinel ? "sentinel" : string.Empty,
                    r.AnimScale.ToString("0.###"),
                    $"{r.AttachX},{r.AttachY},{r.AttachZ}",
                    $"{r.RotXDeg},{r.RotYDeg},{r.RotZDeg}",
                    r.RarityTier.ToString()
                ]
            });
        }

        var summary = $"{records.Length:N0} item-extra records · 48-byte stride";
        if (records.Length > shown)
            summary += $" · capped (showing {shown:N0})";

        return new TableDocument
        {
            Title = node.Name,
            Summary = summary,
            Columns =
            [
                "#", "item id", "flag", "anim scale", "attach x,y,z", "rot x,y,z", "rarity tier"
            ],
            Rows = rows
        };
    }
}