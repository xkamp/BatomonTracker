namespace BatomonTracker.Core.Models;

public sealed record CompletedRun(
    string RunId,
    DateTimeOffset? EndedAt,
    bool? IsRanked,
    int? Lives,
    string? Outcome,
    int? Round,
    string? TrainerId,
    int? Wins,
    string TeamJson,
    string BenchJson = "[]",
    IReadOnlyList<string>? Trinkets = null);
