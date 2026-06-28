using System;
using MartialHeroes.Explorer.Files.Models;

namespace MartialHeroes.Explorer.Files.Services.Decoders;

public sealed class PlainTextDecoder : IFormatDecoder
{
    public DecodedDocument Decode(VfsFileNode node, ReadOnlyMemory<byte> bytes)
    {
        var text = TextEncodings.Cp949.GetString(bytes.Span);
        var lineCount = 1;
        foreach (var c in text)
            if (c == '\n')
                lineCount++;

        return new TextDocument
        {
            Title = node.Name,
            Summary = $"{lineCount:N0} lines · {bytes.Length:N0} bytes · CP949",
            Text = text
        };
    }
}