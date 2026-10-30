using System.Security.Cryptography;
using System.Text;
using Promptlet.Core;
using Microsoft.Win32;

namespace Promptlet.Services;

internal sealed class SettingsService
{
    public static string DirectoryPath => Environment.GetEnvironmentVariable("PROMPTLET_DATA_DIR") is { Length: > 0 } custom
        ? Path.GetFullPath(custom) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Promptlet");
    public static string FilePath => Path.Combine(DirectoryPath, "settings.json");
    private readonly ISecretProtector _protector = new DpapiProtector();
    private bool _legacy;
    public bool ReadOnly { get; private set; }
    public string? LoadWarning { get; private set; }

    public WorkspaceSettings Load()
    {
        if (!File.Exists(FilePath)) return new();
        try
        {
            var loaded = ConfigurationCodec.Decode(File.ReadAllText(FilePath), _protector);
            _legacy = loaded.Migrated;
            LoadWarning = loaded.Warning;
            return loaded.Settings;
        }
        catch (Exception ex)
        {
            ReadOnly = true;
            LoadWarning = ex is PromptletException known ? known.Message : "配置读取失败。为保护原文件，本次禁止保存；请打开配置目录检查备份。";
            ErrorLog.Write(ex);
            return new();
        }
    }

    public void Save(WorkspaceSettings settings)
    {
        if (ReadOnly) throw new PromptletException(LoadWarning ?? "配置处于只读保护状态。");
        var encoded = ConfigurationCodec.Encode(settings, _protector);
        Directory.CreateDirectory(DirectoryPath);
        if (_legacy && File.Exists(FilePath) && !File.Exists(FilePath + ".v1.backup")) File.Copy(FilePath, FilePath + ".v1.backup", overwrite: false);
        var temporary = FilePath + ".tmp";
        try
        {
            File.WriteAllText(temporary, encoded);
            File.Move(temporary, FilePath, overwrite: true);
            _legacy = false;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static void SetStartAtLogin(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled)
        {
            var executable = Environment.ProcessPath ?? throw new PromptletException("无法确定程序路径。");
            key.SetValue("Promptlet", $"\"{executable}\" --background");
        }
        else key.DeleteValue("Promptlet", false);
    }

    private sealed class DpapiProtector : ISecretProtector
    {
        public string Protect(string value) => Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
        public string Unprotect(string value) => Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser));
    }
}
