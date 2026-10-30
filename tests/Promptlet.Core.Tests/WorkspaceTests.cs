using System.Text;
using System.Text.Json;
using Promptlet.Core;

namespace Promptlet.Core.Tests;

public class WorkspaceTests
{
    private readonly Protector _protector = new();
    private static WorkspaceSettings Sample() => new()
    {
        ModelProfiles = [new("a", "写作", "https://example.com/v1", "model-a") { ApiKey = "private-a" }, new("b", "本机", "http://localhost:3000", "model-b", false) { ApiKey = "private-b" }],
        ActiveProfileId = "a"
    };
    [Fact] public void MultiModelRoundTripNeverSerializesPlainKeys()
    {
        var source = Sample(); var json = ConfigurationCodec.Encode(source, _protector);
        Assert.DoesNotContain("private-a", json); Assert.DoesNotContain("private-b", json); Assert.DoesNotContain("ApiKey", json);
        var result = ConfigurationCodec.Decode(json, _protector).Settings;
        Assert.Equal("private-a", result.ResolveRequest().ApiKey);
        Assert.Equal("private-b", result.ResolveRequest("b").ApiKey);
        Assert.False(result.ResolveRequest("b").Stream);
    }
    [Fact] public void LegacyMigrationPreservesEditedTemplatesAndPreferences()
    {
        var old = new AppSettings { BaseUrl = "http://localhost:3000/v1", Model = "old", GeneralTemplate = "edited", ActiveTemplate = "General", AutoHideSeconds = 9, Theme = "Dark", StartAtLogin = true };
        var json = JsonSerializer.Serialize(new { Settings = old, Secret = _protector.Protect("old-key") });
        var result = ConfigurationCodec.Decode(json, _protector);
        Assert.True(result.Migrated); Assert.Equal("edited", result.Settings.ResolveRequest().SystemPrompt);
        Assert.Equal("old-key", result.Settings.ResolveRequest().ApiKey); Assert.Equal(9, result.Settings.AutoHideSeconds);
        Assert.Equal("Dark", result.Settings.Theme); Assert.True(result.Settings.StartAtLogin);
    }
    [Fact] public void EmptyLegacyConnectionRemainsUnconfigured()
    {
        var json = JsonSerializer.Serialize(new { Settings = new AppSettings(), Secret = "" });
        var settings = ConfigurationCodec.Decode(json, _protector).Settings;
        Assert.Empty(settings.ModelProfiles); Assert.Null(settings.ActiveProfileId);
    }
    [Fact] public void OneBrokenKeyDoesNotLoseOtherKeysOrItsCiphertext()
    {
        var json = ConfigurationCodec.Encode(Sample(), _protector).Replace(_protector.Protect("private-a"), "broken");
        var loaded = ConfigurationCodec.Decode(json, _protector);
        Assert.NotNull(loaded.Warning); Assert.Equal("private-b", loaded.Settings.ResolveRequest("b").ApiKey);
        Assert.Throws<PromptletException>(() => loaded.Settings.ResolveRequest());
        Assert.Contains("broken", ConfigurationCodec.Encode(loaded.Settings, _protector));
        var replaced = loaded.Settings with { ModelProfiles = loaded.Settings.ModelProfiles.Select(p => p.Id == "a" ? p with { UnreadableSecret = null, ApiKey = "replacement" } : p).ToArray() };
        Assert.Equal("replacement", ConfigurationCodec.Decode(ConfigurationCodec.Encode(replaced, _protector), _protector).Settings.ResolveRequest().ApiKey);
    }
    [Fact] public void MissingActiveProfileNeverFallsBackToAnotherProvider()
    {
        var settings = Sample() with { ActiveProfileId = null };
        settings.Validate(); Assert.Throws<PromptletException>(() => settings.ResolveRequest());
    }
    [Fact] public void RenamingAndRestoringBodyDoNotChangeIdentityOrKey()
    {
        var settings = Sample(); var original = settings.ResolveRequest();
        var renamed = settings with { ModelProfiles = settings.ModelProfiles.Select(p => p with { Alias = p.Alias + "新" }).ToArray(),
            Templates = settings.Templates.Select(t => t with { Name = t.Name + "新" }).ToArray() };
        Assert.Equal(original.ProfileId, renamed.ResolveRequest().ProfileId); Assert.Equal(original.ApiKey, renamed.ResolveRequest().ApiKey);
        Assert.Equal(original.TemplateName + "新", renamed.ResolveRequest().TemplateName);
        Assert.Equal("科研项目", original.TemplateName); // Prior request remains an independent snapshot.
    }
    [Fact] public void FutureSchemaIsRejected()
    { Assert.Throws<PromptletException>(() => ConfigurationCodec.Decode("{\"SchemaVersion\":999}", _protector)); }
    [Fact] public void BuiltinsCannotBeRemoved()
    { Assert.Throws<PromptletException>(() => new WorkspaceSettings { Templates = [] }.Validate()); }
    [Fact] public void NamesAreUniqueIgnoringCaseAndWhitespace()
    { Assert.Throws<PromptletException>(() => (Sample() with { ModelProfiles = [new("a", "one"), new("b", " ONE ")] }).Validate()); }
    [Fact] public void IncompleteNonCurrentDraftDoesNotBlockSaving()
    { (Sample() with { ModelProfiles = [..Sample().ModelProfiles, new("draft", "草稿", "htt")] }).Validate(); }
    [Fact] public void CollectionsAreCopiedForDrafts()
    { var original = Sample(); var copy = original.Copy(); copy.ModelProfiles[0] = copy.ModelProfiles[0] with { ApiKey = "changed" }; Assert.Equal("private-a", original.ModelProfiles[0].ApiKey); }
    private sealed class Protector : ISecretProtector
    {
        public string Protect(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        public string Unprotect(string value) => Encoding.UTF8.GetString(Convert.FromBase64String(value));
    }
}
