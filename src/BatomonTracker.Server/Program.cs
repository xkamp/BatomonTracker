using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using NpgsqlTypes;
using System.Text.Json;
using System.Xml.Linq;
using System.Xml;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient();
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 256L * 1024L * 1024L;
});

string jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Jwt:Secret must be configured.");
if (Encoding.UTF8.GetByteCount(jwtSecret) < 32)
{
    throw new InvalidOperationException("Jwt:Secret must contain at least 32 bytes.");
}

string jwtIssuer = builder.Configuration["Jwt:Issuer"]
    ?? throw new InvalidOperationException("Jwt:Issuer must be configured.");
string jwtAudience = builder.Configuration["Jwt:Audience"] ?? "batomon-tracker-client";
string connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres must be configured.");

builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

const string LocalCallbackUrl = "http://127.0.0.1:48721/auth/callback/";
var pendingAuthStates = new ConcurrentDictionary<string, PendingAuthState>(StringComparer.Ordinal);
var pendingAuthCodes = new ConcurrentDictionary<string, PendingAuthCode>(StringComparer.Ordinal);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
             ValidateAudience = true,
             ValidAudience = jwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapGet("/", () => Results.Text("Batomon Tracker API is running."));

app.MapGet("/health", async () =>
{
    try
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT 1", connection);
        await command.ExecuteScalarAsync();
        return Results.Ok(new { status = "ok", database = "ok" });
    }
    catch (NpgsqlException exception)
    {
        app.Logger.LogError(exception, "Health check database failure.");
        return Results.Json(new { status = "error", database = "unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapGet("/auth/steam/login", (HttpRequest request) =>
{
    string returnUrl = request.Query["return_url"].ToString();
    string state = request.Query["state"].ToString();
    if (!Uri.TryCreate(returnUrl, UriKind.Absolute, out Uri? callbackUri) ||
        !string.Equals(returnUrl, LocalCallbackUrl + "?state=" + Uri.EscapeDataString(state), StringComparison.Ordinal) ||
        string.IsNullOrWhiteSpace(state))
    {
        return Results.BadRequest("Callback locale non valido.");
    }

    pendingAuthStates[state] = new PendingAuthState(returnUrl, DateTimeOffset.UtcNow.AddMinutes(5));
    string callback = $"{request.Scheme}://{request.Host}/auth/steam/callback?state={Uri.EscapeDataString(state)}";
    string realm = $"{request.Scheme}://{request.Host}";
    var parameters = new Dictionary<string, string>
    {
        ["openid.ns"] = "http://specs.openid.net/auth/2.0",
        ["openid.mode"] = "checkid_setup",
        ["openid.op_endpoint"] = "https://steamcommunity.com/openid/login",
        ["openid.claimed_id"] = "http://specs.openid.net/auth/2.0/identifier_select",
        ["openid.identity"] = "http://specs.openid.net/auth/2.0/identifier_select",
        ["openid.return_to"] = callback,
        ["openid.realm"] = realm
    };
    string query = string.Join("&", parameters.Select(pair =>
        $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
    return Results.Redirect($"https://steamcommunity.com/openid/login?{query}");
});

app.MapGet("/auth/steam/callback", async (HttpRequest request, IHttpClientFactory clients) =>
{
    if (!string.Equals(request.Query["openid.mode"].ToString(), "id_res", StringComparison.Ordinal))
    {
        return Results.BadRequest("Steam login non completato.");
    }

    string state = request.Query["state"].ToString();
    if (string.IsNullOrWhiteSpace(state) ||
        !pendingAuthStates.TryRemove(state, out PendingAuthState? pendingState) ||
        pendingState.ExpiresAt <= DateTimeOffset.UtcNow)
    {
        return Results.BadRequest("Sessione di autenticazione scaduta o non valida.");
    }

    string expectedReturnTo = $"{request.Scheme}://{request.Host}/auth/steam/callback?state={Uri.EscapeDataString(state)}";
    if (!string.Equals(request.Query["openid.return_to"].ToString(), expectedReturnTo, StringComparison.Ordinal))
    {
        return Results.BadRequest("Callback Steam non valido.");
    }

    var verification = request.Query
        .Where(item => item.Key.StartsWith("openid.", StringComparison.Ordinal))
        .ToDictionary(item => item.Key, item => item.Value.ToString());
    verification["openid.mode"] = "check_authentication";

    using HttpClient client = clients.CreateClient();
    using var content = new FormUrlEncodedContent(verification);
    using HttpResponseMessage response = await client.PostAsync("https://steamcommunity.com/openid/login", content);
    string verificationResponse = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode || !verificationResponse.Contains("is_valid:true", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest("La verifica Steam non e riuscita.");
    }

    string claimedId = request.Query["openid.claimed_id"].ToString();
    Match match = Regex.Match(claimedId, @"/id/(\d+)$");
    if (!match.Success)
    {
        return Results.BadRequest("SteamID non valido.");
    }

    string steamId = match.Groups[1].Value;
    string? displayName = null;
    string? avatarUrl = null;
    try
    {
        using HttpClient profileClient = clients.CreateClient();
        profileClient.Timeout = TimeSpan.FromSeconds(8);
        string profileXml = await profileClient.GetStringAsync($"https://steamcommunity.com/profiles/{steamId}?xml=1");
        XDocument profileDocument = XDocument.Parse(profileXml);
        displayName = profileDocument.Root?.Element("steamID")?.Value;
        avatarUrl = profileDocument.Root?.Element("avatarFull")?.Value;
    }
    catch (HttpRequestException)
    {
        // Authentication remains valid even if Steam profile metadata is temporarily unavailable.
    }
    catch (TaskCanceledException)
    {
        // Authentication remains valid even if Steam profile metadata times out.
    }
    catch (XmlException)
    {
        // Ignore malformed profile metadata and keep the SteamID.
    }

    Guid userId;
    await using (var connection = new NpgsqlConnection(connectionString))
    {
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO users (steam_id, display_name, avatar_url, last_login_at)
            VALUES (@steam_id, @display_name, @avatar_url, NOW())
            ON CONFLICT (steam_id) DO UPDATE SET
                display_name = COALESCE(EXCLUDED.display_name, users.display_name),
                avatar_url = COALESCE(EXCLUDED.avatar_url, users.avatar_url),
                last_login_at = NOW()
            RETURNING id;
            """, connection);
        command.Parameters.AddWithValue("steam_id", steamId);
        command.Parameters.AddWithValue("display_name", (object?)displayName ?? DBNull.Value);
        command.Parameters.AddWithValue("avatar_url", (object?)avatarUrl ?? DBNull.Value);
        userId = (Guid)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("User was not saved."));
    }

    string code = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    string refreshToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    await using (var connection = new NpgsqlConnection(connectionString))
    {
        await connection.OpenAsync();
        await using var sessionCommand = new NpgsqlCommand("""
            INSERT INTO auth_sessions (user_id, refresh_token_hash, expires_at)
            VALUES (@user_id, @refresh_token_hash, NOW() + INTERVAL '30 days');
            """, connection);
        sessionCommand.Parameters.AddWithValue("user_id", userId);
        sessionCommand.Parameters.AddWithValue("refresh_token_hash", HashToken(refreshToken));
        await sessionCommand.ExecuteNonQueryAsync();
    }

    pendingAuthCodes[code] = new PendingAuthCode(
        steamId,
        CreateToken(steamId, jwtSecret, jwtIssuer, jwtAudience),
        refreshToken,
        DateTimeOffset.UtcNow.AddMinutes(1));
    return Results.Redirect($"{pendingState.ReturnUrl}&code={Uri.EscapeDataString(code)}");
});

app.MapPost("/auth/token", (AuthCodeExchangeRequest payload) =>
{
    if (string.IsNullOrWhiteSpace(payload.Code) ||
        !pendingAuthCodes.TryRemove(payload.Code, out PendingAuthCode? pendingCode) ||
        pendingCode.ExpiresAt <= DateTimeOffset.UtcNow)
    {
        return Results.Unauthorized();
    }

    return Results.Ok(new { steamId = pendingCode.SteamId, accessToken = pendingCode.AccessToken, refreshToken = pendingCode.RefreshToken });
});

app.MapPost("/auth/refresh", async (RefreshTokenRequest payload) =>
{
    if (string.IsNullOrWhiteSpace(payload.RefreshToken))
    {
        return Results.Unauthorized();
    }

    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    await using var findCommand = new NpgsqlCommand("""
        SELECT s.id, u.steam_id
        FROM auth_sessions s JOIN users u ON u.id = s.user_id
        WHERE s.refresh_token_hash = @hash
          AND s.revoked_at IS NULL
          AND s.expires_at > NOW();
        """, connection, transaction);
    findCommand.Parameters.AddWithValue("hash", HashToken(payload.RefreshToken));
    await using NpgsqlDataReader reader = await findCommand.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        return Results.Unauthorized();
    }

    Guid sessionId = reader.GetGuid(0);
    string steamId = reader.GetString(1);
    await reader.CloseAsync();
    await using var revokeCommand = new NpgsqlCommand("UPDATE auth_sessions SET revoked_at = NOW() WHERE id = @id;", connection, transaction);
    revokeCommand.Parameters.AddWithValue("id", sessionId);
    await revokeCommand.ExecuteNonQueryAsync();
    string newRefreshToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    await using var insertCommand = new NpgsqlCommand("""
        INSERT INTO auth_sessions (user_id, refresh_token_hash, expires_at)
        SELECT user_id, @hash, NOW() + INTERVAL '30 days' FROM auth_sessions WHERE id = @id;
        """, connection, transaction);
    insertCommand.Parameters.AddWithValue("hash", HashToken(newRefreshToken));
    insertCommand.Parameters.AddWithValue("id", sessionId);
    await insertCommand.ExecuteNonQueryAsync();
    await transaction.CommitAsync();
    return Results.Ok(new { steamId, accessToken = CreateToken(steamId, jwtSecret, jwtIssuer, jwtAudience), refreshToken = newRefreshToken });
});

app.MapGet("/api/me", async (ClaimsPrincipal user) =>
{
    string steamId = user.FindFirstValue("steam_id") ?? string.Empty;
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand("SELECT steam_id, display_name, avatar_url FROM users WHERE steam_id = @steam_id", connection);
    command.Parameters.AddWithValue("steam_id", steamId);
    await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
    return await reader.ReadAsync()
        ? Results.Ok(new { steamId = reader.GetString(0), displayName = reader.IsDBNull(1) ? null : reader.GetString(1), avatarUrl = reader.IsDBNull(2) ? null : reader.GetString(2) })
        : Results.NotFound();
}).RequireAuthorization();

app.MapGet("/api/me/stats", async (ClaimsPrincipal user, string? filter) =>
{
    string steamId = user.FindFirstValue("steam_id") ?? string.Empty;
    bool? rankedFilter = filter?.Trim().ToLowerInvariant() switch
    {
        "ranked" => true,
        "unranked" => false,
        _ => null
    };
    object rankedValue = rankedFilter.HasValue ? rankedFilter.Value : DBNull.Value;
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();

    await using var summaryCommand = new NpgsqlCommand("""
        SELECT COUNT(*)::int,
               COUNT(*) FILTER (WHERE wins >= 10 OR LOWER(outcome) = 'won')::int,
               COALESCE(AVG(wins), 0)
        FROM runs r JOIN users u ON u.id = r.user_id
        WHERE u.steam_id = @steam_id
          AND r.ended_at IS NOT NULL
          AND (CAST(@ranked_filter AS boolean) IS NULL OR r.is_ranked = CAST(@ranked_filter AS boolean));
        """, connection);
    summaryCommand.Parameters.AddWithValue("steam_id", steamId);
    summaryCommand.Parameters.AddWithValue("ranked_filter", rankedValue);
    await using NpgsqlDataReader summaryReader = await summaryCommand.ExecuteReaderAsync();
    await summaryReader.ReadAsync();
    int completedRuns = summaryReader.GetInt32(0);
    int completedWins = summaryReader.GetInt32(1);
    double averageWins = summaryReader.GetDouble(2);
    await summaryReader.CloseAsync();

    await using var trainerCommand = new NpgsqlCommand("""
        SELECT COALESCE(r.trainer_id, 'unknown'), COUNT(*)::int,
               COUNT(*) FILTER (WHERE r.wins >= 10 OR LOWER(r.outcome) = 'won')::int
        FROM runs r JOIN users u ON u.id = r.user_id
        WHERE u.steam_id = @steam_id AND r.ended_at IS NOT NULL
          AND (CAST(@ranked_filter AS boolean) IS NULL OR r.is_ranked = CAST(@ranked_filter AS boolean))
        GROUP BY r.trainer_id ORDER BY 3 DESC, 2 DESC LIMIT 20;
        """, connection);
    trainerCommand.Parameters.AddWithValue("steam_id", steamId);
    trainerCommand.Parameters.AddWithValue("ranked_filter", rankedValue);
    await using NpgsqlDataReader trainerReader = await trainerCommand.ExecuteReaderAsync();
    var trainers = new List<object>();
    while (await trainerReader.ReadAsync())
    {
        int runs = trainerReader.GetInt32(1);
        int wins = trainerReader.GetInt32(2);
        trainers.Add(new { trainerId = trainerReader.GetString(0), runs, wins, winRate = runs == 0 ? 0 : (double)wins / runs });
    }
    await trainerReader.CloseAsync();

    await using var batomonCommand = new NpgsqlCommand("""
        SELECT ru.species_id, COUNT(DISTINCT r.id)::int,
               COUNT(DISTINCT r.id) FILTER (WHERE r.wins >= 10 OR LOWER(r.outcome) = 'won')::int
        FROM run_units ru
        JOIN runs r ON r.id = ru.run_id
        JOIN users u ON u.id = r.user_id
        WHERE u.steam_id = @steam_id AND r.ended_at IS NOT NULL AND ru.location = 'team'
          AND (CAST(@ranked_filter AS boolean) IS NULL OR r.is_ranked = CAST(@ranked_filter AS boolean))
        GROUP BY ru.species_id ORDER BY 3 DESC, 2 DESC LIMIT 20;
        """, connection);
    batomonCommand.Parameters.AddWithValue("steam_id", steamId);
    batomonCommand.Parameters.AddWithValue("ranked_filter", rankedValue);
    await using NpgsqlDataReader batomonReader = await batomonCommand.ExecuteReaderAsync();
    var batomon = new List<object>();
    while (await batomonReader.ReadAsync())
    {
        int runs = batomonReader.GetInt32(1);
        int wins = batomonReader.GetInt32(2);
        batomon.Add(new { speciesId = batomonReader.GetString(0), runs, wins, winRate = runs == 0 ? 0 : (double)wins / runs });
    }

    return Results.Ok(new
    {
        completedRuns,
        completedWins,
        winRate = completedRuns == 0 ? 0 : (double)completedWins / completedRuns,
        averageWins,
        byTrainer = trainers,
        byBatomon = batomon
    });
}).RequireAuthorization();

app.MapPost("/api/sync/runs", async (ClaimsPrincipal user, SyncRunRequest payload) =>
{
    string steamId = user.FindFirstValue("steam_id") ?? string.Empty;
    if (string.IsNullOrWhiteSpace(payload.ExternalRunId) ||
        payload.ExternalRunId.Length > 160 ||
        payload.Team is null || payload.Team.Count > 10 ||
        payload.Bench is null || payload.Bench.Count > 20 ||
        payload.Trinkets is null || payload.Trinkets.Count > 30 ||
        payload.Events is null || payload.Events.Count > 50)
    {
        return Results.BadRequest("Payload run non valido o troppo grande.");
    }

    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
    Guid userId = await GetUserIdAsync(connection, transaction, steamId);
    Guid runId = await UpsertRunAsync(connection, transaction, userId, payload);

    await ExecuteAsync(connection, transaction, "DELETE FROM run_units WHERE run_id = @run_id; DELETE FROM run_trinkets WHERE run_id = @run_id;", ("run_id", runId));
    foreach (SyncUnit unit in payload.Team)
    {
        await ExecuteAsync(connection, transaction, """
            INSERT INTO run_units (run_id, location, slot, species_id, level, is_shiny)
            VALUES (@run_id, @location, @slot, @species_id, @level, @is_shiny);
            """, ("run_id", runId), ("location", "team"), ("slot", unit.Slot), ("species_id", unit.SpeciesId), ("level", unit.Level), ("is_shiny", unit.IsShiny));
    }

    foreach (SyncUnit unit in payload.Bench)
    {
        await ExecuteAsync(connection, transaction, """
            INSERT INTO run_units (run_id, location, slot, species_id, level, is_shiny)
            VALUES (@run_id, @location, @slot, @species_id, @level, @is_shiny);
            """, ("run_id", runId), ("location", "bench"), ("slot", unit.Slot), ("species_id", unit.SpeciesId), ("level", unit.Level), ("is_shiny", unit.IsShiny));
    }

    foreach (string trinket in payload.Trinkets.Distinct(StringComparer.Ordinal))
    {
        await ExecuteAsync(connection, transaction, "INSERT INTO run_trinkets (run_id, trinket_id) VALUES (@run_id, @trinket_id);", ("run_id", runId), ("trinket_id", trinket));
    }

    foreach (SyncEvent trackerEvent in payload.Events)
    {
        if (string.IsNullOrWhiteSpace(trackerEvent.EventType) || trackerEvent.EventType.Length > 64 ||
            trackerEvent.Confidence.Length > 32 || trackerEvent.Payload.Count > 30)
        {
            return Results.BadRequest("Evento tracker non valido.");
        }

        await ExecuteAsync(connection, transaction, """
            INSERT INTO run_events (run_id, save_seq, event_type, confidence, payload, detected_at)
            VALUES (@run_id, @save_seq, @event_type, @confidence, @payload, @detected_at);
            """, ("run_id", runId), ("save_seq", trackerEvent.SaveSequence),
            ("event_type", trackerEvent.EventType), ("confidence", trackerEvent.Confidence),
            ("payload", JsonSerializer.Serialize(trackerEvent.Payload)), ("detected_at", trackerEvent.DetectedAt));
    }

    await transaction.CommitAsync();
    return Results.Ok(new { runId, synced = true });
}).RequireAuthorization();

app.MapGet("/api/patch-notes", async () =>
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand("SELECT title, body, published_at FROM patch_notes WHERE published_at IS NOT NULL ORDER BY published_at DESC LIMIT 20", connection);
    await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
    var notes = new List<object>();
    while (await reader.ReadAsync())
    {
        notes.Add(new { title = reader.GetString(0), body = reader.GetString(1), publishedAt = reader.GetDateTime(2) });
    }

    return Results.Ok(notes);
});

app.MapGet("/api/leaderboard", async () =>
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand("""
        SELECT u.display_name, l.score, l.wins, l.games_played, l.rank
        FROM leaderboard_entries l
        JOIN users u ON u.id = l.user_id
        JOIN seasons s ON s.id = l.season_id AND s.is_active = TRUE
        ORDER BY l.score DESC
        LIMIT 100;
        """, connection);
    await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
    var entries = new List<object>();
    while (await reader.ReadAsync())
    {
        entries.Add(new { displayName = reader.IsDBNull(0) ? null : reader.GetString(0), score = reader.GetInt32(1), wins = reader.GetInt32(2), gamesPlayed = reader.GetInt32(3), rank = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4) });
    }

    return Results.Ok(entries);
});

app.MapGet("/api/analytics/batomon/{speciesId}", async (ClaimsPrincipal user, string speciesId, string? datasetId, string? phase, string? scope) =>
{
    if (string.IsNullOrWhiteSpace(speciesId))
    {
        return Results.BadRequest("speciesId is required.");
    }

    string normalizedPhase = NormalizeAnalyticsPhase(phase);
    string? ownerSteamId = ResolveAnalyticsOwner(user, scope);
    if (scope?.Equals("mine", StringComparison.OrdinalIgnoreCase) == true && ownerSteamId is null)
    {
        return Results.Unauthorized();
    }
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand("""
        WITH target_snapshots AS (
            SELECT DISTINCT s.id, s.dataset_id, s.user_id, s.external_run_id, s.won
            FROM analytics_snapshots s
            JOIN analytics_datasets d ON d.id = s.dataset_id
            JOIN analytics_snapshot_units u ON u.snapshot_id = s.id
            WHERE LOWER(u.species_id) = LOWER(@species_id)
              AND (CAST(@dataset_id AS text) IS NULL OR d.dataset_id = CAST(@dataset_id AS text))
              AND (CAST(@owner_steam_id AS text) IS NULL OR s.user_id = CAST(@owner_steam_id AS text))
              AND (@phase = 'all' OR (@phase = 'standard' AND (s.wins_before IS NULL OR s.wins_before < 10)) OR (@phase = 'extended' AND s.wins_before >= 10))
        ),
        target_runs AS (
            SELECT DISTINCT rf.*
            FROM analytics_run_facts rf
            JOIN target_snapshots ts ON ts.dataset_id = rf.dataset_id
                AND ts.user_id = rf.user_id
                AND ts.external_run_id = rf.external_run_id
        )
        SELECT
            COUNT(*)::bigint AS battle_count,
            COUNT(*) FILTER (WHERE won)::bigint AS battle_wins,
            (SELECT COUNT(*) FROM target_runs)::bigint AS run_count,
            (SELECT COUNT(*) FROM target_runs WHERE is_complete)::bigint AS completed_run_count,
            (SELECT COUNT(*) FROM target_runs WHERE is_complete AND reached_ten_wins)::bigint AS run_wins
        FROM target_snapshots;
        """, connection);
    command.Parameters.AddWithValue("species_id", speciesId);
    command.Parameters.AddWithValue("dataset_id", (object?)datasetId ?? DBNull.Value);
    command.Parameters.AddWithValue("phase", normalizedPhase);
    command.Parameters.AddWithValue("owner_steam_id", (object?)ownerSteamId ?? DBNull.Value);
    await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
    await reader.ReadAsync();
    return Results.Ok(new
    {
        speciesId,
        phase = normalizedPhase,
        battleCount = reader.GetInt64(0),
        battleWins = reader.GetInt64(1),
        runCount = reader.GetInt64(2),
        completedRunCount = reader.GetInt64(3),
        runWins = reader.GetInt64(4)
    });
});

app.MapGet("/api/analytics/batomon/{speciesId}/companions", async (ClaimsPrincipal user, string speciesId, string? datasetId, string? phase, string? scope) =>
{
    if (string.IsNullOrWhiteSpace(speciesId))
    {
        return Results.BadRequest("speciesId is required.");
    }

    string normalizedPhase = NormalizeAnalyticsPhase(phase);
    string? ownerSteamId = ResolveAnalyticsOwner(user, scope);
    if (scope?.Equals("mine", StringComparison.OrdinalIgnoreCase) == true && ownerSteamId is null)
    {
        return Results.Unauthorized();
    }
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand("""
        WITH target_runs AS (
            SELECT DISTINCT s.dataset_id, s.user_id, s.external_run_id
            FROM analytics_snapshots s
            JOIN analytics_datasets d ON d.id = s.dataset_id
            JOIN analytics_snapshot_units target_unit ON target_unit.snapshot_id = s.id
            WHERE LOWER(target_unit.species_id) = LOWER(@species_id)
              AND (CAST(@dataset_id AS text) IS NULL OR d.dataset_id = CAST(@dataset_id AS text))
              AND (CAST(@owner_steam_id AS text) IS NULL OR s.user_id = CAST(@owner_steam_id AS text))
              AND (@phase = 'all' OR (@phase = 'standard' AND (s.wins_before IS NULL OR s.wins_before < 10)) OR (@phase = 'extended' AND s.wins_before >= 10))
        ),
        companion_runs AS (
            SELECT DISTINCT tr.dataset_id, tr.user_id, tr.external_run_id, companion.species_id
            FROM target_runs tr
            JOIN analytics_snapshots s ON s.dataset_id = tr.dataset_id
                AND s.user_id = tr.user_id
                AND s.external_run_id = tr.external_run_id
            JOIN analytics_snapshot_units companion ON companion.snapshot_id = s.id
            WHERE LOWER(companion.species_id) <> LOWER(@species_id)
              AND (@phase = 'all' OR (@phase = 'standard' AND (s.wins_before IS NULL OR s.wins_before < 10)) OR (@phase = 'extended' AND s.wins_before >= 10))
        )
        SELECT
            cr.species_id,
            COUNT(*)::bigint AS shared_run_count,
            COUNT(*) FILTER (WHERE rf.is_complete)::bigint AS completed_shared_run_count,
            COUNT(*) FILTER (WHERE rf.is_complete AND rf.reached_ten_wins)::bigint AS shared_run_wins
        FROM companion_runs cr
        JOIN analytics_run_facts rf ON rf.dataset_id = cr.dataset_id
            AND rf.user_id = cr.user_id
            AND rf.external_run_id = cr.external_run_id
        GROUP BY cr.species_id
        ORDER BY shared_run_count DESC, cr.species_id;
        """, connection);
    command.Parameters.AddWithValue("species_id", speciesId);
    command.Parameters.AddWithValue("dataset_id", (object?)datasetId ?? DBNull.Value);
    command.Parameters.AddWithValue("phase", normalizedPhase);
    command.Parameters.AddWithValue("owner_steam_id", (object?)ownerSteamId ?? DBNull.Value);
    await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
    var companions = new List<object>();
    while (await reader.ReadAsync())
    {
        companions.Add(new
        {
            speciesId = reader.GetString(0),
            sharedRunCount = reader.GetInt64(1),
            completedSharedRunCount = reader.GetInt64(2),
            sharedRunWins = reader.GetInt64(3)
        });
    }

    return Results.Ok(new { speciesId, phase = normalizedPhase, companions });
});

app.MapGet("/api/analytics/rounds", async (ClaimsPrincipal user, string? datasetId, string? phase, string? scope) =>
{
    string normalizedPhase = NormalizeAnalyticsPhase(phase);
    string? ownerSteamId = ResolveAnalyticsOwner(user, scope);
    if (scope?.Equals("mine", StringComparison.OrdinalIgnoreCase) == true && ownerSteamId is null)
    {
        return Results.Unauthorized();
    }
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand("""
        SELECT s.round,
               COUNT(*)::bigint AS battle_count,
               COUNT(*) FILTER (WHERE s.won)::bigint AS battle_wins
        FROM analytics_snapshots s
        JOIN analytics_datasets d ON d.id = s.dataset_id
           WHERE (CAST(@dataset_id AS text) IS NULL OR d.dataset_id = CAST(@dataset_id AS text))
           AND (CAST(@owner_steam_id AS text) IS NULL OR s.user_id = CAST(@owner_steam_id AS text))
          AND (@phase = 'all' OR (@phase = 'standard' AND (s.wins_before IS NULL OR s.wins_before < 10)) OR (@phase = 'extended' AND s.wins_before >= 10))
        GROUP BY s.round
        ORDER BY s.round;
        """, connection);
    command.Parameters.AddWithValue("dataset_id", (object?)datasetId ?? DBNull.Value);
    command.Parameters.AddWithValue("phase", normalizedPhase);
    command.Parameters.AddWithValue("owner_steam_id", (object?)ownerSteamId ?? DBNull.Value);
    await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
    var rounds = new List<object>();
    while (await reader.ReadAsync())
    {
        rounds.Add(new
        {
            round = reader.GetInt32(0),
            battleCount = reader.GetInt64(1),
            battleWins = reader.GetInt64(2)
        });
    }

    return Results.Ok(new { phase = normalizedPhase, rounds });
});

app.MapGet("/api/analytics/overview", async (ClaimsPrincipal user, string? datasetId, string? phase, string? scope) =>
{
    string normalizedPhase = NormalizeAnalyticsPhase(phase);
    string? ownerSteamId = ResolveAnalyticsOwner(user, scope);
    if (scope?.Equals("mine", StringComparison.OrdinalIgnoreCase) == true && ownerSteamId is null)
    {
        return Results.Unauthorized();
    }
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand("""
        WITH filtered_snapshots AS (
            SELECT s.*
            FROM analytics_snapshots s
            JOIN analytics_datasets d ON d.id = s.dataset_id
             WHERE (CAST(@dataset_id AS text) IS NULL OR d.dataset_id = CAST(@dataset_id AS text))
               AND (CAST(@owner_steam_id AS text) IS NULL OR s.user_id = CAST(@owner_steam_id AS text))
              AND (@phase = 'all' OR (@phase = 'standard' AND (s.wins_before IS NULL OR s.wins_before < 10)) OR (@phase = 'extended' AND s.wins_before >= 10))
        ),
        filtered_runs AS (
            SELECT rf.*
            FROM analytics_run_facts rf
            WHERE EXISTS (
                SELECT 1
                FROM filtered_snapshots fs
                WHERE fs.dataset_id = rf.dataset_id
                  AND fs.user_id = rf.user_id
                  AND fs.external_run_id = rf.external_run_id
            )
        )
        SELECT
            (SELECT COUNT(*) FROM filtered_snapshots)::bigint AS snapshot_count,
            (SELECT COUNT(*) FROM filtered_runs)::bigint AS observed_run_count,
            (SELECT COUNT(*) FROM filtered_runs WHERE is_complete)::bigint AS complete_run_count,
            (SELECT COUNT(*) FROM filtered_runs WHERE reached_ten_wins)::bigint AS ten_win_run_count,
            (SELECT COUNT(*) FROM filtered_runs WHERE has_extended_phase)::bigint AS extended_run_count,
            (SELECT COUNT(DISTINCT dataset_id) FROM filtered_snapshots)::bigint AS dataset_count,
            (SELECT MIN(round) FROM filtered_snapshots) AS first_round,
            (SELECT MAX(round) FROM filtered_snapshots) AS last_round;
        """, connection);
    command.Parameters.AddWithValue("dataset_id", (object?)datasetId ?? DBNull.Value);
    command.Parameters.AddWithValue("phase", normalizedPhase);
    command.Parameters.AddWithValue("owner_steam_id", (object?)ownerSteamId ?? DBNull.Value);
    await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
    await reader.ReadAsync();
    return Results.Ok(new
    {
        phase = normalizedPhase,
        snapshotCount = reader.GetInt64(0),
        observedRunCount = reader.GetInt64(1),
        completeRunCount = reader.GetInt64(2),
        tenWinRunCount = reader.GetInt64(3),
        extendedRunCount = reader.GetInt64(4),
        datasetCount = reader.GetInt64(5),
        firstRound = reader.IsDBNull(6) ? (int?)null : reader.GetInt32(6),
        lastRound = reader.IsDBNull(7) ? (int?)null : reader.GetInt32(7)
    });
});

app.MapGet("/api/analytics/top-batomon", async (ClaimsPrincipal user, string? datasetId, string? phase, string? scope, string? sort) =>
{
    string normalizedPhase = NormalizeAnalyticsPhase(phase);
    string orderBy = string.Equals(sort, "wins", StringComparison.OrdinalIgnoreCase)
        ? "run_wins DESC, battle_wins DESC, ub.species_id"
        : "battle_count DESC, ub.species_id";
    string? ownerSteamId = ResolveAnalyticsOwner(user, scope);
    if (scope?.Equals("mine", StringComparison.OrdinalIgnoreCase) == true && ownerSteamId is null)
    {
        return Results.Unauthorized();
    }
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand($$"""
        WITH filtered_snapshots AS (
            SELECT s.id, s.dataset_id, s.user_id, s.external_run_id, s.won
            FROM analytics_snapshots s
            JOIN analytics_datasets d ON d.id = s.dataset_id
             WHERE (CAST(@dataset_id AS text) IS NULL OR d.dataset_id = CAST(@dataset_id AS text))
               AND (CAST(@owner_steam_id AS text) IS NULL OR s.user_id = CAST(@owner_steam_id AS text))
              AND (@phase = 'all' OR (@phase = 'standard' AND (s.wins_before IS NULL OR s.wins_before < 10)) OR (@phase = 'extended' AND s.wins_before >= 10))
        ),
        unit_battles AS (
            SELECT DISTINCT fs.id, fs.dataset_id, fs.user_id, fs.external_run_id, fs.won, LOWER(u.species_id) AS species_id
            FROM filtered_snapshots fs
            JOIN analytics_snapshot_units u ON u.snapshot_id = fs.id
        ),
        unit_runs AS (
            SELECT DISTINCT dataset_id, user_id, external_run_id, species_id
            FROM unit_battles
        )
        SELECT
            ub.species_id,
            COUNT(*)::bigint AS battle_count,
            COUNT(*) FILTER (WHERE ub.won)::bigint AS battle_wins,
            COUNT(DISTINCT ur.dataset_id || ':' || ur.user_id || ':' || ur.external_run_id)::bigint AS run_count,
            COUNT(DISTINCT ur.dataset_id || ':' || ur.user_id || ':' || ur.external_run_id) FILTER (WHERE rf.is_complete)::bigint AS complete_run_count,
            COUNT(DISTINCT ur.dataset_id || ':' || ur.user_id || ':' || ur.external_run_id) FILTER (WHERE rf.is_complete AND rf.reached_ten_wins)::bigint AS run_wins
        FROM unit_battles ub
        JOIN unit_runs ur ON ur.dataset_id = ub.dataset_id AND ur.user_id = ub.user_id
            AND ur.external_run_id = ub.external_run_id AND ur.species_id = ub.species_id
        JOIN analytics_run_facts rf ON rf.dataset_id = ur.dataset_id AND rf.user_id = ur.user_id
            AND rf.external_run_id = ur.external_run_id
        GROUP BY ub.species_id
         ORDER BY {{orderBy}}
         LIMIT 20;
        """, connection);
    command.Parameters.AddWithValue("dataset_id", (object?)datasetId ?? DBNull.Value);
    command.Parameters.AddWithValue("phase", normalizedPhase);
    command.Parameters.AddWithValue("owner_steam_id", (object?)ownerSteamId ?? DBNull.Value);
    await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
    var batomon = new List<object>();
    while (await reader.ReadAsync())
    {
        batomon.Add(new
        {
            speciesId = reader.GetString(0),
            battleCount = reader.GetInt64(1),
            battleWins = reader.GetInt64(2),
            runCount = reader.GetInt64(3),
            completeRunCount = reader.GetInt64(4),
            runWins = reader.GetInt64(5)
        });
    }

    return Results.Ok(new { phase = normalizedPhase, batomon });
});

app.MapGet("/api/analytics/datasets", async () =>
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand("""
        SELECT dataset_id, file_name, mode, set_id, segment, fetched_at
        FROM analytics_datasets
        ORDER BY mode, set_id, segment NULLS FIRST;
        """, connection);
    await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
    var datasets = new List<object>();
    while (await reader.ReadAsync())
    {
        datasets.Add(new
        {
            datasetId = reader.GetString(0),
            fileName = reader.GetString(1),
            mode = reader.GetString(2),
            setId = reader.GetString(3),
            segment = reader.IsDBNull(4) ? null : reader.GetString(4),
            fetchedAt = reader.IsDBNull(5) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(5)
        });
    }

    return Results.Ok(datasets);
});

app.MapGet("/api/analytics/best-team", async () =>
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand("""
        WITH winning_runs AS (
            SELECT DISTINCT ON (s.dataset_id, s.user_id, s.external_run_id)
                s.id, s.dataset_id, s.user_id, s.external_run_id,
                s.wins_before + CASE WHEN s.won THEN 1 ELSE 0 END AS wins
            FROM analytics_snapshots s
            JOIN analytics_run_facts rf ON rf.dataset_id = s.dataset_id
                AND rf.user_id = s.user_id AND rf.external_run_id = s.external_run_id
            WHERE rf.reached_ten_wins
            ORDER BY s.dataset_id, s.user_id, s.external_run_id, s.round DESC, s.id DESC
        )
        SELECT array_agg(DISTINCT LOWER(u.species_id) ORDER BY LOWER(u.species_id)), MAX(wr.wins)::bigint
        FROM winning_runs wr
        JOIN analytics_snapshot_units u ON u.snapshot_id = wr.id
        GROUP BY wr.dataset_id, wr.user_id, wr.external_run_id
        ORDER BY MAX(wr.wins) DESC
        LIMIT 1;
        """, connection);
    await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        return Results.Ok(new { team = Array.Empty<string>(), wins = (long?)null });
    }

    return Results.Ok(new
    {
        team = reader.IsDBNull(0) ? Array.Empty<string>() : reader.GetFieldValue<string[]>(0),
        wins = reader.IsDBNull(1) ? (long?)null : reader.GetInt64(1)
    });
});

app.MapGet("/api/analytics/best-combo", async () =>
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand("""
        WITH winning_runs AS (
            SELECT DISTINCT dataset_id, user_id, external_run_id
            FROM analytics_run_facts
            WHERE reached_ten_wins
        ),
        run_species AS (
            SELECT DISTINCT wr.dataset_id, wr.user_id, wr.external_run_id, LOWER(u.species_id) AS species_id
            FROM winning_runs wr
            JOIN analytics_snapshots s ON s.dataset_id = wr.dataset_id
                AND s.user_id = wr.user_id AND s.external_run_id = wr.external_run_id
            JOIN analytics_snapshot_units u ON u.snapshot_id = s.id
        )
        SELECT first_species.species_id, second_species.species_id, COUNT(*)::bigint AS shared_runs
        FROM run_species first_species
        JOIN run_species second_species ON second_species.dataset_id = first_species.dataset_id
            AND second_species.user_id = first_species.user_id
            AND second_species.external_run_id = first_species.external_run_id
            AND first_species.species_id < second_species.species_id
        GROUP BY first_species.species_id, second_species.species_id
        ORDER BY shared_runs DESC, first_species.species_id, second_species.species_id
        LIMIT 1;
        """, connection);
    await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
    return await reader.ReadAsync()
        ? Results.Ok(new { firstSpeciesId = reader.GetString(0), secondSpeciesId = reader.GetString(1), sharedRuns = reader.GetInt64(2) })
        : Results.Ok(new { firstSpeciesId = (string?)null, secondSpeciesId = (string?)null, sharedRuns = 0L });
});

app.MapPost("/api/analytics/recommendation", async (GlobalRecommendationRequest payload) =>
{
    string[] currentTeam = payload.Team
        .Where(speciesId => !string.IsNullOrWhiteSpace(speciesId))
        .Select(speciesId => speciesId.Trim().ToLowerInvariant())
        .Distinct(StringComparer.Ordinal)
        .ToArray();
    if (currentTeam.Length == 0)
    {
        return Results.Ok(new GlobalRecommendationResponse(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), 0, 0));
    }

    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var matchCommand = new NpgsqlCommand("""
        WITH winning_runs AS (
            SELECT dataset_id, user_id, external_run_id, MAX(max_wins_observed)::bigint AS wins
            FROM analytics_run_facts
            WHERE reached_ten_wins
            GROUP BY dataset_id, user_id, external_run_id
        ),
        run_species AS (
            SELECT DISTINCT wr.dataset_id, wr.user_id, wr.external_run_id, wr.wins, LOWER(u.species_id) AS species_id
            FROM winning_runs wr
            JOIN analytics_snapshots s ON s.dataset_id = wr.dataset_id
                AND s.user_id = wr.user_id AND s.external_run_id = wr.external_run_id
            JOIN analytics_snapshot_units u ON u.snapshot_id = s.id
        )
        SELECT dataset_id, user_id, external_run_id, wins,
               COUNT(*) FILTER (WHERE species_id = ANY(@current_team))::int AS overlap,
               COUNT(*)::int AS species_count
        FROM run_species
        GROUP BY dataset_id, user_id, external_run_id, wins
        ORDER BY overlap DESC, species_count ASC, wins DESC
        LIMIT 1;
        """, connection);
    matchCommand.Parameters.AddWithValue("current_team", currentTeam);
    await using NpgsqlDataReader matchReader = await matchCommand.ExecuteReaderAsync();
    if (!await matchReader.ReadAsync())
    {
        return Results.Ok(new GlobalRecommendationResponse(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), 0, 0));
    }

    Guid datasetId = matchReader.GetGuid(0);
    string userId = matchReader.GetString(1);
    string externalRunId = matchReader.GetString(2);
    long wins = matchReader.GetInt64(3);
    int overlap = matchReader.GetInt32(4);
    await matchReader.CloseAsync();

    await using var detailCommand = new NpgsqlCommand("""
        SELECT
            COALESCE((SELECT array_agg(DISTINCT LOWER(u.species_id) ORDER BY LOWER(u.species_id))
                      FROM analytics_snapshots s JOIN analytics_snapshot_units u ON u.snapshot_id = s.id
                      WHERE s.dataset_id = @dataset_id AND s.user_id = @user_id AND s.external_run_id = @external_run_id), ARRAY[]::text[]),
            COALESCE((SELECT array_agg(DISTINCT t.trinket_id ORDER BY t.trinket_id)
                      FROM analytics_snapshots s JOIN analytics_snapshot_trinkets t ON t.snapshot_id = s.id
                      WHERE s.dataset_id = @dataset_id AND s.user_id = @user_id AND s.external_run_id = @external_run_id), ARRAY[]::text[]);
        """, connection);
    detailCommand.Parameters.AddWithValue("dataset_id", datasetId);
    detailCommand.Parameters.AddWithValue("user_id", userId);
    detailCommand.Parameters.AddWithValue("external_run_id", externalRunId);
    await using NpgsqlDataReader detailReader = await detailCommand.ExecuteReaderAsync();
    await detailReader.ReadAsync();
    string[] referenceTeam = detailReader.GetFieldValue<string[]>(0);
    string[] tools = detailReader.GetFieldValue<string[]>(1);
    string[] recommendedSpecies = referenceTeam
        .Where(speciesId => !currentTeam.Contains(speciesId, StringComparer.OrdinalIgnoreCase))
        .ToArray();
    return Results.Ok(new GlobalRecommendationResponse(recommendedSpecies, referenceTeam, tools, wins, overlap));
});

app.MapPost("/api/analytics/import", async (ClaimsPrincipal user, AnalyticsImportRequest payload) =>
{
    if (string.IsNullOrWhiteSpace(payload.Dataset.DatasetId) || payload.Snapshots.Count == 0 || payload.Snapshots.Count > 1000)
    {
        return Results.BadRequest("Dataset non valido o numero di snapshot fuori limite.");
    }

    string steamId = user.FindFirstValue("steam_id") ?? string.Empty;
    if (payload.Dataset.DatasetId.Length > 160 ||
        payload.Dataset.FileName.Length > 255 ||
        payload.Dataset.Mode.Length > 32 ||
        payload.Dataset.SetId.Length > 64 ||
        payload.Snapshots.Any(snapshot =>
            snapshot.RawJson.Length > 200_000 ||
            snapshot.Units is null || snapshot.Units.Count > 10 ||
            snapshot.TrinketIds is null || snapshot.TrinketIds.Count > 30 ||
            snapshot.UsedTrinketIds is null || snapshot.UsedTrinketIds.Count > 30 ||
            snapshot.RunId.Length > 160 || snapshot.SnapshotHash.Length > 64 || snapshot.UserId.Length > 64))
    {
        return Results.BadRequest("Payload analytics non valido o troppo grande.");
    }

    payload = payload with
    {
        Snapshots = payload.Snapshots.Select(snapshot => snapshot with { UserId = steamId }).ToArray()
    };
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
    Guid datasetDatabaseId = await UpsertAnalyticsDatasetAsync(connection, transaction, payload.Dataset, payload.Etag, payload.FetchedAt);
    int imported = 0;

    foreach (AnalyticsSnapshotRequest snapshot in payload.Snapshots)
    {
        Guid snapshotId = await UpsertAnalyticsSnapshotAsync(connection, transaction, datasetDatabaseId, snapshot);
        await InsertAnalyticsChildrenAsync(connection, transaction, snapshotId, snapshot);
        imported++;
    }

    await transaction.CommitAsync();
    return Results.Ok(new { steamId, datasetId = payload.Dataset.DatasetId, imported });
}).RequireAuthorization();

app.Run();

static string CreateToken(string steamId, string secret, string issuer, string audience)
{
    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
    var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    var claims = new[]
    {
        new Claim(JwtRegisteredClaimNames.Sub, steamId),
        new Claim("steam_id", steamId)
    };
    var token = new JwtSecurityToken(issuer, audience, claims, expires: DateTime.UtcNow.AddMinutes(30), signingCredentials: credentials);
    return new JwtSecurityTokenHandler().WriteToken(token);
}

static string HashToken(string token)
{
    return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

static async Task<Guid> GetUserIdAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string steamId)
{
    await using var command = new NpgsqlCommand("SELECT id FROM users WHERE steam_id = @steam_id", connection, transaction);
    command.Parameters.AddWithValue("steam_id", steamId);
    return (Guid)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("User not found."));
}

static async Task<Guid> UpsertRunAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid userId, SyncRunRequest payload)
{
    await using var command = new NpgsqlCommand("""
        INSERT INTO runs (user_id, external_run_id, mode, is_ranked, trainer_id, outcome, ended_at, round, wins, lives)
        VALUES (@user_id, @external_run_id, @mode, @is_ranked, @trainer_id, @outcome, @ended_at, @round, @wins, @lives)
        ON CONFLICT (user_id, external_run_id) DO UPDATE SET
            mode = excluded.mode, is_ranked = excluded.is_ranked, trainer_id = excluded.trainer_id,
            outcome = excluded.outcome, ended_at = excluded.ended_at, round = excluded.round,
            wins = excluded.wins, lives = excluded.lives, uploaded_at = NOW()
        RETURNING id;
        """, connection, transaction);
    command.Parameters.AddWithValue("user_id", userId);
    command.Parameters.AddWithValue("external_run_id", payload.ExternalRunId);
    command.Parameters.AddWithValue("mode", (object?)payload.Mode ?? DBNull.Value);
    command.Parameters.AddWithValue("is_ranked", payload.IsRanked);
    command.Parameters.AddWithValue("trainer_id", (object?)payload.TrainerId ?? DBNull.Value);
    command.Parameters.AddWithValue("outcome", (object?)payload.Outcome ?? DBNull.Value);
    command.Parameters.AddWithValue("ended_at", (object?)payload.EndedAt ?? DBNull.Value);
    command.Parameters.AddWithValue("round", (object?)payload.Round ?? DBNull.Value);
    command.Parameters.AddWithValue("wins", (object?)payload.Wins ?? DBNull.Value);
    command.Parameters.AddWithValue("lives", (object?)payload.Lives ?? DBNull.Value);
    return (Guid)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Run was not saved."));
}

static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, params (string Name, object? Value)[] parameters)
{
    await using var command = new NpgsqlCommand(sql, connection, transaction);
    foreach ((string name, object? value) in parameters)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    await command.ExecuteNonQueryAsync();
}

static string NormalizeAnalyticsPhase(string? phase)
{
    return phase?.Trim().ToLowerInvariant() switch
    {
        "standard" => "standard",
        "extended" => "extended",
        _ => "all"
    };
}

static string? ResolveAnalyticsOwner(ClaimsPrincipal user, string? scope)
{
    if (!string.Equals(scope, "mine", StringComparison.OrdinalIgnoreCase))
    {
        return null;
    }

    return user.FindFirstValue("steam_id");
}

static async Task<Guid> UpsertAnalyticsDatasetAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    AnalyticsDatasetRequest dataset,
    string? etag,
    DateTimeOffset? fetchedAt)
{
    await using var command = new NpgsqlCommand("""
        INSERT INTO analytics_datasets (dataset_id, file_name, mode, set_id, segment, etag, fetched_at)
        VALUES (@dataset_id, @file_name, @mode, @set_id, @segment, @etag, @fetched_at)
        ON CONFLICT (dataset_id) DO UPDATE SET
            file_name = EXCLUDED.file_name,
            mode = EXCLUDED.mode,
            set_id = EXCLUDED.set_id,
            segment = EXCLUDED.segment,
            etag = EXCLUDED.etag,
            fetched_at = EXCLUDED.fetched_at,
            imported_at = NOW();
        SELECT id FROM analytics_datasets WHERE dataset_id = @dataset_id;
        """, connection, transaction);
    command.Parameters.AddWithValue("dataset_id", dataset.DatasetId);
    command.Parameters.AddWithValue("file_name", dataset.FileName);
    command.Parameters.AddWithValue("mode", dataset.Mode);
    command.Parameters.AddWithValue("set_id", dataset.SetId);
    command.Parameters.AddWithValue("segment", (object?)dataset.Segment ?? DBNull.Value);
    command.Parameters.AddWithValue("etag", (object?)etag ?? DBNull.Value);
    command.Parameters.AddWithValue("fetched_at", (object?)fetchedAt ?? DBNull.Value);
    return (Guid)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Analytics dataset was not saved."));
}

static async Task<Guid> UpsertAnalyticsSnapshotAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    Guid datasetId,
    AnalyticsSnapshotRequest snapshot)
{
    await using var command = new NpgsqlCommand("""
        INSERT INTO analytics_snapshots (
            dataset_id, snapshot_hash, user_id, display_name, external_run_id, round, mmr,
            rank_tier, source_set_id, game_version, won, wins_before, lives_lost, trainer_id,
            shop_rank, raw_json)
        VALUES (
            @dataset_id, @snapshot_hash, @user_id, @display_name, @external_run_id, @round, @mmr,
            @rank_tier, @source_set_id, @game_version, @won, @wins_before, @lives_lost, @trainer_id,
            @shop_rank, @raw_json)
        ON CONFLICT (dataset_id, user_id, external_run_id, round, snapshot_hash) DO NOTHING;
        SELECT id FROM analytics_snapshots
        WHERE dataset_id = @dataset_id
          AND user_id = @user_id
          AND external_run_id = @external_run_id
          AND round = @round
          AND snapshot_hash = @snapshot_hash;
        """, connection, transaction);
    command.Parameters.AddWithValue("dataset_id", datasetId);
    command.Parameters.AddWithValue("snapshot_hash", snapshot.SnapshotHash);
    command.Parameters.AddWithValue("user_id", snapshot.UserId);
    command.Parameters.AddWithValue("display_name", (object?)snapshot.DisplayName ?? DBNull.Value);
    command.Parameters.AddWithValue("external_run_id", snapshot.RunId);
    command.Parameters.AddWithValue("round", snapshot.Round);
    command.Parameters.AddWithValue("mmr", (object?)snapshot.Mmr ?? DBNull.Value);
    command.Parameters.AddWithValue("rank_tier", (object?)snapshot.RankTier ?? DBNull.Value);
    command.Parameters.AddWithValue("source_set_id", (object?)snapshot.SetId ?? DBNull.Value);
    command.Parameters.AddWithValue("game_version", (object?)snapshot.Version ?? DBNull.Value);
    command.Parameters.AddWithValue("won", snapshot.Won);
    command.Parameters.AddWithValue("wins_before", (object?)snapshot.WinsBefore ?? DBNull.Value);
    command.Parameters.AddWithValue("lives_lost", (object?)snapshot.LivesLost ?? DBNull.Value);
    command.Parameters.AddWithValue("trainer_id", (object?)snapshot.TrainerId ?? DBNull.Value);
    command.Parameters.AddWithValue("shop_rank", (object?)snapshot.ShopRank ?? DBNull.Value);
    command.Parameters.Add("raw_json", NpgsqlDbType.Jsonb).Value = snapshot.RawJson;
    return (Guid)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Analytics snapshot was not saved."));
}

static async Task InsertAnalyticsChildrenAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    Guid snapshotId,
    AnalyticsSnapshotRequest snapshot)
{
    foreach (AnalyticsUnitRequest unit in snapshot.Units)
    {
        await using var unitCommand = new NpgsqlCommand("""
            INSERT INTO analytics_snapshot_units (snapshot_id, position, species_id, level, is_shiny, extra_types, permanent_buffs, blackboard)
            VALUES (@snapshot_id, @position, @species_id, @level, @is_shiny, @extra_types, @permanent_buffs, @blackboard)
            ON CONFLICT (snapshot_id, position) DO NOTHING;
            """, connection, transaction);
        unitCommand.Parameters.AddWithValue("snapshot_id", snapshotId);
        unitCommand.Parameters.AddWithValue("position", unit.Position);
        unitCommand.Parameters.AddWithValue("species_id", unit.SpeciesId);
        unitCommand.Parameters.AddWithValue("level", (object?)unit.Level ?? DBNull.Value);
        unitCommand.Parameters.AddWithValue("is_shiny", (object?)unit.IsShiny ?? DBNull.Value);
        unitCommand.Parameters.Add("extra_types", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(unit.ExtraTypes);
        unitCommand.Parameters.Add("permanent_buffs", NpgsqlDbType.Jsonb).Value = (object?)unit.PermanentBuffsJson ?? DBNull.Value;
        unitCommand.Parameters.Add("blackboard", NpgsqlDbType.Jsonb).Value = (object?)unit.BlackboardJson ?? DBNull.Value;
        await unitCommand.ExecuteNonQueryAsync();
    }

    foreach (string trinketId in snapshot.TrinketIds.Distinct(StringComparer.Ordinal))
    {
        await using var trinketCommand = new NpgsqlCommand("""
            INSERT INTO analytics_snapshot_trinkets (snapshot_id, trinket_id, used)
            VALUES (@snapshot_id, @trinket_id, @used)
            ON CONFLICT (snapshot_id, trinket_id) DO NOTHING;
            """, connection, transaction);
        trinketCommand.Parameters.AddWithValue("snapshot_id", snapshotId);
        trinketCommand.Parameters.AddWithValue("trinket_id", trinketId);
        trinketCommand.Parameters.AddWithValue("used", snapshot.UsedTrinketIds.Contains(trinketId, StringComparer.Ordinal));
        await trinketCommand.ExecuteNonQueryAsync();
    }
}

public sealed record SyncRunRequest(
    string ExternalRunId,
    string? Mode,
    bool IsRanked,
    string? TrainerId,
    string? Outcome,
    DateTimeOffset? EndedAt,
    int? Round,
    int? Wins,
    int? Lives,
    IReadOnlyList<SyncUnit> Team,
    IReadOnlyList<SyncUnit> Bench,
    IReadOnlyList<string> Trinkets,
    IReadOnlyList<SyncEvent> Events);

public sealed record SyncUnit(
    int? Slot,
    string SpeciesId,
    int? Level,
    bool? IsShiny);

public sealed record SyncEvent(
    string EventType,
    DateTimeOffset DetectedAt,
    long? SaveSequence,
    string Confidence,
    IReadOnlyDictionary<string, string> Payload);

public sealed record AnalyticsImportRequest(
    AnalyticsDatasetRequest Dataset,
    string? Etag,
    DateTimeOffset? FetchedAt,
    IReadOnlyList<AnalyticsSnapshotRequest> Snapshots);

public sealed record AnalyticsDatasetRequest(
    string DatasetId,
    string FileName,
    string Mode,
    string SetId,
    string? Segment);

public sealed record AnalyticsSnapshotRequest(
    string SnapshotHash,
    string UserId,
    string? DisplayName,
    string RunId,
    int Round,
    int? Mmr,
    string? RankTier,
    string? SetId,
    int? Version,
    bool Won,
    int? WinsBefore,
    int? LivesLost,
    string? TrainerId,
    int? ShopRank,
    string RawJson,
    IReadOnlyList<AnalyticsUnitRequest> Units,
    IReadOnlyList<string> TrinketIds,
    IReadOnlyList<string> UsedTrinketIds);

public sealed record AnalyticsUnitRequest(
    int Position,
    string SpeciesId,
    int? Level,
    bool? IsShiny,
    IReadOnlyList<string> ExtraTypes,
    string? PermanentBuffsJson,
    string? BlackboardJson);

public sealed record GlobalRecommendationRequest(IReadOnlyList<string> Team, IReadOnlyList<string>? Trinkets = null);

public sealed record GlobalRecommendationResponse(
    IReadOnlyList<string> RecommendedSpecies,
    IReadOnlyList<string> ReferenceTeam,
    IReadOnlyList<string> RecommendedTrinkets,
    long ReferenceWins,
    int TeamOverlap);

public sealed record AuthCodeExchangeRequest(string Code);

internal sealed record PendingAuthState(string ReturnUrl, DateTimeOffset ExpiresAt);

internal sealed record PendingAuthCode(string SteamId, string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

public sealed record RefreshTokenRequest(string RefreshToken);
