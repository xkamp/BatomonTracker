namespace BatomonTracker.Core.Models;

public sealed record StoredRunSummary(
    string RunId,
    DateTimeOffset LastSeen,
    string? TrainerId,
    int? Round,
    int? Wins,
    int? Lives,
    int? Gold,
    int SnapshotCount,
    int EventCount);
