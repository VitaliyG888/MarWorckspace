using TaskbarBanner.Core.Geometry;

namespace TaskbarBanner.Core;

public sealed record TaskbarInfo
{
    public required IntPtr Hwnd { get; init; }
    public required bool IsPrimary { get; init; }
    public required string MonitorDevice { get; init; }
    public required RectI MonitorBounds { get; init; }
    public required RectI Bounds { get; init; }
    public required RectI WorkArea { get; init; }
    public required DockEdge DockEdge { get; init; }
    public required bool IsAutoHideEnabled { get; init; }
    public required bool IsEffectivelyVisible { get; init; }
    public required double Dpi { get; init; }
}
