using System.Drawing;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using OpenRend.Core;
using OpenRend.Project;
using Xunit;

namespace OpenRend.App.Tests;

public sealed partial class EditorWorkflowTests
{
    private const uint GetClientAreaAnimation = 0x1042;

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadSystemAnimationSetting(uint action, uint parameter, out int enabled, uint flags);

    [WindowsDesktopFact]
    [Trait("Category", "Desktop")]
    public async Task TransitionPresetPreviewsShowDirectionAndAnimateOnHover()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var directory = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "transition-previews.openrend");
        try
        {
            var source = new MediaAsset { Path = "openrend://color?hex=162538", HasVideo = true };
            var outgoing = new TimelineClip { MediaId = source.Id, Name = "Outgoing", DurationFrames = 36 };
            var incoming = new TimelineClip { MediaId = source.Id, Name = "Incoming", StartFrame = 24, DurationFrames = 36 };
            await ProjectStore.SaveAsync(new ProjectDocument { Name = "UI integration", Width = 320, Height = 180, Media = [source],
                Tracks = [new() { Kind = TrackKind.Video, Clips = [outgoing, incoming] }] }, path);
            using var running = Launch(executable, path, directory);
            var window = running.Window;
            FitTimeline(window);
            OpenTransitions(window);
            var defaultThumbnail = ThumbnailIn(Find(window, "DefaultDissolvePreset"));
            WaitFor(() => Color.FromArgb(ReadThumbnail(defaultThumbnail).Left).G > 100, "The default preview did not render.");
            var search = Find(window, "TransitionSearch").AsTextBox();
            search.Text = "Linear Wipe";
            var topDown = FindThumbnail("Top-Down, Hard Edge");
            var leftRight = FindThumbnail("Left-Right, Hard Edge");
            Mouse.MoveTo(search.GetClickablePoint());
            var topPoster = ReadThumbnail(topDown);
            var leftPoster = ReadThumbnail(leftRight);
            Assert.NotEqual(topPoster, leftPoster);
            Assert.NotEqual(leftPoster.Left, leftPoster.Right);

            var center = ThumbnailCenter(leftRight);
            Mouse.MoveTo(center);
            Assert.True(ReadSystemAnimationSetting(GetClientAreaAnimation, 0, out var animationsEnabled, 0));
            if (animationsEnabled != 0)
                WaitFor(() => ReadThumbnail(leftRight) != leftPoster, "The preset did not animate on hover.");
            else
            {
                Wait.UntilInputIsProcessed();
                Assert.Equal(leftPoster, ReadThumbnail(leftRight));
            }
            Mouse.MoveTo(search.GetClickablePoint());
            WaitFor(() => ReadThumbnail(leftRight) == leftPoster, "Leaving the preset did not restore its thumbnail.");
            Assert.Equal(topPoster, ReadThumbnail(topDown));
            Mouse.Click(center);
            Assert.DoesNotContain('*', window.Title);
            Mouse.MoveTo(search.GetClickablePoint());
            var artifacts = Path.Combine(root, ".local", "desktop-validation");
            Directory.CreateDirectory(artifacts);
            using (var capture = Capture.Element(Find(window, "ToolsTabs"))) capture.ToFile(Path.Combine(artifacts, "transition-preset-previews.png"));
            var firstBounds = Find(window, $"Clip_{outgoing.Id:N}").BoundingRectangle;
            var secondBounds = Find(window, $"Clip_{incoming.Id:N}").BoundingRectangle;
            var drop = new Point((secondBounds.Left + firstBounds.Right) / 2, firstBounds.Top + firstBounds.Height / 2);
            Mouse.Drag(ThumbnailCenter(topDown), drop, MouseButton.Left);
            WaitFor(() => window.Title.Contains('*'), "Dragging from the thumbnail did not apply the transition.");
            var saved = (await SaveGrade(window, path)).Tracks[0].Clips[1].FadeInTransition;
            Assert.Equal(VideoTransitionKind.LinearWipe, saved?.Kind);
            Assert.Equal("Top-Down, Hard Edge", saved?.Preset);
            running.CloseGracefully();

            AutomationElement FindThumbnail(string name)
            {
                AutomationElement? preset = null;
                WaitFor(() => (preset = window.FindFirstDescendant(c => c.ByControlType(ControlType.ListItem).And(c.ByName(name)))) is not null,
                    $"The {name} preset was not shown.");
                if (preset!.Patterns.ScrollItem.TryGetPattern(out var scroll)) scroll.ScrollIntoView();
                return ThumbnailIn(preset);
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static AutomationElement ThumbnailIn(AutomationElement preset) =>
        Assert.Single(preset.FindAllDescendants(c => c.ByAutomationId("TransitionPresetThumbnail")));

    private static Point ThumbnailCenter(AutomationElement image)
    {
        var bounds = image.BoundingRectangle;
        return new(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
    }

    private static (int Left, int Right, int Top, int Bottom) ReadThumbnail(AutomationElement thumbnail)
    {
        using var capture = Capture.Element(thumbnail);
        var image = capture.Bitmap;
        return (image.GetPixel(image.Width / 4, image.Height / 2).ToArgb(), image.GetPixel(image.Width * 3 / 4, image.Height / 2).ToArgb(),
            image.GetPixel(image.Width / 2, image.Height / 4).ToArgb(), image.GetPixel(image.Width / 2, image.Height * 3 / 4).ToArgb());
    }
}
