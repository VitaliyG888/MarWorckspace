using Xunit;

namespace TaskbarBanner.Core.Tests;

public class VerificationEngineTests
{
    [Fact]
    public void FiveEligibleEpochs_CompletePriming_WithoutCounting()
    {
        EngineHarness h = new();
        h.RunActiveThrough(300);

        Assert.Equal(VerificationState.Counting, h.Engine.State);
        Assert.Equal(0, h.Engine.TotalCountedMinutes);
        Assert.Equal(5, h.Epochs.Count);
        Assert.All(h.Epochs, e =>
        {
            Assert.True(e.EpochEligible);
            Assert.True(e.FullyObserved);
            Assert.False(e.MinuteCounted);
        });
        Assert.Empty(h.Verified);
    }

    [Fact]
    public void SixthEligibleEpoch_CountsVerifiedMinute_AtUtcBoundary()
    {
        EngineHarness h = new();
        h.RunActiveThrough(360);

        Assert.Equal(VerificationState.Counting, h.Engine.State);
        Assert.Equal(1, h.Engine.TotalCountedMinutes);
        MinuteVerifiedEventArgs minute = Assert.Single(h.Verified);
        Assert.Equal(h.Start.AddMinutes(5), minute.MinuteStartUtc);
        Assert.Equal(h.Start.AddMinutes(6), minute.MinuteEndUtc);
    }

    [Fact]
    public void ConsecutiveEligibleEpochs_EachCountOnce()
    {
        EngineHarness h = new();
        h.RunActiveThrough(300);
        h.ContinueActive(301, 420);

        Assert.Equal(2, h.Engine.TotalCountedMinutes);
        Assert.Equal(2, h.Verified.Count);
        Assert.Equal(h.Start.AddMinutes(5), h.Verified[0].MinuteStartUtc);
        Assert.Equal(h.Start.AddMinutes(6), h.Verified[1].MinuteStartUtc);
    }

    [Fact]
    public void LockedSession_MidStream_InvalidatesEpoch_AndRequiresFullReprime()
    {
        EngineHarness h = new();
        h.RunActiveThrough(120);

        h.Conditions.SystemActive = false;
        for (int s = 121; s <= 300; s++)
        {
            h.ActiveSecond(s);
        }

        Assert.Equal(0, h.Engine.TotalCountedMinutes);
        Assert.Equal(VerificationState.Idle, h.Engine.State);
        Assert.Contains(h.Epochs, e => !e.EpochEligible && (e.FailureFlags & VerificationConditionFlags.SystemInactive) != 0);

        h.Conditions.SystemActive = true;
        h.ContinueActive(301, 600);
        Assert.Equal(0, h.Engine.TotalCountedMinutes);
        Assert.Equal(VerificationState.Priming, h.Engine.State);

        h.ContinueActive(601, 720);
        Assert.Equal(1, h.Engine.TotalCountedMinutes);
        MinuteVerifiedEventArgs minute = Assert.Single(h.Verified);
        Assert.Equal(h.Start.AddMinutes(11), minute.MinuteStartUtc);
    }

    [Fact]
    public void MidEpochTaskbarFailure_DiscardsWholeEpoch_AndRestartsPrime()
    {
        EngineHarness h = new();
        h.RunActiveThrough(60);

        for (int s = 61; s <= 63; s++)
        {
            h.Conditions.TaskbarOk = false;
            h.ActiveSecond(s);
        }

        h.Conditions.TaskbarOk = true;
        h.ContinueActive(64, 120);

        EpochFinalizedEventArgs bad = Assert.Single(h.Epochs, e => e.EpochStartUtc == h.Start.AddMinutes(1));
        Assert.False(bad.EpochEligible);
        Assert.True(bad.FailedTickCount > 0);
        Assert.True((bad.FailureFlags & VerificationConditionFlags.TaskbarNotEligible) != 0);
        Assert.Equal(VerificationState.Idle, bad.StateAfter);
        Assert.Equal(0, h.Engine.TotalCountedMinutes);

        h.ContinueActive(121, 480);
        Assert.Equal(1, h.Engine.TotalCountedMinutes);
    }

    [Fact]
    public void IdleBeyond60s_StopsCounting_AndNeverCountsAgainWhileIdle()
    {
        EngineHarness h = new();
        h.RunActiveThrough(360);
        Assert.Equal(1, h.Engine.TotalCountedMinutes);

        for (int s = 361; s <= 900; s++)
        {
            h.IdleSecond(s);
        }

        Assert.Equal(1, h.Engine.TotalCountedMinutes);
        Assert.Equal(VerificationState.Idle, h.Engine.State);
        Assert.Single(h.Verified);
        Assert.Contains(h.Epochs, e =>
            (e.FailureFlags & VerificationConditionFlags.IdleTooLong) != 0);
    }

    [Fact]
    public void ActivityChangeMustOccurWithinEpoch_OtherwiseNotEligible()
    {
        EngineHarness h = new();
        h.RunActiveThrough(60);

        for (int s = 61; s <= 120; s++)
        {
            h.StaticIdleSecond(s);
        }

        EpochFinalizedEventArgs bad = h.Epochs[^1];
        Assert.False(bad.EpochEligible);
        Assert.False(bad.ActivityChangeConfirmed);
        Assert.Equal(VerificationState.Idle, bad.StateAfter);
        Assert.Empty(h.Verified);

        h.ContinueActive(121, 480);
        Assert.Equal(1, h.Engine.TotalCountedMinutes);
    }

    [Fact]
    public void NoBannerVisible_NeverBecomesEligible()
    {
        EngineHarness h = new();
        h.Conditions.BannerOk = false;
        h.RunActiveThrough(420);

        Assert.Equal(VerificationState.Idle, h.Engine.State);
        Assert.Equal(0, h.Engine.TotalCountedMinutes);
        Assert.All(h.Epochs, e => Assert.False(e.EpochEligible));
        Assert.Contains(h.Epochs, e => (e.FailureFlags & VerificationConditionFlags.BannerNotVisible) != 0);
        Assert.Empty(h.Verified);
    }

    [Fact]
    public void AutoHiddenTaskbar_NeverBecomesEligible()
    {
        EngineHarness h = new();
        h.Conditions.TaskbarOk = false;
        h.RunActiveThrough(420);

        Assert.Equal(VerificationState.Idle, h.Engine.State);
        Assert.Equal(0, h.Engine.TotalCountedMinutes);
        Assert.Contains(h.Epochs, e => (e.FailureFlags & VerificationConditionFlags.TaskbarNotEligible) != 0);
        Assert.Empty(h.Verified);
    }

    [Fact]
    public void TimeGapJump_InvalidatesCurrentEpoch_AndRecoversWithFreshPrime()
    {
        EngineHarness h = new();
        h.RunActiveThrough(60);

        h.ActiveSecond(300);
        Assert.Equal(VerificationState.Idle, h.Engine.State);
        EpochFinalizedEventArgs gapEpoch = h.Epochs[^1];
        Assert.False(gapEpoch.EpochEligible);
        Assert.False(gapEpoch.FullyObserved);

        h.ContinueActive(301, 660);
        Assert.Equal(0, h.Engine.TotalCountedMinutes);
        Assert.Equal(VerificationState.Counting, h.Engine.State);

        h.ContinueActive(661, 720);
        Assert.Equal(1, h.Engine.TotalCountedMinutes);
    }

    [Fact]
    public void EpochFinalized_ReportedForEveryObservedEpoch()
    {
        EngineHarness h = new();
        h.RunActiveThrough(360);

        Assert.Equal(6, h.Epochs.Count);
        for (int i = 0; i < h.Epochs.Count; i++)
        {
            Assert.Equal(h.Start.AddMinutes(i), h.Epochs[i].EpochStartUtc);
            Assert.Equal(h.Start.AddMinutes(i + 1), h.Epochs[i].EpochEndUtc);
        }
    }
}
