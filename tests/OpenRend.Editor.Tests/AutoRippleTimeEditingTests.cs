using System;
using System.Linq;
using System.Threading;
using OpenRend.App;
using OpenRend.Core;
using OpenRend.Media;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class AutoRippleTimeEditingTests
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

    [Fact]
    public void InsertTime_AllTracksAndMarkers_ShiftsClipsMarkersTrackAndBusEnvelopes_AndUndoes()
    {
        var doc = new ProjectDocument();
        var media = new MediaAsset { Path = "sample.mp4", DurationSeconds = 20, HasVideo = true, HasAudio = true };
        doc.Media.Add(media);

        var track1 = new TimelineTrack { Kind = TrackKind.Video };
        var clip1A = new TimelineClip { MediaId = media.Id, StartFrame = 0, DurationFrames = 30 };
        var clip1B = new TimelineClip { MediaId = media.Id, StartFrame = 40, DurationFrames = 30 };
        track1.Clips.Add(clip1A);
        track1.Clips.Add(clip1B);
        track1.Envelopes =
        [
            new ClipEnvelope
            {
                Type = EnvelopeType.Composite,
                IsEnabled = true,
                Points =
                [
                    new EnvelopePoint { Frame = 10, Value = 0.5 },
                    new EnvelopePoint { Frame = 45, Value = 1.0 }
                ]
            }
        ];
        doc.Tracks.Add(track1);

        var track2 = new TimelineTrack { Kind = TrackKind.Audio };
        var clip2 = new TimelineClip { MediaId = media.Id, StartFrame = 10, DurationFrames = 40 }; // 10..50 straddles 35
        track2.Clips.Add(clip2);
        doc.Tracks.Add(track2);

        var bus = new AudioBusTrack
        {
            Name = "Bus A",
            Envelopes =
            [
                new ClipEnvelope
                {
                    Type = EnvelopeType.Volume,
                    IsEnabled = true,
                    Points =
                    [
                        new EnvelopePoint { Frame = 15, Value = 0.8 },
                        new EnvelopePoint { Frame = 60, Value = 0.4 }
                    ]
                }
            ]
        };
        doc.AudioBuses = [bus];

        doc.MasterAudioEnvelopes =
        [
            new ClipEnvelope
            {
                Type = EnvelopeType.Volume,
                IsEnabled = true,
                Points =
                [
                    new EnvelopePoint { Frame = 55, Value = 0.9 }
                ]
            }
        ];

        doc.Markers.Add(new TimelineMarker { Frame = 20, Label = "Before" });
        doc.Markers.Add(new TimelineMarker { Frame = 50, Label = "After" });

        var editor = new TimelineEditor(doc);

        // Insert 20 frames at frame 35 with AllTracksAndMarkers
        editor.InsertTime(atFrame: 35, durationFrames: 20, rippleMode: AutoRippleMode.AllTracksAndMarkers);

        // Verify Track 1
        var t1 = editor.Project.Tracks[0];
        Assert.Equal(0, t1.Clips.First(c => c.Id == clip1A.Id).StartFrame); // Untouched (< 35)
        Assert.Equal(60, t1.Clips.First(c => c.Id == clip1B.Id).StartFrame); // 40 + 20 = 60
        Assert.Equal(10, t1.Envelopes![0].Points[0].Frame); // 10 untouched
        Assert.Equal(65, t1.Envelopes![0].Points[1].Frame); // 45 + 20 = 65

        // Verify Track 2 straddling clip was split
        var t2 = editor.Project.Tracks[1];
        Assert.Equal(2, t2.Clips.Count);
        var leftHalf = t2.Clips.First(c => c.StartFrame == 10);
        var rightHalf = t2.Clips.First(c => c.StartFrame == 55); // 35 + 20 = 55
        Assert.Equal(25, leftHalf.DurationFrames); // 35 - 10 = 25
        Assert.Equal(15, rightHalf.DurationFrames); // 40 - 25 = 15

        // Verify Markers
        Assert.Equal(20, editor.Project.Markers.First(m => m.Label == "Before").Frame);
        Assert.Equal(70, editor.Project.Markers.First(m => m.Label == "After").Frame); // 50 + 20 = 70

        // Verify Bus and Master Envelopes
        Assert.Equal(15, editor.Project.AudioBuses![0].Envelopes![0].Points[0].Frame);
        Assert.Equal(80, editor.Project.AudioBuses![0].Envelopes![0].Points[1].Frame); // 60 + 20 = 80
        Assert.Equal(75, editor.Project.MasterAudioEnvelopes![0].Points[0].Frame); // 55 + 20 = 75

        // Verify atomic Undo
        Assert.True(editor.CanUndo);
        editor.Undo();

        t1 = editor.Project.Tracks[0];
        t2 = editor.Project.Tracks[1];
        Assert.Equal(40, t1.Clips.First(c => c.Id == clip1B.Id).StartFrame);
        Assert.Equal(45, t1.Envelopes![0].Points[1].Frame);
        Assert.Single(t2.Clips);
        Assert.Equal(40, t2.Clips[0].DurationFrames);
        Assert.Equal(50, editor.Project.Markers.First(m => m.Label == "After").Frame);
        Assert.Equal(60, editor.Project.AudioBuses![0].Envelopes![0].Points[1].Frame);
        Assert.Equal(55, editor.Project.MasterAudioEnvelopes![0].Points[0].Frame);

        // Verify Redo
        Assert.True(editor.CanRedo);
        editor.Redo();
        t1 = editor.Project.Tracks[0];
        Assert.Equal(60, t1.Clips.First(c => c.Id == clip1B.Id).StartFrame);
        Assert.Equal(65, t1.Envelopes![0].Points[1].Frame);
    }

    [Fact]
    public void DeleteTime_AffectedTracks_CutsMiddleOfClipAndRipplesDownstreamClipsAndEnvelopes()
    {
        var doc = new ProjectDocument();
        var media = new MediaAsset { Path = "sample.mp4", DurationSeconds = 30, HasVideo = true };
        doc.Media.Add(media);

        var track1 = new TimelineTrack { Kind = TrackKind.Video };
        var clip1 = new TimelineClip { MediaId = media.Id, StartFrame = 0, SourceInFrame = 0, DurationFrames = 100 };
        track1.Clips.Add(clip1);
        track1.Envelopes =
        [
            new ClipEnvelope
            {
                Type = EnvelopeType.Composite,
                IsEnabled = true,
                Points =
                [
                    new EnvelopePoint { Frame = 15, Value = 0.5 },
                    new EnvelopePoint { Frame = 45, Value = 0.8 }, // Inside cut
                    new EnvelopePoint { Frame = 80, Value = 1.0 }  // Downstream
                ]
            }
        ];
        doc.Tracks.Add(track1);

        var editor = new TimelineEditor(doc);

        // Delete span [30..60] (duration 30)
        editor.DeleteTime(startFrame: 30, durationFrames: 30, rippleMode: AutoRippleMode.AffectedTracks);

        var t1 = editor.Project.Tracks[0];
        Assert.Equal(2, t1.Clips.Count);

        var left = t1.Clips.First(c => c.StartFrame == 0);
        var right = t1.Clips.First(c => c.StartFrame == 30);
        Assert.Equal(30, left.DurationFrames);
        Assert.Equal(40, right.DurationFrames); // 100 - 30 - 30 = 40
        Assert.Equal(60, right.SourceInFrame); // 0 + 60 = 60

        // Envelope: point at 15 kept, point at 45 deleted, point at 80 shifted left by 30 to 50
        var env = t1.Envelopes![0];
        Assert.Equal(2, env.Points.Count);
        Assert.Equal(15, env.Points[0].Frame);
        Assert.Equal(50, env.Points[1].Frame);

        // Undo
        editor.Undo();
        t1 = editor.Project.Tracks[0];
        Assert.Single(t1.Clips);
        Assert.Equal(100, t1.Clips[0].DurationFrames);
        Assert.Equal(3, t1.Envelopes![0].Points.Count);
    }

    [Fact]
    public void DeleteTime_AllTracksAndMarkers_DeletesInteriorMarkersAndEnvelopesAndRipplesLeft()
    {
        var doc = new ProjectDocument();
        var media = new MediaAsset { Path = "sample.mp4", DurationSeconds = 30, HasVideo = true };
        doc.Media.Add(media);

        var track1 = new TimelineTrack { Kind = TrackKind.Video };
        var clipA = new TimelineClip { MediaId = media.Id, StartFrame = 0, DurationFrames = 30 };
        var clipB = new TimelineClip { MediaId = media.Id, StartFrame = 40, DurationFrames = 20 }; // Strictly inside [35..65]
        var clipC = new TimelineClip { MediaId = media.Id, StartFrame = 70, DurationFrames = 30 };
        track1.Clips.Add(clipA);
        track1.Clips.Add(clipB);
        track1.Clips.Add(clipC);
        doc.Tracks.Add(track1);

        doc.Markers.Add(new TimelineMarker { Frame = 10, Label = "M1" });
        doc.Markers.Add(new TimelineMarker { Frame = 45, Label = "M2" }); // Inside [35..65]
        doc.Markers.Add(new TimelineMarker { Frame = 80, Label = "M3" });

        var bus = new AudioBusTrack
        {
            Name = "Bus A",
            Envelopes =
            [
                new ClipEnvelope
                {
                    Type = EnvelopeType.Volume,
                    IsEnabled = true,
                    Points =
                    [
                        new EnvelopePoint { Frame = 15, Value = 0.5 },
                        new EnvelopePoint { Frame = 50, Value = 0.7 }, // Inside
                        new EnvelopePoint { Frame = 90, Value = 0.9 }
                    ]
                }
            ]
        };
        doc.AudioBuses = [bus];

        var editor = new TimelineEditor(doc);

        // Delete time [35..65] (duration 30)
        editor.DeleteTime(startFrame: 35, durationFrames: 30, rippleMode: AutoRippleMode.AllTracksAndMarkers);

        var t1 = editor.Project.Tracks[0];
        Assert.Equal(2, t1.Clips.Count); // clipB removed
        Assert.Contains(t1.Clips, c => c.Id == clipA.Id && c.StartFrame == 0);
        var cC = t1.Clips.First(c => c.Id == clipC.Id);
        Assert.Equal(40, cC.StartFrame); // 70 - 30 = 40

        // Markers: M2 removed, M3 shifted to 50
        Assert.Equal(2, editor.Project.Markers.Count);
        Assert.Equal(10, editor.Project.Markers[0].Frame);
        Assert.Equal(50, editor.Project.Markers[1].Frame); // 80 - 30 = 50

        // Bus envelope: interior deleted, downstream shifted left by 30
        var bEnv = editor.Project.AudioBuses![0].Envelopes![0];
        Assert.Equal(2, bEnv.Points.Count);
        Assert.Equal(15, bEnv.Points[0].Frame);
        Assert.Equal(60, bEnv.Points[1].Frame); // 90 - 30 = 60

        // Undo
        editor.Undo();
        Assert.Equal(3, editor.Project.Tracks[0].Clips.Count);
        Assert.Equal(3, editor.Project.Markers.Count);
        Assert.Equal(3, editor.Project.AudioBuses![0].Envelopes![0].Points.Count);
    }

    [Fact]
    public void EditorViewModel_InsertTime_And_DeleteTime_ExecuteWithProperStatusAndRipple() => RunSta(() =>
    {
        var mediaService = new MediaService();
        var renderer = new TimelineRenderService(mediaService);
        var vm = new EditorViewModel(mediaService, renderer, new ExportService(renderer));

        var media = new MediaAsset { Path = "sample.mp4", DurationSeconds = 20, HasVideo = true };
        vm.Project.Media.Add(media);

        vm.Project.Tracks.Clear();
        var track = new TimelineTrack { Kind = TrackKind.Video };
        var clip1 = new TimelineClip { MediaId = media.Id, StartFrame = 0, DurationFrames = 30 };
        var clip2 = new TimelineClip { MediaId = media.Id, StartFrame = 50, DurationFrames = 30 };
        track.Clips.Add(clip1);
        track.Clips.Add(clip2);
        vm.Project.Tracks.Add(track);

        // Position cursor at 40
        vm.Position = 40;
        vm.RippleMode = AutoRippleMode.AllTracksAndMarkers;
        vm.Project.RulerFormat = RulerFormat.AbsoluteFrames;
        vm.Project.RulerStartTimeFrames = 100;

        // Insert 25 frames
        vm.InsertTime(25);

        var c2 = vm.Project.Tracks[0].Clips.First(c => c.Id == clip2.Id);
        Assert.Equal(75, c2.StartFrame); // 50 + 25 = 75
        Assert.Equal("Inserted 25 at 140 (AllTracksAndMarkers).", vm.Status);

        // Mark loop region from 30 to 60 (length 30)
        vm.LoopRegion = new FrameRange(30, 30);
        vm.DeleteTime();

        c2 = vm.Project.Tracks[0].Clips.First(c => c.Id == clip2.Id);
        Assert.Equal(45, c2.StartFrame); // 75 - 30 = 45
        Assert.Equal("Deleted 30 from 130 (AllTracksAndMarkers).", vm.Status);
    });

    [Theory]
    [InlineData(AutoRippleMode.AffectedTracks, false)]
    [InlineData(AutoRippleMode.AffectedTracksAndMarkers, true)]
    [InlineData(AutoRippleMode.AllTracksAndMarkers, true)]
    public void EventMoveAndTrimRippleAffectedEnvelopesAndScopeTheRuler(AutoRippleMode mode, bool ripplesRuler)
    {
        var media = new MediaAsset { Path = "sample.mp4", DurationSeconds = 20, HasVideo = true };
        var clip = new TimelineClip { MediaId = media.Id, StartFrame = 20, DurationFrames = 40 };
        var track = new TimelineTrack { Kind = TrackKind.Video, Clips = [clip] };
        track.Envelopes = [OpacityAt(65)];
        var otherTrack = new TimelineTrack { Kind = TrackKind.Video, Envelopes = [OpacityAt(65)] };
        var doc = new ProjectDocument
        {
            Media = [media], Tracks = [track, otherTrack], Markers = [new() { Frame = 60 }],
            Regions = [new() { StartFrame = 10, EndFrame = 60 }, new() { StartFrame = 55, EndFrame = 70 },
                new() { StartFrame = 60, EndFrame = 90 }]
        };
        var editor = new TimelineEditor(doc);
        var initialRanges = doc.Regions.Select(region => (region.StartFrame, region.EndFrame)).ToArray();
        (long Start, long End)[] shiftedRanges = [(10, 60), (55, 80), (70, 100)];

        editor.MoveSelection([clip.Id], clip.Id, track.Id, 30, ripple: true, rippleMode: mode);
        CheckShift();
        Assert.True(editor.Undo());
        Assert.Equal(initialRanges, doc.Regions.Select(region => (region.StartFrame, region.EndFrame)));
        Assert.Equal(60, Assert.Single(doc.Markers).Frame);
        Assert.Equal(65, doc.Tracks[0].Envelopes!.Single().Points.Single().Frame);

        editor.TrimSelection(clip.Id, 20, 0, 50, ripple: true, rippleMode: mode);
        CheckShift();

        void CheckShift()
        {
            Assert.Equal(ripplesRuler ? shiftedRanges : initialRanges, doc.Regions.Select(region => (region.StartFrame, region.EndFrame)));
            Assert.Equal(ripplesRuler ? 70 : 60, Assert.Single(doc.Markers).Frame);
            Assert.Equal(75, doc.Tracks[0].Envelopes!.Single().Points.Single().Frame);
            Assert.Equal(mode == AutoRippleMode.AllTracksAndMarkers ? 75 : 65, doc.Tracks[1].Envelopes!.Single().Points.Single().Frame);
        }
    }

    [Theory]
    [InlineData(AutoRippleMode.AffectedTracks, false)]
    [InlineData(AutoRippleMode.AllTracksAndMarkers, true)]
    public void DeleteEventsRipplesTrackEnvelopesAndProjectsRegionsThroughDisjointSpans(AutoRippleMode mode, bool ripplesRuler)
    {
        var media = new MediaAsset { Path = "sample.mp4", DurationSeconds = 20, HasVideo = true };
        var first = new TimelineClip { MediaId = media.Id, StartFrame = 20, DurationFrames = 20 };
        var second = new TimelineClip { MediaId = media.Id, StartFrame = 50, DurationFrames = 10 };
        var track = new TimelineTrack { Kind = TrackKind.Video, Clips = [first, second] };
        track.Envelopes = [OpacityAt(65)];
        var doc = new ProjectDocument
        {
            Media = [media], Tracks = [track],
            Regions =
            [
                new() { StartFrame = 15, EndFrame = 55 },
                new() { StartFrame = 20, EndFrame = 40 },
                new() { StartFrame = 45, EndFrame = 65 },
                new() { StartFrame = 60, EndFrame = 80 }
            ]
        };
        var editor = new TimelineEditor(doc);
        var initialRanges = doc.Regions.Select(region => (region.StartFrame, region.EndFrame)).ToArray();

        editor.DeleteSelection([first.Id, second.Id], ripple: true, rippleMode: mode);

        (long Start, long End)[] expected = [(15, 30), (25, 35), (30, 50)];
        Assert.Equal(ripplesRuler ? expected : initialRanges, doc.Regions.Select(region => (region.StartFrame, region.EndFrame)));
        Assert.Equal(35, doc.Tracks[0].Envelopes!.Single().Points.Single().Frame);
        Assert.True(editor.Undo());
        Assert.Equal(initialRanges, doc.Regions.Select(region => (region.StartFrame, region.EndFrame)));
    }

    private static ClipEnvelope OpacityAt(long frame) => new()
    {
        IsEnabled = true, Type = EnvelopeType.Composite, Points = [new() { Frame = frame, Value = 0.5 }]
    };

    [Theory]
    [InlineData(AutoRippleMode.AffectedTracks, false)]
    [InlineData(AutoRippleMode.AffectedTracksAndMarkers, true)]
    [InlineData(AutoRippleMode.AllTracksAndMarkers, true)]
    public void PlaceSubclipAndPasteAlwaysRippleAffectedTrackEnvelopes(AutoRippleMode mode, bool ripplesRuler) => RunSta(() =>
    {
        var mediaService = new MediaService();
        var renderer = new TimelineRenderService(mediaService);
        var vm = new EditorViewModel(mediaService, renderer, new ExportService(renderer));

        var media = new MediaAsset { Path = "sample.mp4", DurationSeconds = 20, HasVideo = true, HasAudio = true };
        vm.Project.Media.Add(media);

        vm.Project.Tracks.Clear();
        var track = new TimelineTrack { Kind = TrackKind.Video };
        var clip1 = new TimelineClip { MediaId = media.Id, StartFrame = 0, DurationFrames = 30 };
        var clip2 = new TimelineClip { MediaId = media.Id, StartFrame = 40, DurationFrames = 30 };
        track.Clips.Add(clip1);
        track.Clips.Add(clip2);
        track.Envelopes =
        [
            new ClipEnvelope
            {
                Type = EnvelopeType.Composite,
                IsEnabled = true,
                Points =
                [
                    new EnvelopePoint { Frame = 10, Value = 0.5 },
                    new EnvelopePoint { Frame = 45, Value = 1.0 }
                ]
            }
        ];
        vm.Project.Tracks.Add(track);

        var bus = new AudioBusTrack
        {
            Name = "Bus A",
            Envelopes =
            [
                new ClipEnvelope
                {
                    Type = EnvelopeType.Volume,
                    IsEnabled = true,
                    Points =
                    [
                        new EnvelopePoint { Frame = 50, Value = 0.8 }
                    ]
                }
            ]
        };
        vm.Project.AudioBuses = [bus];
        vm.Project.Regions = [new() { StartFrame = 30, EndFrame = 50 }];

        vm.Ripple = true;
        vm.RippleMode = mode;

        // Place subclip at 35 with duration 15
        vm.PlaceSubclip(media.Id, sourceIn: 0, duration: 15, trackId: track.Id, at: 35, advanceCursor: false);

        var t = vm.Project.Tracks[0];
        Assert.Equal(55, t.Clips.First(c => c.Id == clip2.Id).StartFrame); // 40 + 15 = 55
        Assert.Equal(60, t.Envelopes![0].Points[1].Frame); // 45 + 15 = 60
        Assert.Equal(ripplesRuler ? 65 : 50, vm.Project.AudioBuses![0].Envelopes![0].Points[0].Frame);
        Assert.Equal((30L, ripplesRuler ? 65L : 50L), vm.Project.Regions.Select(region => (region.StartFrame, region.EndFrame)).Single());

        vm.Copy();
        vm.Position = 30;
        vm.Paste();

        Assert.Equal(ripplesRuler ? (45L, 80L) : (30L, 50L), vm.Project.Regions.Select(region => (region.StartFrame, region.EndFrame)).Single());
        Assert.Equal(75, t.Envelopes![0].Points[1].Frame);
        Assert.Equal(ripplesRuler ? 80 : 50, vm.Project.AudioBuses![0].Envelopes![0].Points[0].Frame);
    });

    [Fact]
    public void InsertTimeDialog_InitializesAndParsesValidDuration() => RunSta(() =>
    {
        var rate = new Rational(30, 1);
        var dialog = new InsertTimeDialog(
            initialDurationFrames: 30,
            currentRippleMode: AutoRippleMode.AllTracksAndMarkers,
            rate: rate,
            format: RulerFormat.TimeAndFrames);

        Assert.Equal(30, dialog.DurationFrames);
        Assert.Equal(AutoRippleMode.AllTracksAndMarkers, dialog.SelectedRippleMode);
    });
}
