using System;
using System.Collections.Generic;
using MartialHeroes.Assets.Parsers.Effects;
using MartialHeroes.Assets.Parsers.Effects.Models;
using MartialHeroes.Assets.Parsers.Texture;
using MartialHeroes.Explorer.Files.Models;

namespace MartialHeroes.Explorer.Files.Services.Decoders;

public sealed class LstDecoder : IFormatDecoder
{
    private const int MaxRows = 100_000;

    private static readonly HexDumpDecoder Fallback = new();
    private static readonly UniversalDecoder Text = new();

    public DecodedDocument Decode(VfsFileNode node, ReadOnlyMemory<byte> bytes)
    {
        try
        {
            if (Is(node, "bgtexture.lst"))
                return Bgtexture(node, bytes);
            if (Is(node, "xobj.lst"))
                return Names(node, bytes, XobjLstParser.Parse(bytes), "object", 34);
            if (Is(node, "bmplist.lst"))
                return Names(node, bytes, BmplistLstParser.Parse(bytes), "bitmap", 30);
            if (Is(node, "xeffect.lst"))
                return Names(node, bytes, XeffectLstParser.Parse(bytes), "effect", 30);

            return Text.Decode(node, bytes);
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

    private static DecodedDocument Bgtexture(VfsFileNode node, ReadOnlyMemory<byte> bytes)
    {
        var catalog = BgtextureLstParser.Parse(bytes);
        var rows = new List<TableRow>(catalog.Records.Count);
        for (var i = 0; i < catalog.Records.Count && i < MaxRows; i++)
        {
            var r = catalog.Records[i];
            rows.Add(new TableRow
            {
                Cells =
                [
                    r.Index.ToString(),
                    r.KindRaw.ToString(),
                    r.KindEnum.ToString(),
                    r.RelPath
                ]
            });
        }

        return new TableDocument
        {
            Title = node.Name,
            Summary = $"{catalog.Count:N0} background-texture records · u32 count + 48-byte records · CP949",
            Columns = ["index", "kind", "kind name", "rel path"],
            Rows = rows
        };
    }

    private static DecodedDocument Names(
        VfsFileNode node, ReadOnlyMemory<byte> bytes, EffectNameManifest manifest, string label, int stride)
    {
        var rows = new List<TableRow>(manifest.Count);
        for (var i = 0; i < manifest.Entries.Count && i < MaxRows; i++)
        {
            var e = manifest.Entries[i];
            rows.Add(new TableRow { Cells = [e.Index.ToString(), e.Name] });
        }

        return new TableDocument
        {
            Title = node.Name,
            Summary = $"{manifest.Count:N0} {label} names · u32 count + {stride}-byte records · CP949",
            Columns = ["index", "name"],
            Rows = rows
        };
    }
}