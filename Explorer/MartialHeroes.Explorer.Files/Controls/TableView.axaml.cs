using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using MartialHeroes.Explorer.Files.Models;

namespace MartialHeroes.Explorer.Files.Controls;

public partial class TableView : UserControl
{
    public static readonly StyledProperty<TableDocument?> DocumentProperty =
        AvaloniaProperty.Register<TableView, TableDocument?>(nameof(Document));

    private const int PageSize = 1000;

    private readonly DataGrid _grid;
    private readonly Border _pager;
    private readonly TextBlock _pageInfo;
    private readonly Button _first;
    private readonly Button _prev;
    private readonly Button _next;
    private readonly Button _last;

    private int _page;
    private int _pageCount = 1;

    public TableView()
    {
        InitializeComponent();
        _grid = this.FindControl<DataGrid>("Grid")!;
        _pager = this.FindControl<Border>("PagerBar")!;
        _pageInfo = this.FindControl<TextBlock>("PageInfo")!;
        _first = this.FindControl<Button>("FirstButton")!;
        _prev = this.FindControl<Button>("PrevButton")!;
        _next = this.FindControl<Button>("NextButton")!;
        _last = this.FindControl<Button>("LastButton")!;

        _first.Click += (_, _) => GoTo(0);
        _prev.Click += (_, _) => GoTo(_page - 1);
        _next.Click += (_, _) => GoTo(_page + 1);
        _last.Click += (_, _) => GoTo(_pageCount - 1);
    }

    public TableDocument? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DocumentProperty)
            Rebuild(change.GetNewValue<TableDocument?>());
    }

    private void Rebuild(TableDocument? document)
    {
        _grid.Columns.Clear();
        _grid.ItemsSource = null;
        _page = 0;
        _pageCount = 1;

        if (document is null)
        {
            _pager.IsVisible = false;
            return;
        }

        for (var i = 0; i < document.Columns.Count; i++)
        {
            var column = new DataGridTextColumn
            {
                Header = document.Columns[i],
                Binding = new Binding($"[{i}]"),
                IsReadOnly = true
            };

            if (i == 0)
            {
                column.Width = new DataGridLength(64);
            }
            else if (i == document.Columns.Count - 1)
            {
                column.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
                column.MinWidth = 80;
                column.MaxWidth = 720;
            }
            else
            {
                column.Width = new DataGridLength(0, DataGridLengthUnitType.Auto);
                column.MinWidth = 44;
                column.MaxWidth = 560;
            }

            _grid.Columns.Add(column);
        }

        _pageCount = Math.Max(1, (document.Rows.Count + PageSize - 1) / PageSize);
        RenderPage(document);
    }

    private void GoTo(int page)
    {
        if (Document is not { } document) return;

        var clamped = Math.Clamp(page, 0, _pageCount - 1);
        if (clamped == _page) return;

        _page = clamped;
        RenderPage(document);
    }

    private void RenderPage(TableDocument document)
    {
        var total = document.Rows.Count;
        var start = _page * PageSize;
        var count = Math.Clamp(total - start, 0, PageSize);

        var slice = new TableRow[count];
        for (var i = 0; i < count; i++)
            slice[i] = document.Rows[start + i];

        _grid.ItemsSource = slice;
        if (count > 0)
            _grid.ScrollIntoView(slice[0], null);

        var paged = total > PageSize;
        _pager.IsVisible = paged;
        if (!paged) return;

        var from = total == 0 ? 0 : start + 1;
        var to = start + count;
        _pageInfo.Text = $"rows {from:N0}–{to:N0} of {total:N0}   ·   page {_page + 1:N0} / {_pageCount:N0}";

        _first.IsEnabled = _prev.IsEnabled = _page > 0;
        _next.IsEnabled = _last.IsEnabled = _page < _pageCount - 1;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
