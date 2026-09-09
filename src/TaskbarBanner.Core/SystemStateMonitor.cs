using Microsoft.Win32;

namespace TaskbarBanner.Core;

public sealed class SystemStateMonitor : ISystemStateMonitor
{
    private readonly object _gate = new();
    private bool _locked;
    private bool _suspended;
    private bool _disconnected;
    private bool _disposed;

    public event EventHandler<SystemStateChangedEventArgs>? StateChanged;

    public SystemStateMonitor()
    {
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionEnding += OnSessionEnding;
    }

    public SystemStateSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return new SystemStateSnapshot(DateTimeOffset.UtcNow, !_suspended, !_locked, !_disconnected);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionEnding -= OnSessionEnding;
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        switch (e.Reason)
        {
            case SessionSwitchReason.SessionLock:
                Apply(locked: true, kind: SystemStateChangeKind.SessionLock, description: "Session locked");
                break;
            case SessionSwitchReason.SessionUnlock:
                Apply(locked: false, kind: SystemStateChangeKind.SessionUnlock, description: "Session unlocked");
                break;
            case SessionSwitchReason.ConsoleConnect:
                Apply(disconnected: false, kind: SystemStateChangeKind.ConsoleConnect, description: "Console connected");
                break;
            case SessionSwitchReason.ConsoleDisconnect:
                Apply(disconnected: true, kind: SystemStateChangeKind.ConsoleDisconnect, description: "Console disconnected");
                break;
            case SessionSwitchReason.RemoteConnect:
                Apply(disconnected: false, kind: SystemStateChangeKind.RemoteConnect, description: "Remote session connected");
                break;
            case SessionSwitchReason.RemoteDisconnect:
                Apply(disconnected: true, kind: SystemStateChangeKind.RemoteDisconnect, description: "Remote session disconnected");
                break;
            case SessionSwitchReason.SessionLogon:
                Apply(disconnected: false, kind: SystemStateChangeKind.SessionLogon, description: "User logged on");
                break;
            case SessionSwitchReason.SessionLogoff:
                Apply(disconnected: true, kind: SystemStateChangeKind.SessionLogoff, description: "User logged off");
                break;
        }
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        switch (e.Mode)
        {
            case PowerModes.Suspend:
                Apply(suspended: true, kind: SystemStateChangeKind.Suspend, description: "System suspended (sleep)");
                break;
            case PowerModes.Resume:
                Apply(suspended: false, kind: SystemStateChangeKind.Resume, description: "System resumed");
                break;
            case PowerModes.StatusChange:
                Raise(SystemStateChangeKind.PowerStatusChange, "Power status changed");
                break;
        }
    }

    private void OnSessionEnding(object? sender, SessionEndingEventArgs e)
        => Apply(disconnected: true, kind: SystemStateChangeKind.SessionEnding, description: "Session ending (logoff or shutdown)");

    private void Apply(bool? locked = null, bool? suspended = null, bool? disconnected = null, SystemStateChangeKind? kind = null, string? description = null)
    {
        lock (_gate)
        {
            if (locked.HasValue)
            {
                _locked = locked.Value;
            }

            if (suspended.HasValue)
            {
                _suspended = suspended.Value;
            }

            if (disconnected.HasValue)
            {
                _disconnected = disconnected.Value;
            }
        }

        if (kind is { } k && description is { } d)
        {
            Raise(k, d);
        }
    }

    private void Raise(SystemStateChangeKind kind, string description)
    {
        SystemStateSnapshot snapshot;
        lock (_gate)
        {
            snapshot = new SystemStateSnapshot(DateTimeOffset.UtcNow, !_suspended, !_locked, !_disconnected);
        }

        StateChanged?.Invoke(this, new SystemStateChangedEventArgs
        {
            Kind = kind,
            Snapshot = snapshot,
            Description = description,
        });
    }
}
