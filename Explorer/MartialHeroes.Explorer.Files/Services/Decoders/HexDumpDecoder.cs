using System;
using System.Text;
using MartialHeroes.Explorer.Files.Models;

namespace MartialHeroes.Explorer.Files.Services.Decoders;

public sealed class HexDumpDecoder : IFormatDecoder
{
    private const int BytesPerLine = 16;
    private const int MaxBytes = 256 * 1024;

    public DecodedDocument Decode(VfsFileNode node, ReadOnlyMemory<byte> bytes)
    {
        var span = bytes.Span;
        var shown = Math.Min(span.Length, MaxBytes);
        var sb = new StringBuilder(shown * 4);

        for (var offset = 0; offset < shown; offset += BytesPerLine)
        {
            sb.Append(offset.ToString("X8"));
            sb.Append("  ");

            for (var i = 0; i < BytesPerLine; i++)
            {
                if (offset + i < shown)
                    sb.Append(span[offset + i].ToString("X2")).Append(' ');
                else
                    sb.Append("   ");
                if (i == 7) sb.Append(' ');
            }

            sb.Append(' ');
            for (var i = 0; i < BytesPerLine && offset + i < shown; i++)
            {
                var b = span[offset + i];
                sb.Append(b >= 0x20 && b < 0x7F ? (char)b : '.');
            }

            sb.Append('\n');
        }

        if (span.Length > shown)
            sb.Append($"\n… {span.Length - shown:N0} more bytes not shown (capped at {MaxBytes / 1024} KB).");

        return new TextDocument
        {
            Title = node.Name,
            Summary = $"{span.Length:N0} bytes · binary hex dump",
            Text = sb.ToString()
        };
    }
}