using SideDock.Models;

namespace SideDock.Services;

internal static class ItemClassifier
{
    internal static DockItemKind Classify(string path)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && !uri.IsFile)
        {
            return DockItemKind.Website;
        }
        if (Directory.Exists(path)) return DockItemKind.Folder;

        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".exe" or ".com" or ".bat" or ".cmd" => DockItemKind.Application,
            ".lnk" => DockItemKind.Shortcut,
            ".url" => DockItemKind.Website,
            _ => DockItemKind.File
        };
    }

    internal static string GetLabel(string path)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && !uri.IsFile)
        {
            return uri.Host.Replace("www.", "", StringComparison.OrdinalIgnoreCase);
        }

        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var extension = Path.GetExtension(name);
        return extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".url", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(name)
            : name;
    }
}
