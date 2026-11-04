using System.Diagnostics;
using System.Text.Json.Serialization;

namespace OpenRend.Media;

public sealed record HardwareEncoderSupport(bool HasNvenc, bool HasAmf, bool HasQsv)
{
    public string SummaryText
    {
        get
        {
            var list = new List<string>();
            if (HasNvenc) list.Add("NVIDIA NVENC");
            if (HasAmf) list.Add("AMD AMF");
            if (HasQsv) list.Add("Intel QuickSync (QSV)");
            if (list.Count == 0) return "No hardware video acceleration detected. Software (CPU) encoding is active.";
            return $"{string.Join(", ", list)} acceleration is available on this system.";
        }
    }
}

public static class HardwareAccelerationHelper
{
    private static HardwareEncoderSupport? cached;
    private static readonly object syncLock = new();

    public static HardwareEncoderSupport Current
    {
        get
        {
            if (cached != null) return cached;
            lock (syncLock)
            {
                cached ??= Detect();
            }
            return cached;
        }
    }

    public static HardwareEncoderSupport Check(bool forceRefresh = false)
    {
        if (forceRefresh || cached == null)
        {
            lock (syncLock)
            {
                cached = Detect();
            }
        }
        return cached;
    }

    private static HardwareEncoderSupport Detect()
    {
        try
        {
            var ffmpegPath = MediaExecutables.Find("ffmpeg");
            var hasNvenc = ProbeEncoder(ffmpegPath, "h264_nvenc");
            var hasAmf = ProbeEncoder(ffmpegPath, "h264_amf");
            var hasQsv = ProbeEncoder(ffmpegPath, "h264_qsv");
            return new HardwareEncoderSupport(hasNvenc, hasAmf, hasQsv);
        }
        catch
        {
            return new HardwareEncoderSupport(false, false, false);
        }
    }

    public static bool ProbeEncoder(string ffmpegPath, string encoderName)
    {
        try
        {
            var psi = new ProcessStartInfo(ffmpegPath)
            {
                Arguments = $"-hide_banner -nostdin -f lavfi -i nullsrc=s=256x256:d=0.04 -c:v {encoderName} -f null -",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return false;
            proc.WaitForExit(3000);
            return proc.HasExited && proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}

