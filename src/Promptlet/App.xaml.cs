using Microsoft.UI.Xaml;
using Promptlet.Services;

namespace Promptlet;

public partial class App : Application
{
    private AppController? _controller;
    private Mutex? _instance;
    private EventWaitHandle? _activation;
    private EventWaitHandle? _settingsActivation;
    private EventWaitHandle? _quitSignal;
    private bool _ownsMutex;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, args) =>
        {
            ErrorLog.Write(args.Exception);
            try
            {
                Directory.CreateDirectory(SettingsService.DirectoryPath);
                File.WriteAllText(Path.Combine(SettingsService.DirectoryPath, "crash.log"),
                    $"{DateTimeOffset.Now:O}\n{args.Exception.GetType().FullName}\n" +
                    (args.Exception is Microsoft.UI.Xaml.Markup.XamlParseException ? args.Exception.Message + "\n" : "") +
                    args.Exception.StackTrace);
            }
            catch { /* Crash reporting must never replace the original failure. */ }
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _instance = new Mutex(true, @"Local\Promptlet.Desktop.Singleton", out _ownsMutex);
        _activation = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Promptlet.Desktop.Activate");
        _settingsActivation = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Promptlet.Desktop.Settings");
        _quitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Promptlet.Desktop.Quit");
        var commandLine = Environment.GetCommandLineArgs();
        if (!_ownsMutex)
        {
            if (commandLine.Contains("--quit")) _quitSignal.Set();
            else if (commandLine.Contains("--settings")) _settingsActivation.Set();
            else _activation.Set();
            Exit();
            return;
        }
        if (commandLine.Contains("--quit")) { Quit(); return; }
        _controller = new AppController(this);
        var dispatcher = _controller.Dispatcher;
        _ = Task.Run(() =>
        {
            while (true)
            {
                var signal = WaitHandle.WaitAny([_activation, _settingsActivation, _quitSignal]);
                if (signal == 2) { dispatcher.TryEnqueue(() => { if (_controller is not null) _ = _controller.QuitAsync(); }); break; }
                if (signal == 1) dispatcher.TryEnqueue(() => _controller?.ShowSettings());
                else dispatcher.TryEnqueue(() => _controller?.ShowPrompt());
            }
        });
        if (commandLine.Contains("--self-test")) _ = _controller.SmokeAsync();
        else _controller.Start(commandLine.Contains("--settings"), commandLine.Contains("--show"));
    }

    internal void Quit()
    {
        if (_ownsMutex) { _instance?.ReleaseMutex(); _ownsMutex = false; }
        Exit();
    }
}
