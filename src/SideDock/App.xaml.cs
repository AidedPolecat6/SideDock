using System.Collections.ObjectModel;
using System.Windows;

using SideDock.Models;
using SideDock.Services;

namespace SideDock;

public partial class App : Application
{
    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstance = new Mutex(true, "Local\\SideDock.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            Shutdown();
            return;
        }

        try
        {
            Directory.CreateDirectory(ShortcutImporter.DefaultFolderPath);
            var store = new ConfigurationStore();
            var configuration = store.Load() ?? CreateFirstRunConfiguration();
            configuration.StartWithWindows = StartupService.IsEnabled();
            var shortcutResolver = new ShortcutResolver();
            var tabFolderService = new TabFolderService(shortcutResolver, new ShortcutWriter());
            tabFolderService.Initialize(configuration);
            store.Save(configuration);

            var window = new MainWindow(
                configuration,
                store,
                new FolderBrowser(shortcutResolver),
                tabFolderService);
            MainWindow = window;
            window.Show();
        }
        catch (Exception exception)
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SideDock");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "startup-error.txt"), exception.ToString());
            MessageBox.Show(exception.Message, "SideDock could not start", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private static SideDockConfiguration CreateFirstRunConfiguration()
    {
        var importer = new ShortcutImporter(new ShortcutResolver());
        var importedItems = importer.Import(ShortcutImporter.DefaultFolderPath);
        var tab = new DockTab
        {
            Name = importedItems.Count > 0 ? "Shortcuts" : "Favorites",
            Items = new ObservableCollection<DockItem>(importedItems)
        };
        return new SideDockConfiguration
        {
            Tabs = [tab]
        };
    }
}
