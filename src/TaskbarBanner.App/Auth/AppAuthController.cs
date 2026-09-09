using System.IO;
using System.Net.Http;
using TaskbarBanner.App.Overlays;
using TaskbarBanner.App.Reporting;
using TaskbarBanner.Core;
using TaskbarBanner.Core.Auth;

namespace TaskbarBanner.App.Auth;

public sealed class AppAuthController
{
    private readonly AuthSessionManager? _manager;
    private readonly OverlayManager _overlayManager;
    private readonly ReportService _reportService;
    private readonly bool _devAuthEnabled;
    private bool _demoAuthenticated;
    private bool _wasAuthenticated;

    public AppAuthController(
        OAuthOptions? options,
        HttpClient http,
        OverlayManager overlayManager,
        ReportService reportService,
        bool devAuthEnabled)
    {
        _overlayManager = overlayManager;
        _reportService = reportService;
        _devAuthEnabled = devAuthEnabled;

        if (options is not null)
        {
            string tokenPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TaskbarBanner",
                "auth.json");
            _manager = new AuthSessionManager(
                options,
                new DpapiTokenStore(tokenPath),
                new OAuthTokenClient(options, http),
                new SystemClock());
            _manager.StateChanged += (_, e) => OnAuthenticated(e.IsAuthenticated, e.Description);
        }
    }

    public event EventHandler? Changed;

    public bool NeedsRefresh => _manager?.NeedsRefresh == true;

    public string? AccessToken => _demoAuthenticated ? null : _manager?.AccessToken;

    public async Task<string?> GetAccessTokenAsync()
    {
        if (_demoAuthenticated || _manager is null)
        {
            return null;
        }

        if (_manager.NeedsRefresh)
        {
            await _manager.TryRefreshAsync(CancellationToken.None);
        }

        return _manager.AccessToken;
    }

    public async Task<bool> TryRefreshAsync()
    {
        if (_manager is null)
        {
            return false;
        }

        return await _manager.TryRefreshAsync(CancellationToken.None);
    }

    public bool CanSignIn => _manager is not null || _devAuthEnabled;

    public bool IsAuthenticated => _demoAuthenticated || _manager?.IsAuthenticated == true;

    public string StatusText => _demoAuthenticated
        ? "Signed in (dev demo)"
        : _manager?.Description
          ?? (_devAuthEnabled
              ? "Not signed in (dev demo available)"
              : "Not signed in - OAuth not configured");

    public async Task StartAsync()
    {
        if (_manager is null)
        {
            OnAuthenticated(false, StatusText);
            return;
        }

        try
        {
            await _manager.LoadAsync(CancellationToken.None);
        }
        catch
        {
            // startup load is best-effort; user can sign in manually
        }
    }

    public async Task<bool> SignInAsync()
    {
        if (_manager is not null)
        {
            Uri startUri;
            try
            {
                startUri = await _manager.StartLoginAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                OnAuthenticated(false, $"Sign in error: {ex.Message}");
                return false;
            }

            var window = new LoginWindow(startUri, _manager.CurrentRedirectUri!);
            window.Show();
            window.Activate();
            string? callback = await window.CallbackTask;

            if (callback is null)
            {
                _manager.CancelLogin();
                return false;
            }

            try
            {
                await _manager.CompleteLoginAsync(callback, CancellationToken.None);
                return _manager.IsAuthenticated;
            }
            catch (Exception ex)
            {
                OnAuthenticated(false, $"Sign in failed: {ex.Message}");
                return false;
            }
        }

        if (_devAuthEnabled)
        {
            _demoAuthenticated = true;
            OnAuthenticated(true, "Signed in (dev demo)");
            return true;
        }

        return false;
    }

    public async Task SignOutAsync()
    {
        if (_manager is not null)
        {
            try
            {
                await _manager.LogoutAsync(CancellationToken.None);
            }
            catch
            {
                // best-effort
            }
        }

        _demoAuthenticated = false;
        if (_manager is null)
        {
            OnAuthenticated(false, "Signed out");
        }
    }

    private void OnAuthenticated(bool authenticated, string description)
    {
        _overlayManager.SessionEnabled = authenticated;
        if (_wasAuthenticated && !authenticated)
        {
            _reportService.ResetSession();
        }

        _wasAuthenticated = authenticated;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
