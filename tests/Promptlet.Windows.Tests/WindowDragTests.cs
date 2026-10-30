using Promptlet.Services;

namespace Promptlet.Windows.Tests;

public class WindowDragTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, -3)]
    public void ClickAndSmallJitterDoNotMoveWindow(int x, int y)
    {
        var drag = new WindowDragGesture();
        Assert.True(drag.Begin(1, true, 0, 0, 100, 200, 4, 4));
        Assert.Null(drag.Move(1, true, x, y));
        Assert.Null(drag.Move(1, false, 80, 90));
        Assert.Null(drag.Move(1, false, 180, 190));
        Assert.Null(drag.PointerId);
    }

    [Theory]
    [InlineData(4, 0)]
    [InlineData(0, -4)]
    [InlineData(-40, 50)]
    public void HeldPointerMovesWindowAfterThreshold(int x, int y)
    {
        var drag = new WindowDragGesture();
        drag.Begin(1, true, 10, 20, 100, 200, 4, 4);
        Assert.Equal((100 + x, 200 + y), drag.Move(1, true, 10 + x, 20 + y)!.Value);
        // Returning inside the original threshold must keep tracking after a drag starts.
        Assert.Equal((101, 202), drag.Move(1, true, 11, 22)!.Value);
    }

    [Fact]
    public void ReleaseStopsDragEvenIfReleaseEventWasMissed()
    {
        var drag = new WindowDragGesture();
        drag.Begin(1, true, 0, 0, -300, 200, 4, 4);
        Assert.Equal((-280, 230), drag.Move(1, true, 20, 30)!.Value);
        Assert.Null(drag.Move(1, false, 40, 50));
        Assert.Null(drag.Move(1, true, 80, 90)); // A new down is required, not just motion.
        Assert.Null(drag.PointerId);
    }

    [Fact]
    public void CancellationDoesNotLeaveAStaleOriginForNextDrag()
    {
        var drag = new WindowDragGesture();
        drag.Begin(1, true, 50, 50, 100, 100, 4, 4);
        drag.Move(1, true, 100, 100);
        drag.Reset();
        Assert.Null(drag.Move(1, true, 500, 500));
        Assert.True(drag.Begin(1, true, 500, 500, 300, -200, 8, 8));
        Assert.Null(drag.Move(1, true, 507, 507));
        Assert.Equal((310, -190), drag.Move(1, true, 510, 510)!.Value);
    }

    [Fact]
    public void OtherPointerCannotStartReplaceOrEndMouseDrag()
    {
        var drag = new WindowDragGesture();
        Assert.False(drag.Begin(1, false, 0, 0, 0, 0, 4, 4));
        Assert.True(drag.Begin(1, true, 0, 0, 0, 0, 4, 4));
        Assert.False(drag.Begin(2, true, 30, 30, 0, 0, 4, 4));
        Assert.Null(drag.Move(2, false, 30, 30));
        Assert.Equal((20, 30), drag.Move(1, true, 20, 30)!.Value);
    }
}
