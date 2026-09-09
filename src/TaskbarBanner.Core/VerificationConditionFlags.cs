namespace TaskbarBanner.Core;

[Flags]
public enum VerificationConditionFlags
{
    None = 0,
    SystemInactive = 1 << 0,
    TaskbarNotEligible = 1 << 1,
    BannerNotVisible = 1 << 2,
    IdleTooLong = 1 << 3,
}
