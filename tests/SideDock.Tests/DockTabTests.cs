using SideDock.Models;

namespace SideDock.Tests;

[TestClass]
public sealed class DockTabTests
{
    [TestMethod]
    public void Changing_name_notifies_the_tab_header()
    {
        var tab = new DockTab();
        string? changedProperty = null;
        tab.PropertyChanged += (_, args) => changedProperty = args.PropertyName;

        tab.Name = "Projects";

        Assert.AreEqual(nameof(DockTab.Name), changedProperty);
        Assert.AreEqual("Projects", tab.Name);
    }

    [TestMethod]
    public void Changing_layout_notifies_the_tab_content()
    {
        var tab = new DockTab();
        string? changedProperty = null;
        tab.PropertyChanged += (_, args) => changedProperty = args.PropertyName;

        tab.UseFullWidthRows = true;

        Assert.AreEqual(nameof(DockTab.UseFullWidthRows), changedProperty);
        Assert.IsTrue(tab.UseFullWidthRows);
    }
}
