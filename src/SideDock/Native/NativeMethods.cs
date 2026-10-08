using System.Runtime.InteropServices;

namespace SideDock.Native;

internal static class NativeMethods
{
    internal const int HotKeyId = 0x5344;
    internal const int WmHotKey = 0x0312;

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(
        IntPtr window,
        ref WindowCompositionAttributeData data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(IntPtr window, int id);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr window, out Rect rect);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr window, IntPtr region, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    private static extern int CombineRgn(IntPtr destination, IntPtr source1, IntPtr source2, int combineMode);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(Point point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    internal static void ApplyToolWindowStyle(IntPtr window)
    {
        var style = GetWindowLongPtr(window, -20).ToInt64();
        SetWindowLongPtr(window, -20, new IntPtr(BuildToolWindowExtendedStyle(style)));
    }

    internal static void ApplyBlurBehind(IntPtr window)
    {
        var accent = new AccentPolicy { State = 3 };
        var accentSize = Marshal.SizeOf<AccentPolicy>();
        var accentPointer = Marshal.AllocHGlobal(accentSize);
        try
        {
            Marshal.StructureToPtr(accent, accentPointer, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = 19,
                Data = accentPointer,
                DataSize = accentSize
            };
            _ = SetWindowCompositionAttribute(window, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(accentPointer);
        }
    }

    internal static void ApplyLeftRoundedRegion(IntPtr window, double cornerRadius)
    {
        if (!GetClientRect(window, out var bounds) || bounds.Right <= 0 || bounds.Bottom <= 0) return;

        var dpi = GetDpiForWindow(window);
        var radius = Math.Max(1, (int)Math.Round(cornerRadius * (dpi == 0 ? 1 : dpi / 96d)));
        var roundedRegion = CreateRoundRectRgn(0, 0, bounds.Right + 1, bounds.Bottom + 1, radius * 2, radius * 2);
        var squareRightRegion = CreateRectRgn(radius, 0, bounds.Right + 1, bounds.Bottom + 1);
        if (roundedRegion == IntPtr.Zero || squareRightRegion == IntPtr.Zero)
        {
            if (roundedRegion != IntPtr.Zero) _ = DeleteObject(roundedRegion);
            if (squareRightRegion != IntPtr.Zero) _ = DeleteObject(squareRightRegion);
            return;
        }

        const int regionOr = 2;
        var combined = CombineRgn(roundedRegion, roundedRegion, squareRightRegion, regionOr) != 0;
        _ = DeleteObject(squareRightRegion);
        if (!combined || SetWindowRgn(window, roundedRegion, true) == 0)
        {
            _ = DeleteObject(roundedRegion);
        }
    }

    internal static long BuildToolWindowExtendedStyle(long style)
    {
        const long toolWindow = 0x00000080L;
        const long appWindow = 0x00040000L;
        return (style | toolWindow) & ~appWindow;
    }

    internal static PixelRect GetCursorMonitorWorkArea()
    {
        if (!GetCursorPos(out var point)) return default;
        var monitor = MonitorFromPoint(point, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        return GetMonitorInfo(monitor, ref info)
            ? new PixelRect(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom)
            : default;
    }

    internal static bool IsCursorOverProcess(int processId)
    {
        if (!GetCursorPos(out var point)) return false;
        var window = WindowFromPoint(point);
        if (window == IntPtr.Zero) return false;
        _ = GetWindowThreadProcessId(window, out var windowProcessId);
        return windowProcessId == processId;
    }

    internal static PixelRect GetWindowBounds(IntPtr window)
    {
        return GetWindowRect(window, out var rect)
            ? new PixelRect(rect.Left, rect.Top, rect.Right, rect.Bottom)
            : default;
    }

    internal static bool SetWindowPosition(IntPtr window, int left, int top)
    {
        const uint noSize = 0x0001;
        const uint noZOrder = 0x0004;
        const uint noActivate = 0x0010;
        return SetWindowPos(window, IntPtr.Zero, left, top, 0, 0, noSize | noZOrder | noActivate);
    }

    internal static void FlushDesktopComposition() => _ = DwmFlush();

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        internal int Size;
        internal Rect Monitor;
        internal Rect Work;
        internal uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        internal int State;
        internal int Flags;
        internal int GradientColor;
        internal int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        internal int Attribute;
        internal IntPtr Data;
        internal int DataSize;
    }

}

internal readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    internal int Width => Right - Left;
    internal int Height => Bottom - Top;
    internal bool IsEmpty => Width <= 0 || Height <= 0;
}
