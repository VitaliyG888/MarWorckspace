namespace TaskbarBanner.Core;

public interface ISystemStateMonitor : IDisposable
{
    event EventHandler<SystemStateChangedEventArgs>? StateChanged;

    SystemStateSnapshot Snapshot { get; }
}
