using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BatomonTracker.Core.Analytics;

public sealed class OpponentCacheParser
{
    public IReadOnlyList<OpponentCacheSnapshot> ParseFile(
        string fileName,
        string json,
        string? datasetId = null)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Opponent cache file must contain a JSON array.");
        }

        OpponentCacheDataset dataset = DescribeDataset(fileName, datasetId);
        var snapshots = new List<OpponentCacheSnapshot>();
        foreach (JsonElement element in document.RootElement.EnumerateArray())
        {
            snapshots.Add(ParseSnapshot(dataset, element));
        }

        return snapshots;
    }

    public OpponentCacheDataset DescribeDataset(string fileName, string? datasetId = null)
    {
        string name = Path.GetFileNameWithoutExtension(fileName);
        string[] parts = name.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            throw new FormatException($"Unrecognized opponent cache filename: {fileName}");
        }

        string mode = parts[0];
        string setId = parts[1];
        string? segment = parts.Length > 2 ? string.Join('_', parts.Skip(2)) : null;
        return new OpponentCacheDataset(datasetId ?? name, Path.GetFileName(fileName), mode, setId, segment);
    }

    private static OpponentCacheSnapshot ParseSnapshot(OpponentCacheDataset dataset, JsonElement element)
    {
        string userId = RequiredString(element, "user_id");
        string runId = RequiredString(element, "run_id");
        int round = RequiredInt(element, "round");
        JsonElement teamData = GetObject(element, "team_data");
        JsonElement? board = GetOptional(teamData, "board");

        var units = new List<OpponentCacheUnit>();
        if (board is { ValueKind: JsonValueKind.Array })
        {
            int position = 0;
            foreach (JsonElement unit in board.Value.EnumerateArray())
            {
                if (unit.ValueKind == JsonValueKind.Object && TryString(unit, "species_id") is { } speciesId && !string.IsNullOrWhiteSpace(speciesId))
                {
                    units.Add(new OpponentCacheUnit(
                        position,
                        speciesId,
                        TryInt(unit, "level"),
                        TryBool(unit, "is_shiny"),
                        ReadStringArray(unit, "extra_types"),
                        RawProperty(unit, "perm_buffs"),
                        RawProperty(unit, "blackboard")));
                }

                position++;
            }
        }

        return new OpponentCacheSnapshot(
            dataset,
            userId,
            TryString(element, "display_name"),
            TryInt(element, "mmr"),
            TryString(element, "rank_tier"),
            round,
            TryString(element, "set_id"),
            TryInt(element, "version"),
            runId,
            TryBool(element, "won") ?? false,
            TryInt(teamData, "wins"),
            TryInt(teamData, "lives_lost"),
            TryString(teamData, "trainer_id"),
            TryInt(teamData, "shop_rank"),
            units,
            ReadStringArray(teamData, "trinket_ids"),
            ReadStringArray(teamData, "used_trinket_ids"),
            element.GetRawText(),
            ComputeHash(element.GetRawText()));
    }

    private static JsonElement GetObject(JsonElement parent, string property)
    {
        JsonElement value = parent.GetProperty(property);
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException($"Property '{property}' must be an object.");
        }

        return value;
    }

    private static JsonElement? GetOptional(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out JsonElement value) ? value : null;

    private static string RequiredString(JsonElement parent, string property) =>
        TryString(parent, property) ?? throw new JsonException($"Missing string property '{property}'.");

    private static int RequiredInt(JsonElement parent, string property) =>
        TryInt(parent, property) ?? throw new JsonException($"Missing integer property '{property}'.");

    private static string? TryString(JsonElement parent, string property)
    {
        return parent.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static int? TryInt(JsonElement parent, string property)
    {
        return parent.TryGetProperty(property, out JsonElement value) && value.TryGetInt32(out int result)
            ? result
            : null;
    }

    private static bool? TryBool(JsonElement parent, string property)
    {
        return parent.TryGetProperty(property, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out JsonElement value) || value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!)
            .ToArray();
    }

    private static string? RawProperty(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out JsonElement value) ? value.GetRawText() : null;

    private static string ComputeHash(string rawJson)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawJson));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
