namespace Digger.Data.Common.Enums;

public enum MovieStatus
{
    Stopped,
    PendingCheck,
    Checking,
    PendingDownload,
    Downloading,
    PendingSeed,
    Seeding,
    NotEnqueued,
    Enqueued,
    Failed,
    RolledOut,
    Complete,
    Skipped
}
