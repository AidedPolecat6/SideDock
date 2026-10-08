using SideDock.Models;

namespace SideDock.Services;

internal sealed class ShortcutImporter(IShortcutResolver shortcutResolver)
{
    internal const string DefaultFolderPath = @"C:\tools\SideDock shortcuts";

    internal IReadOnlyList<DockItem> Import(string folderPath)
    {
        if (!Directory.Exists(folderPath)) return [];

        var items = new List<DockItem>();
        foreach (var path in Directory.EnumerateFileSystemEntries(folderPath)
                     .OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase))
        {
            var kind = ItemClassifier.Classify(path);
            string? browsePath = null;
            var isPdfTarget = false;
            if (kind == DockItemKind.Shortcut)
            {
                var target = shortcutResolver.ResolveTarget(path);
                if (target != null && Directory.Exists(target))
                {
                    kind = DockItemKind.Folder;
                    browsePath = target;
                }
                else if (target != null)
                {
                    isPdfTarget = Path.GetExtension(target).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
                }
            }

            items.Add(new DockItem
            {
                Label = ItemClassifier.GetLabel(path),
                Path = path,
                BrowsePath = browsePath,
                Kind = kind,
                IsPdf = isPdfTarget,
                Accent = AccentFor(kind)
            });
        }
        return items;
    }

    private static string AccentFor(DockItemKind kind) => kind switch
    {
        DockItemKind.Folder => "#0F766E",
        DockItemKind.Application => "#1D4ED8",
        DockItemKind.Website => "#7C3AED",
        DockItemKind.Shortcut => "#B45309",
        _ => "#334155"
    };
}
