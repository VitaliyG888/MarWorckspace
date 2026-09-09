using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using TaskbarBanner.App.Ads;
using TaskbarBanner.App.Auth;
using TaskbarBanner.App.Overlays;
using TaskbarBanner.App.Reporting;
using TaskbarBanner.Core;
using TaskbarBanner.Core.Reporting;

namespace TaskbarBanner.App;

public partial class MainWindow : Window
{
    private const int MaxStateEvents = 300;

    private readonly DispatcherTimer _refreshTimer = new()
    {
        Interval = TimeSpan.FromSeconds(1),
    };

    private readonly ObservableCollection<string> _stateEvents = new();

    private TaskbarService? _taskbarService;
    private OverlayManager? _overlayManager;
    private ISystemStateMonitor? _systemState;
    private IActivityMonitor? _activityMonitor;
    private VerificationEngine? _engine;
    private IMinuteStore? _minuteStore;
    private MinuteUploader? _uploader;
    private AppAuthController? _auth;
    private string? _lastAuthStatus;
    private AdContent? _currentAd;

    public MainWindow()
    {
        InitializeComponent();
        StateEventsList.ItemsSource = _stateEvents;
        _refreshTimer.Tick += (_, _) => RefreshStatus();
        Loaded += (_, _) =>
        {
            _refreshTimer.Start();
            RefreshStatus();
        };
    }

    public void Attach(
        TaskbarService taskbarService,
        OverlayManager overlayManager,
        ISystemStateMonitor systemState,
        IActivityMonitor activityMonitor,
        VerificationEngine engine,
        IMinuteStore minuteStore,
        MinuteUploader uploader,
        AppAuthController auth)
    {
        _taskbarService = taskbarService;
        _overlayManager = overlayManager;
        _systemState = systemState;
        _activityMonitor = activityMonitor;
        _engine = engine;
        _minuteStore = minuteStore;
        _uploader = uploader;
        _auth = auth;

        _systemState.StateChanged += OnSystemStateChanged;
        _engine.EpochFinalized += OnEngineEpochFinalized;
        _engine.MinuteVerified += OnEngineMinuteVerified;
        _uploader.AttemptCompleted += OnUploadAttemptCompleted;
        _auth.Changed += OnAuthChanged;
        _overlayManager.AdChanged += OnAdChanged;
        if (_overlayManager.LastShownAd is { } ad)
        {
            UpdateAdPanel(ad);
        }

        AddStateEvent("Diagnostics started");
        UpdateSystemState();
        UpdateAuthUi();
    }

    protected override void OnClosed(EventArgs e)
    {
        _refreshTimer.Stop();
        if (_systemState is not null)
        {
            _systemState.StateChanged -= OnSystemStateChanged;
        }

        if (_engine is not null)
        {
            _engine.EpochFinalized -= OnEngineEpochFinalized;
            _engine.MinuteVerified -= OnEngineMinuteVerified;
        }

        if (_uploader is not null)
        {
            _uploader.AttemptCompleted -= OnUploadAttemptCompleted;
        }

        if (_auth is not null)
        {
            _auth.Changed -= OnAuthChanged;
        }

        if (_overlayManager is not null)
        {
            _overlayManager.AdChanged -= OnAdChanged;
        }

        base.OnClosed(e);
    }

    private void OnToggleBanners(object sender, RoutedEventArgs e)
    {
        if (_overlayManager is null)
        {
            return;
        }

        _overlayManager.UserEnabled = !_overlayManager.UserEnabled;
        UpdateAuthUi();
        RefreshStatus();
    }

    private void OnRefresh(object sender, RoutedEventArgs e) => RefreshStatus();

    private async void OnSignIn(object sender, RoutedEventArgs e)
    {
        if (_auth is null)
        {
            return;
        }

        SignInButton.IsEnabled = false;
        try
        {
            bool ok = await _auth.SignInAsync();
            if (!ok && _auth.StatusText.StartsWith("Not signed in"))
            {
                AddStateEvent("Sign in cancelled");
            }
        }
        catch (Exception ex)
        {
            AddStateEvent($"Sign in error: {ex.Message}");
        }
        finally
        {
            UpdateAuthUi();
            UpdateSystemState();
        }
    }

    private async void OnSignOut(object sender, RoutedEventArgs e)
    {
        if (_auth is null)
        {
            return;
        }

        SignOutButton.IsEnabled = false;
        try
        {
            await _auth.SignOutAsync();
        }
        catch (Exception ex)
        {
            AddStateEvent($"Sign out error: {ex.Message}");
        }
        finally
        {
            UpdateAuthUi();
            UpdateSystemState();
        }
    }

    private void OnAuthChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => OnAuthChanged(sender, e)));
            return;
        }

        string status = _auth?.StatusText ?? string.Empty;
        if (status != _lastAuthStatus)
        {
            _lastAuthStatus = status;
            AddStateEvent($"auth: {status}");
        }

        UpdateAuthUi();
        UpdateSystemState();
    }

    private void UpdateAuthUi()
    {
        if (_auth is null || _overlayManager is null)
        {
            return;
        }

        bool sessionOn = _overlayManager.SessionEnabled;
        SignInButton.IsEnabled = !_auth.IsAuthenticated && _auth.CanSignIn;
        SignOutButton.IsEnabled = _auth.IsAuthenticated;
        ToggleBannersButton.IsEnabled = sessionOn;

        bool enabled = _overlayManager.BannersEnabled;
        ToggleBannersButton.Content = enabled
            ? "Hide banners"
            : sessionOn
                ? "Show banners"
                : "Sign in to show";

        FooterText.Text =
            $"Banner overlays: {_overlayManager.OverlayWindowCount} window(s), {_overlayManager.VisibleOverlayCount} visible - banners {( enabled ? "ENABLED" : "DISABLED")} - auth {( _auth.IsAuthenticated ? "ON" : "OFF")}";
    }

    private void OnSystemStateChanged(object? sender, SystemStateChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => OnSystemStateChanged(sender, e)));
            return;
        }

        AddStateEvent(e.Description);
        UpdateSystemState();
    }

    private void OnEngineEpochFinalized(object? sender, EpochFinalizedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => OnEngineEpochFinalized(sender, e)));
            return;
        }

        string flags = e.FailureFlags == VerificationConditionFlags.None
            ? "-"
            : e.FailureFlags.ToString();
        string line = $"epoch {e.EpochStartUtc:HH:mm:ss}->{e.EpochEndUtc:HH:mm:ss}  " +
                      $"ok={e.EpochEligible}  fail={flags}  state={e.StateAfter}" +
                      (e.MinuteCounted ? "  +1 MIN" : string.Empty);
        AddStateEvent(line);
        UpdateSystemState();
    }

    private void OnEngineMinuteVerified(object? sender, MinuteVerifiedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => OnEngineMinuteVerified(sender, e)));
            return;
        }

        AddStateEvent($"MINUTE VERIFIED {e.MinuteStartUtc:HH:mm:ss} - {e.MinuteEndUtc:HH:mm:ss}");
    }

    private void OnUploadAttemptCompleted(object? sender, UploadAttemptEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => OnUploadAttemptCompleted(sender, e)));
            return;
        }

        AddStateEvent(e.Succeeded
            ? $"Uploaded {e.UploadedCount} verified minute(s) to server"
            : "Upload failed (offline) - scheduled retry");
    }

    private void OnAdChanged(AdContent ad)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => OnAdChanged(ad)));
            return;
        }

        UpdateAdPanel(ad);
    }

    private void UpdateAdPanel(AdContent ad)
    {
        _currentAd = ad;
        AdDetailText.Text = $"{ad.BrandName} - {ad.Headline} - {ad.Tagline}";
        OpenWebsiteButton.Visibility = string.IsNullOrWhiteSpace(ad.WebsiteUrl)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void OnOpenWebsite(object sender, RoutedEventArgs e)
    {
        if (_currentAd?.WebsiteUrl is not { } url)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not open {url}: {ex.Message}",
                "Open website",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void AddStateEvent(string description)
    {
        AppLog.Write(description);
        _stateEvents.Insert(0, $"{DateTime.Now:HH:mm:ss}  {description}");
        while (_stateEvents.Count > MaxStateEvents)
        {
            _stateEvents.RemoveAt(_stateEvents.Count - 1);
        }
    }

    private void RefreshStatus()
    {
        UpdateTaskbarStatus();
        UpdateSystemState();
    }

    private void UpdateTaskbarStatus()
    {
        if (_taskbarService is null)
        {
            return;
        }

        IReadOnlyList<TaskbarInfo> bars = _taskbarService.GetTaskbars();

        var sb = new StringBuilder();
        sb.AppendLine($"OS: {OsInfo.Describe()}");
        if (bars.Count == 0)
        {
            sb.AppendLine("(no taskbar detected)");
        }

        foreach (TaskbarInfo t in bars)
        {
            sb.AppendLine($"{t.MonitorDevice}  primary={t.IsPrimary}  edge={t.DockEdge}");
            sb.AppendLine($"   visible={t.IsEffectivelyVisible}  autoHide={t.IsAutoHideEnabled}  dpi={t.Dpi:0.#}");
            sb.AppendLine($"   bar=({t.Bounds.X},{t.Bounds.Y} {t.Bounds.Width}x{t.Bounds.Height})  work=({t.WorkArea.X},{t.WorkArea.Y} {t.WorkArea.Width}x{t.WorkArea.Height})");
        }

        StatusText.Text = sb.ToString().TrimEnd();
        UpdateAuthUi();
    }

    private void UpdateSystemState()
    {
        if (_systemState is null)
        {
            return;
        }

        SystemStateSnapshot snapshot = _systemState.Snapshot;

        string idleLine = "Idle        : n/a";
        string lastInputLine = "Last input  : n/a";
        if (_activityMonitor is not null)
        {
            try
            {
                TimeSpan idle = _activityMonitor.GetIdleTime();
                idleLine = $"Idle        : {idle.TotalSeconds:F1} s";
                lastInputLine = $"Last input  : {_activityMonitor.GetLastInputUtc():HH:mm:ss} UTC";
            }
            catch
            {
                // ignore transient read failures
            }
        }

        string reportingBlock = _minuteStore is null || _uploader is null
            ? string.Empty
            : $"\n\nPending min  : {_minuteStore.CountPending()}\n" +
              $"Uploaded     : {_minuteStore.CountUploaded()}\n" +
              $"Retry delay  : {_uploader.CurrentBackoff.TotalSeconds:0} s";

        string authBlock = _auth is null
            ? string.Empty
            : $"\n\nAuth        : {_auth.StatusText}";

        StateSummaryText.Text =
            $"Powered on  : {(snapshot.IsPoweredOn ? "Yes" : "No")}\n" +
            $"Unlocked    : {(snapshot.IsUnlocked ? "Yes" : "No")}\n" +
            $"Connected   : {(snapshot.IsSessionConnected ? "Yes" : "No")}\n" +
            $"Baseline ok : {(snapshot.MeetsBaseline ? "Yes" : "No")}\n" +
            $"{idleLine}\n" +
            $"{lastInputLine}" +
            (_engine is null
                ? string.Empty
                : $"\n\nEngine state : {_engine.State}\n" +
                  $"Prime epochs : {_engine.PrimingProgress} / {VerificationEngine.PrimeEpochCount}\n" +
                  $"Counted min  : {_engine.TotalCountedMinutes}") +
            reportingBlock +
            authBlock;
    }
}
