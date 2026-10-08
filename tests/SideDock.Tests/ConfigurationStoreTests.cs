using SideDock.Models;
using SideDock.Services;

namespace SideDock.Tests;

[TestClass]
public sealed class ConfigurationStoreTests
{
    [TestMethod]
    public void Save_and_load_round_trips_tabs_and_items()
    {
        using var directory = new TempDirectory();
        var store = new ConfigurationStore(directory.Path);
        var configuration = new SideDockConfiguration
        {
            Tabs =
            [
                new DockTab
                {
                    Name = "Work",
                    FolderPath = @"C:\Tabs\Work",
                    UseFullWidthRows = true,
                    Items = [new DockItem { Label = "Tool", Path = @"C:\Tool.exe", Kind = DockItemKind.Application }]
                }
            ]
        };

        store.Save(configuration);
        var loaded = store.Load();

        Assert.IsNotNull(loaded);
        Assert.AreEqual("Work", loaded.Tabs[0].Name);
        Assert.AreEqual(@"C:\Tabs\Work", loaded.Tabs[0].FolderPath);
        Assert.IsTrue(loaded.Tabs[0].UseFullWidthRows);
        Assert.AreEqual("Tool", loaded.Tabs[0].Items[0].Label);
        Assert.AreEqual(DockItemKind.Application, loaded.Tabs[0].Items[0].Kind);
    }

    [TestMethod]
    public void Load_uses_backup_when_current_configuration_is_corrupt()
    {
        using var directory = new TempDirectory();
        var store = new ConfigurationStore(directory.Path);
        var configuration = new SideDockConfiguration { Tabs = [new DockTab { Name = "Original" }] };
        store.Save(configuration);
        configuration.Tabs[0].Name = "Current";
        store.Save(configuration);
        File.WriteAllText(store.ConfigurationPath, "not json");

        var loaded = store.Load();

        Assert.IsNotNull(loaded);
        Assert.AreEqual("Original", loaded.Tabs[0].Name);
    }

    [TestMethod]
    public void Load_migrates_the_original_handle_to_the_labeled_width()
    {
        using var directory = new TempDirectory();
        var store = new ConfigurationStore(directory.Path);
        File.WriteAllText(store.ConfigurationPath, """
            {
              "SchemaVersion": 1,
              "HandleWidth": 24,
              "Tabs": []
            }
            """);

        var loaded = store.Load();

        Assert.IsNotNull(loaded);
        Assert.AreEqual(6, loaded.SchemaVersion);
        Assert.AreEqual(28, loaded.HandleWidth);
        Assert.AreEqual(280, loaded.AnimationMilliseconds);
    }

    [TestMethod]
    public void Load_migrates_the_original_animation_timing()
    {
        using var directory = new TempDirectory();
        var store = new ConfigurationStore(directory.Path);
        File.WriteAllText(store.ConfigurationPath, """
            {
              "SchemaVersion": 3,
              "HandleWidth": 28,
              "AnimationMilliseconds": 220,
              "Tabs": []
            }
            """);

        var loaded = store.Load();

        Assert.IsNotNull(loaded);
        Assert.AreEqual(6, loaded.SchemaVersion);
        Assert.AreEqual(280, loaded.AnimationMilliseconds);
    }

    [TestMethod]
    public void Load_preserves_custom_animation_timing_during_migration()
    {
        using var directory = new TempDirectory();
        var store = new ConfigurationStore(directory.Path);
        File.WriteAllText(store.ConfigurationPath, """
            {
              "SchemaVersion": 3,
              "HandleWidth": 28,
              "AnimationMilliseconds": 350,
              "Tabs": []
            }
            """);

        var loaded = store.Load();

        Assert.IsNotNull(loaded);
        Assert.AreEqual(6, loaded.SchemaVersion);
        Assert.AreEqual(350, loaded.AnimationMilliseconds);
    }
}
