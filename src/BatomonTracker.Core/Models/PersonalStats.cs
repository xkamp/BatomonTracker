namespace BatomonTracker.Core.Models;

public sealed record TrainerStats(
    string TrainerId,
    int Runs,
    int Wins,
    double WinRate);

public sealed record BatomonStats(
    string SpeciesId,
    int Runs,
    int Wins,
    double WinRate);

public sealed record PersonalStats(
    int CompletedRuns,
    int CompletedWins,
    double WinRate,
    double AverageWins,
    IReadOnlyList<TrainerStats> ByTrainer,
    IReadOnlyList<BatomonStats> ByBatomon);
