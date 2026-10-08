using BatomonTracker.Core.Diff;
using BatomonTracker.Core.Models;
using BatomonTracker.Infrastructure.Filesystem;
using BatomonTracker.Infrastructure.Monitoring;
using Xunit;

namespace BatomonTracker.Core.Tests;

public sealed class RunSaveMonitorTests
{
    [Fact]
    public async Task PublishesInitialStateAndSubsequentFileChanges()
    {
        string root = CreateTempDirectory();
        try
        {
            string account = Path.Combine(root, "Godot", "app_userdata", "Batomon Showdown", "accounts", "test-account");
            Directory.CreateDirectory(account);
            string path = Path.Combine(account, "run_save.json");
            File.WriteAllText(path, CreateRunJson(round: 1));

            var firstState = NewCompletionSource();
            var secondState = NewCompletionSource();
            using var monitor = new RunSaveMonitor(
                new GodotSaveLocator(root),
                new SafeJsonReader(),
                new StateDiff(),
                debounce: TimeSpan.FromMilliseconds(10),
                pollInterval: TimeSpan.FromMilliseconds(50));

            monitor.StateUpdated += (_, args) =>
            {
                if (args.State.Round == 1)
                {
                    firstState.TrySetResult(args.State);
                }
                else if (args.State.Round == 2)
                {
                    secondState.TrySetResult(args.State);
                }
            };
            monitor.Start();

            Assert.Same(firstState.Task, await Task.WhenAny(firstState.Task, Task.Delay(3000)));
            File.WriteAllText(path, CreateRunJson(round: 2));
            Assert.Same(secondState.Task, await Task.WhenAny(secondState.Task, Task.Delay(3000)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PublishesNewRunWhenRunIdChanges()
    {
        string root = CreateTempDirectory();
        try
        {
            string account = Path.Combine(root, "Godot", "app_userdata", "Batomon Showdown", "accounts", "test-account");
            Directory.CreateDirectory(account);
            string path = Path.Combine(account, "run_save.json");
            File.WriteAllText(path, CreateRunJson(round: 1, runId: "run-one"));

            var secondState = NewCompletionSource();
            using var monitor = CreateMonitor(root);
            monitor.StateUpdated += (_, args) =>
            {
                if (args.State.RunId == "run-two")
                {
                    secondState.TrySetResult(args.State);
                }
            };
            monitor.Start();

            await WaitForStateAsync(monitor, "run-one");
            File.WriteAllText(path, CreateRunJson(round: 1, runId: "run-two"));
            Assert.Same(secondState.Task, await Task.WhenAny(secondState.Task, Task.Delay(3000)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AnnouncesWhenLiveSaveDisappears()
    {
        string root = CreateTempDirectory();
        try
        {
            string account = Path.Combine(root, "Godot", "app_userdata", "Batomon Showdown", "accounts", "test-account");
            Directory.CreateDirectory(account);
            string path = Path.Combine(account, "run_save.json");
            File.WriteAllText(path, CreateRunJson(round: 1));

            var unavailable = new TaskCompletionSource<RunStateUnavailableEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var monitor = CreateMonitor(root);
            monitor.StateUnavailable += (_, args) => unavailable.TrySetResult(args);
            monitor.Start();

            await WaitForStateAsync(monitor, "monitor-test");
            File.Delete(path);

            Assert.Same(unavailable.Task, await Task.WhenAny(unavailable.Task, Task.Delay(3000)));
            RunStateUnavailableEventArgs result = await unavailable.Task;
            Assert.Equal("Run live non trovata.", result.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SwitchesToTheNewestAccountWithALiveRun()
    {
        string root = CreateTempDirectory();
        try
        {
            string accounts = Path.Combine(root, "Godot", "app_userdata", "Batomon Showdown", "accounts");
            string first = Path.Combine(accounts, "first");
            string second = Path.Combine(accounts, "second");
            Directory.CreateDirectory(first);
            Directory.CreateDirectory(second);
            File.WriteAllText(Path.Combine(first, "run_save.json"), CreateRunJson(round: 1, runId: "first-run"));

            var secondState = NewCompletionSource();
            using var monitor = CreateMonitor(root);
            monitor.StateUpdated += (_, args) =>
            {
                if (args.State.RunId == "second-run")
                {
                    secondState.TrySetResult(args.State);
                }
            };
            monitor.Start();
            await WaitForStateAsync(monitor, "first-run");

            File.WriteAllText(Path.Combine(second, "run_save.json"), CreateRunJson(round: 1, runId: "second-run"));
            Assert.Same(secondState.Task, await Task.WhenAny(secondState.Task, Task.Delay(3000)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static RunSaveMonitor CreateMonitor(string root)
    {
        return new RunSaveMonitor(
            new GodotSaveLocator(root),
            new SafeJsonReader(),
            new StateDiff(),
            debounce: TimeSpan.FromMilliseconds(10),
            pollInterval: TimeSpan.FromMilliseconds(50));
    }

    private static async Task WaitForStateAsync(RunSaveMonitor monitor, string runId)
    {
        var state = NewCompletionSource();
        void Handler(object? _, RunStateUpdatedEventArgs args)
        {
            if (args.State.RunId == runId)
            {
                state.TrySetResult(args.State);
            }
        }

        monitor.StateUpdated += Handler;
        try
        {
            Assert.Same(state.Task, await Task.WhenAny(state.Task, Task.Delay(3000)));
        }
        finally
        {
            monitor.StateUpdated -= Handler;
        }
    }

    private static TaskCompletionSource<BatomonTracker.Core.Models.RunState> NewCompletionSource()
    {
        return new TaskCompletionSource<BatomonTracker.Core.Models.RunState>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static string CreateRunJson(int round, string runId = "monitor-test")
    {
        return $$"""
        {
          "run_id": "{{runId}}",
          "save_version": 1,
          "save_seq": {{round}},
          "round": {{round}},
          "wins": 0,
          "lives": 10,
          "gold": 20,
          "trainer_id": "trainer",
          "shop_rank": 1,
          "team": [],
          "shop_content": []
        }
        """;
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "BatomonTrackerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
