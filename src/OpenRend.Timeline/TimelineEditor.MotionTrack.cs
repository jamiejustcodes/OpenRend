using OpenRend.Core;

namespace OpenRend.Timeline;

public sealed partial class TimelineEditor
{
    public IReadOnlyList<MotionTrackData> GetMotionTracks(Guid clipId)
    {
        var clip = Project.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == clipId);
        return clip?.MotionTracks.Select(t => t.DeepClone()).ToList() ?? [];
    }

    public void AddMotionTrack(Guid clipId, MotionTrackData region)
    {
        ArgumentNullException.ThrowIfNull(region);
        region.Validate();
        Execute(p =>
        {
            var clip = p.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == clipId)
                ?? throw new KeyNotFoundException("Timeline clip was not found.");
            clip.MotionTracks.Add(region.DeepClone());
        });
    }

    public void RemoveMotionTrack(Guid clipId, Guid regionId)
    {
        Execute(p =>
        {
            var clip = p.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == clipId)
                ?? throw new KeyNotFoundException("Timeline clip was not found.");
            clip.MotionTracks.RemoveAll(r => r.Id == regionId);
        });
    }

    public void UpdateMotionTrack(Guid clipId, MotionTrackData region)
    {
        ArgumentNullException.ThrowIfNull(region);
        region.Validate();
        Execute(p =>
        {
            var clip = p.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == clipId)
                ?? throw new KeyNotFoundException("Timeline clip was not found.");
            var idx = clip.MotionTracks.FindIndex(r => r.Id == region.Id);
            if (idx >= 0) clip.MotionTracks[idx] = region.DeepClone();
            else clip.MotionTracks.Add(region.DeepClone());
        });
    }

    public void SetMotionTrackKeyframes(Guid clipId, Guid regionId, IReadOnlyList<TrackingKeyframe> keyframes)
    {
        ArgumentNullException.ThrowIfNull(keyframes);
        foreach (var k in keyframes) k.Validate();
        Execute(p =>
        {
            var clip = p.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == clipId)
                ?? throw new KeyNotFoundException("Timeline clip was not found.");
            var region = clip.MotionTracks.FirstOrDefault(r => r.Id == regionId)
                ?? throw new KeyNotFoundException("Motion tracking region was not found.");
            region.Keyframes = keyframes.Select(k => k.DeepClone()).ToList();
            region.SortKeyframes();
        });
    }

    public void TransferMotionToPictureInPicture(Guid sourceClipId, Guid regionId, Guid targetClipId)
    {
        Execute("Transfer Motion to Picture in Picture", p =>
        {
            var sourceClip = p.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == sourceClipId)
                ?? throw new KeyNotFoundException("Source clip was not found.");
            var region = sourceClip.MotionTracks.FirstOrDefault(r => r.Id == regionId)
                ?? throw new KeyNotFoundException("Motion tracking region was not found.");
            var targetClip = p.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == targetClipId)
                ?? throw new KeyNotFoundException("Target clip was not found.");

            // Find or add Picture In Picture effect
            var pip = targetClip.VideoEffects.FirstOrDefault(e => e.Kind == VideoEffectKind.PictureInPicture);
            if (pip == null)
            {
                pip = VideoEffect.Create(VideoEffectKind.PictureInPicture);
                targetClip.VideoEffects.Add(pip);
            }

            // If region has keyframes, evaluate at target clip start frame (or align with source)
            var startFrame = Math.Max(sourceClip.StartFrame, targetClip.StartFrame);
            var kf = region.Evaluate(startFrame);
            if (kf is not null)
            {
                pip.SetParameter("LocationX", Math.Clamp(kf.CenterX, 0.0, 1.0));
                pip.SetParameter("LocationY", Math.Clamp(kf.CenterY, 0.0, 1.0));
                pip.SetParameter("Scale", Math.Clamp(Math.Max(kf.ScaleX, kf.ScaleY), 0.01, 2.0));
                pip.SetParameter("Angle", Math.Clamp(kf.Angle, -360.0, 360.0));
            }
        });
    }

    public void TransferMotionToCrop(Guid sourceClipId, Guid regionId, Guid targetClipId, bool stabilize)
    {
        Execute("Transfer Motion to Pan/Crop", p =>
        {
            var sourceClip = p.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == sourceClipId)
                ?? throw new KeyNotFoundException("Source clip was not found.");
            var region = sourceClip.MotionTracks.FirstOrDefault(r => r.Id == regionId)
                ?? throw new KeyNotFoundException("Motion tracking region was not found.");
            var targetClip = p.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == targetClipId)
                ?? throw new KeyNotFoundException("Target clip was not found.");

            // Create or update crop animation with keyframes
            if (region.Keyframes.Count == 0) return;

            var media = p.Media.FirstOrDefault(m => m.Id == targetClip.MediaId);
            var srcW = media?.Width > 0 ? media.Width : p.Width;
            var srcH = media?.Height > 0 ? media.Height : p.Height;

            var cropAnim = new CropAnimation();
            var refCenter = region.Keyframes[0];

            foreach (var kf in region.Keyframes)
            {
                var relFrame = kf.ProjectFrame - targetClip.StartFrame;
                if (relFrame < 0 || relFrame > targetClip.DurationFrames) continue;

                var dx = (kf.CenterX - refCenter.CenterX) * srcW;
                var dy = (kf.CenterY - refCenter.CenterY) * srcH;

                // For stabilization: invert movement; for follow: follow movement
                var factor = stabilize ? -1.0 : 1.0;
                var cx = (srcW / 2.0) + dx * factor;
                var cy = (srcH / 2.0) + dy * factor;

                var state = new CropState(srcW, srcH, cx, cy, kf.Angle * factor, srcW / 2.0, srcH / 2.0);

                cropAnim.Keyframes.Add(new CropKeyframe
                {
                    Frame = relFrame,
                    State = state,
                    Interpolation = CropInterpolation.Smooth
                });
            }

            if (cropAnim.Keyframes.Count > 0)
            {
                cropAnim.Keyframes.Sort((a, b) => a.Frame.CompareTo(b.Frame));
                targetClip.Crop = cropAnim;
            }
        });
    }

    /// <summary>
    /// Transfers motion tracking keyframe trajectory to an animated Bézier Mask on the target clip,
    /// matching Vegas Pro MotionTrackWindow.CopyMotionTracks.CopyToBzMasking.
    /// </summary>
    public void TransferMotionToMask(Guid sourceClipId, Guid regionId, Guid targetClipId)
    {
        Execute("Transfer Motion to Mask", p =>
        {
            var sourceClip = p.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == sourceClipId)
                ?? throw new KeyNotFoundException("Source clip was not found.");
            var region = sourceClip.MotionTracks.FirstOrDefault(r => r.Id == regionId)
                ?? throw new KeyNotFoundException("Motion tracking region was not found.");
            var targetClip = p.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == targetClipId)
                ?? throw new KeyNotFoundException("Target clip was not found.");

            var media = p.Media.FirstOrDefault(m => m.Id == targetClip.MediaId);
            var srcW = media?.Width > 0 ? media.Width : p.Width;
            var srcH = media?.Height > 0 ? media.Height : p.Height;

            var maskAnim = new MaskAnimation { IsEnabled = true };
            var mode = region.IsInverted ? MaskMode.Negative : MaskMode.Positive;

            // Generate animated MaskKeyframes for all tracking keyframes intersecting target clip
            var keyframesToProcess = region.Keyframes.Count > 0
                ? region.Keyframes
                : [new TrackingKeyframe(targetClip.StartFrame, 0.5, 0.5, 0.25, 0.25)];

            foreach (var kf in keyframesToProcess)
            {
                var relFrame = kf.ProjectFrame - targetClip.StartFrame;
                if (relFrame < 0 || relFrame > targetClip.DurationFrames) continue;

                var cx = Math.Clamp(kf.CenterX, 0.0, 1.0) * srcW;
                var cy = Math.Clamp(kf.CenterY, 0.0, 1.0) * srcH;
                var w = Math.Clamp(kf.Width * kf.ScaleX, 0.01, 2.0) * srcW;
                var h = Math.Clamp(kf.Height * kf.ScaleY, 0.01, 2.0) * srcH;

                MaskShape shape;
                if (region.Shape == TrackingShape.Oval)
                {
                    shape = MaskShape.CreateOval(cx, cy, w * 0.5, h * 0.5);
                }
                else if (region.Shape == TrackingShape.Bezier && region.BezierPoints.Count >= 3)
                {
                    shape = new MaskShape();
                    var path = new MaskPath { IsClosed = true, Mode = mode };
                    foreach (var bp in region.BezierPoints)
                    {
                        var ptX = cx + (bp.X - 0.5) * w;
                        var ptY = cy + (bp.Y - 0.5) * h;
                        var inX = bp.InHandleX * w;
                        var inY = bp.InHandleY * h;
                        var outX = bp.OutHandleX * w;
                        var outY = bp.OutHandleY * h;
                        path.Points.Add(new MaskPoint(ptX, ptY, inX, inY, outX, outY));
                    }
                    shape.Paths.Add(path);
                }
                else
                {
                    shape = MaskShape.CreateRectangle(cx - w * 0.5, cy - h * 0.5, w, h);
                }

                foreach (var path in shape.Paths)
                {
                    path.Mode = mode;
                    // Rotate mask points around center if angle is non-zero
                    if (Math.Abs(kf.Angle) > 0.001)
                    {
                        var rad = kf.Angle * Math.PI / 180.0;
                        var cos = Math.Cos(rad);
                        var sin = Math.Sin(rad);
                        foreach (var pt in path.Points)
                        {
                            var dx = pt.X - cx;
                            var dy = pt.Y - cy;
                            pt.X = cx + dx * cos - dy * sin;
                            pt.Y = cy + dx * sin + dy * cos;

                            var rInX = pt.InTangentX * cos - pt.InTangentY * sin;
                            var rInY = pt.InTangentX * sin + pt.InTangentY * cos;
                            pt.InTangentX = rInX;
                            pt.InTangentY = rInY;

                            var rOutX = pt.OutTangentX * cos - pt.OutTangentY * sin;
                            var rOutY = pt.OutTangentX * sin + pt.OutTangentY * cos;
                            pt.OutTangentX = rOutX;
                            pt.OutTangentY = rOutY;
                        }
                    }
                }

                maskAnim.Keyframes.Add(new MaskKeyframe
                {
                    Frame = relFrame,
                    Shape = shape,
                    Interpolation = CropInterpolation.Smooth
                });
            }

            if (maskAnim.Keyframes.Count > 0)
            {
                maskAnim.Keyframes.Sort((a, b) => a.Frame.CompareTo(b.Frame));
                targetClip.Mask = maskAnim;
            }

            // Also ensure MaskGenerator effect exists for compatibility
            var mask = targetClip.VideoEffects.FirstOrDefault(e => e.Kind == VideoEffectKind.MaskGenerator);
            if (mask == null)
            {
                mask = VideoEffect.Create(VideoEffectKind.MaskGenerator);
                targetClip.VideoEffects.Add(mask);
            }
            if (region.IsInverted)
            {
                mask.SetParameter("Invert", 1.0);
            }
        });
    }

    /// <summary>
    /// Exports the motion tracking keyframe parameters matching Vegas Pro CopyMotionTracks.CopyKeyframesParametric.
    /// </summary>
    public IReadOnlyList<(long Frame, double CenterX, double CenterY, double ScaleX, double ScaleY, double Angle, double Shear, double Confidence)>
        GetMotionTrackParametricKeyframes(Guid clipId, Guid regionId)
    {
        var clip = Project.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == clipId);
        var region = clip?.MotionTracks.FirstOrDefault(r => r.Id == regionId);
        if (region == null) return Array.Empty<(long, double, double, double, double, double, double, double)>();

        return region.Keyframes
            .OrderBy(k => k.ProjectFrame)
            .Select(k => (k.ProjectFrame, k.CenterX, k.CenterY, k.ScaleX, k.ScaleY, k.Angle, k.Shear, k.Confidence))
            .ToList();
    }
}
