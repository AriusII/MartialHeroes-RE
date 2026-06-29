using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MartialHeroes.Explorer.Files.Models;
using MartialHeroes.Explorer.Files.Services;

namespace MartialHeroes.Explorer.Files.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private const int MaxDisplayedFiles = 10000;

    private readonly FormatCatalog _catalog = new();

    [ObservableProperty] private string? _clientDir;

    [ObservableProperty] private DecodedDocument? _currentDocument;
    private int _decodeToken;

    [ObservableProperty] private string _filterText = string.Empty;

    [ObservableProperty] private bool _isBusy;

    [ObservableProperty] private VfsFileNode? _selectedFile;

    [ObservableProperty] private FormatTabViewModel? _selectedTab;

    private VfsSession? _session;

    [ObservableProperty] private string _statusText = "No client loaded.";

    public ObservableCollection<FormatTabViewModel> Tabs { get; } = [];

    public ObservableCollection<VfsFileNode> Files { get; } = [];

    public bool HasClient => _session is not null;

    public bool HasFiles => Files.Count > 0;

    public async Task TryAutoOpenAsync()
    {
        var dir = ClientLocator.ResolveClientDir();
        if (dir is not null)
            await OpenClientAsync(dir);
        else
            StatusText = "No client found. Use \"Open client…\" to pick a folder containing data.inf.";
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        if (ClientDir is not null)
            await OpenClientAsync(ClientDir);
    }

    public async Task OpenClientAsync(string dir)
    {
        if (IsBusy) return;

        if (!ClientLocator.IsValidClientDir(dir))
        {
            StatusText = $"Invalid client folder (data.inf / data/data.vfs missing): {dir}";
            return;
        }

        IsBusy = true;
        StatusText = $"Opening {dir} …";

        try
        {
            var session = await Task.Run(() => VfsSession.Open(dir));
            var tabs = BuildTabs(session.Files);

            var previous = _session;
            _session = session;
            ClientDir = dir;

            if (previous is not null)
                _ = Task.Run(previous.Dispose);

            Tabs.Clear();
            foreach (var tab in tabs)
                Tabs.Add(tab);

            OnPropertyChanged(nameof(HasClient));

            SelectedTab = Tabs.Count > 0 ? Tabs[0] : null;
            StatusText = $"{session.Files.Count:N0} files · {Tabs.Count} format groups · {dir}";
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to open client: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private List<FormatTabViewModel> BuildTabs(IReadOnlyList<VfsFileNode> files)
    {
        var buckets = new Dictionary<string, List<VfsFileNode>>(StringComparer.Ordinal);
        foreach (var family in _catalog.Families)
            buckets[family.Id] = [];

        var all = new List<VfsFileNode>(files.Count);
        foreach (var file in files)
        {
            all.Add(file);
            var family = _catalog.ResolveFamily(file);
            if (buckets.TryGetValue(family.Id, out var bucket))
                bucket.Add(file);
        }

        var tabs = new List<FormatTabViewModel>();
        foreach (var family in _catalog.Families)
        {
            var bucket = buckets[family.Id];
            if (bucket.Count > 0)
                tabs.Add(new FormatTabViewModel { Family = family, Files = bucket });
        }

        tabs.Add(new FormatTabViewModel { Family = _catalog.AllFiles, Files = all });
        return tabs;
    }

    partial void OnSelectedTabChanged(FormatTabViewModel? value)
    {
        ApplyFilter();
    }

    partial void OnFilterTextChanged(string value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        Files.Clear();
        SelectedFile = null;

        var source = SelectedTab?.Files;
        if (source is null)
        {
            OnPropertyChanged(nameof(HasFiles));
            return;
        }

        var filter = FilterText?.Trim();
        var hasFilter = !string.IsNullOrEmpty(filter);
        var added = 0;

        foreach (var file in source)
        {
            if (hasFilter && file.Path.IndexOf(filter!, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            Files.Add(file);
            if (++added >= MaxDisplayedFiles) break;
        }

        OnPropertyChanged(nameof(HasFiles));

        if (Files.Count > 0)
            SelectedFile = Files[0];
    }

    partial void OnSelectedFileChanged(VfsFileNode? value)
    {
        _ = DecodeSelectedAsync(value, Interlocked.Increment(ref _decodeToken));
    }

    private async Task DecodeSelectedAsync(VfsFileNode? node, int token)
    {
        if (node is null || _session is null)
        {
            CurrentDocument = null;
            return;
        }

        var session = _session;

        try
        {
            var document = await Task.Run(() =>
                session.Use(node, bytes => _catalog.Decode(node, bytes)));

            if (token != Volatile.Read(ref _decodeToken)) return;

            await Dispatcher.UIThread.InvokeAsync(() => CurrentDocument = document);
        }
        catch (Exception ex)
        {
            if (token != Volatile.Read(ref _decodeToken)) return;
            CurrentDocument = new TextDocument
            {
                Title = node.Name,
                Summary = "read error",
                Text = ex.Message
            };
        }
    }
}