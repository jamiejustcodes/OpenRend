using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using OpenRend.App;
using OpenRend.Core;
using OpenRend.Media;
using OpenRend.Timeline;
using OpenRend.UI;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class TrackGroupsEditorTests
{
    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) throw new AggregateException(error);
    }

    private static (ProjectDocument Project, TimelineEditor Editor) CreateProjectWithTracks(int count, TrackKind kind = TrackKind.Audio)
    {
        var project = new ProjectDocument { Name = "Test Project" };
        var editor = new TimelineEditor(project);
        for (var i = 0; i < count; i++)
        {
            editor.AddTrack(kind);
        }
        return (project, editor);
    }

    private static MediaAsset CreateMedia(ProjectDocument project, string name = "Audio.wav", bool hasVideo = false, bool hasAudio = true)
    {
        var media = new MediaAsset
        {
            Id = Guid.NewGuid(),
            Path = $"C:\\Media\\{name}",
            DisplayName = name,
            DurationSeconds = 10.0,
            HasVideo = hasVideo,
            HasAudio = hasAudio
        };
        project.Media.Add(media);
        return media;
    }

    [Fact]
    public void GroupSelectedTracks_ContiguousTracks_CreatesTrackGroupWithDefaultName()
    {
        var (project, editor) = CreateProjectWithTracks(3);
        var t1 = project.Tracks[0];
        var t2 = project.Tracks[1];

        var group = editor.GroupSelectedTracks([t1.Id, t2.Id]);
        Assert.NotNull(group);
        Assert.Equal("Track Group 1", group.Name);
        Assert.Equal(2, group.TrackIds.Count);
        Assert.Contains(t1.Id, group.TrackIds);
        Assert.Contains(t2.Id, group.TrackIds);
        Assert.Single(project.TrackGroups);

        // Group tracks 2 and 3 into second group
        var t3 = project.Tracks[2];
        var group2 = editor.GroupSelectedTracks([t2.Id, t3.Id]);
        Assert.NotNull(group2);
        Assert.Equal("Track Group 2", group2.Name);
        // Track 2 should have been removed from group 1
        Assert.Equal(2, project.TrackGroups.Count);
        Assert.Single(project.TrackGroups[0].TrackIds);
        Assert.Equal(t1.Id, project.TrackGroups[0].TrackIds[0]);
    }

    [Fact]
    public void GroupSelectedTracks_NonContiguousTracks_ThrowsInvalidOperationException()
    {
        var (project, editor) = CreateProjectWithTracks(3);
        var t1 = project.Tracks[0];
        var t3 = project.Tracks[2];

        var ex = Assert.Throws<InvalidOperationException>(() => editor.GroupSelectedTracks([t1.Id, t3.Id]));
        Assert.Contains("Track grouping requires contiguous tracks to be selected", ex.Message);
        Assert.Empty(project.TrackGroups);
    }

    [Fact]
    public void SetTrackGroupMute_MutesAllMemberTracksAndEvaluatorMutesOutput()
    {
        var (project, editor) = CreateProjectWithTracks(3);
        var media = CreateMedia(project);

        var c1 = editor.PlaceClip(media.Id, project.Tracks[0].Id, 0, 100);
        var c2 = editor.PlaceClip(media.Id, project.Tracks[1].Id, 0, 100);
        var c3 = editor.PlaceClip(media.Id, project.Tracks[2].Id, 0, 100);

        var group = editor.GroupSelectedTracks([project.Tracks[0].Id, project.Tracks[1].Id]);

        // Prior to mute: all 3 tracks are evaluated
        var frame0 = TimelineEvaluator.Evaluate(project, 50);
        Assert.Equal(3, frame0.Audio.Count);

        // Mute the track group
        editor.SetTrackGroupMute(group.Id, true);
        Assert.True(project.TrackGroups[0].IsMuted);

        var frameMuted = TimelineEvaluator.Evaluate(project, 50);
        Assert.Single(frameMuted.Audio);
        Assert.Equal(c3.Id, frameMuted.Audio[0].Clip.Id);

        // Unmute the track group
        editor.SetTrackGroupMute(group.Id, false);
        Assert.False(project.TrackGroups[0].IsMuted);

        var frameUnmuted = TimelineEvaluator.Evaluate(project, 50);
        Assert.Equal(3, frameUnmuted.Audio.Count);
    }

    [Fact]
    public void SetTrackGroupSolo_SolosMemberTracksAndEvaluatorIsolatesAudio()
    {
        var (project, editor) = CreateProjectWithTracks(3);
        var media = CreateMedia(project);

        var c1 = editor.PlaceClip(media.Id, project.Tracks[0].Id, 0, 100);
        var c2 = editor.PlaceClip(media.Id, project.Tracks[1].Id, 0, 100);
        var c3 = editor.PlaceClip(media.Id, project.Tracks[2].Id, 0, 100);

        var group = editor.GroupSelectedTracks([project.Tracks[0].Id, project.Tracks[1].Id]);

        // Solo the track group
        editor.SetTrackGroupSolo(group.Id, true);
        Assert.True(project.TrackGroups[0].IsSolo);

        var frameSolo = TimelineEvaluator.Evaluate(project, 50);
        Assert.Equal(2, frameSolo.Audio.Count);
        Assert.Contains(frameSolo.Audio, e => e.Clip.Id == c1.Id);
        Assert.Contains(frameSolo.Audio, e => e.Clip.Id == c2.Id);
        Assert.DoesNotContain(frameSolo.Audio, e => e.Clip.Id == c3.Id);

        // Unsolo
        editor.SetTrackGroupSolo(group.Id, false);
        var frameUnsolo = TimelineEvaluator.Evaluate(project, 50);
        Assert.Equal(3, frameUnsolo.Audio.Count);
    }

    [Fact]
    public void SetTrackGroupCollapsed_TogglesCollapsedStateAndAdjustsTimelineLayout()
    {
        RunSta(() =>
        {
            var (project, editor) = CreateProjectWithTracks(3);
            var group = editor.GroupSelectedTracks([project.Tracks[0].Id, project.Tracks[1].Id]);

            var view = new TimelineView { Project = project };

            // Expanded layout
            var layoutExpanded = view.ComputeTrackLayout();
            Assert.False(layoutExpanded.Tracks[0].IsCollapsed);
            Assert.Equal(TimelineView.TrackHeight, layoutExpanded.Tracks[0].Height);
            Assert.False(layoutExpanded.Tracks[1].IsCollapsed);
            Assert.Equal(TimelineView.TrackHeight, layoutExpanded.Tracks[1].Height);
            Assert.Equal(TimelineView.RulerHeight + 2 * TimelineView.TrackHeight, layoutExpanded.Tracks[2].Y);

            // Collapse the track group
            editor.SetTrackGroupCollapsed(group.Id, true);
            view.Project = project;

            var layoutCollapsed = view.ComputeTrackLayout();
            Assert.True(layoutCollapsed.Tracks[0].IsCollapsed);
            Assert.Equal(0, layoutCollapsed.Tracks[0].Height);
            Assert.True(layoutCollapsed.Tracks[1].IsCollapsed);
            Assert.Equal(0, layoutCollapsed.Tracks[1].Height);

            // Group row height is 26px, track 3 should be placed right below it
            Assert.Equal(TimelineView.RulerHeight + 26.0, layoutCollapsed.Tracks[2].Y);
            Assert.Equal(TimelineView.TrackHeight, layoutCollapsed.Tracks[2].Height);

            // ClipBounds on collapsed track returns Rect.Empty
            var media = CreateMedia(project);
            var clip = new TimelineClip { MediaId = media.Id, StartFrame = 0, DurationFrames = 100 };
            project.Tracks[0].Clips.Add(clip);
            Assert.Equal(System.Windows.Rect.Empty, view.ClipBounds(project.Tracks[0].Id, clip));

            // Expand again
            editor.SetTrackGroupCollapsed(group.Id, false);
            view.Project = project;
            var layoutRestored = view.ComputeTrackLayout();
            Assert.False(layoutRestored.Tracks[0].IsCollapsed);
            Assert.Equal(TimelineView.TrackHeight, layoutRestored.Tracks[0].Height);
        });
    }

    [Fact]
    public void RemoveTrackFromGroup_And_UngroupTracks_WithUndoRedo()
    {
        var (project, editor) = CreateProjectWithTracks(3);
        var t1 = project.Tracks[0];
        var t2 = project.Tracks[1];
        var group = editor.GroupSelectedTracks([t1.Id, t2.Id]);

        // Remove track 1
        editor.RemoveTrackFromGroup(t1.Id);
        Assert.Single(project.TrackGroups);
        Assert.Single(project.TrackGroups[0].TrackIds);
        Assert.Equal(t2.Id, project.TrackGroups[0].TrackIds[0]);

        // Undo removal
        editor.Undo();
        Assert.Equal(2, project.TrackGroups[0].TrackIds.Count);

        // Redo removal
        editor.Redo();
        Assert.Single(project.TrackGroups[0].TrackIds);

        // Rename group
        editor.SetTrackGroupName(group.Id, "Stems Submix");
        Assert.Equal("Stems Submix", project.TrackGroups[0].Name);

        editor.Undo();
        Assert.Equal("Track Group 1", project.TrackGroups[0].Name);

        // Ungroup tracks
        editor.UngroupTracks([t2.Id]);
        Assert.Empty(project.TrackGroups);

        editor.Undo();
        Assert.Single(project.TrackGroups);
    }

    [Fact]
    public void DeleteTrack_CleansUpTrackGroupMembership()
    {
        var (project, editor) = CreateProjectWithTracks(2);
        var t1 = project.Tracks[0];
        var t2 = project.Tracks[1];
        var group = editor.GroupSelectedTracks([t1.Id, t2.Id]);
        Assert.Single(project.TrackGroups);

        // Delete track 1
        editor.DeleteTrack(t1.Id);
        Assert.Single(project.TrackGroups);
        Assert.Single(project.TrackGroups[0].TrackIds);
        Assert.Equal(t2.Id, project.TrackGroups[0].TrackIds[0]);

        // Delete track 2 -> empty group is removed
        editor.DeleteTrack(t2.Id);
        Assert.Empty(project.TrackGroups);
    }

    [Fact]
    public void Validation_And_Serialization_And_EditDetailsView()
    {
        var (project, editor) = CreateProjectWithTracks(3);
        var t1 = project.Tracks[0];
        var t2 = project.Tracks[1];
        var group = editor.GroupSelectedTracks([t1.Id, t2.Id], "Custom Stems");

        // Validate succeeds
        project.Validate();

        // JSON Roundtrip
        var json = JsonSerializer.Serialize(project);
        var deserialized = JsonSerializer.Deserialize<ProjectDocument>(json);
        Assert.NotNull(deserialized);
        deserialized.Validate();
        Assert.Single(deserialized.TrackGroups);
        Assert.Equal("Custom Stems", deserialized.TrackGroups[0].Name);
        Assert.Equal(2, deserialized.TrackGroups[0].TrackIds.Count);

        // Validation catches non-contiguous tracks in TrackGroup
        var t3 = project.Tracks[2];
        project.TrackGroups[0].TrackIds = [t1.Id, t3.Id];
        var ex = Assert.Throws<InvalidDataException>(() => project.Validate());
        Assert.Contains("Track grouping requires contiguous tracks to be selected", ex.Message);

        // EditDetailsWindow TSV Export
        RunSta(() =>
        {
            var mediaService = new OpenRend.Media.MediaService();
            var renderer = new OpenRend.Media.TimelineRenderService(mediaService);
            var vm = new EditorViewModel(mediaService, renderer, new OpenRend.Media.ExportService(renderer));
            var trk1 = vm.Editor.AddTrack(TrackKind.Video);
            var trk2 = vm.Editor.AddTrack(TrackKind.Audio);
            var grp = vm.Editor.GroupSelectedTracks([trk1.Id, trk2.Id], "Custom Stems");
            Assert.NotNull(grp);

            var window = new EditDetailsWindow(vm, () => { });
            window.ShowComboBox.SelectedIndex = 3; // "Track Groups"
            Assert.Equal("Track Groups", (window.ShowComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString());

            window.RefreshGrid();
            Assert.Single(window.CurrentTrackGroupRows);
            Assert.Equal("Custom Stems", window.CurrentTrackGroupRows[0].Name);
            Assert.Equal(2, window.CurrentTrackGroupRows[0].TrackCount);

            var tsv = window.GetTableText(selectedOnly: false);
            Assert.Contains("Group\tName\tTracks\tMute\tSolo\tCollapsed", tsv);
            Assert.Contains("1\tCustom Stems", tsv);
        });
    }
}

