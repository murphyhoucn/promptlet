using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Promptlet.Core;
using Promptlet.Services;

namespace Promptlet;

public sealed partial class SettingsWindow : Window
{
    private readonly AppController _controller;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _test;
    private int _testGeneration;
    private WorkspaceSettings _draft;
    private readonly List<ModelProfile> _profiles;
    private readonly List<PromptTemplate> _templates;
    private string? _profileId, _templateId;
    private bool _loading = true, _closed, _clearSecret;

    internal SettingsWindow(AppController controller)
    {
        _controller = controller;
        _draft = controller.Settings.Copy();
        _profiles = _draft.ModelProfiles.ToList(); _templates = _draft.Templates.ToList();
        InitializeComponent();
        Root.ActualThemeChanged += (_, _) => { if (!_closed) WindowAppearance.SynchronizeTitleBar(this); };
        Root.Loaded += (_, _) => { if (!_closed) WindowAppearance.SynchronizeTitleBar(this); };
        HotkeyBox.Text = _draft.Hotkey; HideSecondsBox.Value = _draft.AutoHideSeconds;
        StartupToggle.IsOn = _draft.StartAtLogin;
        ThemeCombo.SelectedIndex = _draft.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 };
        VersionText.Text = "Promptlet  " + AppVersion.Current; ConfigPathText.Text = SettingsService.FilePath;
        RefreshProfiles(_draft.ActiveProfileId ?? _profiles.FirstOrDefault()?.Id); RefreshTemplates(_draft.ActiveTemplateId);
        WindowAppearance.Apply(this, _draft, false);
        WindowAppearance.Position(this, NativeMethods.GetForegroundWindow(), 800, 820, false);
        Closed += (_, _) => { _closed = true; _test?.Cancel(); _lifetime.Cancel(); ApiKeyBox.Password = ""; };
        _loading = false;
    }
    internal void ShowMessage(string message, bool isError)
    {
        Notice.Message = message; Notice.Severity = isError ? InfoBarSeverity.Error : InfoBarSeverity.Informational;
        Notice.IsOpen = true; SaveStatus.Text = message;
    }
    private void CacheProfile()
    {
        var index = _profiles.FindIndex(p => p.Id == _profileId); if (index < 0) return;
        var old = _profiles[index];
        _profiles[index] = old with { Alias = AliasBox.Text.Trim(), BaseUrl = BaseUrlBox.Text.Trim(), Model = ModelBox.Text.Trim(),
            ApiKey = ApiKeyBox.Password.Trim(), Stream = StreamToggle.IsOn,
            UnreadableSecret = _clearSecret || ApiKeyBox.Password.Length > 0 ? null : old.UnreadableSecret };
    }
    private void CacheTemplate()
    {
        var index = _templates.FindIndex(t => t.Id == _templateId);
        if (index >= 0) _templates[index] = _templates[index] with { Name = TemplateNameBox.Text.Trim(), Content = TemplateBox.Text };
    }
    private void RefreshProfiles(string? id)
    {
        var loading = _loading; _loading = true;
        ProfileList.ItemsSource = null; ProfileList.ItemsSource = _profiles;
        ProfileList.SelectedItem = _profiles.FirstOrDefault(p => p.Id == id);
        ResizeSelector(ProfileList, _profiles.Count);
        EmptyProfileHint.Visibility = _profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        LoadProfile(id); _loading = loading; UpdateCurrentLabels();
    }
    private void LoadProfile(string? id)
    {
        _test?.Cancel(); _testGeneration++; TestButton.IsEnabled = true; TestButton.Content = "测试此模型";
        _profileId = id; _clearSecret = false;
        var p = _profiles.FirstOrDefault(p => p.Id == id);
        ProfileEditor.Visibility = p is null ? Visibility.Collapsed : Visibility.Visible; DeleteProfileButton.IsEnabled = p is not null;
        AliasBox.Text = p?.Alias ?? ""; BaseUrlBox.Text = p?.BaseUrl ?? ""; ModelBox.Text = p?.Model ?? "";
        ApiKeyBox.Password = p?.ApiKey ?? ""; StreamToggle.IsOn = p?.Stream ?? true;
        if (p?.UnreadableSecret is not null) ShowMessage("此项原密钥无法解密，原密文会保留。请替换密钥，或点击清除。", true);
    }
    private void RefreshTemplates(string? id)
    {
        var loading = _loading; _loading = true;
        TemplateList.ItemsSource = null; TemplateList.ItemsSource = _templates;
        TemplateList.SelectedItem = _templates.FirstOrDefault(t => t.Id == id);
        ResizeSelector(TemplateList, _templates.Count);
        LoadTemplate(id); _loading = loading; UpdateCurrentLabels();
    }
    private void LoadTemplate(string? id)
    {
        _templateId = id; var t = _templates.FirstOrDefault(t => t.Id == id);
        TemplateNameBox.Text = t?.Name ?? ""; TemplateBox.Text = t?.Content ?? "";
        DeleteTemplateButton.IsEnabled = t is { BuiltInKind: null }; RestoreButton.IsEnabled = t?.BuiltInKind is not null;
    }
    private static void ResizeSelector(ListView list, int count)
    {
        list.Height = Math.Min(count, 4) * 40 + 8;
        list.Visibility = count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
    private void ProfileChanged(object sender, SelectionChangedEventArgs args)
    { if (_loading) return; var id = (ProfileList.SelectedItem as ModelProfile)?.Id; CacheProfile(); LoadProfile(id); }
    private void TemplateChanged(object sender, SelectionChangedEventArgs args)
    { if (_loading) return; var id = (TemplateList.SelectedItem as PromptTemplate)?.Id; CacheTemplate(); LoadTemplate(id); }
    private void UpdateCurrentLabels()
    {
        ActiveModelText.Text = "当前模型：" + (_profiles.FirstOrDefault(p => p.Id == _draft.ActiveProfileId)?.Alias ?? "未选择");
        ActiveTemplateText.Text = "当前模板：" + (_templates.FirstOrDefault(t => t.Id == _draft.ActiveTemplateId)?.Name ?? "未选择");
    }
    private static string NewName(string prefix, IEnumerable<string> existing)
    { var set = existing.ToHashSet(StringComparer.OrdinalIgnoreCase); var n = 1; while (set.Contains($"{prefix} {n}")) n++; return $"{prefix} {n}"; }
    private void AddProfile(object sender, RoutedEventArgs args)
    { CacheProfile(); var p = new ModelProfile(Guid.NewGuid().ToString("N"), NewName("模型", _profiles.Select(p => p.Alias))); _profiles.Add(p); RefreshProfiles(p.Id); }
    private void DeleteProfile(object sender, RoutedEventArgs args)
    {
        if (_profileId is null) return;
        if (_draft.ActiveProfileId == _profileId) _draft = _draft with { ActiveProfileId = null };
        _profiles.RemoveAll(p => p.Id == _profileId); RefreshProfiles(_profiles.FirstOrDefault()?.Id);
    }
    private void UseProfile(object sender, RoutedEventArgs args)
    { CacheProfile(); _draft = _draft with { ActiveProfileId = _profileId }; RefreshProfiles(_profileId); }
    private void ClearKey(object sender, RoutedEventArgs args) { _clearSecret = true; ApiKeyBox.Password = ""; CacheProfile(); }
    private void AddTemplate(object sender, RoutedEventArgs args)
    { CacheTemplate(); var t = new PromptTemplate(Guid.NewGuid().ToString("N"), NewName("模板", _templates.Select(t => t.Name)), Templates.General); _templates.Add(t); RefreshTemplates(t.Id); }
    private void DeleteTemplate(object sender, RoutedEventArgs args)
    {
        var t = _templates.FirstOrDefault(t => t.Id == _templateId); if (t is null || t.BuiltInKind is not null) return;
        if (_draft.ActiveTemplateId == t.Id) _draft = _draft with { ActiveTemplateId = WorkspaceSettings.ResearchId };
        _templates.Remove(t); RefreshTemplates(_draft.ActiveTemplateId);
    }
    private void UseTemplate(object sender, RoutedEventArgs args)
    { CacheTemplate(); if (_templateId is not null) _draft = _draft with { ActiveTemplateId = _templateId }; RefreshTemplates(_templateId); }
    private void RestoreTemplate(object sender, RoutedEventArgs args)
    { var t = _templates.FirstOrDefault(t => t.Id == _templateId); if (t?.BuiltInKind is not null) TemplateBox.Text = t.BuiltInKind == "General" ? Templates.General : Templates.Research; }
    private WorkspaceSettings Collect()
    {
        CacheProfile(); CacheTemplate();
        if (!double.IsFinite(HideSecondsBox.Value) || HideSecondsBox.Value != Math.Truncate(HideSecondsBox.Value)) throw new PromptletException("隐藏时长应为 0–120 的整数。");
        return _draft with { ModelProfiles = _profiles.ToArray(), Templates = _templates.ToArray(), Hotkey = HotkeyBox.Text.Trim(),
            AutoHideSeconds = (int)HideSecondsBox.Value, StartAtLogin = StartupToggle.IsOn,
            Theme = (ThemeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "System" };
    }
    private async void SaveClicked(object sender, RoutedEventArgs args)
    {
        SaveButton.IsEnabled = false;
        try { var next = Collect(); await _controller.SaveSettingsAsync(next); _draft = next.Copy(); WindowAppearance.Apply(this, next, false);
            RefreshProfiles(_profileId); RefreshTemplates(_templateId); ShowMessage(next.ActiveProfileId is null ? "已保存。尚未选择模型，请选择后再次保存。" : "已保存，下一次任务使用新配置。", false); }
        catch (PromptletException ex) { ShowMessage(ex.Message, true); }
        catch (Exception ex) { ErrorLog.Write(ex); ShowMessage("保存失败，请检查配置目录权限。", true); }
        finally { if (!_closed) SaveButton.IsEnabled = true; }
    }
    private async void TestClicked(object sender, RoutedEventArgs args)
    {
        var generation = ++_testGeneration; _test?.Cancel();
        using var test = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); _test = test;
        TestButton.IsEnabled = false; TestButton.Content = "测试中…";
        try
        {
            var draft = Collect(); var request = draft.ResolveRequest(_profileId); request.ValidateConnection();
            _ = await _controller.Optimizer.OptimizeAsync(request, "请将这句话改写得更清晰：整理今天的工作安排。", _ => { }, test.Token);
            if (!_closed && generation == _testGeneration) ShowMessage("此模型连接成功。测试未读取剪贴板。", false);
        }
        catch (OperationCanceledException) { }
        catch (PromptletException ex) { if (!_closed && generation == _testGeneration) ShowMessage(ex.Message, true); }
        catch (Exception ex) { ErrorLog.Write(ex); if (!_closed && generation == _testGeneration) ShowMessage("测试失败，请检查配置。", true); }
        finally { if (ReferenceEquals(_test, test)) _test = null; if (!_closed && generation == _testGeneration) { TestButton.IsEnabled = true; TestButton.Content = "测试此模型"; } }
    }
    private void OpenConfigDirectory(object sender, RoutedEventArgs args)
    { try { Directory.CreateDirectory(SettingsService.DirectoryPath); Process.Start(new ProcessStartInfo(SettingsService.DirectoryPath) { UseShellExecute = true }); } catch (Exception ex) { ErrorLog.Write(ex); ShowMessage("无法打开配置目录。", true); } }

    internal async Task ExerciseSmokeAsync()
    {
        // Actual controls and handlers, using an isolated profile and synthetic keys only.
        AddProfile(this, new()); AliasBox.Text = "烟测 A"; BaseUrlBox.Text = "http://localhost:18765/v1";
        ModelBox.Text = "mock-a"; ApiKeyBox.Password = "synthetic-a"; UseProfile(this, new()); var first = _profileId;
        AddProfile(this, new()); AliasBox.Text = "烟测 B"; BaseUrlBox.Text = "http://localhost:18765/v1";
        ModelBox.Text = "mock-b"; ApiKeyBox.Password = "synthetic-b"; CacheProfile();
        AddTemplate(this, new()); TemplateNameBox.Text = "自定义烟测"; TemplateBox.Text = "只改写，不回答。"; UseTemplate(this, new());
        var customTemplate = _templateId;
        RefreshTemplates(WorkspaceSettings.ResearchId); TemplateNameBox.Text = "研究改名"; TemplateBox.Text = "临时正文";
        RestoreTemplate(this, new());
        static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');
        if (TemplateNameBox.Text != "研究改名" || Normalize(TemplateBox.Text) != Normalize(Templates.Research)) throw new InvalidOperationException("Template restore failed.");
        CacheTemplate(); RefreshTemplates(customTemplate);
        var next = Collect(); await _controller.SaveSettingsAsync(next); _draft = next.Copy();
        var loaded = new SettingsService().Load();
        if (loaded.ModelProfiles.Length != 2 || loaded.ResolveRequest().ApiKey != "synthetic-a" || loaded.Templates.Length != 3)
            throw new InvalidOperationException("Configuration UI roundtrip failed.");
        RefreshProfiles(first); DeleteProfile(this, new()); next = Collect(); await _controller.SaveSettingsAsync(next); _draft = next.Copy();
        if (_controller.Settings.ActiveProfileId is not null || _controller.Settings.ModelProfiles.Length != 1)
            throw new InvalidOperationException("Deleting the active profile selected another provider.");
        UseProfile(this, new()); next = Collect(); await _controller.SaveSettingsAsync(next);
        if (new SettingsService().Load().ResolveRequest().ApiKey != "synthetic-b") throw new InvalidOperationException("Model keys mixed.");
        Root.UpdateLayout();
        if (Root.ActualWidth <= 0 || SaveButton.ActualWidth < 100) throw new InvalidOperationException("Settings layout was not realized.");
        await ExerciseSelectorLayoutAsync();
    }

    private async Task ExerciseSelectorLayoutAsync()
    {
        var theme = Root.RequestedTheme;
        var profiles = _profiles.ToArray(); var templates = _templates.ToArray(); var draft = _draft;
        try
        {
            foreach (var sampleTheme in new[] { ElementTheme.Dark, ElementTheme.Light })
            {
                Root.RequestedTheme = sampleTheme;
                RefreshProfiles(profiles[0].Id);
                Root.UpdateLayout();
                if (Math.Abs(ProfileList.ActualHeight - 48) > 0.5) throw new InvalidOperationException("Single model leaves empty list space.");
                SettingsScroll.ChangeView(null, 0, null, true);
                await Task.Delay(32);
                if (ProfileList.ContainerFromIndex(0) is not DependencyObject modelRow || UiSmokePreview.Find<TextBlock>(modelRow)?.Text != profiles[0].Alias)
                    throw new InvalidOperationException("Model row name was not rendered.");
                await UiSmokePreview.SaveAsync(Root, $"settings-model-{sampleTheme.ToString().ToLowerInvariant()}");
                _templates.Clear(); _templates.AddRange(templates.Where(t => t.BuiltInKind is not null));
                _draft = _draft with { ActiveTemplateId = WorkspaceSettings.ResearchId };
                RefreshTemplates(WorkspaceSettings.ResearchId);
                Root.UpdateLayout();
                if (Math.Abs(TemplateList.ActualHeight - 88) > 0.5) throw new InvalidOperationException("Two templates leave empty list space.");
                var offset = TemplateCard.TransformToVisual((UIElement)SettingsScroll.Content).TransformPoint(new()).Y;
                SettingsScroll.ChangeView(null, offset, null, true);
                await Task.Delay(32);
                if (TemplateList.ContainerFromIndex(0) is not DependencyObject templateRow || UiSmokePreview.Find<TextBlock>(templateRow)?.Text != _templates[0].Name)
                    throw new InvalidOperationException("Template row name was not rendered.");
                await UiSmokePreview.SaveAsync(Root, $"settings-template-{sampleTheme.ToString().ToLowerInvariant()}");
            }
            for (var i = 0; i < 5; i++) _profiles.Add(new ModelProfile($"layout-{i}", $"较长名称的模型配置，用于验证列表滚动与省略显示 {i + 1}"));
            RefreshProfiles(_profiles[0].Id);
            SettingsScroll.ChangeView(null, 0, null, true);
            Root.UpdateLayout();
            var scroll = UiSmokePreview.Find<ScrollViewer>(ProfileList);
            if (Math.Abs(ProfileList.ActualHeight - 168) > 0.5 || scroll is null || scroll.ScrollableHeight <= 0)
                throw new InvalidOperationException("Long model list does not scroll within four rows.");
            await UiSmokePreview.SaveAsync(Root, "settings-long-list");
        }
        finally
        {
            Root.RequestedTheme = theme; _draft = draft;
            _profiles.Clear(); _profiles.AddRange(profiles); _templates.Clear(); _templates.AddRange(templates);
            RefreshProfiles(_draft.ActiveProfileId); RefreshTemplates(_draft.ActiveTemplateId);
        }
    }
}
