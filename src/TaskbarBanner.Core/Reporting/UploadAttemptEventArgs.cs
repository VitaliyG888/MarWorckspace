namespace TaskbarBanner.Core.Reporting;

public sealed class UploadAttemptEventArgs : EventArgs
{
    public UploadAttemptEventArgs(int uploadedCount, bool succeeded)
    {
        UploadedCount = uploadedCount;
        Succeeded = succeeded;
    }

    public int UploadedCount { get; }

    public bool Succeeded { get; }
}
