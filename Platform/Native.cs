using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace SnapLine;

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left; public int Top; public int Right; public int Bottom; }
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(IntPtr hwnd, StringBuilder name, int count);
    [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] internal static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] internal static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll")] internal static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Point point, uint flags);

    internal static double Scale(Forms.Screen screen)
    {
        try
        {
            var point = new Point { X = screen.Bounds.Left + 1, Y = screen.Bounds.Top + 1 };
            return GetDpiForMonitor(MonitorFromPoint(point, 2), 0, out var x, out _) == 0 ? x / 96.0 : 1;
        }
        catch { return 1; }
    }
    internal static bool FullScreen(Forms.Screen screen, IntPtr own)
    {
        var handle = GetForegroundWindow();
        if (handle == IntPtr.Zero || handle == own) return false;
        var name = new StringBuilder(128);
        GetClassName(handle, name, name.Capacity);
        if (name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        // 最大化窗口也覆盖整个显示器；只有移除常规标题栏和边框的窗口才视作独占全屏。
        var style = GetWindowLongPtr(handle, -16).ToInt64();
        if ((style & 0x00C00000L) != 0 || (style & 0x00040000L) != 0) return false;
        if (!GetWindowRect(handle, out var rect)) return false;
        var bounds = screen.Bounds;
        return rect.Left <= bounds.Left && rect.Top <= bounds.Top && rect.Right >= bounds.Right && rect.Bottom >= bounds.Bottom;
    }
    internal static bool DarkTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }
}
