using Promptlet.Core;
namespace Promptlet.Core.Tests;
public class AutoHideTests
{
    private sealed class Clock : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => _timestamp;
        public void Advance(int seconds) => _timestamp += seconds * 1000;
    }
    [Theory]
    [InlineData(OptimizationStatus.Copied)] [InlineData(OptimizationStatus.CopyBlocked)] [InlineData(OptimizationStatus.CopyFailed)]
    [InlineData(OptimizationStatus.Failed)] [InlineData(OptimizationStatus.Cancelled)]
    public void EveryVisibleTerminalStateGetsFullReadingPeriod(OptimizationStatus terminal)
    {
        var clock = new Clock(); var policy = new AutoHidePolicy(clock); var id = Guid.NewGuid();
        policy.Interact(); policy.Observe(new(Status: OptimizationStatus.Streaming, TaskId: id));
        clock.Advance(60); Assert.False(policy.ShouldHide(5, false));
        var final = new OptimizationState(Status: terminal, TaskId: id); policy.Observe(final);
        clock.Advance(4); Assert.False(policy.ShouldHide(5, false));
        policy.Observe(final); clock.Advance(1); Assert.True(policy.ShouldHide(5, false));
    }
    [Fact] public void EditingPausesAndZeroDisables()
    {
        var clock = new Clock(); var policy = new AutoHidePolicy(clock); policy.Interact();
        clock.Advance(30); Assert.False(policy.ShouldHide(5, true)); clock.Advance(4); Assert.False(policy.ShouldHide(5, false));
        clock.Advance(100); Assert.False(policy.ShouldHide(0, false));
        policy.Interact(); Assert.False(policy.ShouldHide(5, false));
    }
}
