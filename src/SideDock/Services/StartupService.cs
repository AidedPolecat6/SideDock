using Microsoft.Win32;

namespace SideDock.Services;

internal static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    internal static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue("SideDock") is string;
    }

    internal static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            key.SetValue("SideDock", $"\"{Environment.ProcessPath}\"");
        }
        else
        {
            key.DeleteValue("SideDock", false);
        }
    }
}
