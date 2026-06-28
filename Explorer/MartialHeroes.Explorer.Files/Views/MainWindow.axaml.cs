using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MartialHeroes.Explorer.Files.ViewModels;

namespace MartialHeroes.Explorer.Files.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var openButton = this.FindControl<Button>("OpenClientButton");
        if (openButton is not null)
            openButton.Click += OnOpenClientClick;
    }

    private async void OnOpenClientClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select the client folder (containing data.inf and data/data.vfs)",
                AllowMultiple = false
            });

            if (folders.Count == 0)
                return;

            var path = folders[0].TryGetLocalPath();
            if (!string.IsNullOrEmpty(path))
                await viewModel.OpenClientAsync(path);
        }
        catch (Exception)
        {
        }
    }
}