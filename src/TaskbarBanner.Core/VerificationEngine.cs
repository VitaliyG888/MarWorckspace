namespace TaskbarBanner.Core;

public sealed class VerificationEngine
{
    public const int PrimeEpochCount = 5;
    public static readonly TimeSpan IdleThreshold = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan EpochDuration = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan BoundaryGrace = TimeSpan.FromSeconds(2);

    private readonly IVerificationConditions _conditions;
    private readonly IClock _clock;

    private VerificationState _state = VerificationState.Idle;
    private int _primeEpochCount;
    private int _totalCountedMinutes;

    private bool _initialized;
    private DateTimeOffset _epochStartUtc;
    private bool _observeEpoch;
    private bool _epochCompromised;
    private bool _activitySeenInEpoch;
    private int _failedTickCount;
    private VerificationConditionFlags _failureFlags;
    private long _activityBaselineInputTick;

    public VerificationEngine(IVerificationConditions conditions, IClock? clock = null)
    {
        _conditions = conditions ?? throw new ArgumentNullException(nameof(conditions));
        _clock = clock ?? new SystemClock();
    }

    public event EventHandler<EpochFinalizedEventArgs>? EpochFinalized;

    public event EventHandler<MinuteVerifiedEventArgs>? MinuteVerified;

    public VerificationState State => _state;

    public int PrimingProgress => _state switch
    {
        VerificationState.Counting => PrimeEpochCount,
        VerificationState.Priming => _primeEpochCount,
        _ => 0,
    };

    public int TotalCountedMinutes => _totalCountedMinutes;

    public DateTimeOffset CurrentEpochStartUtc => _epochStartUtc;

    public void Tick() => Tick(_clock.UtcNow);

    public void Tick(DateTimeOffset nowUtc)
    {
        DateTimeOffset floor = FloorMinuteUtc(nowUtc);

        if (!_initialized)
        {
            _initialized = true;
            _epochStartUtc = floor;
            _observeEpoch = nowUtc - floor <= BoundaryGrace;
            _activityBaselineInputTick = ReadLastInputTick();
            ResetEpochAccumulators();
        }
        else if (floor != _epochStartUtc)
        {
            long missedMinutes = (long)(floor - _epochStartUtc).TotalMinutes;
            bool continuous = missedMinutes == 1;

            FinalizeEpoch(_observeEpoch && continuous);

            _epochStartUtc = floor;
            _observeEpoch = continuous;
            ResetEpochAccumulators();
            _activityBaselineInputTick = ReadLastInputTick();
        }

        EvaluateCurrentTick();
    }

    private void EvaluateCurrentTick()
    {
        bool systemActive = _conditions.IsSystemActive();
        bool taskbarEligible = _conditions.IsTaskbarEligible();
        bool bannerVisible = _conditions.IsAnyBannerVisible();
        TimeSpan idle = _conditions.GetIdleTime();
        bool idleOk = idle <= IdleThreshold;
        long lastInputTick = _conditions.GetLastInputTickMs();

        if (lastInputTick > _activityBaselineInputTick)
        {
            _activitySeenInEpoch = true;
        }

        var flags = VerificationConditionFlags.None;
        if (!systemActive)
        {
            flags |= VerificationConditionFlags.SystemInactive;
        }

        if (!taskbarEligible)
        {
            flags |= VerificationConditionFlags.TaskbarNotEligible;
        }

        if (!bannerVisible)
        {
            flags |= VerificationConditionFlags.BannerNotVisible;
        }

        if (!idleOk)
        {
            flags |= VerificationConditionFlags.IdleTooLong;
        }

        if (flags != VerificationConditionFlags.None)
        {
            _epochCompromised = true;
            _failedTickCount++;
            _failureFlags |= flags;
        }
    }

    private void FinalizeEpoch(bool endingObserved)
    {
        bool epochEligible = endingObserved && !_epochCompromised && _activitySeenInEpoch;
        DateTimeOffset endUtc = _epochStartUtc + EpochDuration;

        bool minuteCounted = false;
        if (epochEligible)
        {
            switch (_state)
            {
                case VerificationState.Idle:
                    _state = VerificationState.Priming;
                    _primeEpochCount = 1;
                    break;
                case VerificationState.Priming:
                    _primeEpochCount++;
                    if (_primeEpochCount >= PrimeEpochCount)
                    {
                        _state = VerificationState.Counting;
                        _primeEpochCount = 0;
                    }

                    break;
                case VerificationState.Counting:
                    _totalCountedMinutes++;
                    minuteCounted = true;
                    break;
            }
        }
        else
        {
            _state = VerificationState.Idle;
            _primeEpochCount = 0;
        }

        EpochFinalized?.Invoke(this, new EpochFinalizedEventArgs
        {
            EpochStartUtc = _epochStartUtc,
            EpochEndUtc = endUtc,
            FullyObserved = endingObserved,
            EpochEligible = epochEligible,
            FailureFlags = _failureFlags,
            FailedTickCount = _failedTickCount,
            ActivityChangeConfirmed = _activitySeenInEpoch,
            StateAfter = _state,
            MinuteCounted = minuteCounted,
            PrimeEpochCount = _primeEpochCount,
            TotalCountedMinutes = _totalCountedMinutes,
        });

        if (minuteCounted)
        {
            MinuteVerified?.Invoke(this, new MinuteVerifiedEventArgs
            {
                MinuteStartUtc = _epochStartUtc,
                MinuteEndUtc = endUtc,
            });
        }
    }

    private void ResetEpochAccumulators()
    {
        _epochCompromised = false;
        _activitySeenInEpoch = false;
        _failedTickCount = 0;
        _failureFlags = VerificationConditionFlags.None;
    }

    private long ReadLastInputTick() => _conditions.GetLastInputTickMs();

    private static DateTimeOffset FloorMinuteUtc(DateTimeOffset now)
    {
        DateTimeOffset utc = now.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, TimeSpan.Zero);
    }
}
