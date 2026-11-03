using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OpenRend.Core;

namespace OpenRend.UI;

public partial class TimelineView
{
    private TimelineClip? draggingEnvClip;
    private TimelineTrack? draggingEnvTrack;
    private ClipEnvelope? draggingEnv;
    private EnvelopePoint? draggingEnvPt;
    private bool isDraggingEnvelopePoint;

    private void DrawEnvelopes(DrawingContext dc, TimelineTrack track, TimelineClip clip, Rect rect)
    {
        if (track.Kind != TrackKind.Video || clip.Envelopes == null || clip.Envelopes.Count == 0) return;

        var top = rect.Y + (ShowEventHeaders ? 20.0 : 4.0);
        var bottom = rect.Bottom - 4.0;
        var height = Math.Max(12.0, bottom - top);
        if (height <= 0 || rect.Width <= 2) return;

        foreach (var env in clip.Envelopes)
        {
            if (!env.IsEnabled) continue;

            var isVelocity = env.Type == EnvelopeType.Velocity;
            var strokeColor = isVelocity ? Color.FromRgb(50, 205, 50) : Color.FromRgb(0, 162, 232); // Vegas Green or Blue
            var strokeBrush = new SolidColorBrush(strokeColor);
            var strokePen = new Pen(strokeBrush, 1.5);
            strokePen.Freeze();

            // 1. Draw 100% neutral baseline
            var uNeutral = Math.Clamp((env.DefaultValue - env.MinValue) / (env.MaxValue - env.MinValue), 0.0, 1.0);
            var baselineY = bottom - uNeutral * height;
            var baselinePen = new Pen(new SolidColorBrush(Color.FromArgb(90, strokeColor.R, strokeColor.G, strokeColor.B)), 1.0)
            {
                DashStyle = new DashStyle(new double[] { 3, 3 }, 0)
            };
            baselinePen.Freeze();
            dc.DrawLine(baselinePen, new Point(rect.Left, baselineY), new Point(rect.Right, baselineY));

            if (isVelocity && rect.Width > 60)
            {
                Text(dc, "100%", rect.Left + 4, baselineY - 11, Brush("#77AA77"), 8);
            }

            // 2. Draw envelope curve across event
            var geom = new StreamGeometry();
            using (var gc = geom.Open())
            {
                var stepPixels = Math.Max(2.0, Math.Min(6.0, rect.Width / 60.0));
                var startX = rect.Left;
                var startFrame = 0L;
                var startVal = env.ValueAt(startFrame);
                var startNorm = Math.Clamp((startVal - env.MinValue) / (env.MaxValue - env.MinValue), 0.0, 1.0);
                var startY = bottom - startNorm * height;

                gc.BeginFigure(new Point(startX, startY), false, false);

                for (var px = startX + stepPixels; px < rect.Right; px += stepPixels)
                {
                    var progress = (px - rect.Left) / rect.Width;
                    var frame = (long)Math.Round(progress * clip.DurationFrames);
                    var val = env.ValueAt(frame);
                    var norm = Math.Clamp((val - env.MinValue) / (env.MaxValue - env.MinValue), 0.0, 1.0);
                    var y = bottom - norm * height;
                    gc.LineTo(new Point(px, y), true, false);
                }

                // End at right boundary
                var endVal = env.ValueAt(clip.DurationFrames);
                var endNorm = Math.Clamp((endVal - env.MinValue) / (env.MaxValue - env.MinValue), 0.0, 1.0);
                var endY = bottom - endNorm * height;
                gc.LineTo(new Point(rect.Right, endY), true, false);
            }
            geom.Freeze();
            dc.DrawGeometry(null, strokePen, geom);

            // 3. Draw Envelope Point Handles
            var handleFill = Brushes.White;
            var handleStroke = new Pen(Brush("#111111"), 1.0);
            handleStroke.Freeze();
            var activeFill = Brush("#FFAA00");

            foreach (var pt in env.Points)
            {
                var progress = (double)pt.Frame / Math.Max(1, clip.DurationFrames);
                var ptX = rect.Left + progress * rect.Width;
                var norm = Math.Clamp((pt.Value - env.MinValue) / (env.MaxValue - env.MinValue), 0.0, 1.0);
                var ptY = bottom - norm * height;

                var isPointActive = isDraggingEnvelopePoint && draggingEnvPt == pt;
                var fill = isPointActive ? activeFill : handleFill;

                var handleRect = new Rect(ptX - 3.5, ptY - 3.5, 7.0, 7.0);
                dc.DrawRectangle(fill, handleStroke, handleRect);

                if (isPointActive)
                {
                    var label = isVelocity
                        ? $"Velocity: {Math.Round(pt.Value * 100)}%"
                        : $"Composite: {Math.Round(pt.Value * 100)}%";
                    Text(dc, label, ptX + 6, ptY - 14, Brush("#FFAA00"), 9);
                }
            }
        }
    }

    private void DrawTrackEnvelopes(DrawingContext dc, TimelineTrack track, Rect rect)
    {
        if (track.Envelopes == null || track.Envelopes.Count == 0) return;

        var top = rect.Y + 4.0;
        var bottom = rect.Bottom - 4.0;
        var height = Math.Max(12.0, bottom - top);
        if (height <= 0 || rect.Width <= 2) return;

        foreach (var env in track.Envelopes)
        {
            if (!env.IsEnabled) continue;

            var strokeColor = env.Type switch
            {
                EnvelopeType.Composite => Color.FromRgb(0, 162, 232), // Vegas Pro Blue
                EnvelopeType.Volume => Color.FromRgb(212, 175, 55),    // Vegas Pro Gold
                EnvelopeType.Pan => Color.FromRgb(255, 127, 39),       // Vegas Pro Orange
                EnvelopeType.Mute => Color.FromRgb(232, 17, 35),       // Vegas Pro Red
                _ => Color.FromRgb(150, 150, 255)
            };

            var strokeBrush = new SolidColorBrush(strokeColor);
            var strokePen = new Pen(strokeBrush, 1.5);
            strokePen.Freeze();

            // Baseline
            var uNeutral = Math.Clamp((env.DefaultValue - env.MinValue) / (env.MaxValue - env.MinValue), 0.0, 1.0);
            var baselineY = bottom - uNeutral * height;
            var baselinePen = new Pen(new SolidColorBrush(Color.FromArgb(80, strokeColor.R, strokeColor.G, strokeColor.B)), 1.0)
            {
                DashStyle = new DashStyle(new double[] { 3, 3 }, 0)
            };
            baselinePen.Freeze();
            dc.DrawLine(baselinePen, new Point(rect.Left, baselineY), new Point(rect.Right, baselineY));

            var baselineLabel = env.Type switch
            {
                EnvelopeType.Composite => "Track 100%",
                EnvelopeType.Volume => "Track 0 dB",
                EnvelopeType.Pan => "Track Center",
                EnvelopeType.Mute => "Track Active",
                _ => ""
            };
            if (!string.IsNullOrEmpty(baselineLabel))
            {
                Text(dc, baselineLabel, rect.Left + 6, baselineY - 11, new SolidColorBrush(Color.FromArgb(160, strokeColor.R, strokeColor.G, strokeColor.B)), 8);
            }

            // Curve across visible window
            var geom = new StreamGeometry();
            using (var gc = geom.Open())
            {
                var stepPixels = 4.0;
                var startX = rect.Left;
                var startFrame = FrameAt(startX);
                var startVal = env.ValueAt(startFrame);
                var startNorm = Math.Clamp((startVal - env.MinValue) / (env.MaxValue - env.MinValue), 0.0, 1.0);
                var startY = bottom - startNorm * height;

                gc.BeginFigure(new Point(startX, startY), false, false);

                for (var px = startX + stepPixels; px < rect.Right; px += stepPixels)
                {
                    var frame = FrameAt(px);
                    var val = env.ValueAt(frame);
                    var norm = Math.Clamp((val - env.MinValue) / (env.MaxValue - env.MinValue), 0.0, 1.0);
                    var y = bottom - norm * height;
                    gc.LineTo(new Point(px, y), true, false);
                }

                var endFrame = FrameAt(rect.Right);
                var endVal = env.ValueAt(endFrame);
                var endNorm = Math.Clamp((endVal - env.MinValue) / (env.MaxValue - env.MinValue), 0.0, 1.0);
                var endY = bottom - endNorm * height;
                gc.LineTo(new Point(rect.Right, endY), true, false);
            }
            geom.Freeze();
            dc.DrawGeometry(null, strokePen, geom);

            // Handle points
            var handleFill = Brushes.White;
            var handleStroke = new Pen(Brush("#111111"), 1.0);
            handleStroke.Freeze();
            var activeFill = Brush("#FFAA00");

            foreach (var pt in env.Points)
            {
                var ptX = X(pt.Frame);
                if (ptX < HeaderWidth - 6 || ptX > ActualWidth + 6) continue;

                var norm = Math.Clamp((pt.Value - env.MinValue) / (env.MaxValue - env.MinValue), 0.0, 1.0);
                var ptY = bottom - norm * height;

                var isPointActive = isDraggingEnvelopePoint && draggingEnvPt == pt;
                var fill = isPointActive ? activeFill : handleFill;

                var handleRect = new Rect(ptX - 3.5, ptY - 3.5, 7.0, 7.0);
                dc.DrawRectangle(fill, handleStroke, handleRect);

                if (isPointActive)
                {
                    var label = env.Type switch
                    {
                        EnvelopeType.Composite => $"Track Composite: {Math.Round(pt.Value * 100)}%",
                        EnvelopeType.Volume => $"Track Vol: {(pt.Value > 0 ? $"{20 * Math.Log10(pt.Value):0.0} dB" : "-inf dB")}",
                        EnvelopeType.Pan => $"Track Pan: {(Math.Abs(pt.Value) < 0.01 ? "Center" : $"{Math.Abs(pt.Value) * 100:0}% {(pt.Value < 0 ? "L" : "R")}")}",
                        EnvelopeType.Mute => $"Track Mute: {(pt.Value >= 0.5 ? "Muted" : "Active")}",
                        _ => $"Track {env.Type}: {pt.Value:0.0}"
                    };
                    Text(dc, label, ptX + 6, ptY - 14, Brush("#FFAA00"), 9);
                }
            }
        }
    }

    private bool HitEnvelopePoint(Point p, out TimelineClip? outClip, out ClipEnvelope? outEnv, out EnvelopePoint? outPoint)
    {
        outClip = null;
        outEnv = null;
        outPoint = null;

        if (Project?.Tracks == null) return false;

        for (var i = 0; i < Project.Tracks.Count; i++)
        {
            var track = Project.Tracks[i];
            if (track.Kind != TrackKind.Video) continue;

            var y = RulerHeight + i * TrackHeight - VerticalOffset;
            if (p.Y < y || p.Y > y + TrackHeight) continue;

            var top = y + (ShowEventHeaders ? 20.0 : 4.0);
            var bottom = y + TrackHeight - 4.0;
            var height = Math.Max(12.0, bottom - top);

            foreach (var clip in track.Clips)
            {
                if (clip.Envelopes == null || clip.Envelopes.Count == 0) continue;

                var rect = new Rect(X(clip.StartFrame), y + 1, Math.Max(2, Project.FrameRate.Seconds(clip.DurationFrames) * PixelsPerSecond), TrackHeight - 2);
                if (rect.Right < HeaderWidth || rect.Left > ActualWidth) continue;

                foreach (var env in clip.Envelopes)
                {
                    if (!env.IsEnabled) continue;

                    foreach (var pt in env.Points)
                    {
                        var progress = (double)pt.Frame / Math.Max(1, clip.DurationFrames);
                        var ptX = rect.Left + progress * rect.Width;
                        var norm = Math.Clamp((pt.Value - env.MinValue) / (env.MaxValue - env.MinValue), 0.0, 1.0);
                        var ptY = bottom - norm * height;

                        var hitRect = new Rect(ptX - 6.0, ptY - 6.0, 12.0, 12.0);
                        if (hitRect.Contains(p))
                        {
                            outClip = clip;
                            outEnv = env;
                            outPoint = pt;
                            return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    private bool HitTrackEnvelopePoint(Point p, out TimelineTrack? outTrack, out ClipEnvelope? outEnv, out EnvelopePoint? outPoint)
    {
        outTrack = null;
        outEnv = null;
        outPoint = null;

        if (Project?.Tracks == null) return false;

        for (var i = 0; i < Project.Tracks.Count; i++)
        {
            var track = Project.Tracks[i];
            if (track.Envelopes == null || track.Envelopes.Count == 0) continue;

            var y = RulerHeight + i * TrackHeight - VerticalOffset;
            if (p.Y < y || p.Y > y + TrackHeight) continue;

            var top = y + 4.0;
            var bottom = y + TrackHeight - 4.0;
            var height = Math.Max(12.0, bottom - top);

            foreach (var env in track.Envelopes)
            {
                if (!env.IsEnabled) continue;

                foreach (var pt in env.Points)
                {
                    var ptX = X(pt.Frame);
                    if (ptX < HeaderWidth - 6 || ptX > ActualWidth + 6) continue;

                    var norm = Math.Clamp((pt.Value - env.MinValue) / (env.MaxValue - env.MinValue), 0.0, 1.0);
                    var ptY = bottom - norm * height;

                    var hitRect = new Rect(ptX - 6.0, ptY - 6.0, 12.0, 12.0);
                    if (hitRect.Contains(p))
                    {
                        outTrack = track;
                        outEnv = env;
                        outPoint = pt;
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private bool HitEnvelopeLine(Point p, out TimelineClip? outClip, out ClipEnvelope? outEnv, out long outFrame, out double outValue)
    {
        outClip = null;
        outEnv = null;
        outFrame = 0;
        outValue = 1.0;

        if (Project?.Tracks == null) return false;

        for (var i = 0; i < Project.Tracks.Count; i++)
        {
            var track = Project.Tracks[i];
            if (track.Kind != TrackKind.Video) continue;

            var y = RulerHeight + i * TrackHeight - VerticalOffset;
            if (p.Y < y || p.Y > y + TrackHeight) continue;

            var top = y + (ShowEventHeaders ? 20.0 : 4.0);
            var bottom = y + TrackHeight - 4.0;
            var height = Math.Max(12.0, bottom - top);

            foreach (var clip in track.Clips)
            {
                if (clip.Envelopes == null || clip.Envelopes.Count == 0) continue;

                var rect = new Rect(X(clip.StartFrame), y + 1, Math.Max(2, Project.FrameRate.Seconds(clip.DurationFrames) * PixelsPerSecond), TrackHeight - 2);
                if (!rect.Contains(p)) continue;

                var relX = (p.X - rect.Left) / rect.Width;
                var frame = (long)Math.Round(relX * clip.DurationFrames);

                foreach (var env in clip.Envelopes)
                {
                    if (!env.IsEnabled) continue;

                    var valAtFrame = env.ValueAt(frame);
                    var norm = Math.Clamp((valAtFrame - env.MinValue) / (env.MaxValue - env.MinValue), 0.0, 1.0);
                    var expectedY = bottom - norm * height;

                    if (Math.Abs(p.Y - expectedY) <= 8.0)
                    {
                        outClip = clip;
                        outEnv = env;
                        outFrame = frame;
                        var mouseNorm = Math.Clamp((bottom - p.Y) / height, 0.0, 1.0);
                        outValue = env.MinValue + mouseNorm * (env.MaxValue - env.MinValue);
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private bool HitTrackEnvelopeLine(Point p, out TimelineTrack? outTrack, out ClipEnvelope? outEnv, out long outFrame, out double outValue)
    {
        outTrack = null;
        outEnv = null;
        outFrame = 0;
        outValue = 1.0;

        if (Project?.Tracks == null) return false;

        for (var i = 0; i < Project.Tracks.Count; i++)
        {
            var track = Project.Tracks[i];
            if (track.Envelopes == null || track.Envelopes.Count == 0) continue;

            var y = RulerHeight + i * TrackHeight - VerticalOffset;
            if (p.Y < y || p.Y > y + TrackHeight) continue;

            var top = y + 4.0;
            var bottom = y + TrackHeight - 4.0;
            var height = Math.Max(12.0, bottom - top);

            var frame = FrameAt(p.X);

            foreach (var env in track.Envelopes)
            {
                if (!env.IsEnabled) continue;

                var valAtFrame = env.ValueAt(frame);
                var norm = Math.Clamp((valAtFrame - env.MinValue) / (env.MaxValue - env.MinValue), 0.0, 1.0);
                var expectedY = bottom - norm * height;

                if (Math.Abs(p.Y - expectedY) <= 8.0)
                {
                    outTrack = track;
                    outEnv = env;
                    outFrame = frame;
                    var mouseNorm = Math.Clamp((bottom - p.Y) / height, 0.0, 1.0);
                    outValue = env.MinValue + mouseNorm * (env.MaxValue - env.MinValue);
                    return true;
                }
            }
        }

        return false;
    }

    private bool HandleEnvelopeMouseDown(Point p, MouseButtonEventArgs e)
    {
        if (p.X < HeaderWidth) return false;

        // Clip envelope point hit
        if (HitEnvelopePoint(p, out var clip, out var env, out var pt))
        {
            draggingEnvClip = clip;
            draggingEnvTrack = null;
            draggingEnv = env;
            draggingEnvPt = pt;
            isDraggingEnvelopePoint = true;
            CaptureMouse();
            InvalidateVisual();
            return true;
        }

        // Track envelope point hit
        if (HitTrackEnvelopePoint(p, out var track, out var trackEnv, out var trackPt))
        {
            draggingEnvClip = null;
            draggingEnvTrack = track;
            draggingEnv = trackEnv;
            draggingEnvPt = trackPt;
            isDraggingEnvelopePoint = true;
            CaptureMouse();
            InvalidateVisual();
            return true;
        }

        // Double-click to add point
        if (e.ClickCount == 2)
        {
            if (HitEnvelopeLine(p, out var lineClip, out var lineEnv, out var lineFrame, out var lineVal))
            {
                SetEnvelopePointRequested?.Invoke(lineClip!.Id, lineEnv!.Type, lineFrame, lineVal, EnvelopeCurveType.Linear);
                return true;
            }

            if (HitTrackEnvelopeLine(p, out var lineTrack, out var lineTrackEnv, out var lineTrackFrame, out var lineTrackVal))
            {
                SetTrackEnvelopePointRequested?.Invoke(lineTrack!.Id, lineTrackEnv!.Type, lineTrackFrame, lineTrackVal, EnvelopeCurveType.Linear);
                return true;
            }
        }

        return false;
    }

    private bool HandleEnvelopeMouseMove(Point p)
    {
        if (!isDraggingEnvelopePoint || draggingEnv == null || draggingEnvPt == null)
            return false;

        if (draggingEnvClip != null)
        {
            var trackIndex = Project.Tracks.IndexOf(Project.Tracks.First(t => t.Clips.Any(c => c.Id == draggingEnvClip.Id)));
            var y = RulerHeight + trackIndex * TrackHeight - VerticalOffset;
            var top = y + (ShowEventHeaders ? 20.0 : 4.0);
            var bottom = y + TrackHeight - 4.0;
            var height = Math.Max(12.0, bottom - top);

            var rect = new Rect(X(draggingEnvClip.StartFrame), y + 1, Math.Max(2, Project.FrameRate.Seconds(draggingEnvClip.DurationFrames) * PixelsPerSecond), TrackHeight - 2);

            var progress = Math.Clamp((p.X - rect.Left) / rect.Width, 0.0, 1.0);
            var newFrame = (long)Math.Round(progress * draggingEnvClip.DurationFrames);

            var mouseNorm = Math.Clamp((bottom - p.Y) / height, 0.0, 1.0);
            var newVal = draggingEnv.MinValue + mouseNorm * (draggingEnv.MaxValue - draggingEnv.MinValue);

            if (draggingEnvPt.Frame == 0 && progress < 0.1) newFrame = 0;
            else if (draggingEnvPt.Frame == draggingEnvClip.DurationFrames && progress > 0.9) newFrame = draggingEnvClip.DurationFrames;

            draggingEnvPt.Frame = newFrame;
            draggingEnvPt.Value = newVal;
            draggingEnv.Points.Sort((a, b) => a.Frame.CompareTo(b.Frame));

            InvalidateVisual();
            return true;
        }

        if (draggingEnvTrack != null)
        {
            var trackIndex = Project.Tracks.IndexOf(draggingEnvTrack);
            var y = RulerHeight + trackIndex * TrackHeight - VerticalOffset;
            var top = y + 4.0;
            var bottom = y + TrackHeight - 4.0;
            var height = Math.Max(12.0, bottom - top);

            var newFrame = Math.Max(0, FrameAt(p.X));
            var mouseNorm = Math.Clamp((bottom - p.Y) / height, 0.0, 1.0);
            var newVal = draggingEnv.MinValue + mouseNorm * (draggingEnv.MaxValue - draggingEnv.MinValue);

            draggingEnvPt.Frame = newFrame;
            draggingEnvPt.Value = newVal;
            draggingEnv.Points.Sort((a, b) => a.Frame.CompareTo(b.Frame));

            InvalidateVisual();
            return true;
        }

        return false;
    }

    private bool HandleEnvelopeMouseUp(Point p)
    {
        if (!isDraggingEnvelopePoint || draggingEnv == null || draggingEnvPt == null)
            return false;

        ReleaseMouseCapture();
        isDraggingEnvelopePoint = false;

        if (draggingEnvClip != null)
        {
            SetEnvelopePointRequested?.Invoke(draggingEnvClip.Id, draggingEnv.Type, draggingEnvPt.Frame, draggingEnvPt.Value, draggingEnvPt.Curve);
        }
        else if (draggingEnvTrack != null)
        {
            SetTrackEnvelopePointRequested?.Invoke(draggingEnvTrack.Id, draggingEnv.Type, draggingEnvPt.Frame, draggingEnvPt.Value, draggingEnvPt.Curve);
        }

        draggingEnvClip = null;
        draggingEnvTrack = null;
        draggingEnv = null;
        draggingEnvPt = null;
        InvalidateVisual();
        return true;
    }

    private bool HandleEnvelopeRightClick(Point p)
    {
        // 1. Clip envelope point
        if (HitEnvelopePoint(p, out var clip, out var env, out var pt))
        {
            var menu = new ContextMenu();

            var title = new MenuItem
            {
                Header = $"{env!.Type} Point ({Math.Round(pt!.Value * 100)}% at {pt.Frame}f)",
                IsEnabled = false,
                FontWeight = FontWeights.Bold
            };
            menu.Items.Add(title);
            menu.Items.Add(new Separator());

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
                var curveItem = new MenuItem
                {
                    Header = $"{curve} Fade",
                    IsCheckable = true,
                    IsChecked = pt.Curve == curve
                };
                var targetCurve = curve;
                curveItem.Click += (_, _) =>
                {
                    SetEnvelopePointCurveRequested?.Invoke(clip!.Id, env.Type, pt.Frame, targetCurve);
                };
                menu.Items.Add(curveItem);
            }

            if (pt.Frame > 0 && pt.Frame < clip!.DurationFrames)
            {
                menu.Items.Add(new Separator());
                var deleteItem = new MenuItem { Header = "Delete Point" };
                deleteItem.Click += (_, _) =>
                {
                    RemoveEnvelopePointRequested?.Invoke(clip!.Id, env.Type, pt.Frame);
                };
                menu.Items.Add(deleteItem);
            }

            menu.PlacementTarget = this;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            menu.IsOpen = true;
            return true;
        }

        // 2. Track envelope point
        if (HitTrackEnvelopePoint(p, out var track, out var trackEnv, out var trackPt))
        {
            var menu = new ContextMenu();

            var title = new MenuItem
            {
                Header = $"Track {trackEnv!.Type} Point ({Math.Round(trackPt!.Value * 100)}% at {trackPt.Frame}f)",
                IsEnabled = false,
                FontWeight = FontWeights.Bold
            };
            menu.Items.Add(title);
            menu.Items.Add(new Separator());

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
                var curveItem = new MenuItem
                {
                    Header = $"{curve} Fade",
                    IsCheckable = true,
                    IsChecked = trackPt.Curve == curve
                };
                var targetCurve = curve;
                curveItem.Click += (_, _) =>
                {
                    SetTrackEnvelopePointCurveRequested?.Invoke(track!.Id, trackEnv.Type, trackPt.Frame, targetCurve);
                };
                menu.Items.Add(curveItem);
            }

            if (trackEnv.Points.Count > 1)
            {
                menu.Items.Add(new Separator());
                var deleteItem = new MenuItem { Header = "Delete Point" };
                deleteItem.Click += (_, _) =>
                {
                    RemoveTrackEnvelopePointRequested?.Invoke(track!.Id, trackEnv.Type, trackPt.Frame);
                };
                menu.Items.Add(deleteItem);
            }

            menu.PlacementTarget = this;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            menu.IsOpen = true;
            return true;
        }

        return false;
    }
}