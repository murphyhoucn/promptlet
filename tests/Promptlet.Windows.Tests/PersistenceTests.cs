using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Promptlet.Core;
using Promptlet.Services;
namespace Promptlet.Windows.Tests;

public class PersistenceTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "Promptlet-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string? _previous = Environment.GetEnvironmentVariable("PROMPTLET_DATA_DIR");
    public PersistenceTests() { Environment.SetEnvironmentVariable("PROMPTLET_DATA_DIR", _path); Directory.CreateDirectory(_path); }
    [Fact] public void RealDpapiRoundtripAndLegacyBackup()
    {
        var old = JsonSerializer.Serialize(new { Settings = new AppSettings { BaseUrl = "http://localhost:18765/v1", Model = "fixture", Theme = "Dark" },
            Secret = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes("synthetic-test-key"), null, DataProtectionScope.CurrentUser)) });
        File.WriteAllText(SettingsService.FilePath, old);
        var service = new SettingsService(); var migrated = service.Load(); Assert.Null(service.LoadWarning);
        Assert.Equal("synthetic-test-key", migrated.ResolveRequest().ApiKey);
        service.Save(migrated);
        Assert.Equal(old, File.ReadAllText(SettingsService.FilePath + ".v1.backup"));
        Assert.DoesNotContain("synthetic-test-key", File.ReadAllText(SettingsService.FilePath));
        Assert.Equal("synthetic-test-key", new SettingsService().Load().ResolveRequest().ApiKey);
        service.Save(migrated); // Saving again must not overwrite or collide with the first migration backup.
    }
    [Fact] public void CorruptFileIsProtectedAgainstSave()
    {
        File.WriteAllText(SettingsService.FilePath, "{broken}"); var store = new SettingsService(); store.Load();
        Assert.True(store.ReadOnly); Assert.Throws<PromptletException>(() => store.Save(new()));
        Assert.Equal("{broken}", File.ReadAllText(SettingsService.FilePath));
    }
    [Fact] public void FutureSchemaRemainsUntouched()
    {
        File.WriteAllText(SettingsService.FilePath, "{\"SchemaVersion\":999}"); var store = new SettingsService(); store.Load();
        Assert.Throws<PromptletException>(() => store.Save(new()));
        Assert.Contains("999", File.ReadAllText(SettingsService.FilePath));
    }
    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PROMPTLET_DATA_DIR", _previous);
        // Only remove this fixture's unique temporary directory, never the real profile.
        if (Path.GetFullPath(_path).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) Directory.Delete(_path, true);
    }
}
