using BatomonTracker.Core.Parsing;
using Xunit;

namespace BatomonTracker.Core.Tests;

public sealed class ProfileHistoryParserTests
{
    [Fact]
    public void ParsesCompletedRunsAndSkipsEntriesWithoutRunId()
    {
        string json = File.ReadAllText(Path.Combine("Fixtures", "profile_snapshot.json"));

        var runs = new ProfileHistoryParser().Parse(json);

        Assert.Equal(2, runs.Count);
        Assert.Equal("completed-run-001", runs[0].RunId);
        Assert.Equal("won", runs[0].Outcome);
        Assert.True(runs[0].IsRanked == true);
        Assert.Contains("boomagon", runs[0].TeamJson);
        Assert.Null(runs[1].EndedAt);
    }

    [Fact]
    public void PreservesBenchAndTrinketsForRemoteSync()
    {
        const string json = """
        {"match_history":[{"run_id":"run-1","team":[],"bench":[{"species_id":"boomagon"}],"trinket_ids":["lucky_charm"]}]}
        """;

        var run = Assert.Single(new ProfileHistoryParser().Parse(json));

        Assert.Contains("boomagon", run.BenchJson);
        Assert.Contains("lucky_charm", run.Trinkets!);
    }
}
