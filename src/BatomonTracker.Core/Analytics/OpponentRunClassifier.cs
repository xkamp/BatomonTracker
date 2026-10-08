namespace BatomonTracker.Core.Analytics;

public sealed class OpponentRunClassifier
{
    public IReadOnlyList<OpponentRunSummary> Group(IReadOnlyCollection<OpponentCacheSnapshot> snapshots)
    {
        return snapshots
            .GroupBy(snapshot => new { snapshot.Dataset.DatasetId, snapshot.UserId, snapshot.RunId })
            .Select(group =>
            {
                OpponentCacheSnapshot[] ordered = group
                    .GroupBy(snapshot => new { snapshot.Round, snapshot.SnapshotHash })
                    .Select(item => item.First())
                    .OrderBy(snapshot => snapshot.Round)
                    .ToArray();
                bool reachedTenWins = ordered.Any(snapshot =>
                    snapshot.WinsBefore is >= 10 || (snapshot.WinsBefore is not null && snapshot.Won && snapshot.WinsBefore.Value + 1 >= 10));
                bool reachedZeroLives = ordered.Any(snapshot => snapshot.LivesLost is >= 10);
                return new OpponentRunSummary(
                    $"{ordered[0].Dataset.DatasetId}:{ordered[0].UserId}:{ordered[0].RunId}",
                    ordered[0].Dataset,
                    ordered[0].UserId,
                    ordered[0].RunId,
                    ordered.Length,
                    ordered.Min(snapshot => (int?)snapshot.Round),
                    ordered.Max(snapshot => (int?)snapshot.Round),
                    reachedTenWins,
                    reachedZeroLives,
                    reachedTenWins || reachedZeroLives,
                    ordered);
            })
            .ToArray();
    }
}
