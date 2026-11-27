using OpenRend.Core;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class ScopedVideoEffectPayloadTests
{
    [Theory]
    [InlineData(EffectScope.Event)]
    [InlineData(EffectScope.Track)]
    [InlineData(EffectScope.Media)]
    [InlineData(EffectScope.Output)]
    public void ChainReplacementKeepsLutsCurvesAndKeyframes(EffectScope scope)
    {
        var media = new MediaAsset { Path = "source.mp4", HasVideo = true, DurationSeconds = 2 };
        var clip = new TimelineClip { MediaId = media.Id, DurationFrames = 30 };
        var track = new TimelineTrack { Kind = TrackKind.Video, Clips = [clip] };
        var editor = new TimelineEditor(new ProjectDocument { Media = [media], Tracks = [track] });
        var id = scope switch
        {
            EffectScope.Event => clip.Id,
            EffectScope.Track => track.Id,
            EffectScope.Media => media.Id,
            _ => Guid.Empty
        };
        VideoEffect Read() => Assert.Single(editor.GetScopedVideoEffects(scope, id));
        void Replace(Action<VideoEffect> change)
        {
            var copy = Read().DeepClone();
            change(copy);
            editor.SetScopedVideoEffects(scope, id, [copy]);
        }

        editor.AddScopedVideoEffect(scope, id, VideoEffectKind.ColorGrading);
        Replace(effect => effect.LookLut = BuiltinLutLibrary.CreatePreset(BuiltinLutLibrary.LookLutNames[1]));
        Replace(effect => effect.Curves = new() { Red = new() { Points = [new(0, .1), new(1, .9)] } });
        Replace(effect => { effect.HSLCurves = new(); effect.HSLCurves.HueVsSat.Points[0].Y = .7; });
        Replace(effect => effect.Animation = new() { IsEnabled = true, Keyframes = [new() { Frame = 12 }] });
        Replace(effect => Assert.NotNull(effect.Animation));
        Assert.Equal(BuiltinLutLibrary.LookLutNames[1], Read().LookLut?.Title);
        Assert.Equal(.1, Read().Curves?.Red.Points[0].Y);
        Assert.Equal(.7, Read().HSLCurves?.HueVsSat.Points[0].Y);
        Assert.Equal(12, Assert.Single(Read().Animation!.Keyframes).Frame);

        Assert.True(editor.Undo());
        Assert.Null(Read().Animation);
        Assert.True(editor.Undo());
        Assert.Null(Read().HSLCurves);
        Assert.True(editor.Undo());
        Assert.Null(Read().Curves);
        Assert.True(editor.Undo());
        Assert.Null(Read().LookLut);
    }
}
