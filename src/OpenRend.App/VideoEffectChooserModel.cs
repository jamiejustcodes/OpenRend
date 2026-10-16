using OpenRend.Core;

namespace OpenRend.App;

// The chooser owns a draft so Cancel never leaks an edit into the project.
internal sealed class VideoEffectChooserModel
{
    private readonly List<VideoEffect> effects;

    public VideoEffectChooserModel(IReadOnlyList<VideoEffect> source) =>
        effects = source.Select(effect => effect.DeepClone()).ToList();

    public IReadOnlyList<VideoEffect> Effects => effects;

    public int Add(VideoEffectKind kind)
    {
        effects.Add(VideoEffect.Create(kind));
        return effects.Count - 1;
    }

    public void Replace(int index, VideoEffectKind kind) => effects[index] = VideoEffect.Create(kind);
    public void Remove(int index) => effects.RemoveAt(index);

    // A second copy keeps the accepted result independent of the closed dialog.
    public IReadOnlyList<VideoEffect> Snapshot() => effects.Select(effect => effect.DeepClone()).ToArray();
}
