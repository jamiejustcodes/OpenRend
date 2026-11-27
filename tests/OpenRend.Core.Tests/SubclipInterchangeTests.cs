using OpenRend.Core;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class SubclipInterchangeTests
{
    [Theory]
    [InlineData("EDL")]
    [InlineData("FCP7 XML")]
    [InlineData("FCPXML")]
    public void ForwardSubclipsExportOriginalFilePositionsAcrossProjectRates(string format)
    {
        var path = Path.GetTempFileName();
        try
        {
            var rate = new Rational(24, 1);
            var early = Subclip(MediaSourceRange.FromFrames(new(101, 60), new(30000, 1001)), "Early shot");
            var late = Subclip(new(33, 2, 1), "Late shot");
            var project = new ProjectDocument
            {
                FrameRate = rate,
                Media = [early, late],
                Tracks =
                [
                    new() { Kind = TrackKind.Video, Clips = [Event(early, 0, 3, 20), Event(late, 34, 18, 24)] },
                    new() { Kind = TrackKind.Audio, Clips = [Event(early, 0, 3, 20), Event(late, 34, 18, 24)] }
                ]
            };
            project.Validate();

            var reopened = format switch
            {
                "EDL" => EdlInterchange.ImportFromEdl(EdlInterchange.ExportToEdl(project), rate),
                "FCP7 XML" => Fcp7XmlInterchange.ImportFromXml(Fcp7XmlInterchange.ExportToXml(project)),
                "FCPXML" => FcpxmlInterchange.ImportFromXml(FcpxmlInterchange.ExportToXml(project)),
                _ => throw new ArgumentOutOfRangeException(nameof(format))
            };
            reopened.Validate();
            Assert.Equal(rate, reopened.FrameRate);
            foreach (var kind in new[] { TrackKind.Video, TrackKind.Audio })
            {
                var track = Assert.Single(reopened.Tracks, track => track.Kind == kind);
                Assert.Equal(new[] { (0L, 84L, 20L), (34L, 810L, 24L) },
                    track.Clips.Select(clip => (clip.StartFrame, clip.SourceInFrame, clip.DurationFrames)));
                foreach (var clip in track.Clips)
                {
                    var media = reopened.Media.Single(asset => asset.Id == clip.MediaId);
                    Assert.Equal(path, media.Path);
                    Assert.True(rate.Frames(media.DurationSeconds) >= clip.SourceInFrame + clip.DurationFrames);
                }
            }
            Assert.Equal(early.SourceRange, project.Media[0].SourceRange);
            Assert.Equal(3, project.Tracks[0].Clips[0].SourceInFrame);
        }
        finally { File.Delete(path); }

        MediaAsset Subclip(MediaSourceRange range, string name) => new()
        {
            Path = path, DisplayName = name, SourceRange = range, DurationSeconds = range.DurationSeconds,
            HasVideo = true, HasAudio = true, Width = 1920, Height = 1080
        };

        static TimelineClip Event(MediaAsset source, long start, long sourceIn, long frames) => new()
        {
            MediaId = source.Id, Name = source.Name, StartFrame = start, SourceInFrame = sourceIn, DurationFrames = frames
        };
    }
}
