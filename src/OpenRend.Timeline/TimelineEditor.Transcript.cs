using OpenRend.Core;

namespace OpenRend.Timeline;

public sealed partial class TimelineEditor
{
    public ClipTranscript? GetClipTranscript(Guid clipId)
    {
        var (_, clip) = FindClip(clipId);
        return clip.Transcript?.DeepClone();
    }

    public void SetClipTranscript(Guid clipId, ClipTranscript? transcript)
    {
        var (track, clip) = FindClip(clipId);
        EnsureUnlocked(track, clip);
        Execute("Set Clip Transcript", p =>
        {
            var target = p.Tracks.SelectMany(t => t.Clips).First(c => c.Id == clipId);
            target.Transcript = transcript?.DeepClone();
        });
    }

    public TimelineClip SplitClipAtWord(Guid clipId, int wordIndex, bool ignoreGroups = false)
    {
        var (track, clip) = FindClip(clipId);
        EnsureUnlocked(track, clip);
        if (clip.Transcript == null || wordIndex < 0 || wordIndex >= clip.Transcript.Words.Count)
            throw new ArgumentOutOfRangeException(nameof(wordIndex), "Word index is outside transcript.");

        var word = clip.Transcript.Words[wordIndex];
        var splitFrame = clip.StartFrame + word.StartFrame;

        if (clip.GroupId.HasValue && !ignoreGroups)
        {
            var results = SplitSelection([clip.Id], splitFrame, ignoreGroups: false);
            return results.First(c => track.Clips.Any(tc => tc.Id == c.Id));
        }

        return SplitClip(clipId, splitFrame);
    }

    public void DeleteTranscriptRange(Guid clipId, long startOffsetFrames, long endOffsetFrames, bool autoRipple = false, bool ignoreGroups = false)
    {
        var (track, clip) = FindClip(clipId);
        EnsureUnlocked(track, clip);
        if (startOffsetFrames < 0) startOffsetFrames = 0;
        if (endOffsetFrames > clip.DurationFrames) endOffsetFrames = clip.DurationFrames;
        if (startOffsetFrames >= endOffsetFrames) return;

        var cutLen = endOffsetFrames - startOffsetFrames;

        Execute("Delete Transcript Range", p =>
        {
            var targetTrack = p.Tracks.First(t => t.Id == track.Id);
            var targetClip = targetTrack.Clips.First(c => c.Id == clipId);

            // 1. Whole clip deletion
            if (startOffsetFrames == 0 && endOffsetFrames >= targetClip.DurationFrames)
            {
                targetTrack.Clips.Remove(targetClip);
                if (autoRipple)
                {
                    foreach (var later in targetTrack.Clips.Where(c => c.StartFrame >= targetClip.EndFrame))
                        later.StartFrame -= targetClip.DurationFrames;
                }
                return;
            }

            // 2. Trim head
            if (startOffsetFrames == 0)
            {
                var oldStart = targetClip.StartFrame;
                var oldDuration = targetClip.DurationFrames;
                targetClip.SourceInFrame += cutLen;
                targetClip.DurationFrames -= cutLen;
                if (!autoRipple)
                {
                    targetClip.StartFrame += cutLen;
                }
                if (targetClip.Transcript is not null)
                {
                    targetClip.Transcript.Words = targetClip.Transcript.Words
                        .Where(w => w.EndFrame > cutLen)
                        .Select(w =>
                        {
                            var c = w.DeepClone();
                            c.StartFrame = Math.Max(0, c.StartFrame - cutLen);
                            return c;
                        }).ToList();
                }
                if (autoRipple)
                {
                    foreach (var later in targetTrack.Clips.Where(c => c.Id != targetClip.Id && c.StartFrame >= oldStart + oldDuration))
                        later.StartFrame -= cutLen;
                }
                return;
            }

            // 3. Trim tail
            if (endOffsetFrames >= targetClip.DurationFrames)
            {
                var oldEnd = targetClip.EndFrame;
                targetClip.DurationFrames = startOffsetFrames;
                if (targetClip.Transcript is not null)
                {
                    targetClip.Transcript.Words = targetClip.Transcript.Words
                        .Where(w => w.StartFrame < startOffsetFrames)
                        .Select(w =>
                        {
                            var c = w.DeepClone();
                            if (c.EndFrame > startOffsetFrames) c.DurationFrames = startOffsetFrames - c.StartFrame;
                            return c;
                        }).ToList();
                }
                if (autoRipple)
                {
                    foreach (var later in targetTrack.Clips.Where(c => c.Id != targetClip.Id && c.StartFrame >= oldEnd))
                        later.StartFrame -= cutLen;
                }
                return;
            }

            // 4. Middle cut (split into left and right, delete middle)
            var leftDuration = startOffsetFrames;
            var rightDuration = targetClip.DurationFrames - endOffsetFrames;
            var oldClipEnd = targetClip.EndFrame;

            var rightClip = targetClip.DeepClone();
            rightClip.Id = Guid.NewGuid();
            rightClip.StartFrame = autoRipple ? targetClip.StartFrame + leftDuration : targetClip.StartFrame + endOffsetFrames;
            rightClip.SourceInFrame += endOffsetFrames;
            rightClip.DurationFrames = rightDuration;
            if (rightClip.Crop is not null) rightClip.CropOffsetFrames = checked(rightClip.CropOffsetFrames + endOffsetFrames);

            if (targetClip.Transcript is not null)
            {
                var origWords = targetClip.Transcript.Words;
                targetClip.Transcript.Words = origWords
                    .Where(w => w.StartFrame < leftDuration)
                    .Select(w =>
                    {
                        var c = w.DeepClone();
                        if (c.EndFrame > leftDuration) c.DurationFrames = Math.Max(0, leftDuration - c.StartFrame);
                        return c;
                    }).ToList();

                rightClip.Transcript = new ClipTranscript
                {
                    Language = targetClip.Transcript.Language,
                    Words = origWords
                        .Where(w => w.EndFrame > endOffsetFrames)
                        .Select(w =>
                        {
                            var c = w.DeepClone();
                            var oldStart = c.StartFrame;
                            var oldEnd = c.EndFrame;
                            c.StartFrame = Math.Max(0, oldStart - endOffsetFrames);
                            c.DurationFrames = Math.Max(0, oldEnd - Math.Max(endOffsetFrames, oldStart));
                            return c;
                        }).ToList()
                };
            }

            targetClip.DurationFrames = leftDuration;
            targetTrack.Clips.Add(rightClip);

            if (autoRipple)
            {
                foreach (var later in targetTrack.Clips.Where(c => c.Id != targetClip.Id && c.Id != rightClip.Id && c.StartFrame >= oldClipEnd))
                {
                    later.StartFrame -= cutLen;
                }
            }
        });
    }

    public ClipTranscript CutTranscriptRange(Guid clipId, long startOffsetFrames, long endOffsetFrames, bool autoRipple = false, bool ignoreGroups = false)
    {
        var (_, clip) = FindClip(clipId);
        var transcript = clip.Transcript;
        var cutWords = transcript?.GetWordsInRange(startOffsetFrames, endOffsetFrames)
            .Select(w =>
            {
                var clone = w.DeepClone();
                clone.StartFrame = Math.Max(0, clone.StartFrame - startOffsetFrames);
                return clone;
            }).ToList() ?? [];

        DeleteTranscriptRange(clipId, startOffsetFrames, endOffsetFrames, autoRipple, ignoreGroups);
        return new ClipTranscript(cutWords, transcript?.Language ?? "en");
    }

    public int DeletePauses(Guid clipId, long minPauseFrames = 15, bool autoRipple = true, bool ignoreGroups = false)
    {
        var (track, clip) = FindClip(clipId);
        EnsureUnlocked(track, clip);
        if (clip.Transcript == null || clip.Transcript.Words.Count == 0) return 0;

        // Detect all pauses between words and at boundaries
        var pauses = new List<(long Start, long End)>();
        var words = clip.Transcript.Words.OrderBy(w => w.StartFrame).ToList();

        if (words[0].StartFrame >= minPauseFrames)
        {
            pauses.Add((0, words[0].StartFrame));
        }

        for (var i = 0; i < words.Count - 1; i++)
        {
            var gapStart = words[i].EndFrame;
            var gapEnd = words[i + 1].StartFrame;
            if (gapEnd - gapStart >= minPauseFrames)
            {
                pauses.Add((gapStart, gapEnd));
            }
        }

        if (clip.DurationFrames - words[^1].EndFrame >= minPauseFrames)
        {
            pauses.Add((words[^1].EndFrame, clip.DurationFrames));
        }

        if (pauses.Count == 0) return 0;

        // Delete from right to left so earlier pause offsets remain stable
        pauses.Sort((a, b) => b.Start.CompareTo(a.Start));
        var count = 0;
        foreach (var (pStart, pEnd) in pauses)
        {
            DeleteTranscriptRange(clipId, pStart, pEnd, autoRipple: autoRipple, ignoreGroups: ignoreGroups);
            count++;
        }
        return count;
    }
}
