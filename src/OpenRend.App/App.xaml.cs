using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using OpenRend.Core;
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
            try
            {
                (MainWindow as MainWindow)?.SaveEmergencyRecoverySnapshot();
            }
            catch { }
            MessageBox.Show(args.Exception.Message, "OpenRend", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        services = new ServiceCollection().AddSingleton<MediaService>().AddSingleton<TimelineRenderService>()
            .AddSingleton<ExportService>().AddSingleton<EditorViewModel>().AddSingleton<MainWindow>().BuildServiceProvider();
        if (e.Args.Length >= 2 && e.Args[0] == "--capture-assets")
        {
            var outputDir = e.Args[1];
            try
            {
                RunCaptureAssets(outputDir);
            }
            catch (Exception ex)
            {
                File.WriteAllText("capture-err.txt", ex.ToString());
            }
            Shutdown(0);
            Environment.Exit(0);
            return;
        }
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
            Environment.Exit(0);
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

    private void RunCaptureAssets(string outputDir)
    {
        var baseDir = AppContext.BaseDirectory;
        var root = Path.GetFullPath(baseDir);
        while (!Directory.Exists(Path.Combine(root, ".git")) && Directory.GetParent(root) != null)
        {
            root = Directory.GetParent(root)!.FullName;
        }

        Directory.CreateDirectory(outputDir);

        var editorVm = services!.GetRequiredService<EditorViewModel>();
        var fixtureMp4 = Path.Combine(root, "tests", "native", "fixture.mp4");
        var fixtureWav = Path.Combine(root, "tests", "native", "fixture.wav");
        var fixturePng = Path.Combine(root, "tests", "native", "fixture.png");

        var mediaVideo = new MediaAsset
        {
            Id = Guid.NewGuid(),
            Path = fixtureMp4,
            Width = 1920,
            Height = 1080,
            FrameRate = 30,
            DurationSeconds = 60,
            HasVideo = true,
            HasAudio = true
        };
        var mediaAudio = new MediaAsset
        {
            Id = Guid.NewGuid(),
            Path = fixtureWav,
            DurationSeconds = 60,
            HasAudio = true
        };
        var mediaGraphics = new MediaAsset
        {
            Id = Guid.NewGuid(),
            Path = fixturePng,
            Width = 1920,
            Height = 1080,
            DurationSeconds = 15,
            HasVideo = true
        };

        editorVm.Project.Media.Clear();
        editorVm.Project.Media.Add(mediaVideo);
        editorVm.Project.Media.Add(mediaAudio);
        editorVm.Project.Media.Add(mediaGraphics);
        editorVm.RefreshLibrary();

        var clip1 = new TimelineClip
        {
            Id = Guid.NewGuid(),
            MediaId = mediaVideo.Id,
            Name = "Scene 01 — Master Wide",
            StartFrame = 0,
            DurationFrames = 180,
            FadeInFrames = 15,
            FadeOutFrames = 30
        };
        var clip2 = new TimelineClip
        {
            Id = Guid.NewGuid(),
            MediaId = mediaVideo.Id,
            Name = "Scene 02 — Close-Up",
            StartFrame = 150,
            DurationFrames = 210,
            FadeInFrames = 30,
            FadeOutFrames = 30
        };
        var clip3 = new TimelineClip
        {
            Id = Guid.NewGuid(),
            MediaId = mediaVideo.Id,
            Name = "Scene 03 — Dolly Out",
            StartFrame = 330,
            DurationFrames = 240,
            FadeInFrames = 30,
            FadeOutFrames = 15
        };

        var videoTrack1 = new TimelineTrack
        {
            Name = "01 Video — Primary",
            Kind = TrackKind.Video,
            Clips = [clip1, clip2, clip3]
        };

        var clipOverlay1 = new TimelineClip
        {
            Id = Guid.NewGuid(),
            MediaId = mediaGraphics.Id,
            Name = "Lower Third — Presenter",
            StartFrame = 45,
            DurationFrames = 90,
            FadeInFrames = 15,
            FadeOutFrames = 15
        };
        var clipOverlay2 = new TimelineClip
        {
            Id = Guid.NewGuid(),
            MediaId = mediaVideo.Id,
            Name = "Picture-in-Picture B-Roll",
            StartFrame = 210,
            DurationFrames = 105,
            FadeInFrames = 15,
            FadeOutFrames = 15
        };

        var videoTrack2 = new TimelineTrack
        {
            Name = "02 Video — Overlay & Titles",
            Kind = TrackKind.Video,
            Clips = [clipOverlay1, clipOverlay2]
        };

        var audioClip1 = new TimelineClip
        {
            Id = Guid.NewGuid(),
            MediaId = mediaAudio.Id,
            Name = "Dialogue — Scene 1",
            StartFrame = 0,
            DurationFrames = 180,
            FadeInFrames = 5,
            FadeOutFrames = 30
        };
        var audioClip2 = new TimelineClip
        {
            Id = Guid.NewGuid(),
            MediaId = mediaAudio.Id,
            Name = "Dialogue — Scene 2",
            StartFrame = 150,
            DurationFrames = 210,
            FadeInFrames = 30,
            FadeOutFrames = 30
        };
        var audioClip3 = new TimelineClip
        {
            Id = Guid.NewGuid(),
            MediaId = mediaAudio.Id,
            Name = "Dialogue — Scene 3",
            StartFrame = 330,
            DurationFrames = 240,
            FadeInFrames = 30,
            FadeOutFrames = 5
        };

        var audioTrack1 = new TimelineTrack
        {
            Name = "03 Audio — Dialogue & Foley",
            Kind = TrackKind.Audio,
            Clips = [audioClip1, audioClip2, audioClip3]
        };

        var bgMusicClip = new TimelineClip
        {
            Id = Guid.NewGuid(),
            MediaId = mediaAudio.Id,
            Name = "Cinematic Ambient Theme",
            StartFrame = 0,
            DurationFrames = 570,
            FadeInFrames = 30,
            FadeOutFrames = 45
        };

        var audioTrack2 = new TimelineTrack
        {
            Name = "04 Audio — Master Score",
            Kind = TrackKind.Audio,
            Clips = [bgMusicClip]
        };

        editorVm.Project.Tracks.Clear();
        editorVm.Project.Tracks.Add(videoTrack1);
        editorVm.Project.Tracks.Add(videoTrack2);
        editorVm.Project.Tracks.Add(audioTrack1);
        editorVm.Project.Tracks.Add(audioTrack2);

        editorVm.Position = 165;
        editorVm.Selection.Clear();
        editorVm.Selection.Add(clip2.Id);

        var rnd = new Random(42);
        var waveformPeaks = new float[1024];
        for (int i = 0; i < waveformPeaks.Length; i++)
        {
            double env = Math.Sin(i * 0.04) * 0.4 + 0.4;
            double noise = rnd.NextDouble() * 0.25;
            waveformPeaks[i] = (float)Math.Clamp(env + noise, 0.05, 0.95);
        }

        var thumbPixels = new byte[160 * 90 * 4];
        for (int y = 0; y < 90; y++)
        {
            for (int x = 0; x < 160; x++)
            {
                int idx = (y * 160 + x) * 4;
                thumbPixels[idx + 0] = (byte)(40 + x / 2);
                thumbPixels[idx + 1] = (byte)(80 + y);
                thumbPixels[idx + 2] = (byte)(160 + x / 3);
                thumbPixels[idx + 3] = 255;
            }
        }
        var thumbBmp = BitmapSource.Create(160, 90, 96, 96, PixelFormats.Bgra32, null, thumbPixels, 160 * 4);
        thumbBmp.Freeze();

        // 1. Create and render MainWindow
        var window = services!.GetRequiredService<MainWindow>();
        window.Width = 1440;
        window.Height = 880;
        window.Timeline.Waveforms[mediaAudio.Id] = waveformPeaks;
        window.Timeline.Thumbnails[mediaVideo.Id] = thumbBmp;
        window.Timeline.Thumbnails[mediaGraphics.Id] = thumbBmp;
        window.Timecode.Text = "00:00:05.15";
        window.PreviewInfo.Text = "1920×1080×32, 29.970 fps | Direct3D 11 GPU Accelerated";

        // Generate synthetic video frame for preview
        var previewPixels = new byte[640 * 360 * 4];
        for (int y = 0; y < 360; y++)
        {
            for (int x = 0; x < 640; x++)
            {
                int idx = (y * 640 + x) * 4;
                previewPixels[idx + 0] = (byte)(50 + (x * 120 / 640));
                previewPixels[idx + 1] = (byte)(80 + (y * 80 / 360));
                previewPixels[idx + 2] = (byte)(180 + (x * 40 / 640));
                previewPixels[idx + 3] = 255;
            }
        }
        var previewBmp = BitmapSource.Create(640, 360, 96, 96, PixelFormats.Bgra32, null, previewPixels, 640 * 4);
        previewBmp.Freeze();
        window.SetPreviewImageOverride(previewBmp);

        var content = (UIElement)window.Content;
        content.Measure(new Size(1440, 880));
        content.Arrange(new Rect(0, 0, 1440, 880));
        content.UpdateLayout();

        var wsTarget = new RenderTargetBitmap(1440, 880, 96, 96, PixelFormats.Pbgra32);
        wsTarget.Render(content);
        SaveBitmap(wsTarget, Path.Combine(outputDir, "workspace.png"));

        // 2. Render Color Grading Window
        try
        {
            var colorWindow = new ColorGradingWindow(editorVm, EffectScope.Event, clip1.Id, null, null)
            {
                Width = 1080,
                Height = 720
            };
            var colorContent = (UIElement)colorWindow.Content;
            colorContent.Measure(new Size(1080, 720));
            colorContent.Arrange(new Rect(0, 0, 1080, 720));
            colorContent.UpdateLayout();
            var colorTarget = new RenderTargetBitmap(1080, 720, 96, 96, PixelFormats.Pbgra32);
            colorTarget.Render(colorContent);
            SaveBitmap(colorTarget, Path.Combine(outputDir, "color_grading.png"));
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(outputDir, "color-err.txt"), ex.ToString());
        }

        // 3. Render Pan/Crop Window
        try
        {
            var panCropWindow = new PanCropWindow(editorVm, clip1.Id, _ => { })
            {
                Width = 980,
                Height = 680
            };
            var panContent = (UIElement)panCropWindow.Content;
            panContent.Measure(new Size(980, 680));
            panContent.Arrange(new Rect(0, 0, 980, 680));
            panContent.UpdateLayout();
            var panTarget = new RenderTargetBitmap(980, 680, 96, 96, PixelFormats.Pbgra32);
            panTarget.Render(panContent);
            SaveBitmap(panTarget, Path.Combine(outputDir, "pan_crop.png"));
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(outputDir, "pancrop-err.txt"), ex.ToString());
        }

        // 4. Render Motion Tracking Window
        try
        {
            clip1.MotionTracks.Add(new MotionTrackData
            {
                Name = "Actor Head Tracking",
                Model = TrackingModel.Homography,
                Precision = TrackingPrecision.High,
                Keyframes =
                [
                    new TrackingKeyframe(0, 0.45, 0.45, 0.2, 0.2) { Confidence = 0.98 },
                    new TrackingKeyframe(30, 0.48, 0.46, 0.2, 0.2) { Confidence = 0.94 },
                    new TrackingKeyframe(60, 0.52, 0.49, 0.2, 0.2) { Confidence = 0.89 },
                    new TrackingKeyframe(90, 0.58, 0.52, 0.2, 0.2) { Confidence = 0.76 },
                    new TrackingKeyframe(120, 0.63, 0.56, 0.2, 0.2) { Confidence = 0.93 },
                    new TrackingKeyframe(150, 0.67, 0.60, 0.2, 0.2) { Confidence = 0.97 }
                ]
            });
            var motionWindow = new MotionTrackWindow(editorVm, clip1.Id)
            {
                Width = 460,
                Height = 800
            };
            var motionContent = (UIElement)motionWindow.Content;
            motionContent.Measure(new Size(460, 800));
            motionContent.Arrange(new Rect(0, 0, 460, 800));
            motionContent.UpdateLayout();
            var motionTarget = new RenderTargetBitmap(460, 800, 96, 96, PixelFormats.Pbgra32);
            motionTarget.Render(motionContent);
            SaveBitmap(motionTarget, Path.Combine(outputDir, "motion_tracking.png"));
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(outputDir, "motion-err.txt"), ex.ToString());
        }

        // 5. Render Timeline Scrubbing sequence for Demo GIF / Video
        var framesDir = Path.Combine(outputDir, "demo_frames");
        Directory.CreateDirectory(framesDir);
        for (int f = 0; f < 24; f++)
        {
            long frameIndex = 145 + f;
            editorVm.Position = frameIndex;
            window.Timecode.Text = $"00:00:04.{frameIndex % 30:D2}";
            content.UpdateLayout();
            var frameTarget = new RenderTargetBitmap(1440, 880, 96, 96, PixelFormats.Pbgra32);
            frameTarget.Render(content);
            SaveBitmap(frameTarget, Path.Combine(framesDir, $"frame_{f:D3}.png"));
        }
    }

    private static void SaveBitmap(BitmapSource bmp, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
    protected override void OnExit(ExitEventArgs e)
    {
        services?.Dispose();
        base.OnExit(e);
    }
}
