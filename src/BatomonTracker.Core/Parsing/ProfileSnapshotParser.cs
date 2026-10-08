using System.Text.Json;
using BatomonTracker.Core.Models;

namespace BatomonTracker.Core.Parsing;

public sealed class ProfileSnapshotParser
{
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    public ProfileSnapshot Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json, JsonOptions);
        JsonElement root = document.RootElement;

        return new ProfileSnapshot(
            GetString(root, "display_name"),
            GetInt32(root, "games_played"),
            GetInt32(root, "runs_won"),
            GetInt32(root, "mmr"),
            GetInt32(root, "ranked_mmr"),
            GetString(root, "rank_tier"),
            GetInt32(root, "rank_sub"),
            GetInt32(root, "rank_stars"),
            GetArrayLength(root, "match_history"));
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

    private static int GetArrayLength(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Array
            ? value.GetArrayLength()
            : 0;
    }
}
