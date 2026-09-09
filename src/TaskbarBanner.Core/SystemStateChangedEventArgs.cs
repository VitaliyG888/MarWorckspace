namespace TaskbarBanner.Core;

public enum SystemStateChangeKind
{
    Unknown = 0,
    SessionLock,
    SessionUnlock,
    ConsoleConnect,
    ConsoleDisconnect,
    RemoteConnect,
    RemoteDisconnect,
    SessionLogon,
    SessionLogoff,
    SessionEnding,
    Suspend,
    Resume,
    PowerStatusChange,
}

public sealed class SystemStateChangedEventArgs : EventArgs
{
    public required SystemStateChangeKind Kind { get; init; }
    public required SystemStateSnapshot Snapshot { get; init; }
    public required string Description { get; init; }
}
