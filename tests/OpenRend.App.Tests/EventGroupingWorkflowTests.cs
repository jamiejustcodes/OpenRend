using System.Drawing;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using OpenRend.Core;
using OpenRend.Project;
using Xunit;

namespace OpenRend.App.Tests;

public sealed partial class EditorWorkflowTests
{
    [WindowsDesktopFact]
    [Trait("Category", "Desktop")]
    public async Task SlipAndSlideKeepLinkedEventsTogetherUntilGroupingIsIgnored()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var temporary = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var path = Path.Combine(temporary, "group-trim.openrend");
        try
        {
            var asset = new MediaAsset
            {
                Path = Path.Combine(root, "tests", "native", "fixture.mp4"),
                Width = 320, Height = 180, DurationSeconds = 3, FrameRate = 30, HasVideo = true, HasAudio = true
            };
            var video = new TimelineClip { MediaId = asset.Id, GroupId = Guid.NewGuid(), StartFrame = 20, SourceInFrame = 10, DurationFrames = 30 };
            var audio = video.DeepClone();
            audio.Id = Guid.NewGuid();
            var touching = new TimelineClip { MediaId = asset.Id, StartFrame = 50, DurationFrames = 30 };
            var acrossGap = new TimelineClip { MediaId = asset.Id, StartFrame = 90, DurationFrames = 20 };
            await ProjectStore.SaveAsync(new ProjectDocument
            {
                Name = "UI integration", Width = 320, Height = 180, Media = [asset],
                Tracks = [new() { Kind = TrackKind.Video, Clips = [video, touching, acrossGap] },
                          new() { Kind = TrackKind.Audio, Clips = [audio] }]
            }, path);

            using var running = Launch(executable, path, temporary);
            var window = running.Window;
            Chord(window, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_F);
            var grouping = Find(window, "IgnoreGroupsToggle").Patterns.Toggle.Pattern;

            DragEvent(5, VirtualKeyShort.ALT);
            var slipped = await Save();
            Assert.Equal(15, Clip(slipped, video.Id).SourceInFrame);
            Assert.Equal(15, Clip(slipped, audio.Id).SourceInFrame);
            Assert.Equal(20, Clip(slipped, video.Id).StartFrame);

            DragEvent(6, VirtualKeyShort.CONTROL, VirtualKeyShort.ALT);
            var slid = await Save();
            Assert.Equal(26, Clip(slid, video.Id).StartFrame);
            Assert.Equal(26, Clip(slid, audio.Id).StartFrame);
            Assert.Equal((56L, 6L, 24L), (Clip(slid, touching.Id).StartFrame, Clip(slid, touching.Id).SourceInFrame, Clip(slid, touching.Id).DurationFrames));
            Assert.Equal((90L, 20L), (Clip(slid, acrossGap.Id).StartFrame, Clip(slid, acrossGap.Id).DurationFrames));

            Chord(window, VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT, VirtualKeyShort.KEY_U);
            WaitFor(() => grouping.ToggleState.Value == ToggleState.On, "Ctrl+Shift+U did not ignore event grouping.");
            DragEvent(5, VirtualKeyShort.ALT);
            var unlinkedSlip = await Save();
            Assert.Equal(20, Clip(unlinkedSlip, video.Id).SourceInFrame);
            Assert.Equal(15, Clip(unlinkedSlip, audio.Id).SourceInFrame);
            Chord(window, VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT, VirtualKeyShort.KEY_U);
            WaitFor(() => grouping.ToggleState.Value == ToggleState.Off, "Ctrl+Shift+U did not restore event grouping.");
            running.CloseGracefully();

            void DragEvent(int frames, params VirtualKeyShort[] modifiers)
            {
                var bounds = Find(window, $"Clip_{video.Id:N}").BoundingRectangle;
                var start = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
                var x = start.X + (int)Math.Round(bounds.Width * frames / (double)video.DurationFrames);
                using (Keyboard.Pressing(modifiers))
                {
                    Wait.UntilInputIsProcessed();
                    DragTrimmer(start, new Point(x, start.Y));
                }
            }

            async Task<ProjectDocument> Save()
            {
                WaitFor(() => window.Title.Contains('*'), "The event gesture did not edit the project.");
                SaveButton(window).Invoke();
                WaitFor(() => !window.Title.Contains('*'), "The edit was not saved.");
                return await ProjectStore.LoadAsync(path);
            }

            static TimelineClip Clip(ProjectDocument project, Guid id) => project.Tracks.SelectMany(track => track.Clips).Single(clip => clip.Id == id);
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }
}
