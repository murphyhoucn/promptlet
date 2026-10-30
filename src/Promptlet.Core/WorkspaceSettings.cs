using System.Text.Json.Serialization;

namespace Promptlet.Core;

public sealed record PromptTemplate(string Id, string Name, string Content, string? BuiltInKind = null)
{
    public override string ToString() => Name;
}

public sealed record ModelProfile(string Id, string Alias, string BaseUrl = "", string Model = "", bool Stream = true)
{
    [JsonIgnore] public string ApiKey { get; init; } = "";
    [JsonIgnore] public string? UnreadableSecret { get; init; }
    public override string ToString() => Alias;
}

public sealed record WorkspaceSettings
{
    public const string GeneralId = "builtin.general", ResearchId = "builtin.research";
    public PromptTemplate[] Templates { get; init; } =
    [new(ResearchId, "科研项目", global::Promptlet.Core.Templates.Research, "Research"), new(GeneralId, "通用", global::Promptlet.Core.Templates.General, "General")];
    public ModelProfile[] ModelProfiles { get; init; } = [];
    public string ActiveTemplateId { get; init; } = ResearchId;
    public string? ActiveProfileId { get; init; }
    public string Hotkey { get; init; } = "Ctrl+Alt+Space";
    public int AutoHideSeconds { get; init; } = 5;
    public string Theme { get; init; } = "System";
    public bool StartAtLogin { get; init; }

    public WorkspaceSettings Copy() => this with { Templates = Templates.ToArray(), ModelProfiles = ModelProfiles.ToArray() };

    public AppSettings ResolveRequest(string? profileId = null)
    {
        var profile = ModelProfiles.FirstOrDefault(p => p.Id == (profileId ?? ActiveProfileId))
            ?? throw new PromptletException("请在设置中选择当前模型。");
        var template = Templates.FirstOrDefault(t => t.Id == ActiveTemplateId)
            ?? throw new PromptletException("请先选择优化模板。");
        if (profile.UnreadableSecret is not null) throw new PromptletException("此模型的密钥无法解密，请重新填写或明确清除密钥。");
        return new AppSettings
        {
            BaseUrl = profile.BaseUrl, ApiKey = profile.ApiKey, Model = profile.Model, Stream = profile.Stream,
            ActiveTemplate = template.Id, PromptOverride = template.Content, TemplateName = template.Name,
            ProfileId = profile.Id, ProfileAlias = profile.Alias
        };
    }

    public void Validate()
    {
        if (Templates is null || ModelProfiles is null || Hotkey is null) throw new PromptletException("配置结构不完整。");
        if (AutoHideSeconds is < 0 or > 120 || Theme is not ("System" or "Light" or "Dark")) throw new PromptletException("外观或隐藏时长无效。");
        ValidateNames(Templates.Select(t => (t.Id, t.Name)), "模板");
        ValidateNames(ModelProfiles.Select(p => (p.Id, p.Alias)), "模型");
        foreach (var (id, kind) in new[] { (ResearchId, "Research"), (GeneralId, "General") })
            if (Templates.Count(t => t.Id == id && t.BuiltInKind == kind) != 1) throw new PromptletException("内置模板必须保留。");
        if (Templates.Any(t => t.Content is null || (t.BuiltInKind is not null && t.Id != GeneralId && t.Id != ResearchId)))
            throw new PromptletException("模板数据无效。");
        var selected = Templates.FirstOrDefault(t => t.Id == ActiveTemplateId);
        if (selected is null || string.IsNullOrWhiteSpace(selected.Content)) throw new PromptletException("当前模板正文不能为空。");
        if (ActiveProfileId is not null && !ModelProfiles.Any(p => p.Id == ActiveProfileId)) throw new PromptletException("当前模型不存在。");
        foreach (var p in ModelProfiles)
        {
            if (p.BaseUrl is null || p.Model is null || p.ApiKey is null) throw new PromptletException("模型字段无效。");
        }
    }

    private static void ValidateNames(IEnumerable<(string Id, string Name)> items, string label)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, name) in items)
            if (string.IsNullOrWhiteSpace(id) || !ids.Add(id) || string.IsNullOrWhiteSpace(name) || !names.Add(name.Trim()))
                throw new PromptletException($"{label}名称不能为空或重复，内部 ID 必须唯一。");
    }
}
