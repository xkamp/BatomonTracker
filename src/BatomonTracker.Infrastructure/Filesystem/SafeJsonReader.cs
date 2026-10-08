using System.Text.Json;
using BatomonTracker.Core.Models;
using BatomonTracker.Core.Parsing;

namespace BatomonTracker.Infrastructure.Filesystem;

public sealed record JsonReadResult<T>(bool Success, T? Value, string? Error)
{
    public static JsonReadResult<T> Succeeded(T value) => new(true, value, null);

    public static JsonReadResult<T> Failed(string error) => new(false, default, error);
}

public sealed class SafeJsonReader
{
    private readonly RunSaveParser parser;

    public SafeJsonReader(RunSaveParser? parser = null)
    {
        this.parser = parser ?? new RunSaveParser();
    }

    public async Task<JsonReadResult<RunState>> ReadRunAsync(
        string path,
        int attempts = 3,
        TimeSpan? retryDelay = null,
        CancellationToken cancellationToken = default)
    {
        if (attempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(attempts));
        }

        TimeSpan delay = retryDelay ?? TimeSpan.FromMilliseconds(75);
        string? lastError = null;

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                string json = await File.ReadAllTextAsync(path, cancellationToken);
                return JsonReadResult<RunState>.Succeeded(parser.Parse(json));
            }
            catch (IOException exception)
            {
                lastError = exception.Message;
            }
            catch (UnauthorizedAccessException exception)
            {
                lastError = exception.Message;
            }
            catch (JsonException exception)
            {
                lastError = exception.Message;
            }

            if (attempt < attempts - 1)
            {
                await Task.Delay(delay, cancellationToken);
            }
        }

        return JsonReadResult<RunState>.Failed(lastError ?? "Unable to read run save.");
    }
}
