using OpenRend.Core;

namespace OpenRend.Timeline;

public sealed partial class TimelineEditor
{
    public MediaBin CreateMediaBin(string name, Guid? parentBinId = null)
    {
        if (string.IsNullOrWhiteSpace(name)) name = "New Bin";
        var bin = new MediaBin
        {
            Name = name.Trim(),
            ParentBinId = parentBinId
        };
        Execute(p => p.MediaBins.Add(bin.DeepClone()));
        return bin;
    }

    public void RenameMediaBin(Guid binId, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return;
        Execute(p =>
        {
            var bin = p.MediaBins.FirstOrDefault(b => b.Id == binId);
            if (bin is not null)
            {
                bin.Name = newName.Trim();
            }
        });
    }

    public void DeleteMediaBin(Guid binId)
    {
        Execute(p =>
        {
            // Collect all bins in the subtree rooted at binId
            var toRemove = new HashSet<Guid> { binId };
            bool added;
            do
            {
                added = false;
                foreach (var b in p.MediaBins)
                {
                    if (b.ParentBinId.HasValue && toRemove.Contains(b.ParentBinId.Value) && !toRemove.Contains(b.Id))
                    {
                        toRemove.Add(b.Id);
                        added = true;
                    }
                }
            } while (added);

            p.MediaBins.RemoveAll(b => toRemove.Contains(b.Id));
        });
    }

    public void AddMediaToBin(Guid binId, Guid mediaId)
    {
        Execute(p =>
        {
            var bin = p.MediaBins.FirstOrDefault(b => b.Id == binId);
            if (bin is not null && !bin.MediaIds.Contains(mediaId))
            {
                bin.MediaIds.Add(mediaId);
            }
        });
    }

    public void RemoveMediaFromBin(Guid binId, Guid mediaId)
    {
        Execute(p =>
        {
            var bin = p.MediaBins.FirstOrDefault(b => b.Id == binId);
            bin?.MediaIds.Remove(mediaId);
        });
    }

    public void SetMediaTags(Guid mediaId, IEnumerable<string> tags)
    {
        var cleaned = tags.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Execute(p =>
        {
            var asset = p.Media.FirstOrDefault(m => m.Id == mediaId);
            if (asset is not null)
            {
                asset.Tags = [..cleaned];
            }
        });
    }
}
