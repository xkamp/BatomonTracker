namespace BatomonTracker.Core.Models;

public sealed record AccountFiles(
    string AccountDirectory,
    string? RunSavePath,
    string? RunSaveBackupPath,
    string? ProfileSnapshotPath)
{
    public bool HasLiveRun => RunSavePath is not null;
}
