using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenRend.Core;

namespace OpenRend.Timeline;

public sealed partial class TimelineEditor
{
    private static readonly VideoTransitionKind[] AvailableTransitionKinds =
    [
        VideoTransitionKind.Dissolve,
        VideoTransitionKind.LinearWipe,
        VideoTransitionKind.ClockWipe,
        VideoTransitionKind.VenetianBlinds,
        VideoTransitionKind.StarWipe,
        VideoTransitionKind.BarnDoor,
        VideoTransitionKind.Iris,
        VideoTransitionKind.Split,
        VideoTransitionKind.Push,
        VideoTransitionKind.Slide,
        VideoTransitionKind.Zoom,
        VideoTransitionKind.Swap,
        VideoTransitionKind.Squeeze,
        VideoTransitionKind.PagePeel
    ];

    /// <summary>
    /// Builds and places a complete photo slideshow on the timeline in a single atomic transaction.
    /// Replicates ScriptPortal.Vegas.Slideshow.SlideshowMediator.CreateSlideshow.
    /// </summary>
    public List<TimelineClip> CreateSlideshow(SlideshowSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();

        var fps = (double)Project.FrameRate.Numerator / Math.Max(1, Project.FrameRate.Denominator);
        if (fps <= 0) fps = 30.0;

        var effectiveSecsPerSlide = settings.ComputeEffectiveSecondsPerSlide();
        var slideDurationFrames = Math.Max(2L, (long)Math.Round(effectiveSecsPerSlide * fps));
        var overlapFrames = (settings.Effect == SlideshowEffectKind.None || settings.Pictures.Count <= 1)
            ? 0L
            : Math.Clamp((long)Math.Round(settings.SecondsOfOverlap * fps), 0L, slideDurationFrames - 1);

        long startFrame = settings.StartMode switch
        {
            SlideshowStartMode.AtStart => 0L,
            _ => Math.Max(0L, settings.CustomStartFrame)
        };

        var createdClips = new List<TimelineClip>();

        Execute(p =>
        {
            // 1. Locate or create destination video track
            TimelineTrack? targetTrack = null;
            if (settings.TargetTrackId.HasValue)
            {
                targetTrack = p.Tracks.FirstOrDefault(t => t.Id == settings.TargetTrackId.Value && t.Kind == TrackKind.Video);
            }

            if (targetTrack == null)
            {
                targetTrack = p.Tracks.FirstOrDefault(t => t.Kind == TrackKind.Video);
                if (targetTrack == null)
                {
                    targetTrack = new TimelineTrack
                    {
                        Kind = TrackKind.Video,
                        Name = "Slideshow"
                    };
                    p.Tracks.Add(targetTrack);
                }
            }

            long currentStart = startFrame;
            var rand = new Random(settings.Pictures.Count ^ 42);

            for (int i = 0; i < settings.Pictures.Count; i++)
            {
                var pic = settings.Pictures[i];

                // 2. Ensure media asset exists in project
                var asset = p.Media.FirstOrDefault(m => string.Equals(m.Path, pic.Path, StringComparison.OrdinalIgnoreCase));
                if (asset == null)
                {
                    var width = p.Width > 0 ? p.Width : 1920;
                    var height = p.Height > 0 ? p.Height : 1080;
                    asset = new MediaAsset
                    {
                        Id = Guid.NewGuid(),
                        Path = pic.Path,
                        Width = width,
                        Height = height,
                        DurationSeconds = 0,
                        FrameRate = fps,
                        HasVideo = true,
                        HasAudio = false
                    };
                    p.Media.Add(asset);
                }

                // 3. Create timeline clip
                var clip = new TimelineClip
                {
                    Id = Guid.NewGuid(),
                    MediaId = asset.Id,
                    Name = string.IsNullOrEmpty(pic.Filename) ? Path.GetFileName(pic.Path) : pic.Filename,
                    StartFrame = currentStart,
                    SourceInFrame = 0,
                    DurationFrames = slideDurationFrames,
                    Gain = 1.0,
                    Opacity = 1.0
                };

                // 4. Overlap transitions
                if (i > 0 && overlapFrames > 0)
                {
                    clip.FadeInFrames = overlapFrames;
                    clip.FadeInCurve = VideoFadeCurve.Smooth;
                    clip.FadeInReciprocalCurve = VideoFadeCurve.Smooth;

                    if (settings.Effect is SlideshowEffectKind.Crossfade or SlideshowEffectKind.PanAndZoom or SlideshowEffectKind.TransitionsAndPanZoom)
                    {
                        clip.FadeInTransition = new VideoTransition
                        {
                            Kind = VideoTransitionKind.Dissolve,
                            Preset = "Default"
                        };
                    }
                    else if (settings.Effect == SlideshowEffectKind.RandomTransitions)
                    {
                        var randomKind = AvailableTransitionKinds[rand.Next(AvailableTransitionKinds.Length)];
                        clip.FadeInTransition = new VideoTransition
                        {
                            Kind = randomKind,
                            Preset = "Default"
                        };
                    }
                }

                // 5. Ken Burns Pan and Zoom motion
                if (settings.Effect is SlideshowEffectKind.PanAndZoom or SlideshowEffectKind.TransitionsAndPanZoom)
                {
                    var panMode = pic.PanZoom;
                    if (panMode == SlideshowPanZoom.None)
                    {
                        // Cycle through Pan/Zoom motions: ZoomIn, ZoomOut, PanLeft, PanRight
                        panMode = (i % 4) switch
                        {
                            0 => SlideshowPanZoom.ZoomIn,
                            1 => SlideshowPanZoom.ZoomOut,
                            2 => SlideshowPanZoom.PanLeft,
                            _ => SlideshowPanZoom.PanRight
                        };
                    }

                    clip.Crop = CreateKenBurnsCrop(asset.Width, asset.Height, pic.OrientationDegrees, panMode, slideDurationFrames, settings.PanScaleAmount);
                }
                else if (pic.OrientationDegrees != 0)
                {
                    // Apply static rotation
                    var crop = new CropAnimation();
                    var w = Math.Max(1, asset.Width);
                    var h = Math.Max(1, asset.Height);
                    crop.Keyframes.Add(new CropKeyframe
                    {
                        Frame = 0,
                        State = new CropState(w, h, w / 2.0, h / 2.0, pic.OrientationDegrees, w / 2.0, h / 2.0),
                        Interpolation = CropInterpolation.Hold
                    });
                    clip.Crop = crop;
                }

                targetTrack.Clips.Add(clip);
                createdClips.Add(clip);

                // Advance playhead by net duration (slide duration minus overlap)
                currentStart += (slideDurationFrames - overlapFrames);
            }

            targetTrack.Clips.Sort((a, b) => a.StartFrame.CompareTo(b.StartFrame));
        });

        return createdClips;
    }

    private static CropAnimation CreateKenBurnsCrop(int width, int height, int orientation, SlideshowPanZoom mode, long durationFrames, double panScale)
    {
        var crop = new CropAnimation();
        var w = Math.Max(1, width);
        var h = Math.Max(1, height);
        var cx = w / 2.0;
        var cy = h / 2.0;
        var scale = Math.Clamp(panScale, 0.05, 0.5);

        // Scaled bounding box dimensions
        var zoomW = w * (1.0 - scale);
        var zoomH = h * (1.0 - scale);

        CropState startState;
        CropState endState;

        switch (mode)
        {
            case SlideshowPanZoom.ZoomIn:
                startState = new CropState(w, h, cx, cy, orientation, cx, cy);
                endState = new CropState(zoomW, zoomH, cx, cy, orientation, cx, cy);
                break;

            case SlideshowPanZoom.ZoomOut:
                startState = new CropState(zoomW, zoomH, cx, cy, orientation, cx, cy);
                endState = new CropState(w, h, cx, cy, orientation, cx, cy);
                break;

            case SlideshowPanZoom.PanLeft:
                startState = new CropState(zoomW, zoomH, cx + (w * scale * 0.5), cy, orientation, cx, cy);
                endState = new CropState(zoomW, zoomH, cx - (w * scale * 0.5), cy, orientation, cx, cy);
                break;

            case SlideshowPanZoom.PanRight:
            default:
                startState = new CropState(zoomW, zoomH, cx - (w * scale * 0.5), cy, orientation, cx, cy);
                endState = new CropState(zoomW, zoomH, cx + (w * scale * 0.5), cy, orientation, cx, cy);
                break;
        }

        crop.Keyframes.Add(new CropKeyframe
        {
            Frame = 0,
            State = startState,
            Interpolation = CropInterpolation.Smooth
        });

        crop.Keyframes.Add(new CropKeyframe
        {
            Frame = Math.Max(1, durationFrames - 1),
            State = endState,
            Interpolation = CropInterpolation.Smooth
        });

        return crop;
    }
}
