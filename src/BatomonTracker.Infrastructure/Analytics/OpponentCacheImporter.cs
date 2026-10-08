using BatomonTracker.Core.Analytics;
using System.Text.Json;

namespace BatomonTracker.Infrastructure.Analytics;

public sealed record OpponentCacheFileResult(
    string FileName,
    int SnapshotCount,
    string? Error);

public sealed record OpponentCacheImportResult(
    IReadOnlyList<OpponentCacheSnapshot> Snapshots,
    IReadOnlyList<OpponentRunSummary> Runs,
    IReadOnlyList<OpponentCacheFileResult> Files,
    IReadOnlyDictionary<string, OpponentCacheMetadata> Metadata)
{
    public IReadOnlyList<OpponentCacheFileResult> FailedFiles =>
        Files.Where(file => file.Error is not null).ToArray();
}

public sealed class OpponentCacheImporter
{
    private readonly OpponentCacheParser parser;
    private readonly OpponentRunClassifier classifier;

    public OpponentCacheImporter(
        OpponentCacheParser? parser = null,
        OpponentRunClassifier? classifier = null)
    {
        this.parser = parser ?? new OpponentCacheParser();
        this.classifier = classifier ?? new OpponentRunClassifier();
    }

    public async Task<OpponentCacheImportResult> ImportAsync(
        string directory,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException(directory);
        }

        var snapshots = new List<OpponentCacheSnapshot>();
        var files = new List<OpponentCacheFileResult>();
        IReadOnlyDictionary<string, OpponentCacheMetadata> metadata = await ReadMetadataAsync(directory, cancellationToken);
        foreach (string path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
                     .Where(path => !string.Equals(Path.GetFileName(path), "meta.json", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string fileName = Path.GetFileName(path);
            try
            {
                string json = await File.ReadAllTextAsync(path, cancellationToken);
                IReadOnlyList<OpponentCacheSnapshot> parsed = parser.ParseFile(fileName, json);
                snapshots.AddRange(parsed);
                files.Add(new OpponentCacheFileResult(fileName, parsed.Count, null));
            }
            catch (IOException exception)
            {
                files.Add(new OpponentCacheFileResult(fileName, 0, exception.Message));
            }
            catch (UnauthorizedAccessException exception)
            {
                files.Add(new OpponentCacheFileResult(fileName, 0, exception.Message));
            }
            catch (JsonException exception)
            {
                files.Add(new OpponentCacheFileResult(fileName, 0, exception.Message));
            }
            catch (FormatException exception)
            {
                files.Add(new OpponentCacheFileResult(fileName, 0, exception.Message));
            }
        }

        OpponentCacheSnapshot[] deduplicatedSnapshots = snapshots
            .GroupBy(snapshot => new
            {
                snapshot.Dataset.DatasetId,
                snapshot.UserId,
                snapshot.RunId,
                snapshot.Round,
                snapshot.SnapshotHash
            })
            .Select(group => group.First())
            .ToArray();
        return new OpponentCacheImportResult(deduplicatedSnapshots, classifier.Group(deduplicatedSnapshots), files, metadata);
    }

    private static async Task<IReadOnlyDictionary<string, OpponentCacheMetadata>> ReadMetadataAsync(
        string directory,
        CancellationToken cancellationToken)
    {
        string path = Path.Combine(directory, "meta.json");
        if (!File.Exists(path))
        {
            return new Dictionary<string, OpponentCacheMetadata>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(path, cancellationToken));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new Dictionary<string, OpponentCacheMetadata>(StringComparer.OrdinalIgnoreCase);
            }

            var result = new Dictionary<string, OpponentCacheMetadata>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                string? etag = property.Value.TryGetProperty("etag", out JsonElement etagValue) && etagValue.ValueKind == JsonValueKind.String
                    ? etagValue.GetString()
                    : null;
                DateTimeOffset? fetchedAt = null;
                if (property.Value.TryGetProperty("fetched_at", out JsonElement fetchedValue) && fetchedValue.TryGetDouble(out double unixSeconds))
                {
                    fetchedAt = DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(unixSeconds));
                }

                result[property.Name] = new OpponentCacheMetadata(property.Name, etag, fetchedAt);
            }

            return result;
        }
        catch (JsonException)
        {
            return new Dictionary<string, OpponentCacheMetadata>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
