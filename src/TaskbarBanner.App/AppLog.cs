using System.IO;

namespace TaskbarBanner.App;

public static class AppLog
{
    private static readonly object Lock = new();

    private static readonly string PathValue = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TaskbarBanner",
        "events.log");

    public static string FilePath => PathValue;

    public static void Write(string line)
    {
        try
        {
            string? directory = Path.GetDirectoryName(PathValue);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            lock (Lock)
            {
                File.AppendAllText(PathValue, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {line}{Environment.NewLine}");
            }
        }
        catch
        {
            // logging must never break the app
        }
    }
}
