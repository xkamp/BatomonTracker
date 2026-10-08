using System.Globalization;
using System.Text.Json;
using BatomonTracker.Core.Models;

namespace BatomonTracker.Core.Parsing;

public sealed class ProfileHistoryParser
{
    public IReadOnlyList<CompletedRun> Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("match_history", out JsonElement history) ||
            history.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<CompletedRun>();
        }

        return history.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(ParseRun)
            .Where(run => run is not null)
            .Cast<CompletedRun>()
            .ToArray();
    }

    private static CompletedRun? ParseRun(JsonElement value)
    {
        string? runId = GetString(value, "run_id");
        if (string.IsNullOrWhiteSpace(runId))
        {
            return null;
        }

        string? endedAtValue = GetString(value, "ended_at");
        DateTimeOffset? endedAt = DateTimeOffset.TryParse(
            endedAtValue,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out DateTimeOffset parsedEndedAt)
            ? parsedEndedAt
            : null;

        string teamJson = value.TryGetProperty("team", out JsonElement team)
            ? team.GetRawText()
            : "[]";
        string benchJson = value.TryGetProperty("bench", out JsonElement bench)
            ? bench.GetRawText()
            : "[]";
        IReadOnlyList<string> trinkets = value.TryGetProperty("trinket_ids", out JsonElement trinketIds) && trinketIds.ValueKind == JsonValueKind.Array
            ? trinketIds.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()).Where(item => !string.IsNullOrWhiteSpace(item)).Cast<string>().ToArray()
            : Array.Empty<string>();

        return new CompletedRun(
            runId,
            endedAt,
            GetBoolean(value, "is_ranked"),
            GetInt32(value, "lives"),
            GetString(value, "outcome"),
            GetInt32(value, "round"),
            GetString(value, "trainer_id"),
            GetInt32(value, "wins"),
            teamJson,
            benchJson,
            trinkets);
    }

    private static string? GetString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            _ => null
        };
    }

    private static int? GetInt32(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int result)
            ? result
            : null;
    }

    private static bool? GetBoolean(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out JsonElement value) &&
               (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
            ? value.GetBoolean()
            : null;
    }
}
