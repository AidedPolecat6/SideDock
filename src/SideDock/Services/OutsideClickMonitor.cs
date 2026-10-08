using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace SideDock.Services;

internal sealed class OutsideClickMonitor : IDisposable
{
    private const int WhMouseLowLevel = 14;
    private const int WmLeftButtonDown = 0x0201;
    private const int WmRightButtonDown = 0x0204;
    private const int WmMiddleButtonDown = 0x0207;
    private const int WmXButtonDown = 0x020B;

    private readonly Dispatcher _dispatcher;
    private readonly Action _outsideClick;
    private readonly HookProcedure _hookProcedure;
    private IntPtr _hook;

    internal OutsideClickMonitor(Dispatcher dispatcher, Action outsideClick)
    {
        _dispatcher = dispatcher;
        _outsideClick = outsideClick;
        _hookProcedure = HookCallback;
    }

    internal void Start()
    {
        if (_hook != IntPtr.Zero) return;
        _hook = SetWindowsHookEx(WhMouseLowLevel, _hookProcedure, GetModuleHandle(null), 0);
    }

    internal void Stop()
    {
        if (_hook == IntPtr.Zero) return;
        _ = UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    public void Dispose() => Stop();

    private IntPtr HookCallback(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && IsButtonDownMessage(message.ToInt32()))
        {
            var mouseData = Marshal.PtrToStructure<LowLevelMouseData>(data);
            var window = WindowFromPoint(mouseData.Point);
            _ = GetWindowThreadProcessId(window, out var processId);
            if (processId != Environment.ProcessId)
            {
                _ = _dispatcher.BeginInvoke(_outsideClick);
            }
        }

        return CallNextHookEx(_hook, code, message, data);
    }

    internal static bool IsButtonDownMessage(int message) => message is
        WmLeftButtonDown or WmRightButtonDown or WmMiddleButtonDown or WmXButtonDown;

    private delegate IntPtr HookProcedure(int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int hookId, HookProcedure callback, IntPtr module, uint threadId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Point
    {
        internal readonly int X;
        internal readonly int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct LowLevelMouseData
    {
        internal readonly Point Point;
        internal readonly uint MouseData;
        internal readonly uint Flags;
        internal readonly uint Time;
        internal readonly UIntPtr ExtraInfo;
    }
}
