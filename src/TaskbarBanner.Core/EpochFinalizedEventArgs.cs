namespace TaskbarBanner.Core;

public sealed class EpochFinalizedEventArgs : EventArgs
{
    public required DateTimeOffset EpochStartUtc { get; init; }
    public required DateTimeOffset EpochEndUtc { get; init; }
    public required bool FullyObserved { get; init; }
    public required bool EpochEligible { get; init; }
    public required VerificationConditionFlags FailureFlags { get; init; }
    public required int FailedTickCount { get; init; }
    public required bool ActivityChangeConfirmed { get; init; }
    public required VerificationState StateAfter { get; init; }
    public required bool MinuteCounted { get; init; }
    public required int PrimeEpochCount { get; init; }
    public required int TotalCountedMinutes { get; init; }
}
