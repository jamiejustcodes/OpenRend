using System;
using System.IO;
using System.Linq;
using OpenRend.Core;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class EnvelopeEditorTests
{
    private static (ProjectDocument doc, TimelineEditor editor, TimelineClip clip) CreateTestProject()
    {
        var doc = new ProjectDocument
        {
            Width = 1920,
            Height = 1080,
            FrameRate = new Rational(30, 1)
        };

        var media = new MediaAsset
        {
            Id = Guid.NewGuid(),
            Path = "test.mp4",
            HasVideo = true,
            DurationSeconds = 30.0,
            FrameRate = 30.0,
            Width = 1920,
            Height = 1080
        };
        doc.Media.Add(media);

        var track = new TimelineTrack
        {
            Kind = TrackKind.Video,
            Name = "Video 1"
        };
        doc.Tracks.Add(track);

        var clip = new TimelineClip
        {
            MediaId = media.Id,
            Name = "Clip 1",
            StartFrame = 0,
            SourceInFrame = 100,
            DurationFrames = 300
        };
        track.Clips.Add(clip);

        var editor = new TimelineEditor(doc);
        return (doc, editor, clip);
    }

    [Fact]
    public void ToggleEnvelope_AddsAndRemovesVelocityAndCompositeEnvelopes()
    {
        var (doc, editor, clip) = CreateTestProject();

        Assert.False(editor.HasClipEnvelope(clip.Id, EnvelopeType.Velocity));
        Assert.False(editor.HasClipEnvelope(clip.Id, EnvelopeType.Composite));

        // Toggle on Velocity
        editor.ToggleClipEnvelope(clip.Id, EnvelopeType.Velocity);
        Assert.True(editor.HasClipEnvelope(clip.Id, EnvelopeType.Velocity));
        var velEnv = editor.GetClipEnvelope(clip.Id, EnvelopeType.Velocity);
        Assert.NotNull(velEnv);
        Assert.Equal(2, velEnv.Points.Count);
        Assert.Equal(1.0, velEnv.Points[0].Value);

        // Toggle on Composite
        editor.ToggleClipEnvelope(clip.Id, EnvelopeType.Composite);
        Assert.True(editor.HasClipEnvelope(clip.Id, EnvelopeType.Composite));

        // Undo removes Composite
        editor.Undo();
        Assert.False(editor.HasClipEnvelope(clip.Id, EnvelopeType.Composite));
        Assert.True(editor.HasClipEnvelope(clip.Id, EnvelopeType.Velocity));

        // Redo restores Composite
        editor.Redo();
        Assert.True(editor.HasClipEnvelope(clip.Id, EnvelopeType.Composite));

        // Toggle off Velocity
        editor.ToggleClipEnvelope(clip.Id, EnvelopeType.Velocity);
        Assert.False(editor.HasClipEnvelope(clip.Id, EnvelopeType.Velocity));
    }

    [Fact]
    public void SetEnvelopePoint_InsertsPointAndOrdersByFrame()
    {
        var (doc, editor, clip) = CreateTestProject();
        editor.AddClipEnvelope(clip.Id, EnvelopeType.Velocity);

        editor.SetEnvelopePoint(clip.Id, EnvelopeType.Velocity, 150, 2.0, EnvelopeCurveType.Fast);

        var env = editor.GetClipEnvelope(clip.Id, EnvelopeType.Velocity);
        Assert.NotNull(env);
        Assert.Equal(3, env.Points.Count);
        Assert.Equal(150, env.Points[1].Frame);
        Assert.Equal(2.0, env.Points[1].Value);
        Assert.Equal(EnvelopeCurveType.Fast, env.Points[1].Curve);

        // Change curve
        editor.SetEnvelopePointCurve(clip.Id, EnvelopeType.Velocity, 150, EnvelopeCurveType.Smooth);
        Assert.Equal(EnvelopeCurveType.Smooth, env.Points[1].Curve);

        // Remove point
        editor.RemoveEnvelopePoint(clip.Id, EnvelopeType.Velocity, 150);
        Assert.Equal(2, env.Points.Count);
    }

    [Fact]
    public void TimelineEvaluator_AppliesVelocityAndCompositeEnvelopes()
    {
        var (doc, editor, clip) = CreateTestProject();

        // Without envelopes: frame 50 -> source frame 100 + 50 = 150, opacity 1.0
        var evalNormal = TimelineEvaluator.Evaluate(doc, 50);
        Assert.Single(evalNormal.Video);
        Assert.Equal(150, evalNormal.Video[0].SourceFrame);
        Assert.Equal(1.0, evalNormal.Video[0].Opacity, 3);

        // Add 200% Velocity envelope: frame 50 -> source frame 100 + 100 = 200
        editor.AddClipEnvelope(clip.Id, EnvelopeType.Velocity);
        editor.SetEnvelopePoint(clip.Id, EnvelopeType.Velocity, 0, 2.0);
        editor.SetEnvelopePoint(clip.Id, EnvelopeType.Velocity, 300, 2.0);

        // Add 50% Composite envelope: opacity becomes 0.5
        editor.AddClipEnvelope(clip.Id, EnvelopeType.Composite);
        editor.SetEnvelopePoint(clip.Id, EnvelopeType.Composite, 0, 0.5);
        editor.SetEnvelopePoint(clip.Id, EnvelopeType.Composite, 300, 0.5);

        var evalMod = TimelineEvaluator.Evaluate(doc, 50);
        Assert.Single(evalMod.Video);
        Assert.Equal(200, evalMod.Video[0].SourceFrame);
        Assert.Equal(0.5, evalMod.Video[0].Opacity, 3);
    }

    [Fact]
    public void TrackEnvelope_EditorAddsRemovesAndEvaluatesTrackEnvelopes()
    {
        var (doc, editor, clip) = CreateTestProject();
        var track = doc.Tracks[0];

        Assert.False(editor.HasTrackEnvelope(track.Id, EnvelopeType.Composite));

        // Add track composite envelope
        editor.AddTrackEnvelope(track.Id, EnvelopeType.Composite);
        Assert.True(editor.HasTrackEnvelope(track.Id, EnvelopeType.Composite));

        var env = editor.GetTrackEnvelope(track.Id, EnvelopeType.Composite);
        Assert.NotNull(env);
        Assert.Equal(2, env.Points.Count);

        // Insert keyframe point at frame 50 with value 0.4
        editor.SetTrackEnvelopePoint(track.Id, EnvelopeType.Composite, 50, 0.4, EnvelopeCurveType.Linear);
        env = editor.GetTrackEnvelope(track.Id, EnvelopeType.Composite);
        Assert.NotNull(env);
        Assert.Equal(3, env.Points.Count);

        // Evaluate timeline at frame 50: clip opacity should be modulated by track composite level (0.4)
        var evaluated = TimelineEvaluator.Evaluate(doc, 50);
        Assert.Single(evaluated.Video);
        Assert.Equal(0.4, evaluated.Video[0].Opacity, 3);

        // Add track mute envelope and set to 1.0 at frame 50
        editor.AddTrackEnvelope(track.Id, EnvelopeType.Mute);
        editor.SetTrackEnvelopePoint(track.Id, EnvelopeType.Mute, 50, 1.0, EnvelopeCurveType.Hold);
        var evalMuted = TimelineEvaluator.Evaluate(doc, 50);
        Assert.Empty(evalMuted.Video); // Track is muted at frame 50

        // Undo restores unmuted state
        editor.Undo();
        var evalUnmuted = TimelineEvaluator.Evaluate(doc, 50);
        Assert.Single(evalUnmuted.Video);
    }
}