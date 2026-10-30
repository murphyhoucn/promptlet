namespace Promptlet;

internal static class AppVersion
{
    public static string Current => typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.2.2";
}
