using System;
using MartialHeroes.Explorer.Files.Models;

namespace MartialHeroes.Explorer.Files.Services.Decoders;

public sealed class UniversalDecoder : IFormatDecoder
{
    private readonly DelimitedTextDecoder _delimited = new(SeparatorMode.Auto);

    public DecodedDocument Decode(VfsFileNode node, ReadOnlyMemory<byte> bytes)
    {
        return _delimited.Decode(node, bytes);
    }
}
