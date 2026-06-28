namespace MartialHeroes.Explorer.Files.Models;

public sealed class VfsFileNode
{
    public required string Path { get; init; }

    public required string Name { get; init; }

    public required string Directory { get; init; }

    public required string Extension { get; init; }

    public required long Size { get; init; }

    public string SizeDisplay => FormatSize(Size);

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} KB";
        return $"{bytes / (1024.0 * 1024.0):0.##} MB";
    }
}