using System.Diagnostics;

namespace TaskbarBanner.App;

public static class OsInfo
{
    public static string Describe()
    {
        try
        {
            using Microsoft.Win32.RegistryKey? key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            string? product = key?.GetValue("ProductName") as string ?? "Windows";
            string? build = key?.GetValue("CurrentBuildNumber") as string ?? "?";
            string edition = product.Contains("Windows 11", StringComparison.OrdinalIgnoreCase)
                ? "Windows 11"
                : product.Contains("Windows 10", StringComparison.OrdinalIgnoreCase)
                    ? "Windows 10"
                    : product;
            return $"{edition} (build {build})";
        }
        catch
        {
            return $"Windows (Environment.OSVersion {Environment.OSVersion.Version})";
        }
    }
}
