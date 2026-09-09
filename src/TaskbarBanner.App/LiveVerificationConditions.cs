using TaskbarBanner.App.Overlays;
using TaskbarBanner.Core;

namespace TaskbarBanner.App;

public sealed class LiveVerificationConditions : IVerificationConditions
{
    private readonly ISystemStateMonitor _systemState;
    private readonly IActivityMonitor _activity;
    private readonly OverlayManager _overlayManager;

    public LiveVerificationConditions(
        ISystemStateMonitor systemState,
        IActivityMonitor activity,
        OverlayManager overlayManager)
    {
        _systemState = systemState;
        _activity = activity;
        _overlayManager = overlayManager;
    }

    public bool IsSystemActive() => _systemState.Snapshot.MeetsBaseline;

    public bool IsTaskbarEligible() => _overlayManager.AreShownBannerTaskbarsEligible();

    public bool IsAnyBannerVisible() => _overlayManager.IsAnyBannerConfirmedVisible();

    public TimeSpan GetIdleTime() => _activity.GetIdleTime();

    public long GetLastInputTickMs() => _activity.GetLastInputTickMs();
}
