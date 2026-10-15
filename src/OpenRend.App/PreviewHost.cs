using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace OpenRend.App;

public sealed class PreviewHost : HwndHost
{
    private nint child;
    private nint renderer;
    public bool IsHardware => renderer != 0 && Native.or_renderer_is_hardware(renderer) == 1;
    public event Action? Ready;
    protected override HandleRef BuildWindowCore(HandleRef parent)
    {
        Native.EnsureWindowClass();
        child = Native.CreateWindowEx(0, "OpenRend.Preview", "", 0x40000000 | 0x10000000 | 0x04000000,
            0, 0, 640, 360, parent.Handle, 0, Native.GetModuleHandle(null), 0);
        if (child == 0) throw new System.ComponentModel.Win32Exception();
        var result = Native.or_renderer_create(child, 640, 360, out renderer);
        if (result < 0) throw new InvalidOperationException("Video display could not start: " + Marshal.PtrToStringUTF8(Native.or_last_error()));
        Dispatcher.BeginInvoke(() => Ready?.Invoke());
        return new HandleRef(this, child);
    }
    public void Present(byte[] bgra, int width, int height)
    {
        if (renderer == 0) return;
        var result = Native.or_renderer_present(renderer, bgra, width, height, width * 4);
        if (result < 0) throw new InvalidOperationException(Marshal.PtrToStringUTF8(Native.or_last_error()));
    }
    protected override void OnWindowPositionChanged(Rect bounds)
    {
        base.OnWindowPositionChanged(bounds);
        if (renderer != 0 && bounds.Width >= 1 && bounds.Height >= 1)
            Native.or_renderer_resize(renderer, (int)Math.Ceiling(bounds.Width), (int)Math.Ceiling(bounds.Height));
    }
    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        if (renderer != 0) { Native.or_renderer_close(renderer); renderer = 0; }
        Native.DestroyWindow(hwnd.Handle);
    }
    private static class Native
    {
        private delegate nint WindowProcedure(nint hwnd, uint message, nint wparam, nint lparam);
        private static readonly WindowProcedure Procedure = ProcessMessage;
        private static bool registered;
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WindowClass
        {
            public uint Style;
            public nint WndProc;
            public int ClassExtra, WindowExtra;
            public nint Instance, Icon, Cursor, Background;
            public string? MenuName;
            public string ClassName;
        }
        internal static void EnsureWindowClass()
        {
            if (registered) return;
            var cls = new WindowClass { WndProc = Marshal.GetFunctionPointerForDelegate(Procedure), Instance = GetModuleHandle(null), ClassName = "OpenRend.Preview" };
            if (RegisterClass(ref cls) == 0) throw new System.ComponentModel.Win32Exception();
            registered = true;
        }
        private static nint ProcessMessage(nint hwnd, uint message, nint wparam, nint lparam)
        {
            // The swapchain owns the surface; a GDI background erase would cover its frame.
            if (message == 0x14) return 1;
            if (message == 0x0F) { ValidateRect(hwnd, 0); return 0; }
            return DefWindowProc(hwnd, message, wparam, lparam);
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern nint GetModuleHandle(string? name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClass(ref WindowClass windowClass);
        [DllImport("user32.dll")] private static extern bool ValidateRect(nint hwnd, nint rect);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint DefWindowProc(nint hwnd, uint message, nint wparam, nint lparam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern nint CreateWindowEx(int ex, string cls, string name, int style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);
        [DllImport("user32.dll")] internal static extern bool DestroyWindow(nint hwnd);
        [DllImport("OpenRend.Native", CallingConvention = CallingConvention.Cdecl)] internal static extern int or_renderer_create(nint hwnd, int width, int height, out nint renderer);
        [DllImport("OpenRend.Native", CallingConvention = CallingConvention.Cdecl)] internal static extern int or_renderer_present(nint renderer, byte[] bgra, int width, int height, int stride);
        [DllImport("OpenRend.Native", CallingConvention = CallingConvention.Cdecl)] internal static extern int or_renderer_resize(nint renderer, int width, int height);
        [DllImport("OpenRend.Native", CallingConvention = CallingConvention.Cdecl)] internal static extern void or_renderer_close(nint renderer);
        [DllImport("OpenRend.Native", CallingConvention = CallingConvention.Cdecl)] internal static extern int or_renderer_is_hardware(nint renderer);
        [DllImport("OpenRend.Native", CallingConvention = CallingConvention.Cdecl)] internal static extern nint or_last_error();
    }
}
