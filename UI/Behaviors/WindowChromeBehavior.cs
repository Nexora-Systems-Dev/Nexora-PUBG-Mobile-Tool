using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Nexora.UI.Behaviors;

/// <summary>
/// Constrains a borderless window's maximized bounds to the current monitor's work area.
/// </summary>
public static class WindowChromeBehavior
{
    private const int WmGetMinMaxInfo = 0x0024;
    private const uint MonitorDefaultToNearest = 0x00000002;

    /// <summary>
    /// Attaches the WM_GETMINMAXINFO hook to the window's HwndSource and returns an IDisposable lifetime token.
    /// </summary>
    public static IDisposable Attach(Window window)
    {
        if (PresentationSource.FromVisual(window) is not HwndSource source)
        {
            return NullLifetime.Instance;
        }

        HwndSourceHook hook = WindowProc;
        source.AddHook(hook);
        return new HookLifetime(source, hook);
    }

    private sealed class HookLifetime : IDisposable
    {
        private readonly HwndSource _source;
        private readonly HwndSourceHook _hook;
        private bool _disposed;

        public HookLifetime(HwndSource source, HwndSourceHook hook)
        {
            _source = source;
            _hook = hook;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _source.RemoveHook(_hook);
        }
    }

    private sealed class NullLifetime : IDisposable
    {
        public static readonly NullLifetime Instance = new();

        public void Dispose()
        {
        }
    }

    private static IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmGetMinMaxInfo)
        {
            var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            if (monitor != IntPtr.Zero)
            {
                var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
                if (GetMonitorInfo(monitor, ref monitorInfo))
                {
                    var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
                    info.MaxPosition = new Point32(
                        monitorInfo.Work.Left - monitorInfo.Monitor.Left,
                        monitorInfo.Work.Top - monitorInfo.Monitor.Top);
                    info.MaxSize = new Point32(
                        monitorInfo.Work.Right - monitorInfo.Work.Left,
                        monitorInfo.Work.Bottom - monitorInfo.Work.Top);
                    Marshal.StructureToPtr(info, lParam, fDeleteOld: false);
                    handled = true;
                }
            }
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point32
    {
        public int X;
        public int Y;

        public Point32(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point32 Reserved;
        public Point32 MaxSize;
        public Point32 MaxPosition;
        public Point32 MinTrackSize;
        public Point32 MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect32 Monitor;
        public Rect32 Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect32
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
