using System.Text.Json;
using OpenRend.Core;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class TrackAutomationAndColorTests
{
    [Fact]
    public void TimelineTrack_Defaults_MatchVegasProConventions()
    {
        var track = new TimelineTrack();
        Assert.False(track.ArmRecord);
        Assert.Null(track.TrackColor);
        Assert.Equal(TrackAutomationMode.AutomationRead, track.AutomationMode);
    }

    [Fact]
    public void TimelineTrack_DeepClone_PreservesTrackColorArmAndAutomation()
    {
        var track = new TimelineTrack
        {
            Name = "Vocal Track",
            Kind = TrackKind.Audio,
            ArmRecord = true,
            TrackColor = "#FF857A",
            AutomationMode = TrackAutomationMode.AutomationTouch
        };

        var clone = track.DeepClone();

        Assert.Equal(track.ArmRecord, clone.ArmRecord);
        Assert.Equal(track.TrackColor, clone.TrackColor);
        Assert.Equal(track.AutomationMode, clone.AutomationMode);
        Assert.Equal(track.Name, clone.Name);
    }

    [Fact]
    public void VegasTrackColors_Palette_ContainsExpectedSwatches()
    {
        var palette = VegasTrackColors.Palette;
        Assert.True(palette.Length >= 16);
        Assert.Equal("Default", palette[0].Name);
        Assert.Equal("", palette[0].Hex);
        Assert.Contains(palette, p => p.Hex == "#706295");
        Assert.Contains(palette, p => p.Hex == "#FF857A");
        Assert.Contains(palette, p => p.Hex == "#72C889");
    }

    [Fact]
    public void TimelineTrack_Serialization_RoundTripsAllProperties()
    {
        var track = new TimelineTrack
        {
            Name = "Synth Lead",
            Kind = TrackKind.Audio,
            ArmRecord = true,
            TrackColor = "#8CB7CA",
            AutomationMode = TrackAutomationMode.AutomationLatch
        };

        var json = JsonSerializer.Serialize(track);
        var restored = JsonSerializer.Deserialize<TimelineTrack>(json);

        Assert.NotNull(restored);
        Assert.True(restored.ArmRecord);
        Assert.Equal("#8CB7CA", restored.TrackColor);
        Assert.Equal(TrackAutomationMode.AutomationLatch, restored.AutomationMode);
    }
}

