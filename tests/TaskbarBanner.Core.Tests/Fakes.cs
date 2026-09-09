namespace TaskbarBanner.Core.Tests;

internal sealed class FakeConditions : IVerificationConditions
{
    public bool SystemActive { get; set; } = true;
    public bool TaskbarOk { get; set; } = true;
    public bool BannerOk { get; set; } = true;

    private long _lastInputTick = 100_000;
    private TimeSpan _idle;

    public void RecordInput()
    {
        _lastInputTick++;
        _idle = TimeSpan.Zero;
    }

    public void SetIdle(TimeSpan idle) => _idle = idle;

    public void AdvanceIdleBySecond() => _idle += TimeSpan.FromSeconds(1);

    public bool IsSystemActive() => SystemActive;

    public bool IsTaskbarEligible() => TaskbarOk;

    public bool IsAnyBannerVisible() => BannerOk;

    public TimeSpan GetIdleTime() => _idle;

    public long GetLastInputTickMs() => _lastInputTick;
}

internal sealed class EngineHarness
{
    public readonly FakeConditions Conditions = new();
    public readonly VerificationEngine Engine;
    public readonly List<EpochFinalizedEventArgs> Epochs = new();
    public readonly List<MinuteVerifiedEventArgs> Verified = new();
    public readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public EngineHarness()
    {
        Engine = new VerificationEngine(Conditions);
        Engine.EpochFinalized += (_, a) => Epochs.Add(a);
        Engine.MinuteVerified += (_, a) => Verified.Add(a);
    }

    public void ActiveSecond(int second)
    {
        Conditions.RecordInput();
        Engine.Tick(Start.AddSeconds(second));
    }

    public void IdleSecond(int second)
    {
        Conditions.AdvanceIdleBySecond();
        Engine.Tick(Start.AddSeconds(second));
    }

    public void StaticIdleSecond(int second)
    {
        Conditions.SetIdle(TimeSpan.Zero);
        Engine.Tick(Start.AddSeconds(second));
    }

    public void RunActiveThrough(int second)
    {
        for (int s = 0; s <= second; s++)
        {
            ActiveSecond(s);
        }
    }

    public void ContinueActive(int fromInclusive, int toInclusive)
    {
        for (int s = fromInclusive; s <= toInclusive; s++)
        {
            ActiveSecond(s);
        }
    }

    public int VerifiedCount => Verified.Count;
}
