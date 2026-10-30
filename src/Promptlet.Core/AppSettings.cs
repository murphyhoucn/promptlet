using System.Text.Json.Serialization;

namespace Promptlet.Core;

public sealed record AppSettings
{
    [JsonIgnore] public string? PromptOverride { get; init; }
    [JsonIgnore] public string TemplateName { get; init; } = "科研项目";
    [JsonIgnore] public string ProfileId { get; init; } = "";
    [JsonIgnore] public string ProfileAlias { get; init; } = "";
    public string BaseUrl { get; init; } = "";
    [JsonIgnore] public string ApiKey { get; init; } = "";
    public string Model { get; init; } = "";
    public bool Stream { get; init; } = true;
    public string ActiveTemplate { get; init; } = "Research";
    public string GeneralTemplate { get; init; } = Templates.General;
    public string ResearchTemplate { get; init; } = Templates.Research;
    public string Hotkey { get; init; } = "Ctrl+Alt+Space";
    public int AutoHideSeconds { get; init; } = 5;
    public string Theme { get; init; } = "System";
    public bool StartAtLogin { get; init; }

    [JsonIgnore] public string SystemPrompt => PromptOverride ?? (ActiveTemplate == "General" ? GeneralTemplate : ResearchTemplate);

    public Uri GetEndpoint()
    {
        if (!Uri.TryCreate(BaseUrl.Trim(), UriKind.Absolute, out var uri) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
            throw new PromptletException("请填写有效的 HTTPS 接口地址；本机服务可以使用 HTTP。不要在地址中包含密钥或查询参数。");

        var path = uri.AbsoluteUri.TrimEnd('/');
        if (!uri.AbsolutePath.TrimEnd('/').EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            path += "/chat/completions";
        return new Uri(path);
    }

    public void ValidateConnection()
    {
        var endpoint = GetEndpoint();
        if (string.IsNullOrWhiteSpace(Model)) throw new PromptletException("请先在设置中填写模型名称。");
        if (!endpoint.IsLoopback && string.IsNullOrWhiteSpace(ApiKey))
            throw new PromptletException("请先在设置中填写 API Key。");
        if (ApiKey.Contains('\r') || ApiKey.Contains('\n')) throw new PromptletException("API Key 中不能包含换行。");
        if (string.IsNullOrWhiteSpace(SystemPrompt)) throw new PromptletException("优化模板不能为空。");
    }

    public void ValidatePreferences()
    {
        if (BaseUrl is null || Model is null || Hotkey is null || GeneralTemplate is null || ResearchTemplate is null)
            throw new PromptletException("设置文件包含无效字段，请重新配置。");
        if (AutoHideSeconds is < 0 or > 120) throw new PromptletException("自动隐藏时长应为 0–120 秒，0 表示关闭。");
        if (Theme is not ("System" or "Light" or "Dark")) throw new PromptletException("外观设置无效。");
        if (ActiveTemplate is not ("General" or "Research")) throw new PromptletException("请选择有效的优化模板。");
    }
}

public sealed class PromptletException(string message) : Exception(message);
