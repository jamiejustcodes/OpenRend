using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using OpenRend.Media;

namespace OpenRend.App;

public partial class App : Application
{
    private ServiceProvider? services;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "OpenRend", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        services = new ServiceCollection().AddSingleton<MediaService>().AddSingleton<TimelineRenderService>()
            .AddSingleton<ExportService>().AddSingleton<EditorViewModel>().AddSingleton<MainWindow>().BuildServiceProvider();
        var window = services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
        if (e.Args.Length > 0 && File.Exists(e.Args[0]))
            window.Dispatcher.InvokeAsync(async () => await window.OpenPathAsync(e.Args[0]));
        else if (e.Args.Length == 0)
            window.Dispatcher.InvokeAsync(async () => await window.ShowStartupAsync());
    }
    protected override void OnExit(ExitEventArgs e)
    {
        services?.Dispose();
        base.OnExit(e);
    }
}
