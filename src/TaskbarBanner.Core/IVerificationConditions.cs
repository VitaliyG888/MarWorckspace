namespace TaskbarBanner.Core;

public interface IVerificationConditions
{
    bool IsSystemActive();

    bool IsTaskbarEligible();

    bool IsAnyBannerVisible();

    TimeSpan GetIdleTime();

    long GetLastInputTickMs();
}
