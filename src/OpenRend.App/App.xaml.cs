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
        if (e.Args.Length >= 2 && e.Args[0] == "--capture-motion-track")
        {
            var outputPath = e.Args[1];
            try
            {
                var editorVm = services.GetRequiredService<EditorViewModel>();
                var testClip = new OpenRend.Core.TimelineClip
                {
                    Name = "Motion Tracking Sample Event",
                    StartFrame = 0,
                    DurationFrames = 150
                };
                testClip.MotionTracks.Add(new OpenRend.Core.MotionTrackData
                {
                    Name = "Region 1",
                    Model = OpenRend.Core.TrackingModel.Homography,
                    Precision = OpenRend.Core.TrackingPrecision.Medium,
                    Keyframes =
                    [
                        new OpenRend.Core.TrackingKeyframe(0, 0.45, 0.45, 0.2, 0.2) { Confidence = 0.95 },
                        new OpenRend.Core.TrackingKeyframe(30, 0.50, 0.48, 0.2, 0.2) { Confidence = 0.88 },
                        new OpenRend.Core.TrackingKeyframe(60, 0.55, 0.52, 0.2, 0.2) { Confidence = 0.65 },
                        new OpenRend.Core.TrackingKeyframe(90, 0.60, 0.55, 0.2, 0.2) { Confidence = 0.20 },
                        new OpenRend.Core.TrackingKeyframe(120, 0.65, 0.60, 0.2, 0.2) { Confidence = 0.92 }
                    ]
                });
                editorVm.Project.Tracks.Add(new OpenRend.Core.TimelineTrack { Clips = [testClip] });
                var dialog = new MotionTrackWindow(editorVm, testClip.Id)
                {
                    Width = 450,
                    Height = 800
                };
                var content = (UIElement)dialog.Content;
                content.Measure(new Size(450, 800));
                content.Arrange(new Rect(0, 0, 450, 800));
                content.UpdateLayout();
                var target = new System.Windows.Media.Imaging.RenderTargetBitmap(450, 800, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                target.Render(content);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(target));
                var dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                using (var file = File.Create(outputPath))
                {
                    encoder.Save(file);
                }
            }
            catch (Exception ex)
            {
                File.WriteAllText("capture-err.txt", ex.ToString());
            }
            Shutdown(0);
            return;
        }

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
