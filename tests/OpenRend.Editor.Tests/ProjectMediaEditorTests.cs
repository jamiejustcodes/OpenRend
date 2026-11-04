using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Controls;
using OpenRend.App;
using OpenRend.Core;
using OpenRend.Media;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class ProjectMediaEditorTests
{
    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new AggregateException(error);
    }

    private static EditorViewModel CreateViewModel()
    {
        var media = new MediaService();
        var renderer = new TimelineRenderService(media);
        return new EditorViewModel(media, renderer, new ExportService(renderer));
    }

    [Fact]
    public void MediaItem_Properties_ReflectMetadataAndUseCount()
    {
        var asset = new MediaAsset
        {
            Path = @"C:\Media\sample_clip.mp4",
            Width = 1920,
            Height = 1080,
            FrameRate = 29.97,
            DurationSeconds = 65.5,
            HasVideo = true,
            HasAudio = true
        };

        var item = new MediaItem(asset);
        item.UseCount = 3;

        Assert.Equal("sample_clip.mp4", item.Name);
        Assert.Equal("Video", item.Type);
        Assert.Equal("1920 \u00D7 1080", item.FrameSize);
        Assert.Equal("29.97 fps", item.FrameRateText);
        Assert.Equal("00:01:05.50", item.DurationText);
        Assert.Equal("Stereo, 48 kHz", item.AudioInfo);
        Assert.Equal(3, item.UseCount);
    }

    [Fact]
    public void EditorViewModel_RefreshLibrary_CalculatesUseCountsAccurately()
    {
        var doc = new ProjectDocument();
        var media1 = new MediaAsset { Path = "clip1.mp4", HasVideo = true, DurationSeconds = 10 };
        var media2 = new MediaAsset { Path = "audio1.wav", HasAudio = true, DurationSeconds = 20 };
        doc.Media.Add(media1);
        doc.Media.Add(media2);

        var track = new TimelineTrack { Kind = TrackKind.Video };
        // Place media1 twice on the timeline
        track.Clips.Add(new TimelineClip { MediaId = media1.Id, StartFrame = 0, DurationFrames = 30 });
        track.Clips.Add(new TimelineClip { MediaId = media1.Id, StartFrame = 50, DurationFrames = 30 });
        doc.Tracks.Add(track);

        var vm = CreateViewModel();
        vm.Replace(doc, null);

        var item1 = vm.Library.Single(i => i.Asset.Id == media1.Id);
        var item2 = vm.Library.Single(i => i.Asset.Id == media2.Id);

        Assert.Equal(2, item1.UseCount);
        Assert.Equal(0, item2.UseCount);
    }

    [Fact]
    public void MediaPropertiesWindow_CreatesTabsForVideoAndAudioStreams() => RunSta(() =>
    {
        var videoAsset = new MediaAsset
        {
            Path = "test_render.mp4",
            Width = 3840,
            Height = 2160,
            FrameRate = 60.0,
            DurationSeconds = 120.0,
            HasVideo = true,
            HasAudio = true
        };

        var win = new MediaPropertiesWindow(videoAsset);
        Assert.Contains("test_render.mp4", win.Title);

        var rootGrid = Assert.IsType<Grid>(win.Content);
        var tabControl = Assert.IsType<TabControl>(rootGrid.Children[0]);

        // General, Video, Audio tabs
        Assert.Equal(3, tabControl.Items.Count);
        var tab0 = Assert.IsType<TabItem>(tabControl.Items[0]);
        var tab1 = Assert.IsType<TabItem>(tabControl.Items[1]);
        var tab2 = Assert.IsType<TabItem>(tabControl.Items[2]);

        Assert.Equal("General", tab0.Header);
        Assert.Equal("Video", tab1.Header);
        Assert.Equal("Audio", tab2.Header);
    });

    [Fact]
    public void MediaPropertiesWindow_AudioOnly_OmitsVideoTab() => RunSta(() =>
    {
        var audioAsset = new MediaAsset
        {
            Path = "podcast.mp3",
            DurationSeconds = 300.0,
            HasVideo = false,
            HasAudio = true
        };

        var win = new MediaPropertiesWindow(audioAsset);
        var rootGrid = Assert.IsType<Grid>(win.Content);
        var tabControl = Assert.IsType<TabControl>(rootGrid.Children[0]);

        // General, Audio tabs
        Assert.Equal(2, tabControl.Items.Count);
        var tab0 = Assert.IsType<TabItem>(tabControl.Items[0]);
        var tab1 = Assert.IsType<TabItem>(tabControl.Items[1]);

        Assert.Equal("General", tab0.Header);
        Assert.Equal("Audio", tab1.Header);
    });
}

