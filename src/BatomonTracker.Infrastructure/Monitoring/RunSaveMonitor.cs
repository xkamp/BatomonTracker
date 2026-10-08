using BatomonTracker.Core.Diff;
using BatomonTracker.Core.Models;

namespace BatomonTracker.Infrastructure.Monitoring;

public sealed class RunSaveMonitor : IDisposable
{
    private readonly Filesystem.GodotSaveLocator locator;
    private readonly Filesystem.SafeJsonReader reader;
    private readonly StateDiff stateDiff;
    private readonly TimeSpan debounce;
    private readonly TimeSpan pollInterval;
    private readonly SemaphoreSlim readGate = new(1, 1);
    private CancellationTokenSource? cancellation;
    private FileSystemWatcher? watcher;
    private Task? pollingTask;
    private string? currentPath;
    private RunState? previousState;
    private bool statePublished;
    private bool disposed;

    public RunSaveMonitor(
        Filesystem.GodotSaveLocator locator,
        Filesystem.SafeJsonReader reader,
        StateDiff stateDiff,
        TimeSpan? debounce = null,
        TimeSpan? pollInterval = null)
    {
        this.locator = locator;
        this.reader = reader;
        this.stateDiff = stateDiff;
        this.debounce = debounce ?? TimeSpan.FromMilliseconds(100);
        this.pollInterval = pollInterval ?? TimeSpan.FromSeconds(1);
    }

    public event EventHandler<RunStateUpdatedEventArgs>? StateUpdated;

    public event EventHandler<MonitorStatusChangedEventArgs>? StatusChanged;

    public event EventHandler<RunStateUnavailableEventArgs>? StateUnavailable;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (cancellation is not null)
        {
            return;
        }

        cancellation = new CancellationTokenSource();
        pollingTask = PollAsync(cancellation.Token);
        _ = RefreshAndReadAsync(cancellation.Token);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        cancellation?.Cancel();
        watcher?.Dispose();
        readGate.Dispose();
        cancellation?.Dispose();
    }

    private async Task PollAsync(CancellationToken token)
    {
        using PeriodicTimer timer = new(pollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                await RefreshAndReadAsync(token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private async Task RefreshAndReadAsync(CancellationToken token)
    {
        var account = locator.FindActiveAccount();
        string? path = account?.RunSavePath;

        if (!string.Equals(path, currentPath, StringComparison.OrdinalIgnoreCase))
        {
            ConfigureWatcher(path);
            previousState = null;
            if (path is null && statePublished)
            {
                statePublished = false;
                StateUnavailable?.Invoke(this, new RunStateUnavailableEventArgs("Run live non trovata."));
            }
            currentPath = path;
        }

        if (path is null)
        {
            StatusChanged?.Invoke(this, new MonitorStatusChangedEventArgs("Save live non trovato."));
            return;
        }

        await ReadCurrentAsync(token);
    }

    private async Task ReadCurrentAsync(CancellationToken token)
    {
        if (currentPath is null || !await readGate.WaitAsync(0, token))
        {
            return;
        }

        try
        {
            await Task.Delay(debounce, token);
            var result = await reader.ReadRunAsync(currentPath, cancellationToken: token);
            if (!result.Success || result.Value is null)
            {
                StatusChanged?.Invoke(this, new MonitorStatusChangedEventArgs(result.Error ?? "Lettura save fallita."));
                return;
            }

            IReadOnlyList<TrackerEvent> events = stateDiff.Compare(previousState, result.Value);
            previousState = result.Value;
            statePublished = true;
            StatusChanged?.Invoke(this, new MonitorStatusChangedEventArgs("Monitoraggio attivo."));
            StateUpdated?.Invoke(this, new RunStateUpdatedEventArgs(result.Value, events));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        finally
        {
            readGate.Release();
        }
    }

    private void ConfigureWatcher(string? path)
    {
        watcher?.Dispose();
        watcher = null;

        if (path is null)
        {
            return;
        }

        string directory = Path.GetDirectoryName(path)!;
        watcher = new FileSystemWatcher(directory, Path.GetFileName(path))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size
        };
        watcher.Changed += OnFileChanged;
        watcher.Created += OnFileChanged;
        watcher.Renamed += OnFileRenamed;
        watcher.EnableRaisingEvents = true;
    }

    private void OnFileChanged(object sender, FileSystemEventArgs args)
    {
        TriggerRead();
    }

    private void OnFileRenamed(object sender, RenamedEventArgs args)
    {
        TriggerRead();
    }

    private void TriggerRead()
    {
        if (cancellation is not null)
        {
            _ = ReadCurrentAsync(cancellation.Token);
        }
    }
}

public sealed class RunStateUpdatedEventArgs : EventArgs
{
    public RunStateUpdatedEventArgs(RunState state, IReadOnlyList<TrackerEvent> events)
    {
        State = state;
        Events = events;
    }

    public RunState State { get; }

    public IReadOnlyList<TrackerEvent> Events { get; }
}

public sealed class MonitorStatusChangedEventArgs : EventArgs
{
    public MonitorStatusChangedEventArgs(string message)
    {
        Message = message;
    }

    public string Message { get; }
}

public sealed class RunStateUnavailableEventArgs : EventArgs
{
    public RunStateUnavailableEventArgs(string message)
    {
        Message = message;
    }

    public string Message { get; }
}
