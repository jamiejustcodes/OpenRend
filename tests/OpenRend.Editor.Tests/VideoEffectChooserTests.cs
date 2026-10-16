using OpenRend.App;
using OpenRend.Core;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class VideoEffectChooserTests
{
    [Fact]
    public void DraftChangesLeaveSourceChainAndParametersUntouched()
    {
        var source = VideoEffect.Create(VideoEffectKind.ColorBalance);
        source.SetParameter("Red", .25);
        source.IsEnabled = false;
        var draft = new VideoEffectChooserModel([source]);

        draft.Effects[0].SetParameter("Red", -.5);
        draft.Effects[0].IsEnabled = true;
        draft.Add(VideoEffectKind.Invert);

        Assert.Equal(.25, source.GetParameter("Red"));
        Assert.False(source.IsEnabled);
        Assert.Equal(source.Id, draft.Effects[0].Id);
    }

    [Fact]
    public void ReplaceKeepsPositionAndUsesFreshDefaults()
    {
        var first = VideoEffect.Create(VideoEffectKind.Invert);
        var second = VideoEffect.Create(VideoEffectKind.Saturation);
        var draft = new VideoEffectChooserModel([first, second]);
        draft.Replace(0, VideoEffectKind.GaussianBlur);

        Assert.Equal(VideoEffectKind.GaussianBlur, draft.Effects[0].Kind);
        Assert.NotEqual(first.Id, draft.Effects[0].Id);
        Assert.Equal(VideoEffectCatalog.Get(VideoEffectKind.GaussianBlur).Parameters[0].DefaultValue,
            draft.Effects[0].GetParameter("Amount"));
        Assert.Equal(second.Id, draft.Effects[1].Id);
        Assert.Equal(VideoEffectKind.Invert, first.Kind);
    }

    [Fact]
    public void RemoveCanProduceAnEmptyAcceptedChain()
    {
        var draft = new VideoEffectChooserModel([VideoEffect.Create(VideoEffectKind.Invert)]);
        draft.Remove(0);
        Assert.Empty(draft.Snapshot());
        Assert.Equal(0, draft.Add(VideoEffectKind.Sepia));
        Assert.Equal(VideoEffectKind.Sepia, Assert.Single(draft.Effects).Kind);
    }

    [Fact]
    public void AcceptedSnapshotDoesNotShareParametersWithDraftOrSource()
    {
        var source = VideoEffect.Create(VideoEffectKind.ColorBalance);
        var draft = new VideoEffectChooserModel([source]);
        var accepted = draft.Snapshot();
        accepted[0].SetParameter("Blue", .75);
        draft.Effects[0].SetParameter("Red", -.25);

        Assert.Equal(0, source.GetParameter("Blue"));
        Assert.Equal(0, draft.Effects[0].GetParameter("Blue"));
        Assert.Equal(0, accepted[0].GetParameter("Red"));
        Assert.Equal(source.Id, accepted[0].Id);
    }
}
