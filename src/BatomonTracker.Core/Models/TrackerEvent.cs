namespace BatomonTracker.Core.Models;

public enum TrackerEventType
{
    NewRun,
    RoundStarted,
    BattleWon,
    BattleLost,
    ShopRerolled,
    BatomonBought,
    BatomonSold
}

public enum EventConfidence
{
    Confirmed,
    Deduced
}

public sealed record TrackerEvent(
    TrackerEventType Type,
    DateTimeOffset DetectedAt,
    string RunId,
    long? SaveSequence,
    EventConfidence Confidence,
    IReadOnlyDictionary<string, string> Payload);
