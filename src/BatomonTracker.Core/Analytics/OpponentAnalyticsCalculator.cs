namespace BatomonTracker.Core.Analytics;

public enum OpponentAnalyticsPhase
{
    All,
    Standard,
    Extended
}

public sealed record BatomonAnalyticsResult(
    string SpeciesId,
    int BattleCount,
    int BattleWins,
    int RunCount,
    int CompletedRunCount,
    int RunWins)
{
    public double BattleWinRate => BattleCount == 0 ? 0 : (double)BattleWins / BattleCount;

    public double RunWinRate => CompletedRunCount == 0 ? 0 : (double)RunWins / CompletedRunCount;
}

public sealed record BatomonCompanionResult(
    string SpeciesId,
    int SharedRunCount,
    int CompletedSharedRunCount,
    int SharedRunWins)
{
    public double SharedRunWinRate => CompletedSharedRunCount == 0 ? 0 : (double)SharedRunWins / CompletedSharedRunCount;
}

public sealed record RoundAnalyticsResult(int Round, int BattleCount, int BattleWins)
{
    public double BattleWinRate => BattleCount == 0 ? 0 : (double)BattleWins / BattleCount;
}

public sealed class OpponentAnalyticsCalculator
{
    public BatomonAnalyticsResult CalculateBatomon(
        string speciesId,
        IReadOnlyCollection<OpponentCacheSnapshot> snapshots,
        IReadOnlyCollection<OpponentRunSummary> runs,
        OpponentAnalyticsPhase phase = OpponentAnalyticsPhase.All)
    {
        IReadOnlyList<OpponentCacheSnapshot> scopedSnapshots = FilterSnapshots(snapshots, phase);
        HashSet<string> matchingRunKeys = scopedSnapshots
            .Where(snapshot => snapshot.Units.Any(unit => string.Equals(unit.SpeciesId, speciesId, StringComparison.OrdinalIgnoreCase)))
            .Select(GetRunKey)
            .ToHashSet(StringComparer.Ordinal);
        OpponentRunSummary[] matchingRuns = runs.Where(run => matchingRunKeys.Contains(run.RunKey)).ToArray();
        OpponentCacheSnapshot[] matchingBattles = scopedSnapshots
            .Where(snapshot => snapshot.Units.Any(unit => string.Equals(unit.SpeciesId, speciesId, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        return new BatomonAnalyticsResult(
            speciesId,
            matchingBattles.Length,
            matchingBattles.Count(snapshot => snapshot.Won),
            matchingRuns.Length,
            matchingRuns.Count(run => run.IsComplete),
            matchingRuns.Count(run => run.IsComplete && run.ReachedTenWins));
    }

    public IReadOnlyList<BatomonCompanionResult> CalculateCompanions(
        string speciesId,
        IReadOnlyCollection<OpponentCacheSnapshot> snapshots,
        IReadOnlyCollection<OpponentRunSummary> runs,
        OpponentAnalyticsPhase phase = OpponentAnalyticsPhase.All)
    {
        IReadOnlyList<OpponentCacheSnapshot> scopedSnapshots = FilterSnapshots(snapshots, phase);
        var runsContainingTarget = scopedSnapshots
            .Where(snapshot => snapshot.Units.Any(unit => string.Equals(unit.SpeciesId, speciesId, StringComparison.OrdinalIgnoreCase)))
            .GroupBy(GetRunKey, StringComparer.Ordinal);
        var summaries = runs.ToDictionary(run => run.RunKey, StringComparer.Ordinal);
        var results = new Dictionary<string, CompanionAccumulator>(StringComparer.OrdinalIgnoreCase);

        foreach (IGrouping<string, OpponentCacheSnapshot> runGroup in runsContainingTarget)
        {
            HashSet<string> species = runGroup
                .SelectMany(snapshot => snapshot.Units)
                .Select(unit => unit.SpeciesId)
                .Where(candidate => !string.Equals(candidate, speciesId, StringComparison.OrdinalIgnoreCase))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!summaries.TryGetValue(runGroup.Key, out OpponentRunSummary? summary))
            {
                continue;
            }

            foreach (string companion in species)
            {
                if (!results.TryGetValue(companion, out CompanionAccumulator? accumulator))
                {
                    accumulator = new CompanionAccumulator();
                    results[companion] = accumulator;
                }

                accumulator.SharedRunCount++;
                if (summary.IsComplete)
                {
                    accumulator.CompletedSharedRunCount++;
                }

                if (summary.IsComplete && summary.ReachedTenWins)
                {
                    accumulator.SharedRunWins++;
                }
            }
        }

        return results
            .Select(pair => new BatomonCompanionResult(
                pair.Key,
                pair.Value.SharedRunCount,
                pair.Value.CompletedSharedRunCount,
                pair.Value.SharedRunWins))
            .OrderByDescending(result => result.SharedRunCount)
            .ThenBy(result => result.SpeciesId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<RoundAnalyticsResult> CalculateRounds(
        IReadOnlyCollection<OpponentCacheSnapshot> snapshots,
        OpponentAnalyticsPhase phase = OpponentAnalyticsPhase.All)
    {
        return FilterSnapshots(snapshots, phase)
            .GroupBy(snapshot => snapshot.Round)
            .Select(group => new RoundAnalyticsResult(
                group.Key,
                group.Count(),
                group.Count(snapshot => snapshot.Won)))
            .OrderBy(result => result.Round)
            .ToArray();
    }

    private static IReadOnlyList<OpponentCacheSnapshot> FilterSnapshots(
        IReadOnlyCollection<OpponentCacheSnapshot> snapshots,
        OpponentAnalyticsPhase phase)
    {
        return phase switch
        {
            OpponentAnalyticsPhase.Standard => snapshots.Where(snapshot => snapshot.WinsBefore is null or < 10).ToArray(),
            OpponentAnalyticsPhase.Extended => snapshots.Where(snapshot => snapshot.WinsBefore is >= 10).ToArray(),
            _ => snapshots.ToArray()
        };
    }

    private static string GetRunKey(OpponentCacheSnapshot snapshot) =>
        $"{snapshot.Dataset.DatasetId}:{snapshot.UserId}:{snapshot.RunId}";

    private sealed class CompanionAccumulator
    {
        public int SharedRunCount { get; set; }
        public int CompletedSharedRunCount { get; set; }
        public int SharedRunWins { get; set; }
    }
}
