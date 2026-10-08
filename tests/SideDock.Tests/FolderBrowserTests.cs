using SideDock.Models;
using SideDock.Services;

namespace SideDock.Tests;

[TestClass]
public sealed class FolderBrowserTests
{
    [TestMethod]
    public void Enumerate_returns_only_one_level_with_folders_first()
    {
        using var directory = new TempDirectory();
        var subfolder = System.IO.Path.Combine(directory.Path, "Subfolder");
        Directory.CreateDirectory(subfolder);
        File.WriteAllText(System.IO.Path.Combine(subfolder, "deep.txt"), "deep");
        var file = System.IO.Path.Combine(directory.Path, "top.txt");
        File.WriteAllText(file, "top");
        var browser = new FolderBrowser(new FakeShortcutResolver());

        var entries = browser.Enumerate(directory.Path);

        Assert.AreEqual(2, entries.Count);
        Assert.IsTrue(entries[0].IsFolder);
        Assert.AreEqual(subfolder, entries[0].Path);
        Assert.AreEqual(file, entries[1].Path);
    }

    [TestMethod]
    public void Enumerate_resolves_shortcuts_to_folders_without_scanning_them()
    {
        using var directory = new TempDirectory();
        var target = System.IO.Path.Combine(directory.Path, "Target");
        Directory.CreateDirectory(target);
        File.WriteAllText(System.IO.Path.Combine(target, "deep.txt"), "deep");
        var shortcut = System.IO.Path.Combine(directory.Path, "Target.lnk");
        File.WriteAllText(shortcut, "");
        var browser = new FolderBrowser(new FakeShortcutResolver(target));

        var entries = browser.Enumerate(directory.Path);

        var entry = entries.Single(item => item.Path == shortcut);
        Assert.IsTrue(entry.IsFolder);
        Assert.AreEqual(DockItemKind.Folder, entry.Kind);
        Assert.AreEqual(target, entry.BrowsePath);
    }

    private sealed class FakeShortcutResolver(string? target = null) : IShortcutResolver
    {
        public string? ResolveTarget(string shortcutPath) => target;
    }
}
