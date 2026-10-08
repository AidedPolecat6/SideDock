using SideDock.Native;

namespace SideDock.Tests;

[TestClass]
public sealed class NativeMethodsTests
{
    [TestMethod]
    public void BuildToolWindowExtendedStyle_adds_tool_window_and_removes_app_window()
    {
        const long unrelatedStyle = 0x00080000L;
        const long appWindow = 0x00040000L;
        const long toolWindow = 0x00000080L;

        var result = NativeMethods.BuildToolWindowExtendedStyle(unrelatedStyle | appWindow);

        Assert.AreEqual(0, result & appWindow);
        Assert.AreEqual(toolWindow, result & toolWindow);
        Assert.AreEqual(unrelatedStyle, result & unrelatedStyle);
    }
}
