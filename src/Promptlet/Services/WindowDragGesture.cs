namespace Promptlet.Services;

// Screen-pixel coordinates stay stable while the window itself moves.
internal sealed class WindowDragGesture
{
    internal uint? PointerId { get; private set; }
    private int _pointerX, _pointerY, _windowX, _windowY, _thresholdX, _thresholdY;
    private bool _dragging;

    internal bool Begin(uint pointerId, bool leftButtonPressed, int pointerX, int pointerY,
        int windowX, int windowY, int thresholdX, int thresholdY)
    {
        if (!leftButtonPressed || PointerId is not null) return false;
        PointerId = pointerId;
        _pointerX = pointerX; _pointerY = pointerY; _windowX = windowX; _windowY = windowY;
        _thresholdX = Math.Max(1, thresholdX); _thresholdY = Math.Max(1, thresholdY);
        _dragging = false;
        return true;
    }

    internal (int X, int Y)? Move(uint pointerId, bool leftButtonPressed, int screenX, int screenY)
    {
        if (PointerId != pointerId) return null;
        if (!leftButtonPressed) { Reset(); return null; }
        var deltaX = screenX - _pointerX; var deltaY = screenY - _pointerY;
        if (!_dragging && Math.Abs(deltaX) < _thresholdX && Math.Abs(deltaY) < _thresholdY) return null;
        _dragging = true;
        return (_windowX + deltaX, _windowY + deltaY);
    }

    internal void Reset() { PointerId = null; _dragging = false; }
}
