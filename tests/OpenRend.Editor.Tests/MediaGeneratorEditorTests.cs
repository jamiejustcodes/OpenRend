using System;
using System.Linq;
using OpenRend.Core;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class MediaGeneratorEditorTests
{
    [Fact]
    public void TimelineEditor_AddGeneratedMediaClip_CreatesAssetAndClip()
    {
        var doc = new ProjectDocument
        {
            Width = 1920,
            Height = 1080,
            FrameRate = new Rational(30, 1)
        };
        var track = new TimelineTrack { Id = Guid.NewGuid(), Kind = TrackKind.Video, Name = "Video 1" };
        doc.Tracks.Add(track);
        var editor = new TimelineEditor(doc);

        var clip = editor.AddGeneratedMediaClip(track.Id, 0, MediaGeneratorKind.TestPattern, "SMPTE Bars (NTSC)", 150);

        Assert.NotNull(clip);
        Assert.Equal(0, clip.StartFrame);
        Assert.Equal(150, clip.DurationFrames);

        var asset = doc.Media.FirstOrDefault(m => m.Id == clip.MediaId);
        Assert.NotNull(asset);
        Assert.True(asset.IsGenerated);
        Assert.NotNull(asset.Generator);
        Assert.Equal(MediaGeneratorKind.TestPattern, asset.Generator.Kind);
        Assert.Equal("SMPTE Bars (NTSC)", asset.Generator.PresetName);
        Assert.StartsWith("openrend://generator", asset.Path);
    }

    [Fact]
    public void TimelineEditor_AddGeneratedMediaClip_SupportsUndoAndRedo()
    {
        var doc = new ProjectDocument();
        var track = new TimelineTrack { Id = Guid.NewGuid(), Kind = TrackKind.Video };
        doc.Tracks.Add(track);
        var editor = new TimelineEditor(doc);

        var clip = editor.AddGeneratedMediaClip(track.Id, 30, MediaGeneratorKind.SolidColor, "Red", 90);
        Assert.Single(doc.Tracks[0].Clips);
        Assert.Single(doc.Media);

        Assert.True(editor.CanUndo);
        editor.Undo();
        Assert.Empty(doc.Tracks[0].Clips);
        Assert.Empty(doc.Media);

        Assert.True(editor.CanRedo);
        editor.Redo();
        Assert.Single(doc.Tracks[0].Clips);
        Assert.Single(doc.Media);
    }

    [Fact]
    public void TimelineEditor_AddGeneratedMediaClip_RejectsAudioTrack()
    {
        var doc = new ProjectDocument();
        var audioTrack = new TimelineTrack { Id = Guid.NewGuid(), Kind = TrackKind.Audio };
        doc.Tracks.Add(audioTrack);
        var editor = new TimelineEditor(doc);

        Assert.Throws<InvalidOperationException>(() =>
            editor.AddGeneratedMediaClip(audioTrack.Id, 0, MediaGeneratorKind.SolidColor, "Blue", 60));
    }
}

