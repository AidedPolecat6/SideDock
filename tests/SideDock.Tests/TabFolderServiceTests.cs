using SideDock.Models;
using SideDock.Services;

namespace SideDock.Tests;

[TestClass]
public sealed class TabFolderServiceTests
{
    [TestMethod]
    public void Initialize_creates_tab_folders_and_moves_legacy_shortcuts()
    {
        using var directory = new TempDirectory();
        var shortcut = Path.Combine(directory.Path, "Documents.lnk");
        File.WriteAllText(shortcut, "shortcut");
        var tab = new DockTab
        {
            Name = "Shortcuts",
            Items = [new DockItem { Label = "Documents", Path = shortcut, Kind = DockItemKind.Shortcut }]
        };
        var configuration = new SideDockConfiguration { Tabs = [tab] };
        var service = CreateService(directory.Path);

        service.Initialize(configuration);

        Assert.AreEqual(Path.Combine(directory.Path, "Shortcuts"), tab.FolderPath);
        Assert.IsTrue(File.Exists(Path.Combine(tab.FolderPath, "Documents.lnk")));
        Assert.IsFalse(File.Exists(shortcut));
        Assert.AreEqual(Path.Combine(tab.FolderPath, "Documents.lnk"), tab.Items[0].Path);
    }

    [TestMethod]
    public void AddReference_creates_a_backing_shortcut_without_moving_the_target()
    {
        using var directory = new TempDirectory();
        var target = Path.Combine(directory.Path, "Target");
        Directory.CreateDirectory(target);
        var writer = new FakeShortcutWriter();
        var resolver = new FakeShortcutResolver(path => writer.Targets.GetValueOrDefault(path));
        var service = new TabFolderService(resolver, writer, directory.Path);
        var tab = new DockTab { Name = "Work", FolderPath = service.CreateFolder("Work") };

        var item = service.AddReference(tab, target);

        Assert.IsTrue(Directory.Exists(target));
        Assert.IsTrue(File.Exists(item.Path));
        Assert.AreEqual(target, item.BrowsePath);
        Assert.AreEqual(DockItemKind.Folder, item.Kind);
    }

    [TestMethod]
    public void Sync_reflects_external_additions_and_removals()
    {
        using var directory = new TempDirectory();
        var service = CreateService(directory.Path);
        var folder = service.CreateFolder("Work");
        var tab = new DockTab { Name = "Work", FolderPath = folder };
        var shortcut = Path.Combine(folder, "Tool.lnk");
        File.WriteAllText(shortcut, "shortcut");

        Assert.IsTrue(service.Sync(tab));
        Assert.AreEqual("Tool", tab.Items.Single().Label);

        File.Delete(shortcut);
        Assert.IsTrue(service.Sync(tab));
        Assert.AreEqual(0, tab.Items.Count);
    }

    [TestMethod]
    public void RenameFolder_moves_the_folder_and_updates_item_paths()
    {
        using var directory = new TempDirectory();
        var service = CreateService(directory.Path);
        var folder = service.CreateFolder("Work");
        var shortcut = Path.Combine(folder, "Tool.lnk");
        File.WriteAllText(shortcut, "shortcut");
        var tab = new DockTab
        {
            Name = "Work",
            FolderPath = folder,
            Items = [new DockItem { Label = "Tool", Path = shortcut }]
        };

        service.RenameFolder(tab, "Projects");

        Assert.AreEqual(Path.Combine(directory.Path, "Projects"), tab.FolderPath);
        Assert.AreEqual(Path.Combine(tab.FolderPath, "Tool.lnk"), tab.Items[0].Path);
        Assert.IsTrue(File.Exists(tab.Items[0].Path));
        Assert.IsFalse(Directory.Exists(folder));
    }

    [TestMethod]
    public void RemoveReference_deletes_only_the_backing_shortcut()
    {
        using var directory = new TempDirectory();
        var target = Path.Combine(directory.Path, "Target");
        Directory.CreateDirectory(target);
        var writer = new FakeShortcutWriter();
        var resolver = new FakeShortcutResolver(path => writer.Targets.GetValueOrDefault(path));
        var service = new TabFolderService(resolver, writer, directory.Path);
        var tab = new DockTab { Name = "Work", FolderPath = service.CreateFolder("Work") };
        var item = service.AddReference(tab, target);

        service.RemoveReference(tab, item);

        Assert.IsFalse(File.Exists(item.Path));
        Assert.IsTrue(Directory.Exists(target));
        Assert.AreEqual(0, tab.Items.Count);
    }

    [TestMethod]
    public void Root_folder_shortcut_creates_a_read_only_tab_with_direct_children()
    {
        using var directory = new TempDirectory();
        var root = Path.Combine(directory.Path, "Root");
        var target = Path.Combine(directory.Path, "Books");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "Guide.pdf"), "guide");
        var nested = Path.Combine(target, "Nested");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "Deep.txt"), "deep");
        var descriptor = Path.Combine(root, "Books.lnk");
        File.WriteAllText(descriptor, "shortcut");
        var resolver = new FakeShortcutResolver(path => path == descriptor ? target : null);
        var service = new TabFolderService(resolver, new FakeShortcutWriter(), root);
        var configuration = new SideDockConfiguration();

        service.Initialize(configuration);

        var tab = configuration.Tabs.Single();
        Assert.AreEqual("Books", tab.Name);
        Assert.AreEqual(target, tab.FolderPath);
        Assert.AreEqual(descriptor, tab.SourceShortcutPath);
        Assert.IsTrue(tab.IsLinkedFolder);
        CollectionAssert.AreEquivalent(new[] { "Guide.pdf", "Nested" }, tab.Items.Select(item => item.Label).ToArray());
        Assert.IsFalse(tab.Items.Any(item => item.Label == "Deep.txt"));
        Assert.IsTrue(tab.Items.All(item => item.IsReadOnly));
        Assert.IsTrue(File.Exists(descriptor));
    }

    [TestMethod]
    public void Initialize_does_not_move_unassigned_root_shortcuts_after_migration()
    {
        using var directory = new TempDirectory();
        var rootShortcut = Path.Combine(directory.Path, "Loose.lnk");
        File.WriteAllText(rootShortcut, "shortcut");
        var managedFolder = Path.Combine(directory.Path, "Shortcuts");
        Directory.CreateDirectory(managedFolder);
        var configuration = new SideDockConfiguration
        {
            Tabs = [new DockTab { Name = "Shortcuts", FolderPath = managedFolder }]
        };

        CreateService(directory.Path).Initialize(configuration);

        Assert.IsTrue(File.Exists(rootShortcut));
        Assert.AreEqual(0, configuration.Tabs.Single().Items.Count);
    }

    [TestMethod]
    public void Linked_tabs_reject_item_mutation()
    {
        using var directory = new TempDirectory();
        var target = Path.Combine(directory.Path, "Target");
        Directory.CreateDirectory(target);
        var file = Path.Combine(target, "Keep.txt");
        File.WriteAllText(file, "keep");
        var tab = new DockTab
        {
            Name = "Linked",
            FolderPath = target,
            SourceShortcutPath = Path.Combine(directory.Path, "Linked.lnk"),
            Items = [new DockItem { Label = "Keep.txt", Path = file }]
        };
        var service = CreateService(directory.Path);

        Assert.ThrowsException<InvalidOperationException>(() => service.AddReference(tab, file));
        Assert.ThrowsException<InvalidOperationException>(() => service.RemoveReference(tab, tab.Items[0]));
        Assert.IsTrue(File.Exists(file));
    }

    [TestMethod]
    public void Refresh_removes_a_linked_tab_when_its_descriptor_is_deleted()
    {
        using var directory = new TempDirectory();
        var target = Path.Combine(directory.Path, "Target");
        Directory.CreateDirectory(target);
        var descriptor = Path.Combine(directory.Path, "Linked.lnk");
        File.WriteAllText(descriptor, "shortcut");
        var resolver = new FakeShortcutResolver(path => path == descriptor ? target : null);
        var service = new TabFolderService(resolver, new FakeShortcutWriter(), directory.Path);
        var configuration = new SideDockConfiguration();
        service.Initialize(configuration);

        File.Delete(descriptor);
        Assert.IsTrue(service.Refresh(configuration));

        Assert.AreEqual(0, configuration.Tabs.Count);
        Assert.IsTrue(Directory.Exists(target));
    }

    [TestMethod]
    public void RenameFolder_renames_only_a_linked_tabs_descriptor()
    {
        using var directory = new TempDirectory();
        var target = Path.Combine(directory.Path, "Target");
        Directory.CreateDirectory(target);
        var descriptor = Path.Combine(directory.Path, "Linked.lnk");
        File.WriteAllText(descriptor, "shortcut");
        var tab = new DockTab { Name = "Linked", FolderPath = target, SourceShortcutPath = descriptor };
        var service = CreateService(directory.Path);

        service.RenameFolder(tab, "Renamed");

        Assert.AreEqual(Path.Combine(directory.Path, "Renamed.lnk"), tab.SourceShortcutPath);
        Assert.IsTrue(File.Exists(tab.SourceShortcutPath));
        Assert.IsFalse(File.Exists(descriptor));
        Assert.IsTrue(Directory.Exists(target));
    }

    private static TabFolderService CreateService(string root)
    {
        return new TabFolderService(new FakeShortcutResolver(_ => null), new FakeShortcutWriter(), root);
    }

    private sealed class FakeShortcutResolver(Func<string, string?> resolver) : IShortcutResolver
    {
        public string? ResolveTarget(string shortcutPath) => resolver(shortcutPath);
    }

    private sealed class FakeShortcutWriter : IShortcutWriter
    {
        internal Dictionary<string, string> Targets { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void Create(string target, string destination)
        {
            File.WriteAllText(destination, target);
            Targets[destination] = target;
        }
    }
}
