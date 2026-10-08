using BatomonTracker.Core.Analytics;
using BatomonTracker.Infrastructure.Analytics;
using Xunit;

namespace BatomonTracker.Core.Tests;

public sealed class OpponentCacheAnalyticsTests
{
    [Fact]
    public void ParserReadsSnapshotAndBoardData()
    {
        const string json = """
            [{
              "user_id":"user-1",
              "display_name":"Player",
              "mmr":844,
              "rank_tier":"bronze",
              "round":9,
              "set_id":"starter",
              "version":23,
              "run_id":"run-1",
              "won":true,
              "team_data":{
                "lives_lost":2,
                "wins":8,
                "trainer_id":"trainer-a",
                "shop_rank":3,
                "board":[{"species_id":"boomagon","level":2,"is_shiny":false,"extra_types":["fire"],"perm_buffs":{"power":2}},{}],
                "trinket_ids":["trinket-a"],
                "used_trinket_ids":[]
              }
            }]
            """;

        var parser = new OpponentCacheParser();
        OpponentCacheSnapshot snapshot = Assert.Single(parser.ParseFile("ranked_starter_0.json", json));

        Assert.Equal("ranked", snapshot.Dataset.Mode);
        Assert.Equal("starter", snapshot.Dataset.SetId);
        Assert.Equal("0", snapshot.Dataset.Segment);
        Assert.Equal("run-1", snapshot.RunId);
        Assert.Equal(9, snapshot.Round);
        Assert.Equal(8, snapshot.WinsBefore);
        Assert.Equal("boomagon", Assert.Single(snapshot.Units).SpeciesId);
        Assert.Equal("trinket-a", Assert.Single(snapshot.TrinketIds));
        Assert.Equal(64, snapshot.SnapshotHash.Length);
    }

    [Fact]
    public void ClassifierSeparatesExtendedSnapshotsAndDeduplicatesExactCopies()
    {
        const string json = """
            [
              {"user_id":"user-1","round":9,"run_id":"run-1","won":true,"team_data":{"wins":8,"lives_lost":2,"board":[]}},
              {"user_id":"user-1","round":9,"run_id":"run-1","won":true,"team_data":{"wins":8,"lives_lost":2,"board":[]}},
              {"user_id":"user-1","round":10,"run_id":"run-1","won":true,"team_data":{"wins":9,"lives_lost":2,"board":[]}},
              {"user_id":"user-1","round":11,"run_id":"run-1","won":false,"team_data":{"wins":10,"lives_lost":3,"board":[]}}
            ]
            """;

        var parser = new OpponentCacheParser();
        IReadOnlyList<OpponentCacheSnapshot> snapshots = parser.ParseFile("ranked_starter_endless.json", json);
        OpponentRunSummary summary = Assert.Single(new OpponentRunClassifier().Group(snapshots));

        Assert.Equal(3, summary.SnapshotCount);
        Assert.True(summary.ReachedTenWins);
        Assert.True(summary.IsComplete);
        Assert.Equal(2, summary.StandardSnapshots.Count);
        Assert.Single(summary.ExtendedSnapshots);
    }

    [Fact]
    public void IncompleteRunIsNotMarkedComplete()
    {
        const string json = """
            [{"user_id":"user-1","round":4,"run_id":"run-1","won":false,"team_data":{"wins":2,"lives_lost":3,"board":[]}}]
            """;

        var parser = new OpponentCacheParser();
        OpponentRunSummary summary = Assert.Single(new OpponentRunClassifier().Group(parser.ParseFile("casual_starter.json", json)));

        Assert.False(summary.ReachedTenWins);
        Assert.False(summary.ReachedZeroLives);
        Assert.False(summary.IsComplete);
    }

    [Fact]
    public async Task ImporterLoadsDatasetsAndSkipsMetaFile()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"batomon-cache-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "meta.json"), "{\"casual_starter\":{\"etag\":\"etag-1\",\"fetched_at\":1790157958.0}}");
            await File.WriteAllTextAsync(Path.Combine(directory, "casual_starter.json"), """
                [{"user_id":"user-1","round":1,"run_id":"run-1","won":true,"team_data":{"wins":0,"lives_lost":0,"board":[]}}]
                """);

            OpponentCacheImportResult result = await new OpponentCacheImporter().ImportAsync(directory);

            Assert.Single(result.Snapshots);
            Assert.Single(result.Runs);
            Assert.Single(result.Files);
            Assert.Empty(result.FailedFiles);
            Assert.Equal("etag-1", result.Metadata["casual_starter"].Etag);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CalculatorSeparatesBattleAndRunMetrics()
    {
        const string json = """
            [
              {"user_id":"user-1","round":9,"run_id":"run-1","won":true,"team_data":{"wins":8,"lives_lost":2,"board":[{"species_id":"a"},{"species_id":"b"}]}},
              {"user_id":"user-1","round":10,"run_id":"run-1","won":true,"team_data":{"wins":9,"lives_lost":2,"board":[{"species_id":"a"}]}},
              {"user_id":"user-2","round":3,"run_id":"run-2","won":false,"team_data":{"wins":1,"lives_lost":10,"board":[{"species_id":"a"},{"species_id":"c"}]}}
            ]
            """;

        var parser = new OpponentCacheParser();
        IReadOnlyList<OpponentCacheSnapshot> snapshots = parser.ParseFile("ranked_starter_0.json", json);
        IReadOnlyList<OpponentRunSummary> runs = new OpponentRunClassifier().Group(snapshots);
        var calculator = new OpponentAnalyticsCalculator();

        BatomonAnalyticsResult result = calculator.CalculateBatomon("a", snapshots, runs);
        IReadOnlyList<BatomonCompanionResult> companions = calculator.CalculateCompanions("a", snapshots, runs);
        IReadOnlyList<RoundAnalyticsResult> rounds = calculator.CalculateRounds(snapshots);

        Assert.Equal(3, result.BattleCount);
        Assert.Equal(2, result.BattleWins);
        Assert.Equal(2, result.RunCount);
        Assert.Equal(2, result.CompletedRunCount);
        Assert.Equal(1, result.RunWins);
        Assert.Equal("b", companions[0].SpeciesId);
        Assert.Equal(1, companions[0].SharedRunCount);
        Assert.Equal("c", companions[1].SpeciesId);
        Assert.Equal(3, rounds.Count);
        RoundAnalyticsResult roundNine = Assert.Single(rounds, round => round.Round == 9);
        Assert.Equal(1, roundNine.BattleCount);
    }
}
