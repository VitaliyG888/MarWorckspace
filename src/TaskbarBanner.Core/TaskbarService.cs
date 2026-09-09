using System.Runtime.InteropServices;
using TaskbarBanner.Core.Geometry;
using TaskbarBanner.Core.Interop;

namespace TaskbarBanner.Core;

public sealed class TaskbarService
{
    private sealed record Candidate(IntPtr Hwnd, RectI Bounds);

    public IReadOnlyList<TaskbarInfo> GetTaskbars()
    {
        IntPtr primaryTaskbar = NativeMethods.FindWindow(NativeMethods.TaskbarWindowClass, null);

        var candidates = new List<Candidate>();
        NativeMethods.EnumWindows((h, _) =>
        {
            string cls = NativeMethods.GetWindowClassName(h);
            if (cls != NativeMethods.TaskbarWindowClass && cls != NativeMethods.SecondaryTaskbarWindowClass)
            {
                return true;
            }

            if (NativeMethods.GetWindowRect(h, out RECT r))
            {
                candidates.Add(new Candidate(h, RectI.FromLtrb(r.Left, r.Top, r.Right, r.Bottom)));
            }

            return true;
        }, IntPtr.Zero);

        var bestByMonitor = new Dictionary<IntPtr, Candidate>();
        foreach (Candidate c in candidates)
        {
            IntPtr monitor = NativeMethods.MonitorFromWindow(c.Hwnd, NativeMethods.MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero)
            {
                continue;
            }

            if (!bestByMonitor.TryGetValue(monitor, out Candidate? current) || c.Bounds.Area > current.Bounds.Area)
            {
                bestByMonitor[monitor] = c;
            }
        }

        var result = new List<TaskbarInfo>();
        foreach (KeyValuePair<IntPtr, Candidate> pair in bestByMonitor)
        {
            var info = TryBuildInfo(pair.Key, pair.Value, primaryTaskbar);
            if (info is not null)
            {
                result.Add(info);
            }
        }

        return result
            .OrderByDescending(t => t.IsPrimary)
            .ThenBy(t => t.MonitorBounds.X)
            .ThenBy(t => t.MonitorBounds.Y)
            .ToList();
    }

    private static TaskbarInfo? TryBuildInfo(IntPtr monitor, Candidate candidate, IntPtr primaryTaskbar)
    {
        var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref mi))
        {
            return null;
        }

        RectI monitorBounds = RectI.FromLtrb(mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Right, mi.rcMonitor.Bottom);
        DockEdge edge = DetermineDockEdge(monitorBounds, candidate.Bounds);
        if (edge == DockEdge.Unknown)
        {
            return null;
        }

        bool isPrimary = candidate.Hwnd == primaryTaskbar || IsPrimaryMonitor(monitorBounds);
        double overlapFraction = candidate.Bounds.OverlapFraction(monitorBounds);
        bool effectivelyVisible = NativeMethods.IsWindowVisible(candidate.Hwnd) && overlapFraction >= 0.5;

        bool autoHide = false;
        if (candidate.Hwnd == primaryTaskbar && primaryTaskbar != IntPtr.Zero)
        {
            var abd = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = candidate.Hwnd };
            uint state = NativeMethods.SHAppBarMessage(NativeMethods.AbmGetState, ref abd);
            autoHide = (state & NativeMethods.AbsAutohide) != 0;
        }

        double dpi = NativeMethods.GetDpiForWindow(candidate.Hwnd);
        if (dpi <= 0)
        {
            dpi = NativeMethods.GetDpiForSystem();
        }

        if (dpi <= 0)
        {
            dpi = 96;
        }

        return new TaskbarInfo
        {
            Hwnd = candidate.Hwnd,
            IsPrimary = isPrimary,
            MonitorDevice = mi.szDevice,
            MonitorBounds = monitorBounds,
            Bounds = candidate.Bounds,
            WorkArea = ComputeWorkArea(monitorBounds, candidate.Bounds, edge, effectivelyVisible),
            DockEdge = edge,
            IsAutoHideEnabled = autoHide,
            IsEffectivelyVisible = effectivelyVisible,
            Dpi = dpi,
        };
    }

    private static bool IsPrimaryMonitor(RectI monitorBounds)
        => monitorBounds.X == 0 && monitorBounds.Y == 0;

    private static DockEdge DetermineDockEdge(RectI monitor, RectI bar)
    {
        const int tolerance = 8;
        bool fullWidth = Math.Abs(bar.Left - monitor.Left) <= tolerance
            && Math.Abs(bar.Right - monitor.Right) <= tolerance;
        bool fullHeight = Math.Abs(bar.Top - monitor.Top) <= tolerance
            && Math.Abs(bar.Bottom - monitor.Bottom) <= tolerance;

        if (fullWidth && Math.Abs(bar.Bottom - monitor.Bottom) <= tolerance)
        {
            return DockEdge.Bottom;
        }

        if (fullWidth && Math.Abs(bar.Top - monitor.Top) <= tolerance)
        {
            return DockEdge.Top;
        }

        if (fullHeight && Math.Abs(bar.Left - monitor.Left) <= tolerance)
        {
            return DockEdge.Left;
        }

        if (fullHeight && Math.Abs(bar.Right - monitor.Right) <= tolerance)
        {
            return DockEdge.Right;
        }

        return DockEdge.Unknown;
    }

    private static RectI ComputeWorkArea(RectI monitor, RectI bar, DockEdge edge, bool visible)
    {
        if (!visible)
        {
            return monitor;
        }

        return edge switch
        {
            DockEdge.Bottom => new RectI(monitor.X, monitor.Y, monitor.Width, Math.Max(0, bar.Top - monitor.Y)),
            DockEdge.Top => new RectI(monitor.X, bar.Bottom, monitor.Width, Math.Max(0, monitor.Bottom - bar.Bottom)),
            DockEdge.Left => new RectI(bar.Right, monitor.Y, Math.Max(0, monitor.Right - bar.Right), monitor.Height),
            DockEdge.Right => new RectI(monitor.X, monitor.Y, Math.Max(0, bar.Left - monitor.X), monitor.Height),
            _ => monitor,
        };
    }
}
