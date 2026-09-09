using System.Runtime.InteropServices;
using System.Text;
using TaskbarBanner.Core.Geometry;

namespace TaskbarBanner.Core.Interop;

public static class NativeMethods
{
    public const string TaskbarWindowClass = "Shell_TrayWnd";
    public const string SecondaryTaskbarWindowClass = "Shell_SecondaryTrayWnd";
    public const string TrayNotifyWindowClass = "TrayNotifyWnd";

    public const uint AbmGetState = 0x00000004;
    public const uint AbsAutohide = 0x00000001;
    public const uint MonitorDefaultToNearest = 2;

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForSystem();

    [DllImport("shell32.dll")]
    public static extern uint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    [DllImport("user32.dll")]
    public static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [DllImport("kernel32.dll")]
    public static extern uint GetTickCount();

    public static string GetWindowClassName(IntPtr hWnd)
    {
        var sb = new StringBuilder(256);
        return GetClassName(hWnd, sb, sb.Capacity) > 0 ? sb.ToString() : string.Empty;
    }

    public static bool TryFindDescendantRect(IntPtr root, string className, out RectI rect)
    {
        rect = default;
        if (root == IntPtr.Zero)
        {
            return false;
        }

        var queue = new Queue<IntPtr>();
        EnqueueChildren(root, queue);
        while (queue.Count > 0)
        {
            IntPtr handle = queue.Dequeue();
            if (string.Equals(GetWindowClassName(handle), className, StringComparison.Ordinal)
                && GetWindowRect(handle, out RECT r))
            {
                rect = RectI.FromLtrb(r.Left, r.Top, r.Right, r.Bottom);
                return true;
            }

            EnqueueChildren(handle, queue);
        }

        return false;
    }

    private static void EnqueueChildren(IntPtr parent, Queue<IntPtr> queue)
    {
        EnumChildWindows(parent, (h, _) =>
        {
            queue.Enqueue(h);
            return true;
        }, IntPtr.Zero);
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    public RECT(int left, int top, int right, int bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct MONITORINFOEX
{
    public int cbSize;
    public RECT rcMonitor;
    public RECT rcWork;
    public uint dwFlags;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string szDevice;
}

[StructLayout(LayoutKind.Sequential)]
public struct APPBARDATA
{
    public int cbSize;
    public IntPtr hWnd;
    public uint uCallbackMessage;
    public uint uEdge;
    public RECT rc;
    public int lParam;
}

[StructLayout(LayoutKind.Sequential)]
public struct LASTINPUTINFO
{
    public uint cbSize;
    public uint dwTime;
}
