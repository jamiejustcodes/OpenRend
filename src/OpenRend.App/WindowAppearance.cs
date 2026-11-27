using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace OpenRend.App;

internal static class WindowAppearance
{
    private const int UseImmersiveDarkMode = 20;
    private const int CaptionColor = 35;
    private const int TextColor = 36;
    private const uint DefaultColor = 0xFFFFFFFF;

    public static void Initialize(Application application)
    {
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => Apply((Window)sender)));
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        application.Exit += (_, _) => SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;

        void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName != nameof(SystemParameters.HighContrast)) return;
            _ = application.Dispatcher.InvokeAsync(() =>
            {
                foreach (Window window in application.Windows) Apply(window);
            });
        }
    }

    private static void Apply(Window window)
    {
        if (window.WindowStyle == WindowStyle.None || WindowChrome.GetWindowChrome(window) is not null) return;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == 0) return;

        var highContrast = SystemParameters.HighContrast;
        SetAttribute(handle, UseImmersiveDarkMode, highContrast ? 0u : 1u);
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            SetAttribute(handle, CaptionColor, highContrast ? DefaultColor : GetColor(window, "WindowBrush"));
            SetAttribute(handle, TextColor, highContrast ? DefaultColor : GetColor(window, "TextBrush"));
        }
    }

    private static uint GetColor(Window window, string resource)
    {
        if (window.TryFindResource(resource) is not SolidColorBrush brush) return DefaultColor;
        var color = brush.Color;
        return color.R | (uint)color.G << 8 | (uint)color.B << 16; // COLORREF stores red in the low byte.
    }

    private static void SetAttribute(nint window, int attribute, uint value) =>
        _ = DwmSetWindowAttribute(window, attribute, in value, sizeof(uint));

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, in uint value, int size);
}
