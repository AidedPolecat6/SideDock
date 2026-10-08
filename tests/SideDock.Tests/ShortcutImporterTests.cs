using SideDock.Models;
using SideDock.Services;

namespace SideDock.Tests;

[TestClass]
public sealed class ShortcutImporterTests
{
    [TestMethod]
    public void ShortcutWriter_creates_a_resolvable_folder_link()
    {
        using var directory = new TempDirectory();
        var target = System.IO.Path.Combine(directory.Path, "Target");
        Directory.CreateDirectory(target);
        var shortcut = System.IO.Path.Combine(directory.Path, "Target.lnk");

        new ShortcutWriter().Create(target, shortcut);

        Assert.IsTrue(File.Exists(shortcut));
        Assert.AreEqual(target, new ShortcutResolver().ResolveTarget(shortcut));
    }

    [TestMethod]
    public void Import_turns_folder_shortcuts_into_browsable_tiles()
    {
        using var directory = new TempDirectory();
        var target = System.IO.Path.Combine(directory.Path, "Target");
        Directory.CreateDirectory(target);
        var shortcut = System.IO.Path.Combine(directory.Path, "Documents.lnk");
        File.WriteAllText(shortcut, "");
        var importer = new ShortcutImporter(new FakeShortcutResolver(_ => target));

        var items = importer.Import(directory.Path);

        Assert.AreEqual(2, items.Count);
        var item = items.Single(item => item.Path == shortcut);
        Assert.AreEqual("Documents", item.Label);
        Assert.AreEqual(DockItemKind.Folder, item.Kind);
        Assert.AreEqual(target, item.BrowsePath);
    }

    [TestMethod]
    public void Import_marks_shortcuts_to_pdf_files()
    {
        using var directory = new TempDirectory();
        var shortcut = System.IO.Path.Combine(directory.Path, "Manual.lnk");
        File.WriteAllText(shortcut, "");
        var importer = new ShortcutImporter(new FakeShortcutResolver(_ => @"C:\Docs\Manual.PDF"));

        var item = importer.Import(directory.Path).Single();

        Assert.IsTrue(item.IsPdf);
        Assert.AreEqual(DockItemKind.Shortcut, item.Kind);
    }

    private sealed class FakeShortcutResolver(Func<string, string?> resolver) : IShortcutResolver
    {
        public string? ResolveTarget(string shortcutPath) => resolver(shortcutPath);
    }
}
