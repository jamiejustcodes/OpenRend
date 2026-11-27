using FlaUI.Core.AutomationElements;
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
    public async Task TranscriptFilterFindAndWordEditFollowLiveSelection()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var temporary = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var path = Path.Combine(temporary, "transcript.openrend");
        var asset = new MediaAsset
        {
            Path = Path.Combine(root, "tests", "native", "fixture.mp4"),
            Width = 320, Height = 180, DurationSeconds = 3, FrameRate = 30, HasVideo = true
        };
        var interview = new TimelineClip
        {
            MediaId = asset.Id, Name = "Interview", DurationFrames = 60,
            Transcript = new ClipTranscript([new("Scene", 5, 6), new("scene", 25, 6)])
        };
        var notes = new TimelineClip
        {
            MediaId = asset.Id, Name = "Notes", StartFrame = 75, DurationFrames = 60,
            Transcript = new ClipTranscript([new("Notes", 5, 6)])
        };
        await ProjectStore.SaveAsync(new()
        {
            Name = "UI integration", Width = 320, Height = 180, FrameRate = new(30, 1),
            Media = [asset], Tracks = [new() { Kind = TrackKind.Video, Clips = [interview, notes] }]
        }, path);

        try
        {
            using var running = Launch(executable, path, temporary);
            var window = running.Window;
            FitTimeline(window);
            var tools = window.FindFirstDescendant(c => c.ByControlType(ControlType.MenuItem).And(c.ByName("Tools")));
            Assert.NotNull(tools);
            tools.AsMenuItem().Expand();
            Find(window, "TextBasedEditingMenu").AsMenuItem().Invoke();
            var desktop = window.Automation.GetDesktop();
            var processId = window.Properties.ProcessId.Value;
            Window? transcriptWindow = null;
            WaitFor(() => (transcriptWindow = FloatingPane(window, "Text-Based Editing")) is not null,
                "Text-Based Editing did not open.");
            Assert.NotNull(transcriptWindow);

            var firstWordId = $"TranscriptWord_{interview.Id:N}_0";
            var secondWordId = $"TranscriptWord_{interview.Id:N}_1";
            var otherWordId = $"TranscriptWord_{notes.Id:N}_0";
            Assert.Equal("Scene", Find(transcriptWindow, firstWordId).Name);
            Assert.Equal("Notes", Find(transcriptWindow, otherWordId).Name);
            Mouse.Click(Find(transcriptWindow, "TranscriptSelectedEvents").GetClickablePoint());
            WaitFor(() => transcriptWindow.FindFirstDescendant(c => c.ByAutomationId(firstWordId)) is null,
                "Selected events did not hide unselected transcripts.");
            Find(window, $"Clip_{interview.Id:N}").Patterns.SelectionItem.Pattern.Select();
            WaitFor(() => transcriptWindow.FindFirstDescendant(c => c.ByAutomationId(firstWordId)) is not null,
                "The transcript did not follow the timeline selection.");
            Assert.Null(transcriptWindow.FindFirstDescendant(c => c.ByAutomationId(otherWordId)));

            var find = Find(transcriptWindow, "TranscriptFind").AsTextBox();
            find.Text = "scene";
            var next = Find(transcriptWindow, "TranscriptFindNext").AsButton();
            foreach (var expected in new[] { "1 of 2", "2 of 2", "1 of 2" })
            {
                next.Invoke();
                WaitFor(() => Find(transcriptWindow, "TranscriptFindStatus").Name == expected, "Find next did not advance or wrap.");
            }
            Mouse.Click(find.GetClickablePoint());
            Chord(transcriptWindow, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
            Keyboard.Type(VirtualKeyShort.KEY_S);
            WaitFor(() => find.Text == "s", "Typing in Find did not edit its text.");

            Mouse.Click(Find(transcriptWindow, secondWordId).GetClickablePoint(), MouseButton.Right);
            AutomationElement? edit = null;
            WaitFor(() => (edit = desktop.FindFirstDescendant(c => c.ByProcessId(processId).And(c.ByAutomationId("EditTranscriptWordMenu")))) is not null,
                "The transcript word actions did not open.");
            edit!.AsMenuItem().Invoke();
            WaitFor(() => transcriptWindow.ModalWindows.Any(dialog => dialog.Title == "Edit Word"), "Edit Word did not open.");
            var wordEditor = transcriptWindow.ModalWindows.Single(dialog => dialog.Title == "Edit Word");
            var input = Find(wordEditor, "TranscriptWordText").AsTextBox();
            input.Text = "";
            Chord(wordEditor, VirtualKeyShort.RETURN);
            WaitFor(() => Find(wordEditor, "DialogValidationError").Name == "Enter a word.", "An empty word edit was accepted.");
            input.Text = "cut";
            Chord(wordEditor, VirtualKeyShort.RETURN);
            WaitFor(() => transcriptWindow.FindFirstDescendant(c => c.ByAutomationId(secondWordId))?.Name == "cut",
                "The edited word did not appear in the transcript.");
            transcriptWindow.Close();

            SaveButton(window).Invoke();
            WaitFor(() => !window.Title.Contains('*'), "The transcript edit did not save.");
            var saved = await ProjectStore.LoadAsync(path);
            Assert.Equal(2, saved.Tracks[0].Clips.Count);
            var words = saved.Tracks[0].Clips.Single(clip => clip.Id == interview.Id).Transcript!.Words;
            Assert.Equal(new[] { ("Scene", 5L), ("cut", 25L) }, words.Select(word => (word.Word, word.StartFrame)));
            running.CloseGracefully();
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }
}
