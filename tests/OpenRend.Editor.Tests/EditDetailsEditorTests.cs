using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Controls;
using OpenRend.App;
using OpenRend.Core;
using OpenRend.Media;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class EditDetailsEditorTests
{
    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new AggregateException(error);
    }

    private static (EditorViewModel vm, MediaAsset media1, MediaAsset media2) CreateTestProject()
    {
        var mediaService = new MediaService();
        var renderer = new TimelineRenderService(mediaService);
        var vm = new EditorViewModel(mediaService, renderer, new ExportService(renderer));

        var asset1 = new MediaAsset
        {
            Id = Guid.NewGuid(),
            Path = @"C:\Media\intro_video.mp4",
            Width = 1920,
            Height = 1080,
            FrameRate = 30.0,
            DurationSeconds = 10.0,
            HasVideo = true,
            HasAudio = true
        };
        var asset2 = new MediaAsset
        {
            Id = Guid.NewGuid(),
            Path = @"C:\Media\dialogue_track.wav",
            DurationSeconds = 15.0,
            HasVideo = false,
            HasAudio = true
        };

        vm.Project.Media.Add(asset1);
        vm.Project.Media.Add(asset2);

        // Track 1 (Video)
        var clip1 = new TimelineClip
        {
            MediaId = asset1.Id,
            Name = "Intro Title",
            StartFrame = 0,
            DurationFrames = 90,
            SourceInFrame = 15,
            PlaybackRate = 1.0,
            IsLocked = false,
            IsMuted = false
        };
        var clip2 = new TimelineClip
        {
            MediaId = asset1.Id,
            Name = "B-Roll Cut",
            StartFrame = 90,
            DurationFrames = 150,
            SourceInFrame = 0,
            PlaybackRate = 1.5,
            IsLocked = true,
            IsMuted = true
        };
        vm.Project.Tracks[0].Clips.Add(clip1);
        vm.Project.Tracks[0].Clips.Add(clip2);

        // Track 2 (Audio)
        var audioClip = new TimelineClip
        {
            MediaId = asset2.Id,
            Name = "Dialogue Stem",
            StartFrame = 30,
            DurationFrames = 240,
            SourceInFrame = 0,
            PlaybackRate = 1.0,
            IsLocked = false,
            IsMuted = false
        };
        vm.Project.Tracks[1].Clips.Add(audioClip);

        return (vm, asset1, asset2);
    }

    [Fact]
    public void EditDetailsWindow_PopulatesEventRows_ReflectsTimelineStructure()
    {
        RunSta(() =>
        {
            var (vm, _, _) = CreateTestProject();
            var window = new EditDetailsWindow(vm, () => { });

            Assert.Equal(3, window.CurrentEventRows.Count);

            var first = window.CurrentEventRows[0];
            Assert.Equal("1 - Video 1", first.TrackDisplay);
            Assert.Equal(0, first.EventIndex);
            Assert.Equal("Intro Title", first.Name);
            Assert.Equal(0, first.StartFrame);
            Assert.Equal(90, first.EndFrame);
            Assert.Equal(90, first.LengthFrames);
            Assert.Equal("1.000", first.PlaybackRateText);
            Assert.Equal("No", first.MuteText);
            Assert.Equal("No", first.LockText);
            Assert.Equal("intro_video.mp4", first.MediaFile);

            var second = window.CurrentEventRows[1];
            Assert.Equal("B-Roll Cut", second.Name);
            Assert.Equal(90, second.StartFrame);
            Assert.Equal(240, second.EndFrame);
            Assert.Equal("1.500", second.PlaybackRateText);
            Assert.Equal("Yes", second.MuteText);
            Assert.Equal("Yes", second.LockText);

            var third = window.CurrentEventRows[2];
            Assert.Equal("2 - Audio 1", third.TrackDisplay);
            Assert.Equal("Dialogue Stem", third.Name);
            Assert.Equal(30, third.StartFrame);
            Assert.Equal(270, third.EndFrame);
            Assert.Equal("dialogue_track.wav", third.MediaFile);
        });
    }

    [Fact]
    public void EditDetailsWindow_ShowMarkers_ReflectsProjectMarkers()
    {
        RunSta(() =>
        {
            var (vm, _, _) = CreateTestProject();
            vm.Editor.AddMarker(0, "Scene 1 Start", colorId: 0);
            vm.Editor.AddMarker(90, "Cut to B-Roll", colorId: 2);
            vm.Editor.AddMarker(240, "Dialogue End", colorId: 3);

            var window = new EditDetailsWindow(vm, () => { });
            window.ShowComboBox.SelectedIndex = 1; // Markers mode

            Assert.Equal(3, window.CurrentMarkerRows.Count);

            var m1 = window.CurrentMarkerRows[0];
            Assert.Equal(1, m1.Index);
            Assert.Equal(0, m1.Frame);
            Assert.Equal("Scene 1 Start", m1.Label);
            Assert.Equal("Blue", m1.ColorName);

            var m2 = window.CurrentMarkerRows[1];
            Assert.Equal(2, m2.Index);
            Assert.Equal(90, m2.Frame);
            Assert.Equal("Cut to B-Roll", m2.Label);
            Assert.Equal("Green", m2.ColorName);

            var m3 = window.CurrentMarkerRows[2];
            Assert.Equal(3, m3.Index);
            Assert.Equal(240, m3.Frame);
            Assert.Equal("Dialogue End", m3.Label);
            Assert.Equal("Red", m3.ColorName);
        });
    }

    [Fact]
    public void EditDetailsWindow_TrackFilterAndSearch_FiltersRows()
    {
        RunSta(() =>
        {
            var (vm, _, _) = CreateTestProject();
            var window = new EditDetailsWindow(vm, () => { });

            // Initially 3 events
            Assert.Equal(3, window.CurrentEventRows.Count);

            // Filter to selected track only (ActiveTrackIndex = 0 => Track 1 Video)
            vm.ActiveTrackIndex = 0;
            window.SelectedTrackOnlyCheckBox.IsChecked = true;
            Assert.Equal(2, window.CurrentEventRows.Count);
            Assert.All(window.CurrentEventRows, r => Assert.Equal(1, r.TrackIndex));

            // Change active track to 1 (Track 2 Audio)
            vm.ActiveTrackIndex = 1;
            window.RefreshGrid();
            Assert.Single(window.CurrentEventRows);
            Assert.Equal("Dialogue Stem", window.CurrentEventRows[0].Name);

            // Uncheck track filter, test text search
            window.SelectedTrackOnlyCheckBox.IsChecked = false;
            window.SearchTextBox.Text = "B-Roll";
            Assert.Single(window.CurrentEventRows);
            Assert.Equal("B-Roll Cut", window.CurrentEventRows[0].Name);

            // Search by media extension
            window.SearchTextBox.Text = ".wav";
            Assert.Single(window.CurrentEventRows);
            Assert.Equal("Dialogue Stem", window.CurrentEventRows[0].Name);
        });
    }

    [Fact]
    public void EditDetailsWindow_CopyTableToClipboard_GeneratesValidTSV()
    {
        RunSta(() =>
        {
            var (vm, _, _) = CreateTestProject();
            var window = new EditDetailsWindow(vm, () => { });

            // Events mode TSV
            var eventsTsv = window.GetTableText();
            var eventLines = eventsTsv.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(4, eventLines.Length); // Header + 3 rows
            Assert.StartsWith("Track\tEvent\tStart\tEnd\tLength\tName\tActive Take\tSource In\tRate\tMute\tLock\tMedia File", eventLines[0]);
            Assert.Contains("Intro Title\tIntro Title", eventLines[1]);
            Assert.Contains("intro_video.mp4", eventLines[1]);

            // Markers mode TSV
            vm.Editor.AddMarker(45, "Action Marker", colorId: 1);
            window.ShowComboBox.SelectedIndex = 1; // Markers mode
            var markersTsv = window.GetTableText();
            var markerLines = markersTsv.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(2, markerLines.Length); // Header + 1 marker
            Assert.Equal("Marker\tPosition\tFrame\tName\tColor", markerLines[0]);
            Assert.Contains("Action Marker\tOrange", markerLines[1]);
        });
    }

    [Fact]
    public void EditDetailsWindow_DeleteSelectedItems_RemovesFromProjectWithUndo()
    {
        RunSta(() =>
        {
            var (vm, _, _) = CreateTestProject();
            var changedCount = 0;
            var window = new EditDetailsWindow(vm, () => changedCount++);

            Assert.Equal(2, vm.Project.Tracks[0].Clips.Count);

            // Test 1: Attempting to delete a locked clip is safely prevented
            window.EventsListView.SelectedItems.Add(window.CurrentEventRows[1]); // B-Roll Cut is locked
            window.DeleteSelectedItems();
            Assert.Equal("Cannot delete locked event(s).", window.StatusTextBlock.Text);
            Assert.Equal(2, vm.Project.Tracks[0].Clips.Count);

            // Test 2: Deleting unlocked clip succeeds
            window.EventsListView.SelectedItems.Clear();
            window.EventsListView.SelectedItems.Add(window.CurrentEventRows[0]); // Intro Title is unlocked
            window.DeleteSelectedItems();

            Assert.True(changedCount > 0);
            Assert.Single(vm.Project.Tracks[0].Clips);
            Assert.Equal("B-Roll Cut", vm.Project.Tracks[0].Clips[0].Name);

            // Test 3: Undo restores the deleted clip
            vm.Editor.Undo();
            window.RefreshGrid();
            Assert.Equal(2, vm.Project.Tracks[0].Clips.Count);
            Assert.Equal(3, window.CurrentEventRows.Count);
        });
    }
}
