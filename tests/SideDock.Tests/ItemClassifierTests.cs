using SideDock.Models;
using SideDock.Services;

namespace SideDock.Tests;

[TestClass]
public sealed class ItemClassifierTests
{
    [TestMethod]
    public void Classify_recognizes_supported_item_types()
    {
        using var directory = new TempDirectory();
        var folder = System.IO.Path.Combine(directory.Path, "Folder");
        Directory.CreateDirectory(folder);

        Assert.AreEqual(DockItemKind.Folder, ItemClassifier.Classify(folder));
        Assert.AreEqual(DockItemKind.Application, ItemClassifier.Classify(@"C:\Tools\app.exe"));
        Assert.AreEqual(DockItemKind.Shortcut, ItemClassifier.Classify(@"C:\Tools\app.lnk"));
        Assert.AreEqual(DockItemKind.Website, ItemClassifier.Classify("https://example.com/path"));
        Assert.AreEqual(DockItemKind.File, ItemClassifier.Classify(@"C:\Docs\notes.txt"));
    }

    [TestMethod]
    public void GetLabel_removes_shortcut_extensions_and_simplifies_websites()
    {
        Assert.AreEqual("My Tool", ItemClassifier.GetLabel(@"C:\Tools\My Tool.lnk"));
        Assert.AreEqual("example.com", ItemClassifier.GetLabel("https://www.example.com/path"));
    }

    [TestMethod]
    public void Dock_item_recognizes_pdf_extension_case_insensitively()
    {
        Assert.IsTrue(new DockItem { Path = @"C:\Docs\Manual.PDF" }.IsPdf);
        Assert.IsFalse(new DockItem { Path = @"C:\Docs\Manual.docx" }.IsPdf);
    }
}
