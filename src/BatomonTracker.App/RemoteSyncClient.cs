using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net;
using System.Text;
using System.Text.Json;
using BatomonTracker.Core.Analytics;
using BatomonTracker.Core.Models;
using BatomonTracker.Infrastructure.Analytics;

namespace BatomonTracker.App;

public sealed class RemoteSyncClient
{
    private readonly HttpClient client = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly string apiBaseUrl = (Environment.GetEnvironmentVariable("BATOMON_API_URL") ?? "http://127.0.0.1:5000").TrimEnd('/');

    public async Task<AuthSessionData?> ExchangeAuthCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        using var content = new StringContent(
            JsonSerializer.Serialize(new { code }),
            Encoding.UTF8,
            "application/json");
        using HttpResponseMessage response = await client.PostAsync($"{apiBaseUrl}/auth/token", content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        JsonElement root = document.RootElement;
        string? steamId = root.TryGetProperty("steamId", out JsonElement steamIdElement) ? steamIdElement.GetString() : null;
        string? accessToken = root.TryGetProperty("accessToken", out JsonElement tokenElement) ? tokenElement.GetString() : null;
        string? refreshToken = root.TryGetProperty("refreshToken", out JsonElement refreshElement) ? refreshElement.GetString() : null;
        return string.IsNullOrWhiteSpace(steamId) || string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(refreshToken)
            ? null
            : new AuthSessionData(steamId, accessToken, refreshToken);
    }

    public async Task<AuthSessionData?> RefreshSessionAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return null;
        }

        using var content = new StringContent(
            JsonSerializer.Serialize(new { refreshToken }),
            Encoding.UTF8,
            "application/json");
        using HttpResponseMessage response = await client.PostAsync($"{apiBaseUrl}/auth/refresh", content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        JsonElement root = document.RootElement;
        string? steamId = root.GetProperty("steamId").GetString();
        string? accessToken = root.GetProperty("accessToken").GetString();
        string? rotatedRefreshToken = root.GetProperty("refreshToken").GetString();
        return string.IsNullOrWhiteSpace(steamId) || string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(rotatedRefreshToken)
            ? null
            : new AuthSessionData(steamId, accessToken, rotatedRefreshToken);
    }

    public async Task SyncCompletedRunsAsync(
        IReadOnlyList<CompletedRun> runs,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        if (runs.Count == 0 || string.IsNullOrWhiteSpace(accessToken))
        {
            return;
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        foreach (CompletedRun run in runs)
        {
            var payload = new
            {
                externalRunId = run.RunId,
                mode = (string?)null,
                isRanked = run.IsRanked ?? false,
                trainerId = run.TrainerId,
                outcome = run.Outcome,
                endedAt = run.EndedAt,
                round = run.Round,
                wins = run.Wins,
                lives = run.Lives,
                team = ParseTeam(run.TeamJson),
                bench = ParseTeam(run.BenchJson),
                trinkets = run.Trinkets ?? Array.Empty<string>()
            };
            string json = JsonSerializer.Serialize(payload);
            await PostJsonWithRetryAsync($"{apiBaseUrl}/api/sync/runs", json, cancellationToken);
        }
    }

    public async Task<SteamProfile?> GetCurrentProfileAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return null;
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using HttpResponseMessage response = await client.GetAsync($"{apiBaseUrl}/api/me", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        JsonElement root = document.RootElement;
        return new SteamProfile(
            root.TryGetProperty("steamId", out JsonElement steamId) ? steamId.GetString() ?? string.Empty : string.Empty,
            root.TryGetProperty("displayName", out JsonElement displayName) ? displayName.GetString() : null,
            root.TryGetProperty("avatarUrl", out JsonElement avatarUrl) ? avatarUrl.GetString() : null);
    }

    public async Task<PersonalStats?> GetPersonalStatsAsync(string accessToken, string filter, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return null;
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using HttpResponseMessage response = await client.GetAsync($"{apiBaseUrl}/api/me/stats?filter={Uri.EscapeDataString(filter)}", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        JsonElement root = document.RootElement;
        var trainers = root.GetProperty("byTrainer").EnumerateArray()
            .Select(item => new TrainerStats(
                item.GetProperty("trainerId").GetString() ?? "unknown",
                item.GetProperty("runs").GetInt32(),
                item.GetProperty("wins").GetInt32(),
                item.GetProperty("winRate").GetDouble()))
            .ToArray();
        var batomon = root.GetProperty("byBatomon").EnumerateArray()
            .Select(item => new BatomonStats(
                item.GetProperty("speciesId").GetString() ?? "unknown",
                item.GetProperty("runs").GetInt32(),
                item.GetProperty("wins").GetInt32(),
                item.GetProperty("winRate").GetDouble()))
            .ToArray();
        return new PersonalStats(
            root.GetProperty("completedRuns").GetInt32(),
            root.GetProperty("completedWins").GetInt32(),
            root.GetProperty("winRate").GetDouble(),
            root.GetProperty("averageWins").GetDouble(),
            trainers,
            batomon);
    }

    public async Task SyncLiveRunAsync(
        RunState state,
        string accessToken,
        IReadOnlyList<TrackerEvent>? events = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(state.RunId) || string.IsNullOrWhiteSpace(accessToken))
        {
            return;
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var payload = new
        {
            externalRunId = state.RunId,
            mode = (string?)null,
            isRanked = false,
            trainerId = state.TrainerId,
            outcome = (string?)null,
            endedAt = (DateTimeOffset?)null,
            round = state.Round,
            wins = state.Wins,
            lives = state.Lives,
            team = state.Team.Select((unit, index) => new
            {
                slot = index,
                speciesId = unit.SpeciesId,
                level = unit.Level,
                isShiny = unit.IsShiny
            }),
            bench = state.Bench.Select((unit, index) => new
            {
                slot = index,
                speciesId = unit.SpeciesId,
                level = unit.Level,
                isShiny = unit.IsShiny
            }),
            trinkets = state.Trinkets,
            events = (events ?? Array.Empty<TrackerEvent>()).Select(item => new
            {
                eventType = item.Type.ToString(),
                detectedAt = item.DetectedAt,
                saveSequence = item.SaveSequence,
                confidence = item.Confidence.ToString(),
                payload = item.Payload
            })
        };
        string json = JsonSerializer.Serialize(payload);
        await PostJsonWithRetryAsync($"{apiBaseUrl}/api/sync/runs", json, cancellationToken);
    }

    public async Task<IReadOnlyList<GlobalBatomonStat>> GetGlobalTopBatomonAsync(
        string sort,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await client.GetAsync(
            $"{apiBaseUrl}/api/analytics/top-batomon?phase=all&sort={Uri.EscapeDataString(sort)}&scope=global",
            cancellationToken);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("batomon", out JsonElement rows) || rows.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<GlobalBatomonStat>();
        }

        return rows.EnumerateArray()
            .Select(row => new GlobalBatomonStat(
                row.GetProperty("speciesId").GetString() ?? "unknown",
                row.GetProperty("battleCount").GetInt64(),
                row.GetProperty("battleWins").GetInt64(),
                row.GetProperty("runCount").GetInt64(),
                row.GetProperty("runWins").GetInt64()))
            .ToArray();
    }

    public async Task<GlobalBestTeam> GetGlobalBestTeamAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await client.GetAsync($"{apiBaseUrl}/api/analytics/best-team", cancellationToken);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        JsonElement root = document.RootElement;
        string[] team = root.TryGetProperty("team", out JsonElement teamValue) && teamValue.ValueKind == JsonValueKind.Array
            ? teamValue.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()).Where(item => !string.IsNullOrWhiteSpace(item)).Cast<string>().ToArray()
            : Array.Empty<string>();
        long? wins = root.TryGetProperty("wins", out JsonElement winsValue) && winsValue.ValueKind == JsonValueKind.Number && winsValue.TryGetInt64(out long parsedWins)
            ? parsedWins
            : null;
        return new GlobalBestTeam(team, wins);
    }

    public async Task<GlobalBestCombo> GetGlobalBestComboAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await client.GetAsync($"{apiBaseUrl}/api/analytics/best-combo", cancellationToken);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        JsonElement root = document.RootElement;
        return new GlobalBestCombo(
            root.GetProperty("firstSpeciesId").GetString(),
            root.GetProperty("secondSpeciesId").GetString(),
            root.GetProperty("sharedRuns").GetInt64());
    }

    public async Task<GlobalBuildRecommendation> GetGlobalBuildRecommendationAsync(
        IReadOnlyList<string> team,
        IReadOnlyList<string> trinkets,
        CancellationToken cancellationToken = default)
    {
        var payload = new { team, trinkets };
        string json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await client.PostAsync(
            $"{apiBaseUrl}/api/analytics/recommendation", content, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        JsonElement root = document.RootElement;
        static string[] ReadStrings(JsonElement parent, string name) =>
            parent.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Array
                ? value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()).Where(item => !string.IsNullOrWhiteSpace(item)).Cast<string>().ToArray()
                : Array.Empty<string>();
        return new GlobalBuildRecommendation(
            ReadStrings(root, "recommendedSpecies"),
            ReadStrings(root, "referenceTeam"),
            ReadStrings(root, "recommendedTrinkets"),
            root.GetProperty("referenceWins").GetInt64(),
            root.GetProperty("teamOverlap").GetInt32());
    }

    public async Task SyncOpponentCacheAsync(
        OpponentCacheImportResult imported,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        if (imported.Snapshots.Count == 0 || string.IsNullOrWhiteSpace(accessToken))
        {
            return;
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        foreach (IGrouping<string, OpponentCacheSnapshot> group in imported.Snapshots.GroupBy(snapshot => snapshot.Dataset.DatasetId, StringComparer.Ordinal))
        {
            OpponentCacheDataset dataset = group.First().Dataset;
            imported.Metadata.TryGetValue(dataset.DatasetId, out OpponentCacheMetadata? metadata);
            var payload = new
            {
                dataset = new
                {
                    datasetId = dataset.DatasetId,
                    fileName = dataset.FileName,
                    mode = dataset.Mode,
                    setId = dataset.SetId,
                    segment = dataset.Segment
                },
                etag = metadata?.Etag,
                fetchedAt = metadata?.FetchedAt,
                snapshots = group.Select(snapshot => new
                {
                    snapshotHash = snapshot.SnapshotHash,
                    userId = snapshot.UserId,
                    displayName = snapshot.DisplayName,
                    runId = snapshot.RunId,
                    round = snapshot.Round,
                    mmr = snapshot.Mmr,
                    rankTier = snapshot.RankTier,
                    setId = snapshot.SetId,
                    version = snapshot.Version,
                    won = snapshot.Won,
                    winsBefore = snapshot.WinsBefore,
                    livesLost = snapshot.LivesLost,
                    trainerId = snapshot.TrainerId,
                    shopRank = snapshot.ShopRank,
                    rawJson = snapshot.RawJson,
                    units = snapshot.Units.Select(unit => new
                    {
                        position = unit.Position,
                        speciesId = unit.SpeciesId,
                        level = unit.Level,
                        isShiny = unit.IsShiny,
                        extraTypes = unit.ExtraTypes,
                        permanentBuffsJson = unit.PermanentBuffsJson,
                        blackboardJson = unit.BlackboardJson
                    }),
                    trinketIds = snapshot.TrinketIds,
                    usedTrinketIds = snapshot.UsedTrinketIds
                })
            };
            string json = JsonSerializer.Serialize(payload);
            await PostJsonWithRetryAsync($"{apiBaseUrl}/api/analytics/import", json, cancellationToken);
        }
    }

    private async Task PostJsonWithRetryAsync(string endpoint, string json, CancellationToken cancellationToken)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using HttpResponseMessage response = await client.PostAsync(endpoint, content, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }

                bool retryable = response.StatusCode == HttpStatusCode.TooManyRequests ||
                                 (int)response.StatusCode >= 500;
                if (!retryable || attempt >= 2)
                {
                    response.EnsureSuccessStatusCode();
                }
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < 2)
            {
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500 * (attempt + 1)), cancellationToken);
        }
    }

    private static object[] ParseTeam(string teamJson)
    {
        using JsonDocument document = JsonDocument.Parse(teamJson);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<object>();
        }

        return document.RootElement.EnumerateArray()
            .Select((unit, index) => new
            {
                slot = index,
                speciesId = unit.TryGetProperty("species_id", out JsonElement species) ? species.GetString() : null,
                level = unit.TryGetProperty("level", out JsonElement level) && level.TryGetInt32(out int parsedLevel) ? parsedLevel : (int?)null,
                isShiny = unit.TryGetProperty("is_shiny", out JsonElement shiny) && shiny.ValueKind == JsonValueKind.True
            })
            .Where(unit => !string.IsNullOrWhiteSpace(unit.speciesId))
            .Cast<object>()
            .ToArray();
    }
}

public sealed record GlobalBatomonStat(
    string SpeciesId,
    long BattleCount,
    long BattleWins,
    long RunCount,
    long RunWins);

public sealed record SteamProfile(string SteamId, string? DisplayName, string? AvatarUrl);

public sealed record GlobalBestTeam(IReadOnlyList<string> Team, long? Wins);

public sealed record GlobalBestCombo(string? FirstSpeciesId, string? SecondSpeciesId, long SharedRuns);

public sealed record GlobalBuildRecommendation(
    IReadOnlyList<string> RecommendedSpecies,
    IReadOnlyList<string> ReferenceTeam,
    IReadOnlyList<string> RecommendedTrinkets,
    long ReferenceWins,
    int TeamOverlap);
