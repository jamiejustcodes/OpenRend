using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace OpenRend.Media;

internal static class NativeMethods
{
    private const string Library = "OpenRend.Native";

    [StructLayout(LayoutKind.Sequential)]
    internal struct Metadata
    {
        internal double DurationSeconds;
        internal int Width;
        internal int Height;
        internal double FrameRate;
        internal int VideoStreams;
        internal int AudioStreams;
        internal int SampleRate;
        internal int Channels;
        internal long BitRate;
    }

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "or_probe")]
    internal static extern int Probe([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out Metadata metadata);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "or_decoder_open")]
    internal static extern int DecoderOpen([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int width, int height, out DecoderHandle decoder);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "or_decoder_frame")]
    internal static extern int DecoderFrame(DecoderHandle decoder, double seconds, [Out] byte[] bgra, int capacity);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "or_decoder_audio")]
    internal static extern int DecoderAudio(DecoderHandle decoder, double seconds, int frames, [Out] float[] stereo, int capacitySamples);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "or_decoder_close")]
    internal static extern void DecoderClose(IntPtr decoder);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "or_compositor_create")]
    internal static extern int CompositorCreate(int width, int height, out CompositorHandle compositor);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "or_compositor_begin")]
    internal static extern int CompositorBegin(CompositorHandle compositor);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "or_compositor_layer")]
    internal static extern int CompositorLayer(CompositorHandle compositor, byte[] bgra, int width, int height, int stride, double opacity);

    [StructLayout(LayoutKind.Sequential)]
    internal struct LayerTransform
    {
        internal double Xx, Xy, Tx, Yx, Yy, Ty;
        internal int DestinationWidth, DestinationHeight;
    }

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "or_compositor_layer_transform")]
    internal static extern int CompositorLayerTransform(CompositorHandle compositor, byte[] bgra, int width, int height, int stride, double opacity, in LayerTransform transform);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "or_compositor_readback")]
    internal static extern int CompositorReadback(CompositorHandle compositor, [Out] byte[] bgra, int capacity);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "or_compositor_is_hardware")]
    internal static extern int CompositorIsHardware(CompositorHandle compositor);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "or_compositor_close")]
    internal static extern void CompositorClose(IntPtr compositor);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "or_last_error")]
    private static extern IntPtr LastError();

    internal static MediaException Error(string operation) => new($"{operation}: {Marshal.PtrToStringUTF8(LastError()) ?? "Unknown native media error"}");
}

internal sealed class DecoderHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public DecoderHandle() : base(true) { }
    protected override bool ReleaseHandle()
    {
        NativeMethods.DecoderClose(handle);
        return true;
    }
}

internal sealed class CompositorHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public CompositorHandle() : base(true) { }
    protected override bool ReleaseHandle()
    {
        NativeMethods.CompositorClose(handle);
        return true;
    }
}

public sealed class MediaException(string message) : Exception(message);
