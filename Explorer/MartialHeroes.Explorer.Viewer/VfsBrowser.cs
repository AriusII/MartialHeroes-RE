using Godot;
using MartialHeroes.Assets.Vfs;

namespace MartialHeroes.Explorer.Viewer;

public sealed class VfsBrowser : IDisposable
{
    private readonly Dictionary<string, List<string>> _families = new();
    private MappedVfsArchive? _archive;

    public bool IsOpen { get; private set; }
    public int TotalEntries { get; private set; }
    public IReadOnlyList<string> Families { get; private set; } = [];

    public MappedVfsArchive Archive => _archive!;

    public void Dispose()
    {
        _archive?.Dispose();
    }

    public bool TryOpen(string infPath, string vfsPath)
    {
        try
        {
            _archive = MappedVfsArchive.Open(infPath, vfsPath);
            TotalEntries = _archive.EntryCount;
            BuildFamilies();
            IsOpen = true;
            return true;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[Viewer] VfsBrowser.TryOpen failed: {ex.Message}");
            IsOpen = false;
            return false;
        }
    }

    public IReadOnlyList<string> GetFiles(string family)
    {
        return _families.TryGetValue(family, out var list) ? list : [];
    }

    public ReadOnlyMemory<byte> GetContent(string virtualPath)
    {
        return _archive!.GetFileContent(virtualPath);
    }

    private void BuildFamilies()
    {
        _families.Clear();
        var entries = _archive!.GetEntries();
        foreach (var entry in entries)
        {
            var family = ClassifyEntry(entry.Name);
            if (family is null) continue;
            if (!_families.TryGetValue(family, out var list))
            {
                list = [];
                _families[family] = list;
            }

            list.Add(entry.Name);
        }

        foreach (var list in _families.Values)
            list.Sort(StringComparer.OrdinalIgnoreCase);

        Families = _families.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string? ClassifyEntry(string name)
    {
        if (name.EndsWith(".xobj", StringComparison.OrdinalIgnoreCase)) return "Static Models (.xobj)";
        if (name.EndsWith(".skn", StringComparison.OrdinalIgnoreCase)) return "Skinned Characters (.skn)";
        if (name.EndsWith(".bud", StringComparison.OrdinalIgnoreCase)) return "Building Scenes (.bud)";
        if (name.EndsWith(".ted", StringComparison.OrdinalIgnoreCase)) return "Terrain Cells (.ted)";
        return null;
    }
}