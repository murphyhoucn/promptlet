using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Promptlet.Services;

// Render only the application's own isolated test tree, never the desktop or user data.
internal static class UiSmokePreview
{
    internal static async Task SaveAsync(FrameworkElement element, string name)
    {
        var report = Environment.GetEnvironmentVariable("PROMPTLET_SMOKE_REPORT");
        if (string.IsNullOrWhiteSpace(report) || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PROMPTLET_DATA_DIR")))
            throw new InvalidOperationException("UI previews require isolated smoke-test paths.");
        // Let the stock item entrance transitions finish before rasterizing their content.
        await Task.Delay(350);
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap();
        // System backdrops are composed outside XAML. Use a flat theme backdrop for previews.
        var background = element is Panel panel ? panel.Background : (element as Border)?.Background;
        var previewBrush = new SolidColorBrush(element.ActualTheme == ElementTheme.Dark
            ? Windows.UI.Color.FromArgb(255, 32, 32, 32) : Windows.UI.Color.FromArgb(255, 245, 245, 245));
        try
        {
            if (element is Panel p) p.Background = previewBrush;
            if (element is Border b) b.Background = previewBrush;
            await bitmap.RenderAsync(element);
        }
        finally
        {
            if (element is Panel p) p.Background = background;
            if (element is Border b) b.Background = background;
        }
        var buffer = await bitmap.GetPixelsAsync();
        var pixels = new byte[buffer.Length];
        using (var reader = DataReader.FromBuffer(buffer)) reader.ReadBytes(pixels);
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(Path.GetFullPath(report))!);
        var output = await folder.CreateFileAsync(name + ".png", CreationCollisionOption.ReplaceExisting);
        using var stream = await output.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels);
        await encoder.FlushAsync();
    }

    internal static T? Find<T>(DependencyObject parent, string? name = null) where T : FrameworkElement
    {
        if (parent is T match && (name is null || match.Name == name)) return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (Find<T>(VisualTreeHelper.GetChild(parent, i), name) is { } child) return child;
        return null;
    }
}
