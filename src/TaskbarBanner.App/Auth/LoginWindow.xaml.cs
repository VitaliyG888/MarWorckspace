using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace TaskbarBanner.App.Auth;

public partial class LoginWindow : Window
{
    private readonly Uri _startUri;
    private readonly string _redirectPrefix;
    private readonly TaskCompletionSource<string?> _callback = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _completed;

    public LoginWindow(Uri startUri, string redirectPrefix)
    {
        InitializeComponent();
        _startUri = startUri;
        _redirectPrefix = redirectPrefix;
        Loaded += OnLoaded;
        Closed += (_, _) => Complete(null);
    }

    public Task<string?> CallbackTask => _callback.Task;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await Browser.EnsureCoreWebView2Async();
            Browser.CoreWebView2.NavigationStarting += OnNavigationStarting;
            Browser.Source = _startUri;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"WebView2 is not available: {ex.Message}",
                "Sign in",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            Complete(null);
        }
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!e.Uri.StartsWith(_redirectPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        e.Cancel = true;
        Complete(e.Uri);
    }

    private void Complete(string? callbackUrl)
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        _callback.TrySetResult(callbackUrl);
        if (IsLoaded && IsVisible)
        {
            Close();
        }
    }
}
