using System.Collections.ObjectModel;

namespace SideDock.Models;

public sealed class SideDockConfiguration
{
    public int SchemaVersion { get; set; } = 6;
    public double DrawerWidth { get; set; } = 410;
    public double DrawerHeight { get; set; } = 720;
    public double HandleWidth { get; set; } = 28;
    public int AnimationMilliseconds { get; set; } = 280;
    public bool CloseOnDeactivate { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool IsLocked { get; set; } = true;
    public bool IsPinnedOpen { get; set; }
    public uint HotKeyModifiers { get; set; } = 0x0003;
    public uint HotKeyVirtualKey { get; set; } = 0x20;
    public ObservableCollection<DockTab> Tabs { get; set; } = [];
}
