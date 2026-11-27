using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
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
    public async Task ColorGradingDockFitsAndKeepsLutPickersInSync()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var temporary = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var path = Path.Combine(temporary, "grade.openrend");
        var asset = new MediaAsset
        {
            Path = Path.Combine(root, "tests", "native", "fixture.mp4"), Width = 320, Height = 180,
            DurationSeconds = 3, FrameRate = 30, HasVideo = true, HasAudio = true
        };
        var video = new TimelineClip
        {
            MediaId = asset.Id, GroupId = Guid.NewGuid(), DurationFrames = 30,
            Name = "Interview with a deliberately long event name so the grading toolbar still fits"
        };
        var audio = new TimelineClip { MediaId = asset.Id, GroupId = video.GroupId, DurationFrames = 30, Name = "Linked audio" };
        var custom = VideoEffect.Create(VideoEffectKind.ColorGrading);
        custom.LookLut = BuiltinLutLibrary.CreatePreset(BuiltinLutLibrary.LookLutNames[0]);
        Assert.NotNull(custom.LookLut);
        custom.LookLut.Title = "Interview-grade.cube";
        var other = new TimelineClip
        {
            MediaId = asset.Id, StartFrame = 36, DurationFrames = 45, Name = "Alternate grade", VideoEffects = [custom]
        };
        await ProjectStore.SaveAsync(new ProjectDocument
        {
            Name = "UI integration", Width = 320, Height = 180, FrameRate = new(30, 1), Media = [asset],
            Tracks = [new() { Kind = TrackKind.Audio, Clips = [audio] }, new() { Kind = TrackKind.Video, Clips = [video, other] }]
        }, path);

        try
        {
            using var running = Launch(executable, path, temporary);
            var window = running.Window;
            SelectBrowserTab(window, "Color Grading");
            var label = Find(window, "ColorGrading_TargetLabel");
            var quick = Find(window, "ColorGrading_PresetCombo").AsComboBox();
            var preset = Find(window, "LookLut_Preset").AsComboBox();
            var cameraPath = Find(window, "cameraLut_Path");
            var lookPath = Find(window, "lookLut_Path");
            Assert.False(quick.IsEnabled);
            Find(window, $"Clip_{video.Id:N}").Patterns.SelectionItem.Pattern.Select();
            WaitFor(() => label.Name.Contains(video.Name) && quick.IsEnabled, "The linked selection did not bind its video event.");

            var tools = Find(window, "ToolsTabs").BoundingRectangle;
            var popout = Find(window, "ColorGrading_PopoutButton");
            Assert.False(popout.Properties.IsOffscreen.ValueOrDefault);
            Assert.True(tools.Contains(popout.BoundingRectangle));
            Assert.True(lookPath.BoundingRectangle.Top > cameraPath.BoundingRectangle.Top,
                "The LUT slots did not stack in the default dock width.");
            var artifacts = Path.Combine(root, ".local", "desktop-validation");
            Directory.CreateDirectory(artifacts);
            using (var screenshot = Capture.Element(window)) screenshot.ToFile(Path.Combine(artifacts, "color-grading-dock.png"));

            var splitter = Find(window, "ToolsPreviewSplitter");
            var separator = splitter.GetClickablePoint();
            var widePoint = new System.Drawing.Point(separator.X + 275, separator.Y);
            Mouse.Drag(separator, widePoint, MouseButton.Left);
            WaitFor(() => Math.Abs(lookPath.BoundingRectangle.Top - cameraPath.BoundingRectangle.Top) <= 2
                && lookPath.BoundingRectangle.Left > cameraPath.BoundingRectangle.Right,
                "The LUT slots did not move side by side after resizing the dock.");
            Mouse.Drag(splitter.GetClickablePoint(), separator, MouseButton.Left);
            WaitFor(() => lookPath.BoundingRectangle.Top > cameraPath.BoundingRectangle.Top,
                "The LUT slots did not stack again in the compact dock.");

            quick.Select(1);
            WaitFor(() => preset.SelectedItem?.Name == BuiltinLutLibrary.LookLutNames[0], "The slot did not follow the quick picker.");
            preset.Select(2);
            WaitFor(() => quick.SelectedItem?.Name == BuiltinLutLibrary.LookLutNames[1], "The quick picker did not follow the slot.");
            Find(window, "UtilitiesItem").Patterns.SelectionItem.Pattern.Select();
            var slider = Find(window, "inputMin");
            slider.Patterns.RangeValue.Pattern.SetValue(.005);
            WaitFor(() => Math.Abs(slider.Patterns.RangeValue.Pattern.Value.Value - .005) < 0.00001, "The level adjustment did not retain its step.");

            var saved = await SaveGrade(window, path);
            var effect = Assert.Single(saved.Tracks[1].Clips[0].VideoEffects);
            Assert.Equal(BuiltinLutLibrary.LookLutNames[1], effect.LookLut?.Title);
            Assert.Equal(.005, effect.GetParameter("InMinAdjust"), 6);
            Assert.Empty(saved.Tracks[0].Clips[0].VideoEffects);

            quick.Select(0);
            WaitFor(() => quick.SelectedItem?.Name == "None" && window.Title.Contains('*'), "Selecting None did not clear the Look LUT.");
            saved = await SaveGrade(window, path);
            effect = Assert.Single(saved.Tracks[1].Clips[0].VideoEffects);
            Assert.Null(effect.LookLut);
            Assert.Equal(.005, effect.GetParameter("InMinAdjust"), 6);
            Chord(window, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
            WaitFor(() => quick.SelectedItem?.Name == BuiltinLutLibrary.LookLutNames[1], "Undo did not restore the LUT picker.");
            saved = await SaveGrade(window, path);
            Assert.Equal(BuiltinLutLibrary.LookLutNames[1], Assert.Single(saved.Tracks[1].Clips[0].VideoEffects).LookLut?.Title);

            Find(window, "CurvesItem").Patterns.SelectionItem.Pattern.Select();
            var graph = Find(window, "CurvesItem_Control");
            Mouse.Click(PointIn(graph.BoundingRectangle, .42, .35));
            WaitFor(() => window.Title.Contains('*'), "Adding a curve point did not edit the event grade.");
            WaitFor(() => graph.Properties.HasKeyboardFocus.ValueOrDefault, "The curve did not retain keyboard focus after clicking a point.");
            Keyboard.Type(VirtualKeyShort.DELETE);
            saved = await SaveGrade(window, path);
            Assert.Equal(2, saved.Tracks[1].Clips.Count);
            Assert.Null(Assert.Single(saved.Tracks[1].Clips[0].VideoEffects).Curves);

            Find(window, "LUTsItem").Patterns.SelectionItem.Pattern.Select();
            Find(window, $"Clip_{other.Id:N}").Patterns.SelectionItem.Pattern.Select();
            WaitFor(() => quick.SelectedItem?.Name == "Imported LUT" && preset.SelectedItem?.Name == "Imported LUT",
                "An imported LUT was shown as an empty or built-in preset.");
            Assert.Equal("Interview-grade.cube", lookPath.Name);
            Chord(window, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
            WaitFor(() => !quick.IsEnabled, "A selection of multiple video events left grading enabled.");
            Assert.DoesNotContain('*', window.Title);
            running.CloseGracefully();
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    private static async Task<ProjectDocument> SaveGrade(Window window, string path)
    {
        SaveButton(window).Invoke();
        WaitFor(() => !window.Title.Contains('*'), "Saving the color grade did not finish.");
        return await ProjectStore.LoadAsync(path);
    }
}
