using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using MartialHeroes.Explorer.Files.ViewModels;
using MartialHeroes.Explorer.Files.Views;

namespace MartialHeroes.Explorer.Files;

public class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainWindowViewModel();
            desktop.MainWindow = new MainWindow { DataContext = viewModel };
            Dispatcher.UIThread.Post(async () => await viewModel.TryAutoOpenAsync());
        }

        base.OnFrameworkInitializationCompleted();
    }
}