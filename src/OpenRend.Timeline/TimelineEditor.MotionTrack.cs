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
        Execute(p =>
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
        Execute(p =>
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

    public void TransferMotionToMask(Guid sourceClipId, Guid regionId, Guid targetClipId)
    {
        Execute(p =>
        {
            var sourceClip = p.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == sourceClipId)
                ?? throw new KeyNotFoundException("Source clip was not found.");
            var region = sourceClip.MotionTracks.FirstOrDefault(r => r.Id == regionId)
                ?? throw new KeyNotFoundException("Motion tracking region was not found.");
            var targetClip = p.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == targetClipId)
                ?? throw new KeyNotFoundException("Target clip was not found.");

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
}
