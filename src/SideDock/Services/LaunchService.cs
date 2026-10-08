using System.Diagnostics;

namespace SideDock.Services;

internal static class LaunchService
{
    internal static void Launch(string target)
    {
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }

    internal static void ShowInExplorer(string target)
    {
        var path = Directory.Exists(target) ? target : Path.GetDirectoryName(target);
        if (!string.IsNullOrWhiteSpace(path)) Launch(path);
    }
}
