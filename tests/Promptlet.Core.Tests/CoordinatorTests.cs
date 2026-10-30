using Promptlet.Core;

namespace Promptlet.Core.Tests;

public class CoordinatorTests
{
    private static readonly AppSettings Settings = new() { BaseUrl = "http://localhost:1234/v1", Model = "test" };

    [Fact]
    public async Task CompleteResultIsCopiedOnceAndSelfResultIsReused()
    {
        var clip = new FakeClipboard("原文");
        var model = new ControlledModel();
        var coordinator = new OptimizationCoordinator(clip, model);
        var task = coordinator.ActivateAsync(Settings);
        var call = await model.NextAsync();
        call.Progress("部分输出");
        Assert.Equal(0, clip.Writes);
        call.Complete("完整结果");
        await task;
        Assert.Equal("完整结果", clip.Text);
        Assert.Equal(1, clip.Writes);
        Assert.Equal(OptimizationStatus.Copied, coordinator.State.Status);
        await coordinator.ActivateAsync(Settings);
        Assert.Equal(1, model.Calls);
        Assert.Equal("原文", coordinator.State.Raw);
    }

    [Fact]
    public async Task CopyDuringRequestPreventsAutomaticOverwriteEvenIfTextIsSame()
    {
        var clip = new FakeClipboard("原文");
        var model = new ControlledModel();
        var coordinator = new OptimizationCoordinator(clip, model);
        var task = coordinator.ActivateAsync(Settings);
        var call = await model.NextAsync();
        clip.ExternalCopy("原文"); // Same text, different clipboard generation.
        call.Complete("结果");
        await task;
        Assert.Equal("原文", clip.Text);
        Assert.Equal(0, clip.Writes);
        Assert.Equal(OptimizationStatus.CopyBlocked, coordinator.State.Status);
        await coordinator.CopyResultAsync();
        Assert.Equal("结果", clip.Text);
        Assert.Equal(OptimizationStatus.Copied, coordinator.State.Status);
    }

    [Fact]
    public async Task CancelRejectsLateResponseEvenWhenModelIgnoresToken()
    {
        var clip = new FakeClipboard("原文");
        var model = new ControlledModel();
        var coordinator = new OptimizationCoordinator(clip, model);
        var task = coordinator.ActivateAsync(Settings);
        var call = await model.NextAsync();
        await coordinator.CancelAsync();
        call.Progress("迟到片段");
        call.Complete("迟到结果");
        await task;
        Assert.Equal(OptimizationStatus.Cancelled, coordinator.State.Status);
        Assert.Equal("原文", clip.Text);
        Assert.Equal(0, clip.Writes);
    }

    [Fact]
    public async Task ReplacementCannotBeOverwrittenByOldCompletion()
    {
        var clip = new FakeClipboard("原文");
        var model = new ControlledModel();
        var coordinator = new OptimizationCoordinator(clip, model);
        var oldTask = coordinator.ActivateAsync(Settings);
        var old = await model.NextAsync();
        var nextTask = coordinator.ReoptimizeAsync(Settings, "新原文");
        var next = await model.NextAsync();
        next.Complete("新结果");
        await nextTask;
        old.Progress("旧片段");
        old.Complete("旧结果");
        await oldTask;
        Assert.Equal("新结果", clip.Text);
        Assert.Equal("新原文", coordinator.State.Raw);
        Assert.Equal("新结果", coordinator.State.Result);
        Assert.Equal(1, clip.Writes);
    }

    [Fact]
    public async Task RepeatedActivationDoesNotReadAgainOrStartAnotherRequest()
    {
        var clip = new FakeClipboard("原文");
        var model = new ControlledModel();
        var coordinator = new OptimizationCoordinator(clip, model);
        var task = coordinator.ActivateAsync(Settings);
        var call = await model.NextAsync();
        await coordinator.ActivateAsync(Settings);
        Assert.Equal(1, clip.Reads);
        Assert.Equal(1, model.Calls);
        call.Complete("结果");
        await task;
    }

    [Fact]
    public async Task FailedClipboardCommitKeepsResultAndCanBeRetried()
    {
        var clip = new FakeClipboard("原文") { FailWrites = true };
        var model = new ControlledModel();
        var coordinator = new OptimizationCoordinator(clip, model);
        var task = coordinator.ActivateAsync(Settings);
        (await model.NextAsync()).Complete("结果");
        await task;
        Assert.Equal(OptimizationStatus.CopyFailed, coordinator.State.Status);
        Assert.Equal("结果", coordinator.State.Result);
        clip.FailWrites = false;
        await coordinator.CopyResultAsync();
        Assert.Equal("结果", clip.Text);
    }

    [Fact]
    public async Task FailureWithPartialTextNeverCopies()
    {
        var clip = new FakeClipboard("原文");
        var model = new ControlledModel();
        var coordinator = new OptimizationCoordinator(clip, model);
        var task = coordinator.ActivateAsync(Settings);
        var call = await model.NextAsync();
        call.Progress("不完整");
        call.Fail(new PromptletException("异常断流"));
        await task;
        Assert.Equal(OptimizationStatus.Failed, coordinator.State.Status);
        await coordinator.CopyResultAsync();
        Assert.Equal(0, clip.Writes);
    }

    [Fact]
    public async Task StoppingRejectsFutureTasksAndLateResponses()
    {
        var clip = new FakeClipboard("原文");
        var model = new ControlledModel();
        var coordinator = new OptimizationCoordinator(clip, model);
        var task = coordinator.ActivateAsync(Settings);
        var call = await model.NextAsync();
        await coordinator.CancelAsync(stopping: true);
        call.Complete("迟到结果");
        await task;
        await coordinator.ActivateAsync(Settings);
        Assert.Equal(0, clip.Writes);
        Assert.Equal(1, model.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   \n")]
    public async Task NonTextOrEmptyClipboardDoesNotCallModel(string? text)
    {
        var model = new ControlledModel();
        var coordinator = new OptimizationCoordinator(new FakeClipboard(text), model);
        await coordinator.ActivateAsync(Settings);
        Assert.Equal(0, model.Calls);
        Assert.Equal(OptimizationStatus.Failed, coordinator.State.Status);
    }

    [Fact]
    public async Task InputLimitCountsUnicodeScalarsRatherThanUtf16Units()
    {
        var model = new ControlledModel();
        var coordinator = new OptimizationCoordinator(new FakeClipboard(string.Concat(Enumerable.Repeat("🌱", 20_000))), model);
        var task = coordinator.ActivateAsync(Settings);
        (await model.NextAsync()).Complete("结果");
        await task;
        Assert.Equal(OptimizationStatus.Copied, coordinator.State.Status);
        var oversized = new OptimizationCoordinator(new FakeClipboard(new string('字', 20_001)), model);
        await oversized.ActivateAsync(Settings);
        Assert.Equal(OptimizationStatus.Failed, oversized.State.Status);
        Assert.Equal(1, model.Calls);
    }

    private sealed class FakeClipboard(string? text) : IClipboardService
    {
        public string? Text { get; private set; } = text;
        private uint _version = 1;
        public int Writes { get; private set; }
        public int Reads { get; private set; }
        public bool FailWrites { get; set; }
        public void ExternalCopy(string value) { Text = value; _version++; }
        public Task<ClipboardSnapshot> ReadAsync(CancellationToken token) { Reads++; return Task.FromResult(new ClipboardSnapshot(Text, _version)); }
        public Task<ClipboardWriteResult> TryWriteAsync(string value, ClipboardSnapshot expected, CancellationToken token)
        {
            if (FailWrites) throw new PromptletException("复制失败");
            if (expected.Version != _version || expected.Text != Text) return Task.FromResult(ClipboardWriteResult.Changed);
            ExternalCopy(value); Writes++;
            return Task.FromResult(ClipboardWriteResult.Written);
        }
        public Task WriteAsync(string value, CancellationToken token)
        {
            if (FailWrites) throw new PromptletException("复制失败");
            ExternalCopy(value); Writes++;
            return Task.CompletedTask;
        }
    }

    private sealed class ControlledModel : IOptimizationService
    {
        private readonly System.Threading.Channels.Channel<Call> _calls = System.Threading.Channels.Channel.CreateUnbounded<Call>();
        public int Calls { get; private set; }
        public Task<string> OptimizeAsync(AppSettings settings, string input, Action<string> onText, CancellationToken token)
        {
            Calls++;
            var call = new Call(onText);
            _calls.Writer.TryWrite(call);
            return call.Completion.Task; // Deliberately ignores cancellation to exercise stale-result guards.
        }
        public async Task<Call> NextAsync() => await _calls.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class Call(Action<string> progress)
    {
        public TaskCompletionSource<string> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Progress(string text) => progress(text);
        public void Complete(string text) => Completion.SetResult(text);
        public void Fail(Exception exception) => Completion.SetException(exception);
    }
}
