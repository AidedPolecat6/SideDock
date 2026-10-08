using SideDock.Models;

namespace SideDock.Services;

internal sealed class TabFolderService
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    private readonly ShortcutImporter _importer;
    private readonly IShortcutResolver _shortcutResolver;
    private readonly IShortcutWriter _shortcutWriter;

    internal TabFolderService(
        IShortcutResolver shortcutResolver,
        IShortcutWriter shortcutWriter,
        string? rootPath = null)
    {
        RootPath = rootPath ?? ShortcutImporter.DefaultFolderPath;
        _shortcutResolver = shortcutResolver;
        _importer = new ShortcutImporter(shortcutResolver);
        _shortcutWriter = shortcutWriter;
    }

    internal string RootPath { get; }

    internal void Initialize(SideDockConfiguration configuration)
    {
        Directory.CreateDirectory(RootPath);
        var descriptors = EnumerateLinkedFolderDescriptors();
        var descriptorPaths = descriptors.Select(descriptor => descriptor.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var migrationTabs = configuration.Tabs
            .Where(tab => !tab.IsLinkedFolder && string.IsNullOrWhiteSpace(tab.FolderPath))
            .ToList();

        DiscoverLinkedTabs(configuration, descriptors);
        var assignedPaths = configuration.Tabs
            .Where(tab => !tab.IsLinkedFolder && !string.IsNullOrWhiteSpace(tab.FolderPath))
            .Select(tab => Path.GetFullPath(tab.FolderPath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var tab in migrationTabs)
        {
            tab.FolderPath = GetAvailableFolderPath(tab.Name, assignedPaths);
            assignedPaths.Add(Path.GetFullPath(tab.FolderPath));
        }

        foreach (var tab in configuration.Tabs.Where(tab => !tab.IsLinkedFolder))
        {
            Directory.CreateDirectory(tab.FolderPath);
        }

        foreach (var tab in migrationTabs)
        {
            foreach (var item in tab.Items.ToList())
            {
                if (descriptorPaths.Contains(Path.GetFullPath(item.Path)))
                {
                    tab.Items.Remove(item);
                }
                else if (File.Exists(item.Path) && IsDirectChild(item.Path, RootPath))
                {
                    item.Path = MoveIntoTabFolder(item.Path, tab);
                }
            }
        }

        foreach (var tab in configuration.Tabs.Where(tab => !tab.IsLinkedFolder))
        {
            foreach (var item in tab.Items.ToList())
            {
                if (IsDirectChild(item.Path, tab.FolderPath)) continue;
                item.Path = CreateReference(tab, item.Path, item.Label);
            }
        }
        SyncAll(configuration.Tabs);
    }

    internal bool Refresh(SideDockConfiguration configuration)
    {
        var changed = DiscoverLinkedTabs(configuration, EnumerateLinkedFolderDescriptors());
        return SyncAll(configuration.Tabs) || changed;
    }

    internal string CreateFolder(string name)
    {
        ValidateFolderName(name);
        var path = Path.Combine(RootPath, name);
        if (Directory.Exists(path) || File.Exists(path))
        {
            throw new IOException($"A tab folder named '{name}' already exists");
        }
        Directory.CreateDirectory(path);
        return path;
    }

    internal void RenameFolder(DockTab tab, string newName)
    {
        ValidateFolderName(newName);
        if (tab.IsLinkedFolder)
        {
            var destination = Path.Combine(RootPath, newName + ".lnk");
            if (Path.GetFullPath(destination).Equals(Path.GetFullPath(tab.SourceShortcutPath!), StringComparison.OrdinalIgnoreCase)) return;
            if (File.Exists(destination) || Directory.Exists(destination))
            {
                throw new IOException($"A linked tab named '{newName}' already exists");
            }
            File.Move(tab.SourceShortcutPath!, destination);
            tab.SourceShortcutPath = destination;
            return;
        }

        var folderDestination = Path.Combine(RootPath, newName);
        if (Path.GetFullPath(folderDestination).Equals(Path.GetFullPath(tab.FolderPath), StringComparison.OrdinalIgnoreCase)) return;
        if (Directory.Exists(folderDestination) || File.Exists(folderDestination))
        {
            throw new IOException($"A tab folder named '{newName}' already exists");
        }

        Directory.Move(tab.FolderPath, folderDestination);
        tab.FolderPath = folderDestination;
        foreach (var item in tab.Items)
        {
            item.Path = Path.Combine(folderDestination, Path.GetFileName(item.Path));
        }
    }

    internal DockItem AddReference(DockTab tab, string target)
    {
        EnsureManaged(tab);
        var existing = tab.Items.FirstOrDefault(item => GetTarget(item).Equals(target, StringComparison.OrdinalIgnoreCase));
        if (existing != null) return existing;
        var path = CreateReference(tab, target, ItemClassifier.GetLabel(target));
        Sync(tab);
        return tab.Items.First(item => item.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
    }

    internal void RemoveReference(DockTab tab, DockItem item)
    {
        EnsureManaged(tab);
        if (IsDirectChild(item.Path, tab.FolderPath) && File.Exists(item.Path))
        {
            File.Delete(item.Path);
        }
        tab.Items.Remove(item);
    }

    internal void DeleteFolder(DockTab tab)
    {
        if (tab.IsLinkedFolder)
        {
            if (File.Exists(tab.SourceShortcutPath)) File.Delete(tab.SourceShortcutPath);
        }
        else if (Directory.Exists(tab.FolderPath))
        {
            Directory.Delete(tab.FolderPath, true);
        }
    }

    internal bool Sync(DockTab tab)
    {
        if (tab.IsLinkedFolder && !Directory.Exists(tab.FolderPath)) return false;
        if (!tab.IsLinkedFolder) Directory.CreateDirectory(tab.FolderPath);

        var imported = _importer.Import(tab.FolderPath);
        foreach (var item in imported)
        {
            item.UseModernFolderIcon = !tab.IsLinkedFolder && item.Kind == DockItemKind.Folder;
            item.IsReadOnly = tab.IsLinkedFolder;
        }
        var importedByPath = imported.ToDictionary(item => Path.GetFullPath(item.Path), StringComparer.OrdinalIgnoreCase);
        var changed = false;

        for (var index = tab.Items.Count - 1; index >= 0; index--)
        {
            var existing = tab.Items[index];
            if (!importedByPath.Remove(Path.GetFullPath(existing.Path), out var current))
            {
                tab.Items.RemoveAt(index);
                changed = true;
                continue;
            }
            existing.Label = current.Label;
            existing.BrowsePath = current.BrowsePath;
            existing.Kind = current.Kind;
            existing.UseModernFolderIcon = current.UseModernFolderIcon;
            existing.IsReadOnly = current.IsReadOnly;
            existing.IsPdf = current.IsPdf;
        }

        foreach (var item in imported.Where(item => importedByPath.ContainsKey(Path.GetFullPath(item.Path))))
        {
            tab.Items.Add(item);
            changed = true;
        }
        return changed;
    }

    internal bool SyncAll(IEnumerable<DockTab> tabs)
    {
        var changed = false;
        foreach (var tab in tabs) changed = Sync(tab) || changed;
        return changed;
    }

    private bool DiscoverLinkedTabs(SideDockConfiguration configuration, IReadOnlyList<LinkedFolderDescriptor> descriptors)
    {
        var changed = false;
        var matchedTabs = new HashSet<Guid>();
        foreach (var descriptor in descriptors)
        {
            var tab = configuration.Tabs.FirstOrDefault(candidate =>
                candidate.IsLinkedFolder
                && candidate.SourceShortcutPath!.Equals(descriptor.Path, StringComparison.OrdinalIgnoreCase));
            tab ??= configuration.Tabs.FirstOrDefault(candidate =>
                candidate.IsLinkedFolder
                && !matchedTabs.Contains(candidate.Id)
                && candidate.FolderPath.Equals(descriptor.Target, StringComparison.OrdinalIgnoreCase));

            if (tab == null)
            {
                tab = new DockTab
                {
                    Name = descriptor.Name,
                    FolderPath = descriptor.Target,
                    SourceShortcutPath = descriptor.Path
                };
                configuration.Tabs.Add(tab);
                changed = true;
            }
            else
            {
                if (!tab.Name.Equals(descriptor.Name, StringComparison.Ordinal))
                {
                    tab.Name = descriptor.Name;
                    changed = true;
                }
                if (!tab.FolderPath.Equals(descriptor.Target, StringComparison.OrdinalIgnoreCase)
                    || !tab.SourceShortcutPath!.Equals(descriptor.Path, StringComparison.OrdinalIgnoreCase))
                {
                    tab.FolderPath = descriptor.Target;
                    tab.SourceShortcutPath = descriptor.Path;
                    changed = true;
                }
            }
            matchedTabs.Add(tab.Id);
        }

        foreach (var staleTab in configuration.Tabs
                     .Where(tab => tab.IsLinkedFolder
                                   && !matchedTabs.Contains(tab.Id)
                                   && !File.Exists(tab.SourceShortcutPath))
                     .ToList())
        {
            configuration.Tabs.Remove(staleTab);
            changed = true;
        }
        return changed;
    }

    private IReadOnlyList<LinkedFolderDescriptor> EnumerateLinkedFolderDescriptors()
    {
        var descriptors = new List<LinkedFolderDescriptor>();
        foreach (var path in Directory.EnumerateFiles(RootPath, "*.lnk", SearchOption.TopDirectoryOnly))
        {
            var target = _shortcutResolver.ResolveTarget(path);
            if (target == null || !Directory.Exists(target)) continue;
            descriptors.Add(new LinkedFolderDescriptor(
                Path.GetFullPath(path),
                Path.GetFullPath(target),
                Path.GetFileNameWithoutExtension(path)));
        }
        return descriptors;
    }

    private string CreateReference(DockTab tab, string target, string label)
    {
        EnsureManaged(tab);
        Directory.CreateDirectory(tab.FolderPath);
        var sourceExtension = Path.GetExtension(target);
        var isShortcutFile = File.Exists(target)
            && (sourceExtension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                || sourceExtension.Equals(".url", StringComparison.OrdinalIgnoreCase));
        var extension = isShortcutFile
            ? sourceExtension
            : Uri.TryCreate(target, UriKind.Absolute, out var uri) && !uri.IsFile ? ".url" : ".lnk";
        var destination = GetAvailableFilePath(tab.FolderPath, label, extension);
        if (isShortcutFile)
        {
            File.Copy(target, destination);
        }
        else
        {
            _shortcutWriter.Create(target, destination);
        }
        return destination;
    }

    private string GetTarget(DockItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.BrowsePath)) return item.BrowsePath;
        return Path.GetExtension(item.Path).Equals(".lnk", StringComparison.OrdinalIgnoreCase)
            ? _shortcutResolver.ResolveTarget(item.Path) ?? item.Path
            : item.Path;
    }

    private string MoveIntoTabFolder(string source, DockTab tab)
    {
        if (IsDirectChild(source, tab.FolderPath)) return source;
        var destination = GetAvailableFilePath(tab.FolderPath, Path.GetFileNameWithoutExtension(source), Path.GetExtension(source));
        File.Move(source, destination);
        foreach (var item in tab.Items.Where(item => item.Path.Equals(source, StringComparison.OrdinalIgnoreCase)))
        {
            item.Path = destination;
        }
        return destination;
    }

    private string GetAvailableFolderPath(string name, ISet<string> assignedPaths)
    {
        var validName = MakeValidName(name, "Tab");
        var candidate = Path.Combine(RootPath, validName);
        var suffix = 2;
        while (assignedPaths.Contains(Path.GetFullPath(candidate)) || File.Exists(candidate))
        {
            candidate = Path.Combine(RootPath, $"{validName} {suffix++}");
        }
        return candidate;
    }

    private static string GetAvailableFilePath(string folder, string label, string extension)
    {
        var validLabel = MakeValidName(label, "Shortcut");
        var candidate = Path.Combine(folder, validLabel + extension);
        var suffix = 2;
        while (File.Exists(candidate) || Directory.Exists(candidate))
        {
            candidate = Path.Combine(folder, $"{validLabel} {suffix++}{extension}");
        }
        return candidate;
    }

    private static string MakeValidName(string name, string fallback)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Trim().Select(character => invalid.Contains(character) ? '_' : character).ToArray()).TrimEnd('.', ' ');
        return cleaned.Length == 0 || ReservedNames.Contains(cleaned) ? fallback : cleaned;
    }

    private static void ValidateFolderName(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0
            || trimmed.EndsWith('.')
            || trimmed.EndsWith(' ')
            || trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || ReservedNames.Contains(trimmed))
        {
            throw new IOException("The tab name is not valid as a Windows folder name");
        }
    }

    private static void EnsureManaged(DockTab tab)
    {
        if (tab.IsLinkedFolder) throw new InvalidOperationException("Linked folder tabs are read-only");
    }

    private static bool IsDirectChild(string path, string folder)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(path));
        return parent != null && parent.Equals(Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase);
    }

    private sealed record LinkedFolderDescriptor(string Path, string Target, string Name);
}
