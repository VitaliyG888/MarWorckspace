using System.Windows.Interop;
using System.Windows.Threading;
using TaskbarBanner.App.Ads;
using TaskbarBanner.Core;
using TaskbarBanner.Core.Geometry;
using TaskbarBanner.Core.Interop;

namespace TaskbarBanner.App.Overlays;

public sealed class OverlayManager : IDisposable
{
    public const int BannerWidthPx = 180;
    public const int BannerHeightPx = 60;
    public const int BannerEdgeMarginPx = 6;
    public const int PrimaryTrayReservePx = 340;

    private readonly TaskbarService _taskbarService;
    private readonly IAdSource _adSource;
    private readonly Action _focusMainWindow;
    private readonly Dictionary<string, BannerOverlayWindow> _overlays = new();
    private readonly Dictionary<string, TaskbarInfo> _assigned = new();
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<string, AnchorCache> _anchorCache = new();
    private static readonly double? OverrideRightOffsetPx = ReadOverrideOffset();
    private bool _userEnabled = true;
    private bool _sessionEnabled;
    private AdContent? _lastAdShown;

    public event Action<AdContent>? AdChanged;

    public AdContent? LastShownAd => _lastAdShown;

    private sealed record AnchorCache(DateTimeOffset AtUtc, RectI Bar, double? ClusterLeftPx);

    private static double? ReadOverrideOffset()
        => double.TryParse(
            Environment.GetEnvironmentVariable("TASKBARBANNER_TRAY_RIGHT_OFFSET_PX"),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out double value) && value >= 0
                ? value
                : null;

    public OverlayManager(TaskbarService taskbarService, IAdSource adSource, Action focusMainWindow)
    {
        _taskbarService = taskbarService;
        _adSource = adSource;
        _focusMainWindow = focusMainWindow;
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(600),
        };
        _timer.Tick += (_, _) => OnTick();
    }

    public bool BannersEnabled => _userEnabled && _sessionEnabled;

    public bool UserEnabled
    {
        get => _userEnabled;
        set
        {
            if (_userEnabled == value)
            {
                return;
            }

            _userEnabled = value;
            Reconcile();
        }
    }

    public bool SessionEnabled
    {
        get => _sessionEnabled;
        set
        {
            if (_sessionEnabled == value)
            {
                return;
            }

            _sessionEnabled = value;
            Reconcile();
        }
    }

    private void Reconcile()
    {
        if (BannersEnabled)
        {
            OnTick();
        }
        else
        {
            foreach (BannerOverlayWindow overlay in _overlays.Values)
            {
                if (overlay.IsVisible)
                {
                    overlay.Hide();
                }
            }
        }
    }

    public int OverlayWindowCount => _overlays.Count;

    public int VisibleOverlayCount => _overlays.Values.Count(o => o.IsVisible);

    public void Start() => _timer.Start();

    public void Dispose()
    {
        _timer.Stop();
        foreach (BannerOverlayWindow overlay in _overlays.Values.ToList())
        {
            overlay.BannerClicked -= _focusMainWindow;
            overlay.Close();
        }

        _overlays.Clear();
        _assigned.Clear();
    }

    private void OnTick()
    {
        if (!BannersEnabled)
        {
            return;
        }

        IReadOnlyList<TaskbarInfo> bars;
        try
        {
            bars = _taskbarService.GetTaskbars();
        }
        catch
        {
            return;
        }

        var byMonitor = bars
            .GroupBy(t => t.MonitorDevice)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (string key in _overlays.Keys.Where(k => !byMonitor.ContainsKey(k)).ToList())
        {
            BannerOverlayWindow removed = _overlays[key];
            _overlays.Remove(key);
            _assigned.Remove(key);
            _anchorCache.Remove(key);
            removed.BannerClicked -= _focusMainWindow;
            removed.Close();
        }

        AdContent ad = _adSource.GetCurrent();
        if (!Equals(ad, _lastAdShown))
        {
            _lastAdShown = ad;
            AdChanged?.Invoke(ad);
        }

        foreach ((string monitorDevice, TaskbarInfo bar) in byMonitor)
        {
            if (!_overlays.TryGetValue(monitorDevice, out BannerOverlayWindow? overlay))
            {
                overlay = new BannerOverlayWindow();
                overlay.BannerClicked += _focusMainWindow;
                _overlays[monitorDevice] = overlay;
            }

            bool canShow = bar.IsEffectivelyVisible
                && (bar.DockEdge == DockEdge.Bottom || bar.DockEdge == DockEdge.Top);

            if (!canShow)
            {
                _assigned.Remove(monitorDevice);
                if (overlay.IsVisible)
                {
                    overlay.Hide();
                }

                continue;
            }

            Position(overlay, bar, ad);
            _assigned[monitorDevice] = bar;
            if (!overlay.IsVisible)
            {
                overlay.Show();
            }
        }
    }

    public bool IsAnyBannerConfirmedVisible()
    {
        foreach (KeyValuePair<string, BannerOverlayWindow> pair in _overlays)
        {
            if (!_assigned.TryGetValue(pair.Key, out TaskbarInfo? bar) || !pair.Value.IsVisible)
            {
                continue;
            }

            if (IsConfirmedOnScreen(pair.Value, bar))
            {
                return true;
            }
        }

        return false;
    }

    public bool AreShownBannerTaskbarsEligible()
    {
        foreach (KeyValuePair<string, BannerOverlayWindow> pair in _overlays)
        {
            if (!_assigned.TryGetValue(pair.Key, out TaskbarInfo? bar) || !pair.Value.IsVisible)
            {
                continue;
            }

            if (!bar.IsEffectivelyVisible || bar.IsAutoHideEnabled)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsConfirmedOnScreen(BannerOverlayWindow overlay, TaskbarInfo bar)
    {
        IntPtr hwnd = new WindowInteropHelper(overlay).Handle;
        if (hwnd == IntPtr.Zero || !overlay.Topmost || !NativeMethods.IsWindowVisible(hwnd))
        {
            return false;
        }

        if (!NativeMethods.GetWindowRect(hwnd, out RECT rect))
        {
            return false;
        }

        RectI window = RectI.FromLtrb(rect.Left, rect.Top, rect.Right, rect.Bottom);
        return bar.MonitorBounds.Contains(window) && window.Intersect(bar.Bounds).Area > 0;
    }

    private void Position(BannerOverlayWindow overlay, TaskbarInfo bar, AdContent ad)
    {
        overlay.SetAd(ad);

        double scale = bar.Dpi / 96.0;
        int width = (int)Math.Round(BannerWidthPx * scale);
        int height = (int)Math.Round(BannerHeightPx * scale);
        int margin = (int)Math.Round(BannerEdgeMarginPx * scale);

        RectI taskbar = bar.Bounds;
        int anchorRight;
        if (OverrideRightOffsetPx is { } overridePx)
        {
            anchorRight = (int)Math.Round(taskbar.Right - overridePx * scale);
        }
        else if (GetCachedClusterLeft(bar) is { } clusterLeft)
        {
            anchorRight = (int)clusterLeft - margin;
        }
        else if (bar.IsPrimary)
        {
            anchorRight = taskbar.Right - (int)Math.Round(PrimaryTrayReservePx * scale);
        }
        else
        {
            anchorRight = taskbar.Right - margin;
        }

        int left = anchorRight - width;
        int top = bar.DockEdge == DockEdge.Bottom
            ? taskbar.Bottom - margin - height
            : taskbar.Top + margin;

        RectI monitor = bar.MonitorBounds;
        left = Math.Max(monitor.Left, Math.Min(left, monitor.Right - width));
        top = Math.Max(monitor.Top, Math.Min(top, monitor.Bottom - height));

        overlay.Left = PxToDip(left, bar.Dpi);
        overlay.Top = PxToDip(top, bar.Dpi);
        overlay.Width = PxToDip(width, bar.Dpi);
        overlay.Height = PxToDip(height, bar.Dpi);
    }

    private double? GetCachedClusterLeft(TaskbarInfo bar)
    {
        if (_anchorCache.TryGetValue(bar.MonitorDevice, out AnchorCache? cache)
            && cache.Bar == bar.Bounds
            && DateTimeOffset.UtcNow - cache.AtUtc < TimeSpan.FromSeconds(5))
        {
            return cache.ClusterLeftPx;
        }

        double? clusterLeft = NotificationAnchorLocator.TryGetNotificationAreaLeftPx(
            bar.Hwnd,
            out double left,
            out _)
            ? left
            : null;

        _anchorCache[bar.MonitorDevice] = new AnchorCache(DateTimeOffset.UtcNow, bar.Bounds, clusterLeft);
        return clusterLeft;
    }

    private static double PxToDip(int px, double dpi) => px * 96.0 / dpi;
}
