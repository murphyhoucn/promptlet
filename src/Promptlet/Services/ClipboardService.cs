using System.Runtime.InteropServices;
using System.Text;
using Promptlet.Core;

namespace Promptlet.Services;

/// <summary>Checks and writes under one native clipboard lock, including sequence and text equality.</summary>
internal sealed class ClipboardService(nint owner) : IClipboardService
{
    private const uint UnicodeText = 13;

    public Task<ClipboardSnapshot> ReadAsync(CancellationToken token) => Task.Run(() => WithClipboard(() =>
    {
        var text = ReadText(); // Render delayed data before recording the sequence number.
        return new ClipboardSnapshot(text, NativeMethods.GetClipboardSequenceNumber());
    }, token), token);

    public Task<ClipboardWriteResult> TryWriteAsync(string text, ClipboardSnapshot expected, CancellationToken token) => Task.Run(() => WithClipboard(() =>
    {
        var current = ReadText();
        var sequence = NativeMethods.GetClipboardSequenceNumber();
        if (sequence == 0 || expected.Version == 0 || expected.Version != sequence || current != expected.Text)
            return ClipboardWriteResult.Changed;
        token.ThrowIfCancellationRequested();
        WriteText(text);
        return ClipboardWriteResult.Written;
    }, token), token);

    public Task WriteAsync(string text, CancellationToken token) => Task.Run(() => WithClipboard(() =>
    {
        WriteText(text);
        return true;
    }, token), token);

    private T WithClipboard<T>(Func<T> action, CancellationToken token)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            token.ThrowIfCancellationRequested();
            if (NativeMethods.OpenClipboard(owner))
            {
                try { token.ThrowIfCancellationRequested(); return action(); }
                finally { NativeMethods.CloseClipboard(); }
            }
            if (attempt < 2 && token.WaitHandle.WaitOne(100)) token.ThrowIfCancellationRequested();
        }
        throw new PromptletException("剪贴板正在被其他应用使用，请稍后重试。");
    }

    private static string? ReadText()
    {
        if (!NativeMethods.IsClipboardFormatAvailable(UnicodeText)) return null;
        var handle = NativeMethods.GetClipboardData(UnicodeText);
        if (handle == 0) throw new PromptletException("读取剪贴板失败，请重新复制原文后重试。");
        var pointer = NativeMethods.GlobalLock(handle);
        if (pointer == 0) throw new PromptletException("读取剪贴板失败，请重试。");
        try { return Marshal.PtrToStringUni(pointer); }
        finally { NativeMethods.GlobalUnlock(handle); }
    }

    private static void WriteText(string text)
    {
        var bytes = Encoding.Unicode.GetBytes(text + '\0');
        var memory = NativeMethods.GlobalAlloc(0x0042, (nuint)bytes.Length);
        if (memory == 0) throw new PromptletException("复制失败：无法分配剪贴板内存。");
        try
        {
            var pointer = NativeMethods.GlobalLock(memory);
            if (pointer == 0) throw new PromptletException("复制失败，请点击“复制结果”重试。");
            try { Marshal.Copy(bytes, 0, pointer, bytes.Length); }
            finally { NativeMethods.GlobalUnlock(memory); }
            if (!NativeMethods.EmptyClipboard() || NativeMethods.SetClipboardData(UnicodeText, memory) == 0)
                throw new PromptletException("复制失败，请点击“复制结果”重试。");
            memory = 0; // The clipboard owns this allocation now.
        }
        finally { if (memory != 0) NativeMethods.GlobalFree(memory); }
    }
}
