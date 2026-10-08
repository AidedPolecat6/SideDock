using SideDock.Services;

namespace SideDock.Tests;

[TestClass]
public sealed class OutsideClickMonitorTests
{
    [DataTestMethod]
    [DataRow(0x0201)]
    [DataRow(0x0204)]
    [DataRow(0x0207)]
    [DataRow(0x020B)]
    public void Mouse_button_down_messages_trigger_outside_click_detection(int message)
    {
        Assert.IsTrue(OutsideClickMonitor.IsButtonDownMessage(message));
    }

    [TestMethod]
    public void Mouse_movement_does_not_trigger_outside_click_detection()
    {
        Assert.IsFalse(OutsideClickMonitor.IsButtonDownMessage(0x0200));
    }
}
