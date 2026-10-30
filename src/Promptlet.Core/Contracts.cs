namespace Promptlet.Core;

public sealed record ClipboardSnapshot(string? Text, uint Version);
public enum ClipboardWriteResult { Written, Changed }

public interface IClipboardService
{
    Task<ClipboardSnapshot> ReadAsync(CancellationToken cancellationToken);
    Task<ClipboardWriteResult> TryWriteAsync(string text, ClipboardSnapshot expected, CancellationToken cancellationToken);
    Task WriteAsync(string text, CancellationToken cancellationToken);
}

public interface IOptimizationService
{
    Task<string> OptimizeAsync(AppSettings settings, string input, Action<string> onText, CancellationToken cancellationToken);
}

public enum OptimizationStatus { Idle, ReadingClipboard, Requesting, Streaming, Validating, Copied, CopyBlocked, CopyFailed, Failed, Cancelled }

public sealed record OptimizationState(
    string Raw = "",
    string Result = "",
    OptimizationStatus Status = OptimizationStatus.Idle,
    string Message = "复制一段想法，按快捷键开始。",
    string Template = "Research",
    string TemplateName = "科研项目",
    string ProfileId = "",
    string ProfileAlias = "",
    Guid TaskId = default)
{
    public bool IsBusy => Status is OptimizationStatus.ReadingClipboard or OptimizationStatus.Requesting or
        OptimizationStatus.Streaming or OptimizationStatus.Validating;
}
