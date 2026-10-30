using Microsoft.UI.Dispatching;
using Promptlet.Core;
using Promptlet.Services;

namespace Promptlet;

internal sealed class AppController
{
    private readonly App _app;
    private readonly SettingsService _store = new();
    private readonly HttpClient _http;
    private readonly TrayHotkeyService _shell;
    private readonly PromptWindow _prompt;
    private SettingsWindow? _settings;
    private bool _quitting;
    public WorkspaceSettings Settings { get; private set; }
    public OptimizationCoordinator Coordinator { get; }
    public OptimizationService Optimizer { get; }
    public DispatcherQueue Dispatcher => _prompt.DispatcherQueue;

    public AppController(App app)
    {
        _app = app;
        Settings = _store.Load();
        _http = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        }) { Timeout = Timeout.InfiniteTimeSpan };
        Optimizer = new(_http);
        _prompt = new PromptWindow(this);
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(_prompt);
        Coordinator = new(new ClipboardService(handle), Optimizer);
        Coordinator.UnexpectedError += ErrorLog.Write;
        Coordinator.Changed += state => Dispatcher.TryEnqueue(() =>
        {
            if (ReferenceEquals(state, Coordinator.State) && !_quitting) _prompt.Render(state);
        });
        _shell = new TrayHotkeyService(handle);
        _shell.HotkeyPressed += () =>
        {
            ShowPrompt();
            if (!_prompt.IsEditing) BeginOptimization();
        };
        _shell.OpenRequested += ShowPrompt;
        _shell.SettingsRequested += ShowSettings;
        _shell.ExitRequested += () => _ = QuitAsync();
        try { _shell.ChangeHotkey(Settings.Hotkey); }
        catch (PromptletException ex) { StartupWarning = ex.Message; }
    }

    private string? StartupWarning { get; }

    public void Start(bool showSettings, bool showPrompt)
    {
        var configured = true;
        try { Settings.ResolveRequest().ValidateConnection(); } catch (PromptletException) { configured = false; }
        if (showSettings || !configured || _store.LoadWarning is not null || StartupWarning is not null)
        {
            ShowSettings();
            _settings?.ShowMessage(StartupWarning ?? _store.LoadWarning ?? "配置你的模型服务，即可开始使用。", isError: false);
        }
        else if (showPrompt) ShowPrompt();
    }

    public void ShowPrompt()
    {
        if (_quitting) return;
        _prompt.Show(NativeMethods.GetForegroundWindow());
        _prompt.Render(Coordinator.State);
    }

    public void ShowSettings()
    {
        if (_quitting) return;
        _prompt.Hide();
        if (_settings is null)
        {
            _settings = new SettingsWindow(this);
            _settings.Closed += (_, _) => _settings = null;
        }
        _settings.AppWindow.Show();
        _settings.Activate();
    }

    public bool BeginOptimization(string? raw = null)
    {
        if (Coordinator.State.IsBusy && raw is null) return false;
        try
        {
            var request = Settings.ResolveRequest();
            request.ValidateConnection();
            _ = raw is null ? Coordinator.ActivateAsync(request) : Coordinator.ReoptimizeAsync(request, raw);
            return true;
        }
        catch (PromptletException ex) { ShowSettings(); _settings?.ShowMessage(ex.Message, true); return false; }
    }

    public async Task SaveSettingsAsync(WorkspaceSettings next)
    {
        next.Validate();
        var previous = Settings;
        var startupChanged = next.StartAtLogin != previous.StartAtLogin;
        try
        {
            _shell.ChangeHotkey(next.Hotkey);
            if (startupChanged) SettingsService.SetStartAtLogin(next.StartAtLogin);
            _store.Save(next);
        }
        catch
        {
            try { _shell.ChangeHotkey(previous.Hotkey); } catch { }
            if (startupChanged) { try { SettingsService.SetStartAtLogin(previous.StartAtLogin); } catch { } }
            throw;
        }
        Settings = next.Copy();
        var runningProfile = Coordinator.State.ProfileId;
        if (Coordinator.State.IsBusy && !next.ModelProfiles.Any(p => p.Id == runningProfile))
            await Coordinator.CancelAsync(onlyProfileId: runningProfile);
        _prompt.ApplySettings();
        _prompt.Render(Coordinator.State);
    }

    internal async Task QuitAsync()
    {
        if (_quitting) return;
        _quitting = true;
        await Coordinator.CancelAsync(stopping: true);
        _shell.Dispose();
        _settings?.Close();
        _prompt.AllowClose = true;
        _prompt.Close();
        _http.Dispose();
        _app.Quit();
    }

    internal async Task SmokeAsync()
    {
        var path = Environment.GetEnvironmentVariable("PROMPTLET_SMOKE_REPORT");
        try
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PROMPTLET_DATA_DIR")) || string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException("Smoke test requires isolated data and report paths.");
            ShowSettings(); await Task.Delay(350);
            await _settings!.ExerciseSmokeAsync();
            _settings.Close();
            ShowPrompt(); await Task.Delay(350);
            var frame = _prompt.MeasureSmoke();
            await _prompt.ExerciseUiSmokeAsync();
            _prompt.ExerciseDragSmoke();
            for (var i = 0; i < 10; i++) { _prompt.Hide(); ShowPrompt(); }
            _prompt.MeasureSmoke();
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new { Passed = true, Version = AppVersion.Current,
                Checks = new[] { "model/template CRUD", "active deletion", "DPAPI roundtrip", "custom template", "restore keeps name", "realized XAML layout", "compact selectors (1/2 rows)", "long selector scroll", "transparent text states (normal/hover/focus, dark/light)", "dark/light XAML previews", "drag target exclusions", "drag threshold/release/native movement", "hide clears drag", "borderless client", "caption theme", "hidden accelerator", "10 show/hide cycles" }, Frame = frame }));
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
            if (!string.IsNullOrWhiteSpace(path)) File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new { Passed = false, Error = ex.GetType().Name, Detail = ex.Message }));
        }
        finally { await QuitAsync(); }
    }
}
