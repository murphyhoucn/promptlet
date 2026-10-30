using System.Runtime.InteropServices;
using Promptlet.Core;

namespace Promptlet.Services;

internal sealed class TrayHotkeyService : IDisposable
{
    private readonly nint _window;
    private readonly NativeMethods.SubclassProc _procedure;
    private NativeMethods.NotifyIconData _icon;
    private readonly uint _taskbarCreated;
    private int _hotkeyId = 110;
    private string? _hotkey;
    private (uint Modifiers, uint Key)? _registered;
    private bool _disposed;
    public event Action? HotkeyPressed;
    public event Action? OpenRequested;
    public event Action? SettingsRequested;
    public event Action? ExitRequested;

    public TrayHotkeyService(nint window)
    {
        _window = window;
        _procedure = WindowProcedure;
        if (!NativeMethods.SetWindowSubclass(window, _procedure, 1, 0))
            throw new PromptletException("无法初始化系统托盘入口。");
        _taskbarCreated = NativeMethods.RegisterWindowMessageW("TaskbarCreated");
        _icon = new()
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.NotifyIconData>(),
            Window = window, Id = 1, Flags = 0x2 | 0x1 | 0x4 | 0x80,
            CallbackMessage = 0x8001,
            Icon = NativeMethods.LoadImageW(0, Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"), 1, 32, 32, 0x10),
            Tip = "Promptlet · 提示词，恰到好处", Info = "", InfoTitle = "", Version = 4
        };
        AddIcon();
    }

    private void AddIcon()
    {
        NativeMethods.Shell_NotifyIconW(0, ref _icon);
        _icon.Version = 4;
        NativeMethods.Shell_NotifyIconW(4, ref _icon);
    }

    public void ChangeHotkey(string text)
    {
        if (_hotkey == text) return;
        var (modifiers, key) = Parse(text);
        if (_registered == (modifiers, key)) { _hotkey = text; return; }
        var newId = _hotkeyId == 110 ? 111 : 110;
        if (!NativeMethods.RegisterHotKey(_window, newId, modifiers | 0x4000, key))
            throw new PromptletException("这个快捷键已被系统或其他应用占用，请换一个组合；原快捷键仍可使用。");
        NativeMethods.UnregisterHotKey(_window, _hotkeyId);
        _hotkeyId = newId;
        _hotkey = text;
        _registered = (modifiers, key);
    }

    private static (uint Modifiers, uint Key) Parse(string text)
    {
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        uint modifiers = 0;
        uint key = 0;
        foreach (var part in parts)
        {
            var upper = part.ToUpperInvariant();
            switch (upper)
            {
                case "CTRL" or "CONTROL": modifiers |= 2; break;
                case "ALT": modifiers |= 1; break;
                case "SHIFT": modifiers |= 4; break;
                case "WIN": modifiers |= 8; break;
                default:
                    if (key != 0) throw new PromptletException("快捷键应包含修饰键和一个普通按键，例如 Ctrl+Alt+Space。");
                    key = upper switch
                    {
                        "SPACE" => 0x20,
                        _ when upper.Length == 1 && char.IsAsciiLetterOrDigit(upper[0]) => upper[0],
                        _ when upper.StartsWith('F') && int.TryParse(upper.AsSpan(1), out var f) && f is >= 1 and <= 24 => (uint)(0x70 + f - 1),
                        _ => throw new PromptletException("支持字母、数字、Space 或 F1–F24，例如 Ctrl+Alt+Space。")
                    };
                    break;
            }
        }
        if (modifiers == 0 || key == 0) throw new PromptletException("快捷键需要修饰键和一个普通按键。");
        return (modifiers, key);
    }

    private nint WindowProcedure(nint hwnd, uint message, nuint wp, nint lp, nuint id, nuint data)
    {
        if (_disposed) return NativeMethods.DefSubclassProc(hwnd, message, wp, lp);
        if (message == 0x0312 && (int)wp == _hotkeyId) { HotkeyPressed?.Invoke(); return 0; }
        if (message == _taskbarCreated) { AddIcon(); return 0; }
        if (message == 0x8001)
        {
            var notification = (uint)((long)lp & 0xffff);
            if (notification is 0x007B or 0x0205) ShowMenu();
            else if (notification is 0x0400 or 0x0401) OpenRequested?.Invoke();
            return 0;
        }
        return NativeMethods.DefSubclassProc(hwnd, message, wp, lp);
    }

    private void ShowMenu()
    {
        var menu = NativeMethods.CreatePopupMenu();
        try
        {
            NativeMethods.AppendMenuW(menu, 0, 1, "打开 Promptlet");
            NativeMethods.AppendMenuW(menu, 0, 2, "设置");
            NativeMethods.AppendMenuW(menu, 0x800, 0, null);
            NativeMethods.AppendMenuW(menu, 0, 3, "退出 Promptlet");
            NativeMethods.GetCursorPos(out var cursor);
            NativeMethods.SetForegroundWindow(_window);
            var command = NativeMethods.TrackPopupMenuEx(menu, 0x100 | 0x2, cursor.X, cursor.Y, _window, 0);
            NativeMethods.PostMessageW(_window, 0, 0, 0);
            if (command == 1) OpenRequested?.Invoke();
            else if (command == 2) SettingsRequested?.Invoke();
            else if (command == 3) ExitRequested?.Invoke();
        }
        finally { NativeMethods.DestroyMenu(menu); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        NativeMethods.UnregisterHotKey(_window, _hotkeyId);
        NativeMethods.Shell_NotifyIconW(2, ref _icon);
        NativeMethods.RemoveWindowSubclass(_window, _procedure, 1);
        if (_icon.Icon != 0) NativeMethods.DestroyIcon(_icon.Icon);
    }
}
