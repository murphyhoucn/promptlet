namespace Promptlet.Core;

public sealed class AutoHidePolicy(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private long _lastInteraction;
    private bool _busy;
    private Guid _task;
    private OptimizationStatus _status;
    public void Interact() => _lastInteraction = _clock.GetTimestamp();
    public void Observe(OptimizationState state)
    {
        if ((_task != state.TaskId || _status != state.Status) && !state.IsBusy) Interact();
        _task = state.TaskId;
        _status = state.Status;
        _busy = state.IsBusy;
    }
    public bool ShouldHide(int seconds, bool paused)
    {
        if (paused) Interact();
        return seconds > 0 && !_busy && !paused && _clock.GetElapsedTime(_lastInteraction).TotalSeconds >= seconds;
    }
}
