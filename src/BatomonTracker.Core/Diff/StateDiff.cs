using BatomonTracker.Core.Models;

namespace BatomonTracker.Core.Diff;

public sealed class StateDiff
{
    public IReadOnlyList<TrackerEvent> Compare(
        RunState? previous,
        RunState current,
        DateTimeOffset? detectedAt = null)
    {
        DateTimeOffset timestamp = detectedAt ?? DateTimeOffset.UtcNow;

        if (previous is null || !string.Equals(previous.RunId, current.RunId, StringComparison.Ordinal))
        {
            return new[] { Create(current, TrackerEventType.NewRun, timestamp, EventConfidence.Confirmed) };
        }

        var events = new List<TrackerEvent>();

        if (IsGreater(current.Round, previous.Round))
        {
            events.Add(Create(current, TrackerEventType.RoundStarted, timestamp, EventConfidence.Confirmed));
        }

        if (IsGreater(current.Wins, previous.Wins))
        {
            events.Add(Create(current, TrackerEventType.BattleWon, timestamp, EventConfidence.Confirmed));
        }

        if (IsLower(current.Lives, previous.Lives))
        {
            events.Add(Create(current, TrackerEventType.BattleLost, timestamp, EventConfidence.Confirmed));
        }

        if (IsGreater(current.ShopRollSequence, previous.ShopRollSequence))
        {
            events.Add(Create(current, TrackerEventType.ShopRerolled, timestamp, EventConfidence.Confirmed));
        }

        AddTeamTransaction(events, previous, current, timestamp);
        return events;
    }

    private static void AddTeamTransaction(
        ICollection<TrackerEvent> events,
        RunState previous,
        RunState current,
        DateTimeOffset timestamp)
    {
        var previousCounts = CountSpecies(previous.Team);
        var currentCounts = CountSpecies(current.Team);
        var added = Difference(currentCounts, previousCounts);
        var removed = Difference(previousCounts, currentCounts);

        if (added.Count == 1 && removed.Count == 0 && IsLower(current.Gold, previous.Gold))
        {
            events.Add(Create(current, TrackerEventType.BatomonBought, timestamp, EventConfidence.Deduced,
                new Dictionary<string, string> { ["species_id"] = added[0] }));
        }
        else if (removed.Count == 1 && added.Count == 0 && IsGreater(current.Gold, previous.Gold))
        {
            events.Add(Create(current, TrackerEventType.BatomonSold, timestamp, EventConfidence.Deduced,
                new Dictionary<string, string> { ["species_id"] = removed[0] }));
        }
    }

    private static Dictionary<string, int> CountSpecies(IEnumerable<BatomonUnit> units)
    {
        return units.GroupBy(unit => unit.SpeciesId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
    }

    private static List<string> Difference(Dictionary<string, int> left, Dictionary<string, int> right)
    {
        return left.SelectMany(pair => Enumerable.Repeat(pair.Key, Math.Max(0, pair.Value - right.GetValueOrDefault(pair.Key))))
            .ToList();
    }

    private static bool IsGreater(int? current, int? previous) =>
        current.HasValue && previous.HasValue && current.Value > previous.Value;

    private static bool IsGreater(long? current, long? previous) =>
        current.HasValue && previous.HasValue && current.Value > previous.Value;

    private static bool IsLower(int? current, int? previous) =>
        current.HasValue && previous.HasValue && current.Value < previous.Value;

    private static TrackerEvent Create(
        RunState state,
        TrackerEventType type,
        DateTimeOffset timestamp,
        EventConfidence confidence,
        IReadOnlyDictionary<string, string>? payload = null)
    {
        return new TrackerEvent(type, timestamp, state.RunId, state.SaveSequence, confidence,
            payload ?? new Dictionary<string, string>());
    }
}
