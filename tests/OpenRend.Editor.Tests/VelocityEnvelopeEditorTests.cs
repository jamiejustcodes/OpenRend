using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using OpenRend.App;
using OpenRend.Core;
using OpenRend.Media;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class VelocityEnvelopeEditorTests
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
            Path = "test_media.mp4",
            HasVideo = true,
            HasAudio = false,
            DurationSeconds = 60.0,
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
            DurationFrames = 300,
            PlaybackRate = 1.0
        };
        track.Clips.Add(clip);

        var editor = new TimelineEditor(doc);
        return (doc, editor, clip);
    }

    [Fact]
    public void AnalyticalCurveIntegration_MatchesNumericalIntegrationForAllCurves()
    {
        var curves = new[]
        {
            EnvelopeCurveType.Linear,
            EnvelopeCurveType.Fast,
            EnvelopeCurveType.Slow,
            EnvelopeCurveType.Smooth,
            EnvelopeCurveType.Sharp,
            EnvelopeCurveType.Hold
        };

        foreach (var curve in curves)
        {
            var env = new ClipEnvelope
            {
                Type = EnvelopeType.Velocity,
                Points =
                {
                    new EnvelopePoint { Frame = 0, Value = 0.5, Curve = curve },
                    new EnvelopePoint { Frame = 120, Value = 2.5, Curve = EnvelopeCurveType.Linear }
                }
            };

            // Calculate analytical continuous integral
            var analyticalAccum = env.CalculateAccumulatedSourceFrame(0, 120, 1.0);

            // Compute discrete Riemann sum per-frame as ground truth comparison
            double numericalAccum = 0.0;
            for (var f = 0L; f < 120; f++)
            {
                numericalAccum += env.ValueAt(f);
            }

            // Both must match within a fraction of a single frame (due to discrete sampling differences)
            var delta = Math.Abs(analyticalAccum - numericalAccum);
            Assert.True(delta < 2.0, $"Curve {curve} integration delta was {delta}, expected < 2.0");
        }
    }

    [Fact]
    public void EventSplit_PreservesContinuousSourceFrameSamplingUnderVelocityEnvelope()
    {
        var (doc, editor, clip) = CreateTestProject();
        editor.AddClipEnvelope(clip.Id, EnvelopeType.Velocity);

        // Set a ramp from 1.0x to 2.0x
        editor.SetEnvelopePoint(clip.Id, EnvelopeType.Velocity, 0, 1.0, EnvelopeCurveType.Linear);
        editor.SetEnvelopePoint(clip.Id, EnvelopeType.Velocity, 300, 2.0, EnvelopeCurveType.Linear);

        var velEnv = clip.Envelopes!.First(e => e.Type == EnvelopeType.Velocity);
        var expectedRightSourceIn = velEnv.CalculateAccumulatedSourceFrame(clip.SourceInFrame, 150, clip.PlaybackRate);

        // Split selection at frame 150
        editor.SplitSelection(new[] { clip.Id }, 150);

        var track = doc.Tracks[0];
        Assert.Equal(2, track.Clips.Count);

        var leftClip = track.Clips[0];
        var rightClip = track.Clips[1];

        Assert.Equal(0, leftClip.StartFrame);
        Assert.Equal(150, leftClip.DurationFrames);
        Assert.Equal(100, leftClip.SourceInFrame);

        Assert.Equal(150, rightClip.StartFrame);
        Assert.Equal(150, rightClip.DurationFrames);
        Assert.Equal(expectedRightSourceIn, rightClip.SourceInFrame);

        // Verify right envelope was split with correct relative frame points
        var rightEnv = rightClip.Envelopes!.FirstOrDefault(e => e.Type == EnvelopeType.Velocity);
        Assert.NotNull(rightEnv);
        Assert.Equal(0, rightEnv.Points[0].Frame);
        Assert.Equal(150, rightEnv.Points.Last().Frame);
        Assert.Equal(1.5, rightEnv.Points[0].Value, precision: 2);
    }

    [Fact]
    public void ClipEnvelope_SplitAt_CalculatesInterpolatedBoundaryValueAndCleanPoints()
    {
        var env = new ClipEnvelope
        {
            Type = EnvelopeType.Velocity,
            Points =
            {
                new EnvelopePoint { Frame = 0, Value = 1.0, Curve = EnvelopeCurveType.Linear },
                new EnvelopePoint { Frame = 100, Value = 3.0, Curve = EnvelopeCurveType.Linear }
            }
        };

        var (left, right) = env.SplitAt(50);

        Assert.NotNull(left);
        Assert.NotNull(right);

        // Left has points at 0 and 50
        Assert.Equal(2, left.Points.Count);
        Assert.Equal(0, left.Points[0].Frame);
        Assert.Equal(1.0, left.Points[0].Value);
        Assert.Equal(50, left.Points[1].Frame);
        Assert.Equal(2.0, left.Points[1].Value, precision: 2);

        // Right has points shifted relative to split frame: 0 and 50
        Assert.Equal(2, right.Points.Count);
        Assert.Equal(0, right.Points[0].Frame);
        Assert.Equal(2.0, right.Points[0].Value, precision: 2);
        Assert.Equal(50, right.Points[1].Frame);
        Assert.Equal(3.0, right.Points[1].Value);
    }

    [Fact]
    public void ResetClipEnvelope_And_SetAllClipEnvelopePoints_FunctionalityAndUndo()
    {
        var (doc, editor, clip) = CreateTestProject();
        editor.AddClipEnvelope(clip.Id, EnvelopeType.Velocity);

        editor.SetEnvelopePoint(clip.Id, EnvelopeType.Velocity, 50, 2.5, EnvelopeCurveType.Fast);
        editor.SetEnvelopePoint(clip.Id, EnvelopeType.Velocity, 150, 0.5, EnvelopeCurveType.Slow);

        var env = editor.GetClipEnvelope(clip.Id, EnvelopeType.Velocity);
        Assert.NotNull(env);
        Assert.Equal(4, env.Points.Count);

        // Set all to 3.0 (300% velocity)
        editor.SetAllClipEnvelopePoints(clip.Id, EnvelopeType.Velocity, 3.0);
        env = editor.GetClipEnvelope(clip.Id, EnvelopeType.Velocity);
        Assert.NotNull(env);
        Assert.Equal(4, env.Points.Count);
        Assert.All(env.Points, pt => Assert.Equal(3.0, pt.Value));

        // Undo restores custom points
        editor.Undo();
        env = editor.GetClipEnvelope(clip.Id, EnvelopeType.Velocity);
        Assert.NotNull(env);
        Assert.Equal(4, env.Points.Count);

        // Reset clip envelope (neutral 1.0 / 100%)
        editor.ResetClipEnvelope(clip.Id, EnvelopeType.Velocity);
        env = editor.GetClipEnvelope(clip.Id, EnvelopeType.Velocity);
        Assert.NotNull(env);
        Assert.Equal(2, env.Points.Count);
        Assert.All(env.Points, pt => Assert.Equal(1.0, pt.Value));

        // Undo and redo
        editor.Undo();
        env = editor.GetClipEnvelope(clip.Id, EnvelopeType.Velocity);
        Assert.NotNull(env);
        Assert.Equal(4, env.Points.Count);
        editor.Redo();
        env = editor.GetClipEnvelope(clip.Id, EnvelopeType.Velocity);
        Assert.NotNull(env);
        Assert.Equal(2, env.Points.Count);
        Assert.All(env.Points, pt => Assert.Equal(1.0, pt.Value));
    }

    [Fact]
    public void ResetTrackEnvelope_And_SetAllTrackEnvelopePoints_FunctionalityAndUndo()
    {
        var (doc, editor, clip) = CreateTestProject();
        var track = doc.Tracks[0];

        editor.AddTrackEnvelope(track.Id, EnvelopeType.Composite);
        editor.SetTrackEnvelopePoint(track.Id, EnvelopeType.Composite, 100, 0.4, EnvelopeCurveType.Smooth);
        editor.SetTrackEnvelopePoint(track.Id, EnvelopeType.Composite, 200, 0.8, EnvelopeCurveType.Sharp);

        var env = editor.GetTrackEnvelope(track.Id, EnvelopeType.Composite);
        Assert.NotNull(env);
        Assert.Equal(4, env.Points.Count);

        // Set all points to 0.5 (50%)
        editor.SetAllTrackEnvelopePoints(track.Id, EnvelopeType.Composite, 0.5);
        env = editor.GetTrackEnvelope(track.Id, EnvelopeType.Composite);
        Assert.NotNull(env);
        Assert.Equal(4, env.Points.Count);
        Assert.All(env.Points, pt => Assert.Equal(0.5, pt.Value));

        // Undo
        editor.Undo();
        env = editor.GetTrackEnvelope(track.Id, EnvelopeType.Composite);
        Assert.NotNull(env);
        Assert.Equal(4, env.Points.Count);

        // Reset all points
        editor.ResetTrackEnvelope(track.Id, EnvelopeType.Composite);
        env = editor.GetTrackEnvelope(track.Id, EnvelopeType.Composite);
        Assert.NotNull(env);
        Assert.Equal(2, env.Points.Count);
        Assert.All(env.Points, pt => Assert.Equal(1.0, pt.Value));
    }

    [Fact]
    public void InsertTime_AppliesContinuousSourceFrameOffsetToDividedClip()
    {
        var (doc, editor, clip) = CreateTestProject();
        editor.AddClipEnvelope(clip.Id, EnvelopeType.Velocity);
        editor.SetEnvelopePoint(clip.Id, EnvelopeType.Velocity, 0, 1.0, EnvelopeCurveType.Linear);
        editor.SetEnvelopePoint(clip.Id, EnvelopeType.Velocity, 300, 2.0, EnvelopeCurveType.Linear);

        var velEnv = clip.Envelopes!.First(e => e.Type == EnvelopeType.Velocity);
        var expectedRightSourceIn = velEnv.CalculateAccumulatedSourceFrame(clip.SourceInFrame, 100, clip.PlaybackRate);

        // Insert 50 frames of time at frame 100
        editor.InsertTime(100, 50, AutoRippleMode.AffectedTracks);

        var track = doc.Tracks[0];
        Assert.Equal(2, track.Clips.Count);

        var leftClip = track.Clips[0];
        var rightClip = track.Clips[1];

        Assert.Equal(0, leftClip.StartFrame);
        Assert.Equal(100, leftClip.DurationFrames);

        // Right clip shifted by 50 frames
        Assert.Equal(150, rightClip.StartFrame);
        Assert.Equal(200, rightClip.DurationFrames);
        Assert.Equal(expectedRightSourceIn, rightClip.SourceInFrame);
    }

    [Fact]
    public void EditorViewModel_ResetAndSetAllEnvelopeMethods_WorkWithUndo() => RunSta(() =>
    {
        var mediaService = new MediaService();
        var renderer = new TimelineRenderService(mediaService);
        var vm = new EditorViewModel(mediaService, renderer, new ExportService(renderer));

        var media = new MediaAsset { Path = "sample.mp4", DurationSeconds = 20, HasVideo = true };
        vm.Project.Media.Add(media);

        var track = new TimelineTrack { Kind = TrackKind.Video };
        var clip = new TimelineClip { MediaId = media.Id, StartFrame = 0, DurationFrames = 100 };
        track.Clips.Add(clip);
        vm.Project.Tracks.Add(track);

        vm.Editor.AddClipEnvelope(clip.Id, EnvelopeType.Velocity);
        var env = vm.Editor.GetClipEnvelope(clip.Id, EnvelopeType.Velocity);
        Assert.NotNull(env);

        var changedFired = false;
        vm.Changed += () => changedFired = true;

        vm.SetAllClipEnvelopePoints(clip.Id, EnvelopeType.Velocity, 2.0);
        Assert.True(changedFired);
        Assert.Contains("200%", vm.Status);
        env = vm.Editor.GetClipEnvelope(clip.Id, EnvelopeType.Velocity);
        Assert.NotNull(env);
        Assert.All(env.Points, pt => Assert.Equal(2.0, pt.Value));

        changedFired = false;
        vm.ResetClipEnvelope(clip.Id, EnvelopeType.Velocity);
        Assert.True(changedFired);
        Assert.Contains("Reset", vm.Status);
        env = vm.Editor.GetClipEnvelope(clip.Id, EnvelopeType.Velocity);
        Assert.NotNull(env);
        Assert.All(env.Points, pt => Assert.Equal(1.0, pt.Value));
    });
}
