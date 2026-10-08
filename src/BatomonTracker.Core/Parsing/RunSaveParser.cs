using System.Text.Json;
using BatomonTracker.Core.Models;

namespace BatomonTracker.Core.Parsing;

public sealed class RunSaveParser
{
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    public RunState Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json, JsonOptions);
        JsonElement root = document.RootElement;
        string? runId = GetString(root, "run_id");

        if (string.IsNullOrWhiteSpace(runId))
        {
            throw new JsonException("The run save does not contain a valid run_id.");
        }

        return new RunState(
            runId,
            GetInt32(root, "save_version"),
            GetInt64(root, "save_seq"),
            GetInt32(root, "round"),
            GetInt32(root, "wins"),
            GetInt32(root, "lives"),
            GetInt32(root, "gold"),
            GetString(root, "trainer_id"),
            GetInt32(root, "shop_rank"),
            GetNestedInt64(root, "blackboard", "shop_roll_seq"),
            GetUnits(root, "team"),
            GetUnits(root, "bench"),
            GetShop(root),
            GetStringArray(root, "trinket_ids"));
    }

    private static IReadOnlyList<BatomonUnit> GetUnits(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<BatomonUnit>();
        }

        return value.EnumerateArray()
            .Select(ParseUnit)
            .Where(unit => unit is not null)
            .Cast<BatomonUnit>()
            .ToArray();
    }

    private static IReadOnlyList<ShopSlot> GetShop(JsonElement root)
    {
        if (!root.TryGetProperty("shop_content", out JsonElement value) || value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ShopSlot>();
        }

        return value.EnumerateArray()
            .Select((slot, index) => ParseShopSlot(slot, index))
            .ToArray();
    }

    private static ShopSlot ParseShopSlot(JsonElement value, int index)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            return new ShopSlot(index, null, value.GetString());
        }

        return new ShopSlot(index, ParseUnit(value), null);
    }

    private static IReadOnlyList<string> GetStringArray(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Cast<string>()
            .ToArray();
    }

    private static BatomonUnit? ParseUnit(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string? speciesId = GetString(value, "species_id");
        return string.IsNullOrWhiteSpace(speciesId)
            ? null
            : new BatomonUnit(speciesId, GetInt32(value, "level"), GetBoolean(value, "is_shiny"));
    }

    private static string? GetString(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static int? GetInt32(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int result)
            ? result
            : null;
    }

    private static long? GetInt64(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out JsonElement value) && value.TryGetInt64(out long result)
            ? result
            : null;
    }

    private static long? GetNestedInt64(JsonElement root, string objectName, string name)
    {
        return root.TryGetProperty(objectName, out JsonElement value) && value.ValueKind == JsonValueKind.Object
            ? GetInt64(value, name)
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
