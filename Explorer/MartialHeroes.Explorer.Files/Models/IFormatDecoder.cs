using System;

namespace MartialHeroes.Explorer.Files.Models;

public interface IFormatDecoder
{
    DecodedDocument Decode(VfsFileNode node, ReadOnlyMemory<byte> bytes);
}