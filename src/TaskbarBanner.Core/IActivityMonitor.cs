namespace TaskbarBanner.Core;

public interface IActivityMonitor
{
    TimeSpan GetIdleTime();

    DateTimeOffset GetLastInputUtc();

    long GetLastInputTickMs();
}
