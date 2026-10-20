using System;
using System.Runtime.InteropServices;

namespace G8VolumeOsd.Interop;

internal static class NativeMethods
{
    public const int GwlExStyle = -20;
    public const long WsExTransparent = 0x00000020;
    public const long WsExToolWindow = 0x00000080;
    public const long WsExLayered = 0x00080000;
    public const long WsExNoActivate = 0x08000000;
    public const long WsExAppWindow = 0x00040000;

    public const int WmMouseActivate = 0x0021;
    public const int MaNoActivate = 3;

    public static readonly IntPtr HwndTopmost = new(-1);
    public const uint SwpNoSize = 0x0001;
    public const uint SwpNoActivate = 0x0010;
    public const uint SwpNoOwnerZOrder = 0x0200;

    public const uint MonitorDefaultToPrimary = 0x00000001;
    public const int MdtEffectiveDpi = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    public static long GetWindowExStyle(IntPtr hwnd) =>
        IntPtr.Size == 8 ? GetWindowLongPtrW(hwnd, GwlExStyle).ToInt64() : GetWindowLongW(hwnd, GwlExStyle);

    public static void SetWindowExStyle(IntPtr hwnd, long style)
    {
        if (IntPtr.Size == 8)
        {
            SetWindowLongPtrW(hwnd, GwlExStyle, new IntPtr(style));
        }
        else
        {
            SetWindowLongW(hwnd, GwlExStyle, unchecked((int)style));
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowLongPtrW(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int GetWindowLongW(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowLongPtrW(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll")]
    private static extern int SetWindowLongW(IntPtr hwnd, int index, int value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(Point pt, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessageW(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
}
