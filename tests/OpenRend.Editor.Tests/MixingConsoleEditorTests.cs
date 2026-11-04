using System;
using System.Linq;
using OpenRend.App;
using OpenRend.Core;
using OpenRend.Media;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class MixingConsoleEditorTests
{
    private static (EditorViewModel ViewModel, TimelineEditor Editor, ProjectDocument Project) CreateEnvironment()
    {
        var media = new MediaService();
        var renderer = new TimelineRenderService(media);
        var exporter = new ExportService(renderer);
        var vm = new EditorViewModel(media, renderer, exporter);
        vm.Project.Tracks.Clear();
        vm.Project.AudioBuses.Clear();
        return (vm, vm.Editor, vm.Project);
    }

    [Fact]
    public void TimelineEditor_AudioBusCreationAndDeletion_UpdatesAndSupportsUndo()
    {
        var (_, editor, project) = CreateEnvironment();

        Assert.Empty(project.AudioBuses);

        // Auto-named Bus A, Bus B
        var busA = editor.CreateAudioBus();
        Assert.Equal("Bus A", busA.Name);
        Assert.Single(project.AudioBuses);

        var busB = editor.CreateAudioBus();
        Assert.Equal("Bus B", busB.Name);
        Assert.Equal(2, project.AudioBuses.Count);

        // Custom named bus
        var dialogBus = editor.CreateAudioBus("Dialogue Reverb");
        Assert.Equal("Dialogue Reverb", dialogBus.Name);
        Assert.Equal(3, project.AudioBuses.Count);

        // Delete bus
        editor.DeleteAudioBus(busB.Id);
        Assert.Equal(2, project.AudioBuses.Count);
        Assert.DoesNotContain(project.AudioBuses, b => b.Id == busB.Id);

        // Undo deletion
        Assert.True(editor.CanUndo);
        editor.Undo();
        Assert.Equal(3, project.AudioBuses.Count);
        Assert.Contains(project.AudioBuses, b => b.Id == busB.Id);

        // Redo deletion
        Assert.True(editor.CanRedo);
        editor.Redo();
        Assert.Equal(2, project.AudioBuses.Count);
    }

    [Fact]
    public void TimelineEditor_TrackAudioBusRouting_AssignsAndCleansUpOnDelete()
    {
        var (_, editor, project) = CreateEnvironment();

        var track = editor.AddTrack(TrackKind.Audio);
        var bus = editor.CreateAudioBus("SFX Submix");

        Assert.Null(track.BusTrackId);

        // Assign track to bus
        editor.SetTrackAudioBus(track.Id, bus.Id);
        Assert.Equal(bus.Id, project.Tracks[0].BusTrackId);

        // Route back to Master (null)
        editor.SetTrackAudioBus(track.Id, null);
        Assert.Null(project.Tracks[0].BusTrackId);

        // Re-route to bus
        editor.SetTrackAudioBus(track.Id, bus.Id);
        Assert.Equal(bus.Id, project.Tracks[0].BusTrackId);

        // Deleting bus clears routing on assigned tracks to avoid dangling references
        editor.DeleteAudioBus(bus.Id);
        Assert.Null(project.Tracks[0].BusTrackId);

        // Undo restores both bus and routing
        editor.Undo();
        Assert.Equal(bus.Id, project.Tracks[0].BusTrackId);
    }

    [Fact]
    public void TimelineEditor_AudioBusVolumePanMuteSolo_SetsAndUndoes()
    {
        var (_, editor, project) = CreateEnvironment();

        var bus = editor.CreateAudioBus("Music Bus");

        // Volume (Gain)
        editor.SetAudioBusVolume(bus.Id, 0.5);
        Assert.Equal(0.5, project.AudioBuses[0].Gain);

        // Pan
        editor.SetAudioBusPan(bus.Id, -0.75);
        Assert.Equal(-0.75, project.AudioBuses[0].Pan);

        // Mute
        editor.SetAudioBusMute(bus.Id, true);
        Assert.True(project.AudioBuses[0].IsMuted);

        // Solo
        editor.SetAudioBusSolo(bus.Id, true);
        Assert.True(project.AudioBuses[0].IsSolo);

        // Undo chain
        editor.Undo(); // Undo Solo
        Assert.False(project.AudioBuses[0].IsSolo);

        editor.Undo(); // Undo Mute
        Assert.False(project.AudioBuses[0].IsMuted);

        editor.Undo(); // Undo Pan
        Assert.Equal(0.0, project.AudioBuses[0].Pan);

        editor.Undo(); // Undo Volume
        Assert.Equal(1.0, project.AudioBuses[0].Gain);
    }

    [Fact]
    public void TimelineEditor_AudioBusEffects_AddBypassUpdateRemove_WorksWithUndo()
    {
        var (_, editor, project) = CreateEnvironment();

        var bus = editor.CreateAudioBus("Bus A");
        var compressor = AudioEffectCatalog.CreateTrackCompressor("Gentle Compression (2:1)");

        editor.AddAudioBusEffect(bus.Id, compressor);
        Assert.Single(project.AudioBuses[0].AudioEffects);

        var addedFx = project.AudioBuses[0].AudioEffects[0];
        Assert.Equal("Track Compressor", addedFx.Name);
        Assert.False(addedFx.IsBypassed);

        // Bypass
        editor.SetAudioBusEffectBypassed(bus.Id, addedFx.Id, true);
        Assert.True(project.AudioBuses[0].AudioEffects[0].IsBypassed);

        // Update parameter
        editor.UpdateAudioBusEffectParameter(bus.Id, addedFx.Id, "ThresholdDb", -24.0);
        Assert.Equal(-24.0, project.AudioBuses[0].AudioEffects[0].Parameters["ThresholdDb"]);

        // Remove
        editor.RemoveAudioBusEffect(bus.Id, addedFx.Id);
        Assert.Empty(project.AudioBuses[0].AudioEffects);

        // Undo removal
        editor.Undo();
        Assert.Single(project.AudioBuses[0].AudioEffects);
        Assert.Equal(-24.0, project.AudioBuses[0].AudioEffects[0].Parameters["ThresholdDb"]);
    }

    [Fact]
    public void TimelineEditor_MasterAudioGainMuteDim_PersistsAndUndoes()
    {
        var (_, editor, project) = CreateEnvironment();

        // Gain
        editor.SetMasterAudioGain(1.25);
        Assert.Equal(1.25, project.MasterAudioGain);

        // Mute
        editor.SetMasterAudioMute(true);
        Assert.True(project.MasterAudioMute);

        // Dim
        editor.SetMasterAudioDim(true);
        Assert.True(project.MasterAudioDim);

        // Undo
        editor.Undo(); // Dim
        Assert.False(project.MasterAudioDim);

        editor.Undo(); // Mute
        Assert.False(project.MasterAudioMute);

        editor.Undo(); // Gain
        Assert.Equal(1.0, project.MasterAudioGain);
    }
}

