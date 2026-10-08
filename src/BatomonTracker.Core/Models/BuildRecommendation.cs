namespace BatomonTracker.Core.Models;

public enum RecommendationAction
{
    None,
    Buy,
    Hold
}

public sealed record BuildRecommendation(
    RecommendationAction Action,
    string? SpeciesId,
    string Reason,
    EventConfidence Confidence);
