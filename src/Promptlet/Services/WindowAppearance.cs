using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using Promptlet.Core;

namespace Promptlet.Services;

internal static class WindowAppearance
{
    public static void Apply(Window window, WorkspaceSettings settings, bool transient)
    {
        if (window.Content is FrameworkElement root)
            root.RequestedTheme = settings.Theme switch { "Light" => ElementTheme.Light, "Dark" => ElementTheme.Dark, _ => ElementTheme.Default };
        SynchronizeTitleBar(window);
        try
        {
            window.SystemBackdrop ??= transient
                ? new DesktopAcrylicBackdrop() : new MicaBackdrop();
        }
        catch { window.SystemBackdrop = null; }
        if (transient) ApplyFloatingFrame(window);
        window.AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
    }

    public static void SynchronizeTitleBar(Window window)
    {
        // The native caption defaults to Legacy and does not inherit XAML RequestedTheme.
        window.AppWindow.TitleBar.PreferredTheme = window.Content is FrameworkElement { ActualTheme: ElementTheme.Dark }
            ? TitleBarTheme.Dark : TitleBarTheme.Light;
    }

    public static void ApplyFloatingFrame(Window window)
    {
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var reportPath = Environment.GetEnvironmentVariable("PROMPTLET_FRAME_DIAGNOSTICS");
        var before = string.IsNullOrEmpty(reportPath) ? null : FloatingFrameDiagnostics.Capture(handle);

        // WinUI can leave WS_DLGFRAME behind even after SetBorderAndTitleBar(false, false).
        // Clear the actual non-client frame, rather than just hiding its DWM accent color.
        const uint frameStyles = 0x00C40000; // WS_CAPTION (BORDER | DLGFRAME) | WS_THICKFRAME
        const uint edgeStyles = 0x00020301; // DLGMODALFRAME | WINDOWEDGE | CLIENTEDGE | STATICEDGE
        var style = unchecked((uint)(long)NativeMethods.GetWindowLongPtrW(handle, -16));
        var extendedStyle = unchecked((uint)(long)NativeMethods.GetWindowLongPtrW(handle, -20));
        var changed = (style & frameStyles) != 0 || (extendedStyle & edgeStyles) != 0;
        if ((style & frameStyles) != 0)
            NativeMethods.SetWindowLongPtrW(handle, -16, (nint)(style & ~frameStyles));
        if ((extendedStyle & edgeStyles) != 0)
            NativeMethods.SetWindowLongPtrW(handle, -20, (nint)(extendedStyle & ~edgeStyles));
        if (changed)
            _ = NativeMethods.SetWindowPos(handle, 0, 0, 0, 0, 0, 0x0037); // FRAMECHANGED | NOMOVE | NOSIZE | NOZORDER | NOACTIVATE

        // A borderless presenter can still retain the compositor's one-pixel accent frame.
        uint borderColor = 0xFFFFFFFE; // DWMWA_COLOR_NONE
        _ = NativeMethods.DwmSetWindowAttribute(handle, 34, ref borderColor, sizeof(uint));
        uint roundedCorners = 2; // DWMWCP_ROUND
        _ = NativeMethods.DwmSetWindowAttribute(handle, 33, ref roundedCorners, sizeof(uint));
        if (before is not null) FloatingFrameDiagnostics.Record(reportPath!, before, FloatingFrameDiagnostics.Capture(handle));
    }

    public static void Position(Window window, nint source, double widthDip, double heightDip, bool upper)
    {
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var monitor = NativeMethods.MonitorFromWindow(source, 2);
        var info = new NativeMethods.MonitorInfo { Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (!NativeMethods.GetMonitorInfoW(monitor, ref info)) return;
        var dpi = NativeMethods.GetDpiForWindow(source);
        if (dpi == 0) dpi = NativeMethods.GetDpiForWindow(handle);
        var scale = dpi == 0 ? 1 : dpi / 96.0;
        var work = info.Work;
        var width = Math.Min((int)(widthDip * scale), work.Right - work.Left);
        var height = Math.Min((int)(heightDip * scale), work.Bottom - work.Top);
        var x = work.Left + (work.Right - work.Left - width) / 2;
        var y = work.Top + (upper ? Math.Max(0, (work.Bottom - work.Top - height) / 4) : (work.Bottom - work.Top - height) / 2);
        window.AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }
}
