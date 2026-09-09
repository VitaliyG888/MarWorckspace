using System.Runtime.InteropServices;
using TaskbarBanner.Core.Interop;

namespace TaskbarBanner.Core;

public sealed class ActivityMonitor : IActivityMonitor
{
    public TimeSpan GetIdleTime()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!NativeMethods.GetLastInputInfo(ref info))
        {
            return TimeSpan.Zero;
        }

        uint currentTick = NativeMethods.GetTickCount();
        uint idleMs = currentTick - info.dwTime;
        return TimeSpan.FromMilliseconds(idleMs);
    }

    public DateTimeOffset GetLastInputUtc()
        => DateTimeOffset.UtcNow - GetIdleTime();

    public long GetLastInputTickMs()
        => Environment.TickCount64 - (long)GetIdleTime().TotalMilliseconds;
}
