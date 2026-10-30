using System.Text.Json;

namespace Promptlet.Services;

/// <summary>Opt-in native frame measurements. Never reads or logs clipboard/model content.</summary>
internal static class FloatingFrameDiagnostics
{
    internal sealed record Snapshot(uint Style, uint ExtendedStyle, int Width, int Height,
        int ClientWidth, int ClientHeight, int Left, int Top, int Right, int Bottom,
        int BorderAttributeResult, uint BorderColor)
    {
        public bool HasFrameStyle => (Style & 0x00C40000) != 0 || (ExtendedStyle & 0x00020301) != 0;
        public bool ClientFillsWindow => Left == 0 && Top == 0 && Right == 0 && Bottom == 0;
    }

    public static Snapshot Capture(nint handle)
    {
        if (!NativeMethods.GetWindowRect(handle, out var window) || !NativeMethods.GetClientRect(handle, out var client))
            throw new InvalidOperationException("Cannot measure the Promptlet window.");
        var origin = new NativeMethods.Point();
        if (!NativeMethods.ClientToScreen(handle, ref origin)) throw new InvalidOperationException("Cannot measure the client origin.");
        var hr = NativeMethods.DwmGetWindowAttribute(handle, 34, out var border, sizeof(uint));
        return new(unchecked((uint)(long)NativeMethods.GetWindowLongPtrW(handle, -16)),
            unchecked((uint)(long)NativeMethods.GetWindowLongPtrW(handle, -20)),
            window.Right - window.Left, window.Bottom - window.Top,
            client.Right - client.Left, client.Bottom - client.Top,
            origin.X - window.Left, origin.Y - window.Top,
            window.Right - origin.X - client.Right, window.Bottom - origin.Y - client.Bottom, hr, border);
    }

    public static void Record(string path, Snapshot before, Snapshot after)
    {
        try
        {
            if (Path.GetDirectoryName(Path.GetFullPath(path)) is { } directory) Directory.CreateDirectory(directory);
            File.AppendAllText(path, JsonSerializer.Serialize(new { Before = before, After = after }) + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { /* An optional diagnostic sink must not affect the product. */ }
    }
}
