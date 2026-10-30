using System.Text;

namespace Promptlet.Core;

/// <summary>Serializes cancellation and clipboard commits; a hidden view never owns task lifetime.</summary>
public sealed class OptimizationCoordinator(IClipboardService clipboard, IOptimizationService optimizer)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Run? _current;
    private bool _stopping;
    private OptimizationState _state = new();
    private OptimizationState? _lastCopy;
    public OptimizationState State => _state;
    public event Action<OptimizationState>? Changed;
    public event Action<Exception>? UnexpectedError;

    public Task ActivateAsync(AppSettings settings) => StartAsync(settings, null, replace: false);
    public Task ReoptimizeAsync(AppSettings settings, string raw) => StartAsync(settings, raw, replace: true);

    private async Task StartAsync(AppSettings settings, string? edited, bool replace)
    {
        Run run;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_stopping || (_current is not null && !replace)) return;
            _current?.Cancellation.Cancel();
            run = new Run();
            _current = run;
            SetState(new(Raw: edited ?? "", Status: OptimizationStatus.ReadingClipboard, Message: "正在读取剪贴板…", Template: settings.ActiveTemplate,
                TemplateName: settings.TemplateName, ProfileId: settings.ProfileId, ProfileAlias: settings.ProfileAlias, TaskId: Guid.NewGuid()));
        }
        finally { _gate.Release(); }

        try
        {
            settings.ValidateConnection();
            var snapshot = await clipboard.ReadAsync(run.Cancellation.Token).ConfigureAwait(false);
            var raw = edited ?? snapshot.Text;
            if (string.IsNullOrWhiteSpace(raw)) throw new PromptletException("剪贴板里没有可优化的文本，请先复制一段文字。");
            if (raw.EnumerateRunes().Count() > 20_000) throw new PromptletException("文本超过 20,000 字符，请缩短后再试。");
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!IsCurrent(run)) return;
                if (!replace && _lastCopy is { } copy && raw == copy.Result)
                {
                    _current = null;
                    SetState(copy with { Message = "这是上次已复制的结果。" });
                    return;
                }
                SetState(_state with { Raw = raw, Result = "", Status = OptimizationStatus.Requesting, Message = "正在连接模型…" });
            }
            finally { _gate.Release(); }

            var result = await optimizer.OptimizeAsync(settings, raw, text =>
            {
                // The UI dispatches this event; stale queued updates are filtered by state identity.
                _gate.Wait();
                try
                {
                    if (IsCurrent(run)) SetState(_state with { Result = text, Status = OptimizationStatus.Streaming, Message = "正在优化…" });
                }
                finally { _gate.Release(); }
            }, run.Cancellation.Token).ConfigureAwait(false);

            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!IsCurrent(run)) return;
                if (string.IsNullOrWhiteSpace(result)) throw new PromptletException("模型返回了空结果，请重试。");
                SetState(_state with { Result = result, Status = OptimizationStatus.Validating, Message = "正在复制…" });
                try
                {
                    var written = await clipboard.TryWriteAsync(result, snapshot, run.Cancellation.Token).ConfigureAwait(false);
                    if (written == ClipboardWriteResult.Written)
                    {
                        SetState(_state with { Status = OptimizationStatus.Copied, Message = "已复制 · 可以直接粘贴" });
                        _lastCopy = _state;
                    }
                    else SetState(_state with { Status = OptimizationStatus.CopyBlocked, Message = "剪贴板已更新，结果未自动复制。" });
                }
                catch (PromptletException ex)
                {
                    SetState(_state with { Status = OptimizationStatus.CopyFailed, Message = ex.Message });
                }
                _current = null;
            }
            finally { _gate.Release(); }
        }
        catch (OperationCanceledException) { await FinishErrorAsync(run, "已取消", OptimizationStatus.Cancelled).ConfigureAwait(false); }
        catch (PromptletException ex) { await FinishErrorAsync(run, ex.Message, OptimizationStatus.Failed).ConfigureAwait(false); }
        catch (Exception ex) { UnexpectedError?.Invoke(ex); await FinishErrorAsync(run, "处理失败，请检查配置后重试。", OptimizationStatus.Failed).ConfigureAwait(false); }
        finally { run.Cancellation.Dispose(); }
    }

    public async Task CancelAsync(bool stopping = false, string? onlyProfileId = null)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _stopping |= stopping;
            if (_current is not null && (onlyProfileId is null || _state.ProfileId == onlyProfileId))
            {
                _current.Cancellation.Cancel();
                _current = null;
                SetState(_state with { Status = OptimizationStatus.Cancelled, Message = "已取消" });
            }
        }
        finally { _gate.Release(); }
    }

    public async Task CopyResultAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_stopping || _state.Status is not (OptimizationStatus.Copied or OptimizationStatus.CopyBlocked or OptimizationStatus.CopyFailed)
                || string.IsNullOrWhiteSpace(_state.Result)) return;
            try
            {
                await clipboard.WriteAsync(_state.Result, CancellationToken.None).ConfigureAwait(false);
                SetState(_state with { Status = OptimizationStatus.Copied, Message = "已复制 · 可以直接粘贴" });
                _lastCopy = _state;
            }
            catch (PromptletException ex) { SetState(_state with { Status = OptimizationStatus.CopyFailed, Message = ex.Message }); }
        }
        finally { _gate.Release(); }
    }

    private async Task FinishErrorAsync(Run run, string message, OptimizationStatus status)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!IsCurrent(run)) return;
            _current = null;
            SetState(_state with { Status = status, Message = message });
        }
        finally { _gate.Release(); }
    }

    private bool IsCurrent(Run run) => !_stopping && ReferenceEquals(_current, run) && !run.Cancellation.IsCancellationRequested;
    private void SetState(OptimizationState state) { _state = state; Changed?.Invoke(state); }
    private sealed class Run { public CancellationTokenSource Cancellation { get; } = new(); }
}
