namespace BatomonTracker.Core.Models;

public sealed record ProfileSnapshot(
    string? DisplayName,
    int? GamesPlayed,
    int? RunsWon,
    int? Mmr,
    int? RankedMmr,
    string? RankTier,
    int? RankSub,
    int? RankStars,
    int MatchCount);
