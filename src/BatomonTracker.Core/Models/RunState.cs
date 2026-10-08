namespace BatomonTracker.Core.Models;

public sealed record BatomonUnit(
    string SpeciesId,
    int? Level,
    bool? IsShiny);

public sealed record ShopSlot(
    int Index,
    BatomonUnit? Unit,
    string? ItemId);

public sealed record RunState(
    string RunId,
    int? SaveVersion,
    long? SaveSequence,
    int? Round,
    int? Wins,
    int? Lives,
    int? Gold,
    string? TrainerId,
    int? ShopRank,
    long? ShopRollSequence,
    IReadOnlyList<BatomonUnit> Team,
    IReadOnlyList<BatomonUnit> Bench,
    IReadOnlyList<ShopSlot> Shop,
    IReadOnlyList<string> Trinkets);
