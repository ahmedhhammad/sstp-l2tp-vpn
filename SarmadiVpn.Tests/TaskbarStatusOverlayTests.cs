using SarmadiVpn;

namespace SarmadiVpn.Tests;

public sealed class TaskbarStatusOverlayTests
{
    [Fact]
    public void TaskbarListComInterfaceCanBeQueried()
    {
        TaskbarStatusOverlay.VerifyTaskbarInterfaceCanBeQueried();
    }
}
