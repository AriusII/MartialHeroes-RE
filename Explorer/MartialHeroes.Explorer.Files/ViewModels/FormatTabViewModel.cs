using System.Collections.Generic;
using MartialHeroes.Explorer.Files.Models;

namespace MartialHeroes.Explorer.Files.ViewModels;

public sealed class FormatTabViewModel
{
    public required FormatFamily Family { get; init; }

    public required IReadOnlyList<VfsFileNode> Files { get; init; }

    public string Title => Family.Title;

    public string Glyph => Family.Glyph;

    public string Description => Family.Description;

    public int FileCount => Files.Count;

    public string Header => $"{Glyph}  {Title}  ({FileCount:N0})";
}