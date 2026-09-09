using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using TaskbarBanner.App.Ads;
using TaskbarBanner.App.Auth;
using TaskbarBanner.App.Interop;
using TaskbarBanner.App.Overlays;
using TaskbarBanner.App.Reporting;
using TaskbarBanner.Core;
using TaskbarBanner.Core.Auth;

namespace TaskbarBanner.App;

public partial class App : Application
{
    private const string DefaultApiBaseUrl = "http://127.0.0.1:5099";

    private TaskbarService? _taskbarService;
    private SystemStateMonitor? _systemStateMonitor;
    private ActivityMonitor? _activityMonitor;
    private OverlayManager? _overlayManager;
    private VerificationEngine? _verificationEngine;
    private ReportService? _reportService;
    private HttpClient? _httpClient;
    private RemoteAdSource? _remoteAds;
    private AppAuthController? _authController;
    private DispatcherTimer? _engineTimer;
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppLog.Write($"Application started - OS {OsInfo.Describe()}");

        _taskbarService = new TaskbarService();
        _systemStateMonitor = new SystemStateMonitor();
        _activityMonitor = new ActivityMonitor();

        string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string dbPath = Path.Combine(localData, "TaskbarBanner", "report.db");
        Directory.CreateDirectory(Path.Combine(localData, "TaskbarBanner"));
        string apiBase = Environment.GetEnvironmentVariable("TASKBARBANNER_API_BASE") ?? DefaultApiBaseUrl;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _reportService = new ReportService(
            dbPath,
            new HttpReportApiClient(
                _httpClient,
                apiBase,
                () => _reportService?.DeviceId,
                () => Environment.GetEnvironmentVariable("TASKBARBANNER_SIGNING_SECRET")));

        string adCachePath = Path.Combine(localData, "TaskbarBanner", "ad-cache.json");
        _remoteAds = new RemoteAdSource(
            new HttpAdsApiClient(_httpClient, apiBase, GetAdsAccessTokenAsync),
            adCachePath,
            new SampleAdSource().GetCurrent());

        _mainWindow = new MainWindow();
        _overlayManager = new OverlayManager(_taskbarService, _remoteAds, FocusMainWindow);
        _verificationEngine = new VerificationEngine(
            new LiveVerificationConditions(_systemStateMonitor, _activityMonitor, _overlayManager));

        _verificationEngine.MinuteVerified += (_, e) =>
            _reportService?.RecordVerifiedMinute(e.MinuteStartUtc, e.MinuteEndUtc);

        _authController = new AppAuthController(
            BuildOAuthOptionsFromEnvironment(),
            _httpClient,
            _overlayManager,
            _reportService,
            IsDevAuthEnabled());
        _authController.Changed += (_, _) =>
        {
            if (_authController.IsAuthenticated)
            {
                _ = _remoteAds?.RefreshAsync(CancellationToken.None);
            }
        };

        _mainWindow.Attach(
            _taskbarService,
            _overlayManager,
            _systemStateMonitor,
            _activityMonitor,
            _verificationEngine,
            _reportService.Store,
            _reportService.Uploader,
            _authController);

        _overlayManager.Start();
        _remoteAds.Start();

        _engineTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _engineTimer.Tick += (_, _) =>
        {
            _verificationEngine?.Tick();
            if (_authController is { IsAuthenticated: true, NeedsRefresh: true })
            {
                _ = _authController.TryRefreshAsync();
            }
        };
        _engineTimer.Start();

        _ = _authController.StartAsync();
        _mainWindow.Show();
    }

    private async Task<string?> GetAdsAccessTokenAsync(CancellationToken cancellationToken)
        => _authController is null
            ? null
            : await _authController.GetAccessTokenAsync();

    protected override void OnExit(ExitEventArgs e)
    {
        _engineTimer?.Stop();
        _remoteAds?.Dispose();
        _overlayManager?.Dispose();
        _systemStateMonitor?.Dispose();
        _reportService?.Dispose();
        _httpClient?.Dispose();
        AppLog.Write("Application exited");
        base.OnExit(e);
    }

    private static OAuthOptions? BuildOAuthOptionsFromEnvironment()
    {
        string clientId = Environment.GetEnvironmentVariable("TASKBARBANNER_OAUTH_CLIENT_ID") ?? string.Empty;
        string authorizationEndpoint = Environment.GetEnvironmentVariable("TASKBARBANNER_OAUTH_AUTH_URL") ?? string.Empty;
        string tokenEndpoint = Environment.GetEnvironmentVariable("TASKBARBANNER_OAUTH_TOKEN_URL") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(authorizationEndpoint) || string.IsNullOrWhiteSpace(tokenEndpoint))
        {
            return null;
        }

        return new OAuthOptions
        {
            ClientId = clientId,
            AuthorizationEndpoint = authorizationEndpoint,
            TokenEndpoint = tokenEndpoint,
            RedirectUri = Environment.GetEnvironmentVariable("TASKBARBANNER_OAUTH_REDIRECT") ?? string.Empty,
            RevocationEndpoint = Environment.GetEnvironmentVariable("TASKBARBANNER_OAUTH_REVOKE_URL") ?? string.Empty,
            Scope = Environment.GetEnvironmentVariable("TASKBARBANNER_OAUTH_SCOPE") ?? "openid profile offline_access",
        };
    }

    private static bool IsDevAuthEnabled()
    {
        string value = Environment.GetEnvironmentVariable("TASKBARBANNER_DEV_AUTH") ?? string.Empty;
        return value.Equals("1", StringComparison.OrdinalIgnoreCase)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private void FocusMainWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        _mainWindow.Show();
        _mainWindow.Activate();

        IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(_mainWindow).Handle;
        if (hwnd != IntPtr.Zero)
        {
            ForegroundNative.SetForegroundWindow(hwnd);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TaskbarBanner");
            Directory.CreateDirectory(dir);
            File.AppendAllText(
                Path.Combine(dir, "error.log"),
                $"[{DateTime.UtcNow:o}] {e.Exception}{Environment.NewLine}");
        }
        catch
        {
            // ignore logging failures
        }

        MessageBox.Show(
            $"Unexpected error: {e.Exception.Message}",
            "Taskbar Banner",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
