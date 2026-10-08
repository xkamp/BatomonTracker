namespace BatomonTracker.Core.Analytics;

public sealed record OpponentCacheDataset(
    string DatasetId,
    string FileName,
    string Mode,
    string SetId,
    string? Segment);

public sealed record OpponentCacheMetadata(
    string DatasetId,
    string? Etag,
    DateTimeOffset? FetchedAt);

public sealed record OpponentCacheUnit(
    int Position,
    string SpeciesId,
    int? Level,
    bool? IsShiny,
    IReadOnlyList<string> ExtraTypes,
    string? PermanentBuffsJson,
    string? BlackboardJson);

public sealed record OpponentCacheSnapshot(
    OpponentCacheDataset Dataset,
    string UserId,
    string? DisplayName,
    int? Mmr,
    string? RankTier,
    int Round,
    string? SetId,
    int? Version,
    string RunId,
    bool Won,
    int? WinsBefore,
    int? LivesLost,
    string? TrainerId,
    int? ShopRank,
    IReadOnlyList<OpponentCacheUnit> Units,
    IReadOnlyList<string> TrinketIds,
    IReadOnlyList<string> UsedTrinketIds,
    string RawJson,
    string SnapshotHash);

public enum OpponentRunPhase
{
    Standard,
    Extended
}

public sealed record OpponentRunSummary(
    string RunKey,
    OpponentCacheDataset Dataset,
    string UserId,
    string RunId,
    int SnapshotCount,
    int? FirstRound,
    int? LastRound,
    bool ReachedTenWins,
    bool ReachedZeroLives,
    bool IsComplete,
    IReadOnlyList<OpponentCacheSnapshot> Snapshots)
{
    public IReadOnlyList<OpponentCacheSnapshot> StandardSnapshots =>
        Snapshots.Where(snapshot => snapshot.WinsBefore is null || snapshot.WinsBefore.Value < 10).ToArray();

    public IReadOnlyList<OpponentCacheSnapshot> ExtendedSnapshots =>
        Snapshots.Where(snapshot => snapshot.WinsBefore is >= 10).ToArray();
}
