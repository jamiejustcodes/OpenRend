using FlaUI.Core.AutomationElements;
using FlaUI.Core.WindowsAPI;
using OpenRend.Core;
using OpenRend.Project;
using Xunit;

namespace OpenRend.App.Tests;

public sealed partial class EditorWorkflowTests
{
    [WindowsDesktopFact]
    [Trait("Category", "Desktop")]
    public async Task MasterBusReadsActualLoudnessAndOverloadDuringPlayback()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var temporary = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var path = Path.Combine(temporary, "master.openrend");
            var audio = Path.Combine(temporary, "tone.wav");
            WriteMasterTone(audio);
            var media = new MediaAsset { Path = audio, DurationSeconds = 7, HasAudio = true };
            var project = new ProjectDocument { Name = "UI integration", Media = [media] };
            for (var track = 0; track < 2; track++)
                project.Tracks.Add(new TimelineTrack { Kind = TrackKind.Audio, Clips = [new TimelineClip { MediaId = media.Id, DurationFrames = 210 }] });
            await ProjectStore.SaveAsync(project, path);

            using var running = Launch(executable, path, temporary);
            var window = running.Window;
            var momentary = Find(window, "MasterMomentaryLoudness");
            var shortTerm = Find(window, "MasterShortTermLoudness");
            var truePeak = Find(window, "MasterTruePeak");
            var monitorMute = window.FindFirstDescendant(c => c.ByName("Mute monitor"));
            Assert.NotNull(monitorMute);
            monitorMute.AsButton().Invoke();
            Chord(window, VirtualKeyShort.SPACE);
            WaitFor(() => momentary.Name.StartsWith("M 1") && truePeak.Name.StartsWith("1"), "The master meter did not read the overloaded stereo mix.");
            WaitFor(() => window.FindFirstDescendant(c => c.ByAutomationId("MasterClipping"))?.IsOffscreen == false,
                "The master overload indicator did not latch.");
            WaitFor(() => shortTerm.Name.StartsWith("S 1"), "Short-term master loudness did not fill its three-second window.");
            Chord(window, VirtualKeyShort.SPACE);
            WaitFor(() => momentary.Name == "M — LUFS" && truePeak.Name == "−∞ dBTP", "Stopping did not reset the master readouts.");
            Assert.DoesNotContain('*', window.Title);
            running.CloseGracefully();
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    private static void WriteMasterTone(string path)
    {
        const int rate = 48000;
        const int frames = rate * 7;
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8); writer.Write(36 + frames * 4); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)2);
        writer.Write(rate); writer.Write(rate * 4); writer.Write((short)4); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(frames * 4);
        for (var frame = 0; frame < frames; frame++)
        {
            var sample = (short)(short.MaxValue * .6 * Math.Sin(2 * Math.PI * 1000 * frame / rate));
            writer.Write(sample); writer.Write(sample);
        }
    }
}
