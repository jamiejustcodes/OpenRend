using System;
using System.IO;
using System.Text.Json;
using OpenRend.Core;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class AudioPanLawsEditorTests
{
    [Fact]
    public void AudioPanLaw_CalculatesAccurateLinearGains_ForBalanceAndAddChannels()
    {
        // Balance: 0 dB Center
        var centerBalance = AudioPanLaw.CalculateGains(AudioPanMode.Balance, 0.0);
        Assert.Equal(1.0, centerBalance.Left, 6);
        Assert.Equal(1.0, centerBalance.Right, 6);

        var leftBalance = AudioPanLaw.CalculateGains(AudioPanMode.Balance, -1.0);
        Assert.Equal(1.0, leftBalance.Left, 6);
        Assert.Equal(0.0, leftBalance.Right, 6);

        var rightBalance = AudioPanLaw.CalculateGains(AudioPanMode.Balance, 1.0);
        Assert.Equal(0.0, rightBalance.Left, 6);
        Assert.Equal(1.0, rightBalance.Right, 6);

        var midLeftBalance = AudioPanLaw.CalculateGains(AudioPanMode.Balance, -0.5);
        Assert.Equal(1.0, midLeftBalance.Left, 6);
        Assert.Equal(0.5, midLeftBalance.Right, 6);

        // Add Channels: 0 dB Center
        var centerAdd = AudioPanLaw.CalculateGains(AudioPanMode.AddChannels, 0.0);
        Assert.Equal(1.0, centerAdd.Left, 6);
        Assert.Equal(1.0, centerAdd.Right, 6);

        var leftAdd = AudioPanLaw.CalculateGains(AudioPanMode.AddChannels, -1.0);
        Assert.Equal(1.0, leftAdd.Left, 6);
        Assert.Equal(0.0, leftAdd.Right, 6);

        var rightAdd = AudioPanLaw.CalculateGains(AudioPanMode.AddChannels, 1.0);
        Assert.Equal(0.0, rightAdd.Left, 6);
        Assert.Equal(1.0, rightAdd.Right, 6);
    }

    [Fact]
    public void AudioPanLaw_CalculatesAccurateConstantPower_AcousticEnergyPreserved()
    {
        // Center: -3.01 dB = sqrt(0.5)
        var center = AudioPanLaw.CalculateGains(AudioPanMode.ConstantPower, 0.0);
        Assert.Equal(AudioPanLaw.CenterNotch3Db, center.Left, 6);
        Assert.Equal(AudioPanLaw.CenterNotch3Db, center.Right, 6);

        // Acoustic energy conservation: L^2 + R^2 == 1.0
        Assert.Equal(1.0, center.Left * center.Left + center.Right * center.Right, 6);

        var left = AudioPanLaw.CalculateGains(AudioPanMode.ConstantPower, -1.0);
        Assert.Equal(1.0, left.Left, 6);
        Assert.Equal(0.0, left.Right, 6);
        Assert.Equal(1.0, left.Left * left.Left + left.Right * left.Right, 6);

        var right = AudioPanLaw.CalculateGains(AudioPanMode.ConstantPower, 1.0);
        Assert.Equal(0.0, right.Left, 6);
        Assert.Equal(1.0, right.Right, 6);
        Assert.Equal(1.0, right.Left * right.Left + right.Right * right.Right, 6);

        foreach (var p in new[] { -0.75, -0.5, -0.25, 0.25, 0.5, 0.75 })
        {
            var gains = AudioPanLaw.CalculateGains(AudioPanMode.ConstantPower, p);
            var totalPower = gains.Left * gains.Left + gains.Right * gains.Right;
            Assert.Equal(1.0, totalPower, 6);
        }
    }

    [Fact]
    public void AudioPanLaw_CalculatesAccurateNotchLaws_3DbAnd6Db()
    {
        // Notch 3 dB
        var center3Db = AudioPanLaw.CalculateGains(AudioPanMode.Notch3Db, 0.0);
        Assert.Equal(AudioPanLaw.CenterNotch3Db, center3Db.Left, 6);
        Assert.Equal(AudioPanLaw.CenterNotch3Db, center3Db.Right, 6);

        var left3Db = AudioPanLaw.CalculateGains(AudioPanMode.Notch3Db, -1.0);
        Assert.Equal(1.0, left3Db.Left, 6);
        Assert.Equal(0.0, left3Db.Right, 6);

        var right3Db = AudioPanLaw.CalculateGains(AudioPanMode.Notch3Db, 1.0);
        Assert.Equal(0.0, right3Db.Left, 6);
        Assert.Equal(1.0, right3Db.Right, 6);

        // Notch 6 dB
        var center6Db = AudioPanLaw.CalculateGains(AudioPanMode.Notch6Db, 0.0);
        Assert.Equal(0.5, center6Db.Left, 6);
        Assert.Equal(0.5, center6Db.Right, 6);

        var left6Db = AudioPanLaw.CalculateGains(AudioPanMode.Notch6Db, -1.0);
        Assert.Equal(1.0, left6Db.Left, 6);
        Assert.Equal(0.0, left6Db.Right, 6);

        var right6Db = AudioPanLaw.CalculateGains(AudioPanMode.Notch6Db, 1.0);
        Assert.Equal(0.0, right6Db.Left, 6);
        Assert.Equal(1.0, right6Db.Right, 6);
    }

    [Fact]
    public void SetTrackPanMode_UpdatesAndSurvivesUndoRedo()
    {
        var project = new ProjectDocument();
        var track = new TimelineTrack { Name = "Audio 1", Kind = TrackKind.Audio };
        project.Tracks.Add(track);

        var editor = new TimelineEditor(project);
        Assert.Equal(AudioPanMode.Balance, editor.Project.Tracks[0].PanMode);

        editor.SetTrackPanMode(track.Id, AudioPanMode.ConstantPower);
        Assert.Equal(AudioPanMode.ConstantPower, editor.Project.Tracks[0].PanMode);

        editor.SetTrackPanMode(track.Id, AudioPanMode.Notch3Db);
        Assert.Equal(AudioPanMode.Notch3Db, editor.Project.Tracks[0].PanMode);

        editor.Undo();
        Assert.Equal(AudioPanMode.ConstantPower, editor.Project.Tracks[0].PanMode);

        editor.Undo();
        Assert.Equal(AudioPanMode.Balance, editor.Project.Tracks[0].PanMode);

        editor.Redo();
        Assert.Equal(AudioPanMode.ConstantPower, editor.Project.Tracks[0].PanMode);
    }

    [Fact]
    public void SetAudioBusPanMode_UpdatesAndSurvivesUndoRedo()
    {
        var project = new ProjectDocument();
        var editor = new TimelineEditor(project);

        var bus = editor.CreateAudioBus("Bus A");
        Assert.Equal(AudioPanMode.Balance, editor.Project.AudioBuses[0].PanMode);

        editor.SetAudioBusPanMode(bus.Id, AudioPanMode.AddChannels);
        Assert.Equal(AudioPanMode.AddChannels, editor.Project.AudioBuses[0].PanMode);

        editor.Undo();
        Assert.Equal(AudioPanMode.Balance, editor.Project.AudioBuses[0].PanMode);

        editor.Redo();
        Assert.Equal(AudioPanMode.AddChannels, editor.Project.AudioBuses[0].PanMode);
    }

    [Fact]
    public void PanMode_SurvivesProjectDeepCloneAndJsonRoundTrip()
    {
        var project = new ProjectDocument();
        var track = new TimelineTrack { Name = "Audio 1", Kind = TrackKind.Audio, PanMode = AudioPanMode.ConstantPower };
        project.Tracks.Add(track);

        var bus = new AudioBusTrack { Name = "Bus A", PanMode = AudioPanMode.Notch6Db };
        project.AudioBuses.Add(bus);

        // Validate
        project.Validate();

        // Deep clone
        var clone = project.DeepClone();
        Assert.Equal(AudioPanMode.ConstantPower, clone.Tracks[0].PanMode);
        Assert.Equal(AudioPanMode.Notch6Db, clone.AudioBuses[0].PanMode);

        // JSON roundtrip
        var json = JsonSerializer.Serialize(project);
        var deserialized = JsonSerializer.Deserialize<ProjectDocument>(json);
        Assert.NotNull(deserialized);
        deserialized.Validate();
        Assert.Equal(AudioPanMode.ConstantPower, deserialized.Tracks[0].PanMode);
        Assert.Equal(AudioPanMode.Notch6Db, deserialized.AudioBuses[0].PanMode);
    }
}
