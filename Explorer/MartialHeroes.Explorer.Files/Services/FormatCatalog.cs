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
                Id = "text", Title = "Text Tables", Glyph = "▦", Order = 0,
                Description = "TAB-delimited CP949 data tables (skin, actormotion, bgtexture, …).",
                Extensions = [".txt"], Decoder = tabDelimited
            },
            new FormatFamily
            {
                Id = "messages", Title = "Messages (.xdb)", Glyph = "✉", Order = 1,
                Description = "Localised message catalogues — 516-byte records.",
                Extensions = [".xdb"], Decoder = new MsgXdbDecoder()
            },
            new FormatFamily
            {
                Id = "items", Title = "Items (.csv)", Glyph = "≣", Order = 2,
                Description = "Comma-separated item / config rows (CP949).",
                Extensions = [".csv"], Decoder = commaDelimited
            },
            new FormatFamily
            {
                Id = "stances", Title = "Stances (.do)", Glyph = "⚐", Order = 3,
                Description = "Per-class stance icon tables — 116-byte records.",
                Extensions = [".do"], Decoder = new DoStanceDecoder()
            },
            new FormatFamily
            {
                Id = "scripts", Title = "Scripts (.scr)", Glyph = "❯", Order = 4,
                Description = "Script tables — TAB text where readable, hex otherwise.",
                Extensions = [".scr"], Decoder = tabDelimited
            },
            new FormatFamily
            {
                Id = "shaders", Title = "Shaders", Glyph = "✦", Order = 5,
                Description = "D3D9 vertex / pixel shader source (ASCII).",
                Extensions = [".vsh", ".psh"], Decoder = plainText
            },
            new FormatFamily
            {
                Id = "config", Title = "Config & Lists", Glyph = "☰", Order = 6,
                Description = "Configuration files and manifest lists.",
                Extensions = [".ini", ".cfg", ".lst"], Decoder = universal
            }
        ];

        AllFiles = new FormatFamily
        {
            Id = "all", Title = "All Files", Glyph = "✱", Order = 999,
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