using System;
using System.Collections.Generic;
using MartialHeroes.Explorer.Files.Models;
using MartialHeroes.Explorer.Files.Services.Decoders;

namespace MartialHeroes.Explorer.Files.Services;

public sealed class FormatCatalog
{
    public FormatCatalog()
    {
        var tabDelimited = new DelimitedTextDecoder(SeparatorMode.Tab);
        var commaDelimited = new DelimitedTextDecoder(SeparatorMode.Comma);
        var plainText = new PlainTextDecoder();
        var universal = new UniversalDecoder();

        Families =
        [
            new FormatFamily
            {
                Id = "text", Title = "Text Tables", Glyph = "▦",
                Description = "TAB-delimited CP949 data tables (skin, actormotion, bgtexture, …).",
                Extensions = [".txt"], Decoder = tabDelimited
            },
            new FormatFamily
            {
                Id = "messages", Title = "Catalogues (.xdb)", Glyph = "✉",
                Description = "Binary table catalogues — msg / effectscale / buff-icon / vehicle / creature.",
                Extensions = [".xdb"], Decoder = new XdbDecoder()
            },
            new FormatFamily
            {
                Id = "items", Title = "Items (.csv)", Glyph = "≣",
                Description = "Comma-separated item / config rows (CP949).",
                Extensions = [".csv"], Decoder = commaDelimited
            },
            new FormatFamily
            {
                Id = "stances", Title = "Tables (.do)", Glyph = "⚐",
                Description = "Binary .do tables — 12 stance hotbars, textcommand, emoticon, msginfo, items_extra.",
                Extensions = [".do"], Decoder = new DoTableDecoder()
            },
            new FormatFamily
            {
                Id = "scripts", Title = "Scripts (.scr)", Glyph = "❯",
                Description = "Binary fixed-stride record tables — per-file parser, hex fallback.",
                Extensions = [".scr"], Decoder = new ScrDecoder()
            },
            new FormatFamily
            {
                Id = "shaders", Title = "Shaders", Glyph = "✦",
                Description = "D3D9 vertex / pixel shader source (ASCII).",
                Extensions = [".vsh", ".psh"], Decoder = plainText
            },
            new FormatFamily
            {
                Id = "lists", Title = "Lists (.lst)", Glyph = "≡",
                Description = "Manifest lists — binary bgtexture / xobj / bmplist / xeffect indexes, text otherwise.",
                Extensions = [".lst"], Decoder = new LstDecoder()
            },
            new FormatFamily
            {
                Id = "config", Title = "Config", Glyph = "☰",
                Description = "Configuration files (INI / CFG).",
                Extensions = [".ini", ".cfg"], Decoder = universal
            }
        ];

        AllFiles = new FormatFamily
        {
            Id = "all", Title = "All Files", Glyph = "✱",
            Description = "Every VFS entry — auto text/hex preview.",
            Extensions = [], ExtraMatch = static _ => true, Decoder = universal
        };
    }

    public IReadOnlyList<FormatFamily> Families { get; }

    public FormatFamily AllFiles { get; }

    public FormatFamily ResolveFamily(VfsFileNode node)
    {
        foreach (var family in Families)
            if (family.Matches(node))
                return family;

        return AllFiles;
    }

    public DecodedDocument Decode(VfsFileNode node, ReadOnlyMemory<byte> bytes)
    {
        return ResolveFamily(node).Decoder.Decode(node, bytes);
    }
}