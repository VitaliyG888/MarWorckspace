using System.Runtime.InteropServices;

namespace TaskbarBanner.App.Interop;

internal static class ForegroundNative
{
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);
}
