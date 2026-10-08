using SideDock.Models;

namespace SideDock.Services;

internal sealed record FolderEntry(
    string Name,
    string Path,
    string? BrowsePath,
    bool IsFolder,
    DockItemKind Kind)
{
    internal string EffectiveBrowsePath => BrowsePath ?? Path;
}

internal sealed class FolderBrowser(IShortcutResolver shortcutResolver)
{
    internal IReadOnlyList<FolderEntry> Enumerate(string folderPath)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(folderPath)
                .Select(CreateEntry)
                .OrderByDescending(entry => entry.IsFolder)
                .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    private FolderEntry CreateEntry(string path)
    {
        var kind = ItemClassifier.Classify(path);
        string? browsePath = null;
        var isFolder = kind == DockItemKind.Folder;
        if (kind == DockItemKind.Shortcut)
        {
            var target = shortcutResolver.ResolveTarget(path);
            if (target != null && Directory.Exists(target))
            {
                browsePath = target;
                isFolder = true;
                kind = DockItemKind.Folder;
            }
        }

        return new FolderEntry(ItemClassifier.GetLabel(path), path, browsePath, isFolder, kind);
    }
}
