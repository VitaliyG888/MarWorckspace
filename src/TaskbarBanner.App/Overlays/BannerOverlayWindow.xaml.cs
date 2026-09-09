using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using TaskbarBanner.App.Ads;

namespace TaskbarBanner.App.Overlays;

public partial class BannerOverlayWindow : Window
{
    private const long WsExNoActivate = 0x08000000L;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExTopmost = 0x00000008L;
    private const int GwlExStyle = -20;

    private static readonly HttpClient ImageHttp = new() { Timeout = TimeSpan.FromSeconds(6) };

    public event Action? BannerClicked;

    private string? _lastImageUrl;

    public BannerOverlayWindow()
    {
        InitializeComponent();
    }

    public void SetAd(AdContent ad)
    {
        LogoTextBlock.Text = string.IsNullOrWhiteSpace(ad.LogoText) ? "AD" : ad.LogoText;
        HeadlineTextBlock.Text = ad.Headline;
        TaglineTextBlock.Text = ad.Tagline;
        ToolTip = string.IsNullOrWhiteSpace(ad.WebsiteUrl) ? ad.Headline : ad.WebsiteUrl;

        string? imageUrl = ad.LogoImageUrl;
        if (!string.Equals(imageUrl, _lastImageUrl, StringComparison.Ordinal))
        {
            _lastImageUrl = imageUrl;
            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                ShowTextLogo();
            }
            else
            {
                _ = LoadLogoAsync(imageUrl);
            }
        }
    }

    private async Task LoadLogoAsync(string url)
    {
        try
        {
            byte[] bytes = await ImageHttp.GetByteArrayAsync(url);
            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();

            if (string.Equals(_lastImageUrl, url, StringComparison.Ordinal))
            {
                LogoImage.Source = bitmap;
                LogoImage.Visibility = Visibility.Visible;
                LogoTextBlock.Visibility = Visibility.Collapsed;
            }
        }
        catch
        {
            if (string.Equals(_lastImageUrl, url, StringComparison.Ordinal))
            {
                ShowTextLogo();
            }
        }
    }

    private void ShowTextLogo()
    {
        LogoImage.Source = null;
        LogoImage.Visibility = Visibility.Collapsed;
        LogoTextBlock.Visibility = Visibility.Visible;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        long exStyle = GetWindowLongPtr(hwnd, GwlExStyle);
        exStyle |= WsExNoActivate | WsExToolWindow | WsExTopmost;
        SetWindowLongPtr(hwnd, GwlExStyle, exStyle);
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
        BannerClicked?.Invoke();
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern long GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern long SetWindowLongPtr64(IntPtr hWnd, int nIndex, long dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    private static long GetWindowLongPtr(IntPtr hWnd, int nIndex)
        => IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);

    private static long SetWindowLongPtr(IntPtr hWnd, int nIndex, long value)
        => IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, value) : SetWindowLong32(hWnd, nIndex, (int)value);
}
