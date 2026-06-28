using System;
using MartialHeroes.Explorer.Files.Models;

namespace MartialHeroes.Explorer.Files.Services.Decoders;

internal static class DecoderFallback
{
    public static DecodedDocument AsText(
        VfsFileNode node,
        ReadOnlyMemory<byte> bytes,
        Exception error,
        HexDumpDecoder hex)
    {
        var dump = hex.Decode(node, bytes);
        return new TextDocument
        {
            Title = node.Name,
            Summary = $"decode failed ({error.Message}) · showing raw bytes",
            Text = dump is TextDocument t ? t.Text : string.Empty
        };
    }
}