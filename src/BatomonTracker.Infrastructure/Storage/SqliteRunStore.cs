using System.Globalization;
using System.Text.Json;
using BatomonTracker.Core.Models;
using Microsoft.Data.Sqlite;

namespace BatomonTracker.Infrastructure.Storage;

public sealed class SqliteRunStore
{
    static SqliteRunStore()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    private readonly string databasePath;
    private readonly string connectionString;
    private readonly SemaphoreSlim writeGate = new(1, 1);

    public SqliteRunStore(string databasePath)
    {
        this.databasePath = databasePath;
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        string? directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;

            CREATE TABLE IF NOT EXISTS runs (
                run_id TEXT PRIMARY KEY,
                first_seen TEXT NOT NULL,
                last_seen TEXT NOT NULL,
                trainer_id TEXT,
                round INTEGER,
                wins INTEGER,
                lives INTEGER,
                gold INTEGER
            );

            CREATE TABLE IF NOT EXISTS snapshots (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                run_id TEXT NOT NULL,
                save_seq INTEGER,
                captured_at TEXT NOT NULL,
                round INTEGER,
                wins INTEGER,
                lives INTEGER,
                gold INTEGER,
                team_json TEXT NOT NULL,
                bench_json TEXT NOT NULL,
                shop_json TEXT NOT NULL,
                trinkets_json TEXT NOT NULL,
                UNIQUE(run_id, save_seq)
            );

            CREATE TABLE IF NOT EXISTS run_events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                run_id TEXT NOT NULL,
                save_seq INTEGER,
                detected_at TEXT NOT NULL,
                event_type TEXT NOT NULL,
                confidence TEXT NOT NULL,
                payload_json TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS completed_runs (
                run_id TEXT PRIMARY KEY,
                ended_at TEXT,
                is_ranked INTEGER,
                lives INTEGER,
                outcome TEXT,
                round INTEGER,
                trainer_id TEXT,
                wins INTEGER,
                team_json TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveAsync(
        RunState state,
        IReadOnlyList<TrackerEvent> events,
        CancellationToken cancellationToken = default)
    {
        await writeGate.WaitAsync(cancellationToken);
        try
        {
            await SaveCoreAsync(state, events, cancellationToken);
        }
        finally
        {
            writeGate.Release();
        }
    }

    private async Task SaveCoreAsync(
        RunState state,
        IReadOnlyList<TrackerEvent> events,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        string now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        await ExecuteAsync(connection, transaction, """
            INSERT INTO runs (run_id, first_seen, last_seen, trainer_id, round, wins, lives, gold)
            VALUES ($run_id, $now, $now, $trainer_id, $round, $wins, $lives, $gold)
            ON CONFLICT(run_id) DO UPDATE SET
                last_seen = excluded.last_seen,
                trainer_id = excluded.trainer_id,
                round = excluded.round,
                wins = excluded.wins,
                lives = excluded.lives,
                gold = excluded.gold;
            """,
            new Dictionary<string, object?>
            {
                ["$run_id"] = state.RunId,
                ["$now"] = now,
                ["$trainer_id"] = (object?)state.TrainerId ?? DBNull.Value,
                ["$round"] = (object?)state.Round ?? DBNull.Value,
                ["$wins"] = (object?)state.Wins ?? DBNull.Value,
                ["$lives"] = (object?)state.Lives ?? DBNull.Value,
                ["$gold"] = (object?)state.Gold ?? DBNull.Value
            }, cancellationToken);

        await ExecuteAsync(connection, transaction, """
            INSERT OR IGNORE INTO snapshots
                (run_id, save_seq, captured_at, round, wins, lives, gold, team_json, bench_json, shop_json, trinkets_json)
            VALUES
                ($run_id, $save_seq, $captured_at, $round, $wins, $lives, $gold, $team_json, $bench_json, $shop_json, $trinkets_json);
            """,
            new Dictionary<string, object?>
            {
                ["$run_id"] = state.RunId,
                ["$save_seq"] = (object?)state.SaveSequence ?? DBNull.Value,
                ["$captured_at"] = now,
                ["$round"] = (object?)state.Round ?? DBNull.Value,
                ["$wins"] = (object?)state.Wins ?? DBNull.Value,
                ["$lives"] = (object?)state.Lives ?? DBNull.Value,
                ["$gold"] = (object?)state.Gold ?? DBNull.Value,
                ["$team_json"] = JsonSerializer.Serialize(state.Team),
                ["$bench_json"] = JsonSerializer.Serialize(state.Bench),
                ["$shop_json"] = JsonSerializer.Serialize(state.Shop),
                ["$trinkets_json"] = JsonSerializer.Serialize(state.Trinkets)
            }, cancellationToken);

        foreach (TrackerEvent eventItem in events)
        {
            await ExecuteAsync(connection, transaction, """
                INSERT INTO run_events
                    (run_id, save_seq, detected_at, event_type, confidence, payload_json)
                VALUES
                    ($run_id, $save_seq, $detected_at, $event_type, $confidence, $payload_json);
                """,
                new Dictionary<string, object?>
                {
                    ["$run_id"] = eventItem.RunId,
                    ["$save_seq"] = (object?)eventItem.SaveSequence ?? DBNull.Value,
                    ["$detected_at"] = eventItem.DetectedAt.ToString("O", CultureInfo.InvariantCulture),
                    ["$event_type"] = eventItem.Type.ToString(),
                    ["$confidence"] = eventItem.Confidence.ToString(),
                    ["$payload_json"] = JsonSerializer.Serialize(eventItem.Payload)
                }, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<int> CountSnapshotsAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteCountAsync("SELECT COUNT(*) FROM snapshots;", cancellationToken);
    }

    public async Task<int> CountEventsAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteCountAsync("SELECT COUNT(*) FROM run_events;", cancellationToken);
    }

    public async Task<IReadOnlyList<StoredRunSummary>> GetRunSummariesAsync(
        CancellationToken cancellationToken = default)
    {
        var summaries = new List<StoredRunSummary>();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                r.run_id,
                r.last_seen,
                r.trainer_id,
                r.round,
                r.wins,
                r.lives,
                r.gold,
                (SELECT COUNT(*) FROM snapshots s WHERE s.run_id = r.run_id) AS snapshot_count,
                (SELECT COUNT(*) FROM run_events e WHERE e.run_id = r.run_id) AS event_count
            FROM runs r
            ORDER BY r.last_seen DESC;
            """;

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            summaries.Add(new StoredRunSummary(
                reader.GetString(0),
                DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture),
                GetNullableString(reader, 2),
                GetNullableInt32(reader, 3),
                GetNullableInt32(reader, 4),
                GetNullableInt32(reader, 5),
                GetNullableInt32(reader, 6),
                reader.GetInt32(7),
                reader.GetInt32(8)));
        }

        return summaries;
    }

    public async Task ImportCompletedRunsAsync(
        IReadOnlyList<CompletedRun> completedRuns,
        CancellationToken cancellationToken = default)
    {
        if (completedRuns.Count == 0)
        {
            return;
        }

        await writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

            foreach (CompletedRun run in completedRuns)
            {
                await ExecuteAsync(connection, transaction, """
                    INSERT INTO completed_runs
                        (run_id, ended_at, is_ranked, lives, outcome, round, trainer_id, wins, team_json)
                    VALUES
                        ($run_id, $ended_at, $is_ranked, $lives, $outcome, $round, $trainer_id, $wins, $team_json)
                    ON CONFLICT(run_id) DO UPDATE SET
                        ended_at = excluded.ended_at,
                        is_ranked = excluded.is_ranked,
                        lives = excluded.lives,
                        outcome = excluded.outcome,
                        round = excluded.round,
                        trainer_id = excluded.trainer_id,
                        wins = excluded.wins,
                        team_json = excluded.team_json;
                    """,
                    new Dictionary<string, object?>
                    {
                        ["$run_id"] = run.RunId,
                        ["$ended_at"] = run.EndedAt?.ToString("O", CultureInfo.InvariantCulture) ?? (object)DBNull.Value,
                        ["$is_ranked"] = run.IsRanked.HasValue ? (object)(run.IsRanked.Value ? 1 : 0) : DBNull.Value,
                        ["$lives"] = (object?)run.Lives ?? DBNull.Value,
                        ["$outcome"] = (object?)run.Outcome ?? DBNull.Value,
                        ["$round"] = (object?)run.Round ?? DBNull.Value,
                        ["$trainer_id"] = (object?)run.TrainerId ?? DBNull.Value,
                        ["$wins"] = (object?)run.Wins ?? DBNull.Value,
                        ["$team_json"] = run.TeamJson
                    }, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            writeGate.Release();
        }
    }

    public async Task<IReadOnlyList<CompletedRun>> GetCompletedRunsAsync(
        CancellationToken cancellationToken = default)
    {
        var runs = new List<CompletedRun>();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT run_id, ended_at, is_ranked, lives, outcome, round, trainer_id, wins, team_json
            FROM completed_runs
            ORDER BY ended_at DESC;
            """;

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            runs.Add(new CompletedRun(
                reader.GetString(0),
                ParseDateTime(reader, 1),
                ParseBoolean(reader, 2),
                GetNullableInt32(reader, 3),
                GetNullableString(reader, 4),
                GetNullableInt32(reader, 5),
                GetNullableString(reader, 6),
                GetNullableInt32(reader, 7),
                reader.GetString(8)));
        }

        return runs;
    }

    public async Task<PersonalStats> GetPersonalStatsAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = new List<CompletedStatsRow>();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT trainer_id, wins, outcome, team_json
            FROM completed_runs;
            """;

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new CompletedStatsRow(
                GetNullableString(reader, 0),
                GetNullableInt32(reader, 1),
                GetNullableString(reader, 2),
                reader.GetString(3)));
        }

        int completedRuns = rows.Count;
        int completedWins = rows.Count(IsWin);
        double winRate = completedRuns == 0 ? 0 : (double)completedWins / completedRuns;
        double averageWins = completedRuns == 0
            ? 0
            : rows.Average(row => row.Wins ?? 0);

        IReadOnlyList<TrainerStats> byTrainer = rows
            .GroupBy(row => string.IsNullOrWhiteSpace(row.TrainerId) ? "unknown" : row.TrainerId!, StringComparer.Ordinal)
            .Select(group => CreateTrainerStats(group.Key, group))
            .OrderByDescending(stats => stats.WinRate)
            .ThenByDescending(stats => stats.Runs)
            .ToArray();

        var batomonRows = new List<(string SpeciesId, bool Won)>();
        foreach (CompletedStatsRow row in rows)
        {
            foreach (string speciesId in ReadSpeciesIds(row.TeamJson))
            {
                batomonRows.Add((speciesId, IsWin(row)));
            }
        }

        IReadOnlyList<BatomonStats> byBatomon = batomonRows
            .GroupBy(row => row.SpeciesId, StringComparer.Ordinal)
            .Select(group => new BatomonStats(
                group.Key,
                group.Count(),
                group.Count(row => row.Won),
                (double)group.Count(row => row.Won) / group.Count()))
            .OrderByDescending(stats => stats.WinRate)
            .ThenByDescending(stats => stats.Runs)
            .ToArray();

        return new PersonalStats(completedRuns, completedWins, winRate, averageWins, byTrainer, byBatomon);
    }

    private async Task<int> ExecuteCountAsync(string sql, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string? GetNullableString(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static int? GetNullableInt32(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    private static DateTimeOffset? ParseDateTime(SqliteDataReader reader, int ordinal)
    {
        return !reader.IsDBNull(ordinal) && DateTimeOffset.TryParse(
            reader.GetString(ordinal), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset value)
            ? value
            : null;
    }

    private static bool? ParseBoolean(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal) != 0;
    }

    private static TrainerStats CreateTrainerStats(string trainerId, IEnumerable<CompletedStatsRow> rows)
    {
        CompletedStatsRow[] values = rows.ToArray();
        int wins = values.Count(IsWin);
        return new TrainerStats(trainerId, values.Length, wins, (double)wins / values.Length);
    }

    private static bool IsWin(CompletedStatsRow row)
    {
        return string.Equals(row.Outcome, "won", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> ReadSpeciesIds(string teamJson)
    {
        using JsonDocument document = JsonDocument.Parse(teamJson);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (JsonElement unit in document.RootElement.EnumerateArray())
        {
            if (unit.ValueKind == JsonValueKind.Object &&
                unit.TryGetProperty("species_id", out JsonElement speciesId) &&
                speciesId.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(speciesId.GetString()))
            {
                yield return speciesId.GetString()!;
            }
        }
    }

    private sealed record CompletedStatsRow(
        string? TrainerId,
        int? Wins,
        string? Outcome,
        string TeamJson);
}
