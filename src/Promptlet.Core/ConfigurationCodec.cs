using System.Text.Json;

namespace Promptlet.Core;

public interface ISecretProtector { string Protect(string value); string Unprotect(string value); }
public sealed record ConfigurationLoadResult(WorkspaceSettings Settings, bool Migrated, string? Warning);

public static class ConfigurationCodec
{
    public const int SchemaVersion = 2;
    private sealed record Envelope(int SchemaVersion, WorkspaceSettings Settings, Dictionary<string, string> Secrets);
    public static ConfigurationLoadResult Decode(string json, ISecretProtector protector)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("SchemaVersion", out var version))
        {
            var legacy = root.GetProperty("Settings").Deserialize<AppSettings>() ?? throw new JsonException();
            var secret = root.TryGetProperty("Secret", out var s) ? s.GetString() ?? "" : "";
            var profile = new ModelProfile("legacy.default", string.IsNullOrWhiteSpace(legacy.Model) ? "默认模型" : legacy.Model, legacy.BaseUrl, legacy.Model, legacy.Stream);
            var warning = false;
            profile = ReadSecret(profile, secret, protector, ref warning);
            var hasProfile = !string.IsNullOrWhiteSpace(legacy.BaseUrl + legacy.Model + secret);
            var migrated = new WorkspaceSettings
            {
                Templates = [new(WorkspaceSettings.ResearchId, "科研项目", legacy.ResearchTemplate, "Research"), new(WorkspaceSettings.GeneralId, "通用", legacy.GeneralTemplate, "General")],
                ActiveTemplateId = legacy.ActiveTemplate == "General" ? WorkspaceSettings.GeneralId : WorkspaceSettings.ResearchId,
                ModelProfiles = hasProfile ? [profile] : [], ActiveProfileId = hasProfile ? profile.Id : null,
                Hotkey = legacy.Hotkey, AutoHideSeconds = legacy.AutoHideSeconds, Theme = legacy.Theme, StartAtLogin = legacy.StartAtLogin
            };
            migrated.Validate();
            return new(migrated, true, warning ? "旧密钥无法解密；其他设置已保留，请重新填写该密钥。" : null);
        }
        if (version.GetInt32() != SchemaVersion) throw new PromptletException("配置来自不支持的版本；为保护原文件，本次禁止保存。请使用匹配版本。");
        var envelope = root.Deserialize<Envelope>() ?? throw new JsonException();
        if (envelope.Settings is null || envelope.Secrets is null) throw new JsonException();
        envelope.Settings.Validate();
        var failed = false;
        var profiles = envelope.Settings.ModelProfiles.Select(p => ReadSecret(p, envelope.Secrets.GetValueOrDefault(p.Id) ?? "", protector, ref failed)).ToArray();
        return new(envelope.Settings with { ModelProfiles = profiles }, false, failed ? "部分模型密钥无法解密；原密文已保留，请在对应模型中替换或清除。" : null);
    }

    public static string Encode(WorkspaceSettings settings, ISecretProtector protector)
    {
        settings.Validate();
        var secrets = settings.ModelProfiles.ToDictionary(p => p.Id,
            p => p.UnreadableSecret ?? (p.ApiKey.Length == 0 ? "" : protector.Protect(p.ApiKey)));
        return JsonSerializer.Serialize(new Envelope(SchemaVersion, settings, secrets), new JsonSerializerOptions { WriteIndented = true });
    }

    private static ModelProfile ReadSecret(ModelProfile profile, string secret, ISecretProtector protector, ref bool failed)
    {
        if (secret.Length == 0) return profile;
        try { return profile with { ApiKey = protector.Unprotect(secret) }; }
        catch { failed = true; return profile with { UnreadableSecret = secret }; }
    }
}
