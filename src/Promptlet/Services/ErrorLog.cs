namespace Promptlet.Services;

internal static class ErrorLog
{
    private static readonly object Gate = new();
    public static void Write(Exception error)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(SettingsService.DirectoryPath);
                var path = Path.Combine(SettingsService.DirectoryPath, "errors.log");
                if (File.Exists(path) && new FileInfo(path).Length > 512_000) File.Move(path, path + ".1", true);
                File.AppendAllText(path, $"{DateTimeOffset.Now:O}\n{error.GetType().FullName}\n{error.StackTrace}\n\n");
            }
        }
        catch { }
    }
}
