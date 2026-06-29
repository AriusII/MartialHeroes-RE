using System;
using System.Collections.Generic;

namespace MartialHeroes.Explorer.Files.Models;

public sealed class FormatFamily
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required string Glyph { get; init; }

    public required string Description { get; init; }

    public required IReadOnlyCollection<string> Extensions { get; init; }

    public required IFormatDecoder Decoder { get; init; }

    public Func<VfsFileNode, bool>? ExtraMatch { get; init; }

    public bool Matches(VfsFileNode node)
    {
        if (ExtraMatch is not null)
            return ExtraMatch(node);

        foreach (var ext in Extensions)
            if (string.Equals(ext, node.Extension, StringComparison.OrdinalIgnoreCase))
                return true;

        return false;
    }
}