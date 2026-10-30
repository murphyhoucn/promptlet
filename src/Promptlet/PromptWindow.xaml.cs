using System.Diagnostics;
using System.Text;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Promptlet.Core;
using Promptlet.Services;
using Windows.System;

namespace Promptlet;

public sealed partial class PromptWindow : Window
{
    private readonly AppController _controller;
    private readonly DispatcherQueueTimer _hideTimer;
    private readonly AutoHidePolicy _autoHide = new();
    private readonly WindowDragGesture _drag = new();
    private bool _editing;
    internal bool IsEditing => _editing;
    private bool _visible;
    internal bool AllowClose { get; set; }

    internal PromptWindow(AppController controller)
    {
        _controller = controller;
        InitializeComponent();
        Surface.ActualThemeChanged += (_, _) => RefreshFrame();
        Surface.Loaded += (_, _) => RefreshFrame();
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        AppWindow.IsShownInSwitchers = false;
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var extendedStyle = (long)NativeMethods.GetWindowLongPtrW(hwnd, -20);
        NativeMethods.SetWindowLongPtrW(hwnd, -20, (nint)((extendedStyle | 0x80) & ~0x40000));
        AppWindow.Closing += (_, args) => { if (!AllowClose) { args.Cancel = true; Hide(); } };
        Activated += (_, args) =>
        {
            if (args.WindowActivationState != WindowActivationState.Deactivated) return;
            StopDragging();
            if (RawBox.ContextFlyout?.IsOpen != true && ResultBox.ContextFlyout?.IsOpen != true) Hide();
        };
        Root.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler((_, _) => Interacted()), true);
        Root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => Interacted()), true);
        Root.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler((_, _) => Interacted()), true);
        Root.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, _) => Interacted()), true);
        Root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(PagePointerPressed), true);
        Root.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(PagePointerMoved), true);
        Root.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(EndPageDrag), true);
        Root.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(EndPageDrag), true);
        Root.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler((sender, args) =>
        {
            if (ReferenceEquals(args.OriginalSource, Root)) EndPageDrag(sender, args);
        }), true);
        var escape = new KeyboardAccelerator { Key = VirtualKey.Escape };
        escape.Invoked += async (_, args) => { args.Handled = true; StopDragging(); await _controller.Coordinator.CancelAsync(); EndEditing(); Hide(); };
        var submit = new KeyboardAccelerator { Key = VirtualKey.Enter, Modifiers = VirtualKeyModifiers.Control };
        submit.Invoked += (_, args) => { args.Handled = true; Optimize(); };
        Root.KeyboardAccelerators.Add(escape);
        Root.KeyboardAccelerators.Add(submit);
        _hideTimer = DispatcherQueue.CreateTimer();
        _hideTimer.Interval = TimeSpan.FromMilliseconds(250);
        _hideTimer.Tick += (_, _) =>
        {
            var seconds = _controller.Settings.AutoHideSeconds;
            if (!_visible) return;
            var paused = _editing || NativeMethods.GetAsyncKeyState(1) < 0 || RawBox.ContextFlyout?.IsOpen == true || ResultBox.ContextFlyout?.IsOpen == true;
            if (_autoHide.ShouldHide(seconds, paused)) Hide();
        };
        ApplySettings();
    }

    internal void ApplySettings()
    {
        WindowAppearance.Apply(this, _controller.Settings, transient: true);
        ShortcutHint.Text = _controller.Settings.Hotkey.Replace("+", " + ") + " 唤起";
    }

    internal void Show(nint source)
    {
        if (!_visible) WindowAppearance.Position(this, source, 680, 640, upper: true);
        _visible = true;
        Interacted();
        AppWindow.Show();
        RefreshFrame();
        Activate();
        _hideTimer.Start();
    }

    internal void Hide()
    {
        StopDragging();
        _visible = false;
        _hideTimer.Stop();
        BusyRing.IsActive = false;
        AppWindow.Hide();
    }

    internal void Render(OptimizationState state)
    {
        _autoHide.Observe(state);
        if (!_editing && RawBox.Text != state.Raw) { RawBox.Text = state.Raw; UpdateCharacterCount(); }
        if (ResultBox.Text != state.Result) ResultBox.Text = state.Result;
        UpdateTextHitTargets();
        TemplateLabel.Text = state.Status == OptimizationStatus.Idle ? _controller.Settings.Templates.FirstOrDefault(t => t.Id == _controller.Settings.ActiveTemplateId)?.Name ?? "模板" : state.TemplateName;
        ModelLabel.Text = state.Status == OptimizationStatus.Idle ? _controller.Settings.ModelProfiles.FirstOrDefault(p => p.Id == _controller.Settings.ActiveProfileId)?.Alias ?? "未配置模型" : state.ProfileAlias;
        ToolTipService.SetToolTip(TemplateLabel, TemplateLabel.Text);
        ToolTipService.SetToolTip(ModelLabel, ModelLabel.Text);
        StatusText.Text = _editing ? "修改后按 Ctrl + Enter 重新优化" : state.Message;
        BusyRing.Visibility = state.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        BusyRing.IsActive = state.IsBusy && _visible;
        CancelButton.Visibility = state.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        OptimizeButton.Visibility = state.IsBusy && !_editing ? Visibility.Collapsed : Visibility.Visible;
        OptimizeButton.Content = string.IsNullOrEmpty(state.Raw) && !_editing ? "开始优化" : "重新优化";
        CopyButton.Visibility = !_editing && state.Status is OptimizationStatus.Copied or OptimizationStatus.CopyBlocked or OptimizationStatus.CopyFailed
            ? Visibility.Visible : Visibility.Collapsed;
        ResultHint.Text = state.Status == OptimizationStatus.Copied ? "已存入剪贴板" : "完成后自动复制";
    }

    private void Interacted() => _autoHide.Interact();
    private void RefreshFrame()
    {
        WindowAppearance.SynchronizeTitleBar(this);
        WindowAppearance.ApplyFloatingFrame(this);
    }
    internal void EndEditing() { _editing = false; RawBox.IsReadOnly = true; EditButton.Content = "编辑"; DiscardButton.Visibility = Visibility.Collapsed; UpdateTextHitTargets(); }

    private void UpdateTextHitTargets()
    {
        // Empty read-only cards are page background; populated/editable text keeps selection/input.
        RawBox.IsHitTestVisible = !RawBox.IsReadOnly || RawBox.Text.Length > 0;
        ResultBox.IsHitTestVisible = ResultBox.Text.Length > 0;
    }

    private async void EditClicked(object sender, RoutedEventArgs args)
    {
        await _controller.Coordinator.CancelAsync();
        _editing = true;
        RawBox.IsReadOnly = false;
        RawBox.Focus(FocusState.Programmatic);
        EditButton.Content = "编辑中";
        DiscardButton.Visibility = Visibility.Visible;
        Render(_controller.Coordinator.State);
    }

    private void Optimize()
    {
        var text = RawBox.Text;
        var wasEditing = _editing;
        Interacted();
        if (_controller.BeginOptimization(string.IsNullOrEmpty(text) && !wasEditing ? null : text)) EndEditing();
    }

    private void OptimizeClicked(object sender, RoutedEventArgs args) => Optimize();
    private async void CancelClicked(object sender, RoutedEventArgs args) => await _controller.Coordinator.CancelAsync();
    private async void CopyClicked(object sender, RoutedEventArgs args) => await _controller.Coordinator.CopyResultAsync();
    private void OpenSettings(object sender, RoutedEventArgs args) => _controller.ShowSettings();
    private void HideClicked(object sender, RoutedEventArgs args) => Hide();
    private void DiscardClicked(object sender, RoutedEventArgs args) { EndEditing(); Render(_controller.Coordinator.State); Interacted(); }
    private void RawTextChanged(object sender, Microsoft.UI.Xaml.Controls.TextChangedEventArgs args)
        => UpdateCharacterCount();
    private void UpdateCharacterCount()
    { if (CharacterCount is not null) CharacterCount.Text = $"{RawBox.Text.EnumerateRunes().Count():N0} / 20,000"; }
    internal object MeasureSmoke()
    {
        Surface.UpdateLayout();
        var frame = FloatingFrameDiagnostics.Capture(WinRT.Interop.WindowNative.GetWindowHandle(this));
        if (frame.HasFrameStyle || !frame.ClientFillsWindow || Surface.ActualWidth <= 0 || ResultBox.ActualHeight < 30 || Root.KeyboardAcceleratorPlacementMode != KeyboardAcceleratorPlacementMode.Hidden)
            throw new InvalidOperationException("Floating frame or layout check failed.");
        return new { frame.HasFrameStyle, frame.ClientFillsWindow, Surface.ActualWidth, Surface.ActualHeight,
            ResultHeight = ResultBox.ActualHeight, HiddenAccelerator = true, TitleBarTheme = AppWindow.TitleBar.PreferredTheme.ToString() };
    }
    internal async Task ExerciseUiSmokeAsync()
    {
        var theme = Surface.RequestedTheme;
        try
        {
            foreach (var sampleTheme in new[] { ElementTheme.Dark, ElementTheme.Light })
            {
                Surface.RequestedTheme = sampleTheme;
                Render(new());
                await Task.Delay(32);
                if (RawBox.IsHitTestVisible || ResultBox.IsHitTestVisible)
                    throw new InvalidOperationException("Empty read-only text blocks page dragging.");
                RawBox.IsReadOnly = false; UpdateTextHitTargets();
                if (!RawBox.IsHitTestVisible) throw new InvalidOperationException("Empty editor no longer accepts input.");
                EndEditing();
                Surface.UpdateLayout();
                MeasureSmoke();
                var introBottom = IntroPanel.TransformToVisual(Root).TransformPoint(new(0, IntroPanel.ActualHeight)).Y;
                if (RawCard.TransformToVisual(Root).TransformPoint(new()).Y - introBottom < 16)
                    throw new InvalidOperationException("Intro overlaps the original card.");
                foreach (var box in new[] { RawBox, ResultBox })
                foreach (var state in new[] { "Normal", "PointerOver", "Focused" })
                {
                    if (!VisualStateManager.GoToState(box, state, false)) throw new InvalidOperationException("Text visual state missing.");
                    Surface.UpdateLayout();
                    var border = UiSmokePreview.Find<Border>(box, "BorderElement");
                    if (border?.Background is not Microsoft.UI.Xaml.Media.SolidColorBrush { Color.A: 0 })
                        throw new InvalidOperationException($"Opaque inner card in {sampleTheme}/{state}.");
                }
                await UiSmokePreview.SaveAsync(Surface, $"prompt-{sampleTheme.ToString().ToLowerInvariant()}-empty");
                Render(new(Raw: string.Join('\n', Enumerable.Repeat("这是一段用于检查换行和滚动的原始提示词。", 12)),
                    Result: "请将下列素材整理为清晰、可执行的提示词：\n\n1. 保留用户目标和已知事实。\n2. 明确输入、约束和输出格式。\n3. 信息不足时标明待补充内容。",
                    Status: OptimizationStatus.Copied, Message: "已复制优化结果。", ProfileAlias: "这是一个用于验证省略显示的较长模型名称", TemplateName: "这是一个用于验证省略显示的较长模板名称"));
                await Task.Delay(32);
                Surface.UpdateLayout();
                MeasureSmoke();
                if (CharacterCount.Text != $"{RawBox.Text.EnumerateRunes().Count():N0} / 20,000")
                    throw new InvalidOperationException("Original character count did not update.");
                if (!RawBox.IsHitTestVisible || !ResultBox.IsHitTestVisible)
                    throw new InvalidOperationException("Populated text no longer accepts selection.");
                if (RawBox.ActualHeight > 120.5 || RawBox.ActualHeight < 56 || ResultBox.ActualHeight < 90)
                    throw new InvalidOperationException("Prompt content no longer fits the cards.");
                await UiSmokePreview.SaveAsync(Surface, $"prompt-{sampleTheme.ToString().ToLowerInvariant()}-content");
            }
        }
        finally { Surface.RequestedTheme = theme; Render(_controller.Coordinator.State); }
    }
    private bool CanDragFrom(DependencyObject? source)
    {
        for (var node = source; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, Root)) return true;
            // Let controls keep their own clicks, text selection, and scrollbar dragging.
            if (node is ButtonBase or TextBox or RichEditBox or PasswordBox or ScrollViewer or ScrollBar or Thumb ||
                node is TextBlock { IsTextSelectionEnabled: true }) return false;
        }
        return false;
    }

    private void PagePointerPressed(object sender, PointerRoutedEventArgs args)
    {
        if (args.Handled || args.Pointer.PointerDeviceType != Microsoft.UI.Input.PointerDeviceType.Mouse ||
            _drag.PointerId is not null || !CanDragFrom(args.OriginalSource as DependencyObject)) return;
        if (!args.GetCurrentPoint(Root).Properties.IsLeftButtonPressed || NativeMethods.GetAsyncKeyState(1) >= 0 ||
            !NativeMethods.GetCursorPos(out var cursor) || !Root.CapturePointer(args.Pointer)) return;
        var dpi = NativeMethods.GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        var origin = AppWindow.Position;
        _drag.Begin(args.Pointer.PointerId, true, cursor.X, cursor.Y, origin.X, origin.Y,
            NativeMethods.GetSystemMetricsForDpi(68, dpi), NativeMethods.GetSystemMetricsForDpi(69, dpi));
        args.Handled = true;
    }

    private void PagePointerMoved(object sender, PointerRoutedEventArgs args)
    {
        if (_drag.PointerId != args.Pointer.PointerId) return;
        // Check live button state too: a delayed/missed release must never latch a drag.
        if (!NativeMethods.GetCursorPos(out var cursor)) { StopDragging(); return; }
        var pressed = args.GetCurrentPoint(Root).Properties.IsLeftButtonPressed && NativeMethods.GetAsyncKeyState(1) < 0;
        if (MoveWindowForPointer(args.Pointer.PointerId, pressed, cursor.X, cursor.Y)) args.Handled = true;
    }

    private bool MoveWindowForPointer(uint pointerId, bool pressed, int screenX, int screenY)
    {
        var position = _drag.Move(pointerId, pressed, screenX, screenY);
        if (!pressed) { StopDragging(); return false; }
        if (position is not { } point) return false;
        AppWindow.Move(new(point.X, point.Y));
        return true;
    }

    private void EndPageDrag(object sender, PointerRoutedEventArgs args)
    {
        if (_drag.PointerId != args.Pointer.PointerId) return;
        StopDragging();
        Interacted();
    }

    private void StopDragging()
    {
        _drag.Reset(); // Reset first; releasing capture may synchronously raise CaptureLost.
        Root.ReleasePointerCaptures();
    }

    internal void ExerciseDragSmoke()
    {
        if (!CanDragFrom(Root) || !CanDragFrom(BrandLogo) || !CanDragFrom(IntroPanel) || !CanDragFrom(RawCard) ||
            CanDragFrom(RawBox) || CanDragFrom(ResultBox) || CanDragFrom(EditButton) || CanDragFrom(OptimizeButton))
            throw new InvalidOperationException("Window drag targets interfere with controls.");
        var settingsButton = UiSmokePreview.Find<Button>(Root)!;
        var icon = UiSmokePreview.Find<FontIcon>(settingsButton);
        if (icon is null || CanDragFrom(icon)) throw new InvalidOperationException("Button icon starts window dragging.");
        var origin = AppWindow.Position;
        try
        {
            // Synthetic gesture state, real AppWindow movement. This does not inject OS input.
            _drag.Begin(1, true, 100, 100, origin.X, origin.Y, 4, 4);
            if (MoveWindowForPointer(1, true, 102, 102) || !AppWindow.Position.Equals(origin))
                throw new InvalidOperationException("A click/jitter moved the prompt window.");
            if (!MoveWindowForPointer(1, true, 124, 84) || AppWindow.Position.X != origin.X + 24 || AppWindow.Position.Y != origin.Y - 16)
                throw new InvalidOperationException("A held drag did not move the native window.");
            MeasureSmoke();
            MoveWindowForPointer(1, false, 150, 150);
            if (MoveWindowForPointer(1, false, 200, 200) || _drag.PointerId is not null)
                throw new InvalidOperationException("Released pointer still moves the window.");
            _drag.Begin(1, true, 0, 0, origin.X, origin.Y, 4, 4);
            Hide();
            if (_drag.PointerId is not null) throw new InvalidOperationException("Hidden window retained a drag.");
        }
        finally { StopDragging(); Show(0); AppWindow.Move(origin); }
    }
}
