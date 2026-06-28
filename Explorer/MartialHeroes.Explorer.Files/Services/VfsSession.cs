using System;
using System.Collections.Generic;
using System.IO;
using MartialHeroes.Assets.Vfs;
using MartialHeroes.Explorer.Files.Models;

namespace MartialHeroes.Explorer.Files.Services;

public sealed class VfsSession : IDisposable
{
    private readonly MappedVfsArchive _archive;

    private VfsSession(MappedVfsArchive archive, string clientDir, IReadOnlyList<VfsFileNode> files)
    {
        _archive = archive;
        ClientDir = clientDir;
        Files = files;
    }

    public string ClientDir { get; }

    public IReadOnlyList<VfsFileNode> Files { get; }

    public void Dispose()
    {
        _archive.Dispose();
    }

    public static VfsSession Open(string clientDir)
    {
        var infPath = Path.Combine(clientDir, "data.inf");
        var vfsPath = Path.Combine(clientDir, "data", "data.vfs");

        var archive = MappedVfsArchive.Open(infPath, vfsPath);

        var nodes = new List<VfsFileNode>(archive.EntryCount);
        foreach (var entry in archive.GetEntries())
            nodes.Add(ToNode(entry));

        nodes.Sort(static (a, b) => string.CompareOrdinal(a.Path, b.Path));

        return new VfsSession(archive, clientDir, nodes);
    }

    public ReadOnlyMemory<byte> Read(VfsFileNode node)
    {
        return _archive.GetFileContent(node.Path);
    }

    private static VfsFileNode ToNode(VfsEntry entry)
    {
        var path = entry.Name;
        var slash = path.LastIndexOf('/');
        var name = slash >= 0 ? path[(slash + 1)..] : path;
        var directory = slash >= 0 ? path[..slash] : string.Empty;

        var dot = name.LastIndexOf('.');
        var extension = dot >= 0 ? name[dot..] : string.Empty;

        return new VfsFileNode
        {
            Path = path,
            Name = name,
            Directory = directory,
            Extension = extension,
            Size = entry.DataSize
        };
    }
}