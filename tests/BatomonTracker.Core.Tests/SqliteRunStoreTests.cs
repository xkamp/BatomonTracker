using BatomonTracker.Core.Models;
using BatomonTracker.Core.Parsing;
using BatomonTracker.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace BatomonTracker.Core.Tests;

public sealed class SqliteRunStoreTests
{
    [Fact]
    public async Task SavesSnapshotAndEventsInTemporaryDatabase()
    {
        string directory = Path.Combine(Path.GetTempPath(), "BatomonTrackerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string databasePath = Path.Combine(directory, "tracker.db");

        try
        {
            var state = new RunSaveParser().Parse(File.ReadAllText(Path.Combine("Fixtures", "run_save.json")));
            var eventItem = new TrackerEvent(
                TrackerEventType.NewRun,
                DateTimeOffset.UtcNow,
                state.RunId,
                state.SaveSequence,
                EventConfidence.Confirmed,
                new Dictionary<string, string>());
            var store = new SqliteRunStore(databasePath);

            await store.InitializeAsync();
            await store.SaveAsync(state, new[] { eventItem });

            Assert.Equal(1, await store.CountSnapshotsAsync());
            Assert.Equal(1, await store.CountEventsAsync());
            var summaries = await store.GetRunSummariesAsync();
            var summary = Assert.Single(summaries);
            Assert.Equal(state.RunId, summary.RunId);
            Assert.Equal(state.TrainerId, summary.TrainerId);
            Assert.Equal(1, summary.SnapshotCount);
            Assert.Equal(1, summary.EventCount);

            await store.ImportCompletedRunsAsync(new[]
            {
                new CompletedRun(
                    "completed-run-001",
                    DateTimeOffset.UtcNow,
                    true,
                    8,
                    "won",
                    10,
                    "pyromaniac",
                    10,
                    "[]")
            });
            var completedRuns = await store.GetCompletedRunsAsync();
            var completed = Assert.Single(completedRuns);
            Assert.Equal("completed-run-001", completed.RunId);
            Assert.Equal("won", completed.Outcome);

            var stats = await store.GetPersonalStatsAsync();
            Assert.Equal(1, stats.CompletedRuns);
            Assert.Equal(1, stats.CompletedWins);
            Assert.Equal(1, stats.WinRate);
            Assert.Contains(stats.ByTrainer, item => item.TrainerId == "pyromaniac");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }
}
