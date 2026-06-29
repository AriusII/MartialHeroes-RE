using System;
using System.Collections.Generic;
using MartialHeroes.Assets.Parsers.DataTables;
using MartialHeroes.Explorer.Files.Models;

namespace MartialHeroes.Explorer.Files.Services.Decoders;

public sealed class XdbDecoder : IFormatDecoder
{
    private const int MaxRows = 100_000;

    private static readonly HexDumpDecoder Fallback = new();
    private static readonly MsgXdbDecoder Messages = new();

    public DecodedDocument Decode(VfsFileNode node, ReadOnlyMemory<byte> bytes)
    {
        if (Is(node, "msg.xdb"))
            return Messages.Decode(node, bytes);

        try
        {
            if (Is(node, "effectscale.xdb"))
                return EffectScale(node, bytes);
            if (Is(node, "buff_icon_position.xdb"))
                return BuffIcon(node, bytes);
            if (Is(node, "vehicle.xdb"))
                return Vehicle(node, bytes);
            if (Is(node, "creature_item.xdb"))
                return CreatureItem(node, bytes);

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

    private static DecodedDocument EffectScale(VfsFileNode node, ReadOnlyMemory<byte> bytes)
    {
        var records = XdbParser.ParseEffectScaleXdb(bytes);
        var rows = new List<TableRow>(records.Length);
        for (var i = 0; i < records.Length && i < MaxRows; i++)
        {
            var r = records[i];
            rows.Add(new TableRow
            {
                Cells = [(i + 1).ToString(), r.ObjectId.ToString(), r.Scale.ToString("0.###")]
            });
        }

        return new TableDocument
        {
            Title = node.Name,
            Summary = $"{records.Length:N0} effect-scale records · 8-byte stride",
            Columns = ["#", "object id", "scale"],
            Rows = rows
        };
    }

    private static DecodedDocument BuffIcon(VfsFileNode node, ReadOnlyMemory<byte> bytes)
    {
        var records = XdbParser.ParseBuffIconPositionXdb(bytes);
        var rows = new List<TableRow>(records.Length);
        for (var i = 0; i < records.Length && i < MaxRows; i++)
        {
            var r = records[i];
            rows.Add(new TableRow
            {
                Cells = [(i + 1).ToString(), r.BuffId.ToString(), $"{r.AtlasX},{r.AtlasY}"]
            });
        }

        return new TableDocument
        {
            Title = node.Name,
            Summary = $"{records.Length:N0} buff-icon records · 12-byte stride",
            Columns = ["#", "buff id", "atlas x,y"],
            Rows = rows
        };
    }

    private static DecodedDocument Vehicle(VfsFileNode node, ReadOnlyMemory<byte> bytes)
    {
        var records = XdbParser.ParseVehicleXdb(bytes);
        var rows = new List<TableRow>(records.Length);
        for (var i = 0; i < records.Length && i < MaxRows; i++)
        {
            var r = records[i];
            rows.Add(new TableRow
            {
                Cells =
                [
                    (i + 1).ToString(),
                    r.VehicleId.ToString(),
                    r.ItemId.ToString(),
                    r.TagA.ToString(),
                    r.TagB.ToString(),
                    $"{r.Param0:0.##},{r.Param1:0.##},{r.Param2:0.##},{r.Param3:0.##},{r.Param4:0.##}"
                ]
            });
        }

        return new TableDocument
        {
            Title = node.Name,
            Summary = $"{records.Length:N0} vehicle records · 52-byte stride",
            Columns = ["#", "vehicle id", "item id", "tag a", "tag b", "params"],
            Rows = rows
        };
    }

    private static DecodedDocument CreatureItem(VfsFileNode node, ReadOnlyMemory<byte> bytes)
    {
        var records = XdbParser.ParseCreatureItemXdb(bytes);
        var rows = new List<TableRow>(records.Length);
        for (var i = 0; i < records.Length && i < MaxRows; i++)
        {
            var r = records[i];
            rows.Add(new TableRow
            {
                Cells =
                [
                    (i + 1).ToString(),
                    r.CreatureKey.ToString(),
                    r.ItemId.ToString(),
                    r.ScaleOrRadius.ToString("0.###"),
                    r.VisualScale.ToString("0.###"),
                    r.TickInterval.ToString()
                ]
            });
        }

        return new TableDocument
        {
            Title = node.Name,
            Summary = $"{records.Length:N0} creature-item records · 48-byte stride",
            Columns = ["#", "creature key", "item id", "scale/radius", "visual scale", "tick interval"],
            Rows = rows
        };
    }
}