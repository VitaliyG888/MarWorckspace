using Microsoft.Data.Sqlite;
using TaskbarBanner.Core.Reporting;

namespace TaskbarBanner.Core.Tests;

internal sealed class TempDatabase : IDisposable
{
    public TempDatabase()
    {
        Directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tbb-tests", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Directory);
        DatabasePath = System.IO.Path.Combine(Directory, "report.db");
    }

    public string Directory { get; }

    public string DatabasePath { get; }

    public SqliteMinuteStore Open() => SqliteMinuteStore.Open(DatabasePath);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch
        {
            // best effort cleanup
        }
    }
}
