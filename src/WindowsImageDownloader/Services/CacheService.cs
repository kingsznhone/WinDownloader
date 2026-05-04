using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using WindowsImageDownloader.Interfaces;
using WindowsImageDownloader.Models;

namespace WindowsImageDownloader.Services;

/// <summary>
/// Persists <see cref="DownloadTask"/> records in a SQLite database located at
/// <c>%LocalAppData%\WindowsImageDownloader\cache.db</c>.
/// </summary>
public sealed class CacheService : ICacheService
{
    // ── Schema ────────────────────────────────────────────────────────────────

    private const string CreateTableSql = """
        CREATE TABLE IF NOT EXISTS DownloadTasks (
            Sha256              TEXT    NOT NULL PRIMARY KEY,
            LanguageCode        TEXT    NOT NULL,
            Language            TEXT    NOT NULL,
            Architecture        TEXT    NOT NULL,
            EditionLoc          TEXT    NOT NULL,
            Edition             TEXT    NOT NULL,
            FileName            TEXT    NOT NULL,
            Editions            TEXT    NOT NULL DEFAULT '[]',
            DownloadUrl         TEXT    NOT NULL,
            TotalBytes          INTEGER NOT NULL,
            IsRetailOnly        INTEGER NOT NULL DEFAULT 0,
            State               INTEGER NOT NULL DEFAULT 0,
            DownloadedBytes     INTEGER NOT NULL DEFAULT 0,
            ErrorMessage        TEXT,
            CreatedAt           TEXT    NOT NULL,
            UpdatedAt           TEXT    NOT NULL
        );
        """;

    /// <summary>
    /// All column names that must exist in DownloadTasks.
    /// Any mismatch triggers an automatic wipe-and-rebuild.
    /// </summary>
    private static readonly HashSet<string> RequiredColumns =
    [
        "Sha256", "LanguageCode", "Language", "Architecture", "EditionLoc", "Edition",
        "FileName", "Editions", "DownloadUrl", "TotalBytes", "IsRetailOnly",
        "State", "DownloadedBytes", "ErrorMessage", "CreatedAt", "UpdatedAt",
    ];

    // ── Connection string ─────────────────────────────────────────────────────

    private static string DbPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WindowsImageDownloader",
        "cache.db");

    private static string ConnectionString { get; } =
        $"Data Source={DbPath}";

    // ── IHostedService ────────────────────────────────────────────────────────

    /// <inheritdoc/>
    Task IHostedService.StartAsync(CancellationToken cancellationToken) =>
        EnsureSchemaAsync(cancellationToken);

    /// <inheritdoc/>
    Task IHostedService.StopAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;

    // ── ICacheService ─────────────────────────────────────────────────────────

    /// <inheritdoc/>
    /// <remarks>
    /// If the database file is corrupt or has an incompatible schema, the file is
    /// deleted and recreated automatically. Task history is lost in that case, but
    /// the application remains functional. The recovery is attempted only once to
    /// prevent infinite loops on persistent I/O failures.
    /// </remarks>
    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
        await TryCreateSchemaAsync(retried: false, cancellationToken).ConfigureAwait(false);
    }

    private async Task TryCreateSchemaAsync(bool retried, CancellationToken cancellationToken)
    {
        try
        {
            await using var conn = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = CreateTableSql;
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            if (!await HasRequiredColumnsAsync(conn, cancellationToken).ConfigureAwait(false))
                throw new SqliteException("Schema is missing required columns.", 1);
        }
        catch (SqliteException) when (!retried)
        {
            SqliteConnection.ClearAllPools();
            File.Delete(DbPath);
            await TryCreateSchemaAsync(retried: true, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<bool> HasRequiredColumnsAsync(SqliteConnection conn, CancellationToken cancellationToken)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(DownloadTasks);";
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            existing.Add(reader.GetString(reader.GetOrdinal("name")));

        return RequiredColumns.IsSubsetOf(existing);
    }

    /// <inheritdoc/>
    public async Task AddTaskAsync(DownloadTask task, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);

        await using var conn = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();

        cmd.CommandText = """
            INSERT INTO DownloadTasks
                (Sha256, LanguageCode, Language, Architecture, EditionLoc, Edition,
                 FileName, Editions, DownloadUrl, TotalBytes, IsRetailOnly,
                 State, DownloadedBytes, ErrorMessage, CreatedAt, UpdatedAt)
            VALUES
                (@sha256, @languageCode, @language, @architecture, @editionLoc, @edition,
                 @fileName, @editions, @downloadUrl, @totalBytes, @isRetailOnly,
                 @state, @downloadedBytes, @errorMessage, @createdAt, @updatedAt);
            """;

        BindAllParameters(cmd, task);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task UpdateTaskAsync(DownloadTask task, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);

        await using var conn = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();

        cmd.CommandText = """
            UPDATE DownloadTasks SET
                State           = @state,
                DownloadedBytes = @downloadedBytes,
                ErrorMessage    = @errorMessage,
                UpdatedAt       = @updatedAt
            WHERE Sha256 = @sha256;
            """;

        cmd.Parameters.AddWithValue("@sha256",          task.Sha256);
        cmd.Parameters.AddWithValue("@state",           (int)task.State);
        cmd.Parameters.AddWithValue("@downloadedBytes", task.DownloadedBytes);
        cmd.Parameters.AddWithValue("@errorMessage",    task.ErrorMessage ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@updatedAt",       task.UpdatedAt.ToString("O"));

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<DownloadTask>> GetAllTasksAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM DownloadTasks ORDER BY CreatedAt DESC;";

        var tasks = new List<DownloadTask>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            tasks.Add(MapToTask(reader));

        return tasks;
    }

    /// <inheritdoc/>
    public async Task<DownloadTask?> GetTaskAsync(string sha256, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sha256);

        await using var conn = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM DownloadTasks WHERE Sha256 = @sha256;";
        cmd.Parameters.AddWithValue("@sha256", sha256);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? MapToTask(reader)
            : null;
    }

    /// <inheritdoc/>
    public async Task DeleteTaskAsync(string sha256, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sha256);

        await using var conn = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM DownloadTasks WHERE Sha256 = @sha256;";
        cmd.Parameters.AddWithValue("@sha256", sha256);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var conn = new SqliteConnection(ConnectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        return conn;
    }

    /// <summary>Binds all INSERT parameters (identity + mutable fields).</summary>
    private static void BindAllParameters(SqliteCommand cmd, DownloadTask task)
    {
        cmd.Parameters.AddWithValue("@sha256",          task.Sha256);
        cmd.Parameters.AddWithValue("@languageCode",    task.LanguageCode);
        cmd.Parameters.AddWithValue("@language",        task.Language);
        cmd.Parameters.AddWithValue("@architecture",    task.Architecture);
        cmd.Parameters.AddWithValue("@editionLoc",      task.EditionLoc);
        cmd.Parameters.AddWithValue("@edition",         task.Edition);
        cmd.Parameters.AddWithValue("@fileName",        task.FileName);
        cmd.Parameters.AddWithValue("@editions",        JsonSerializer.Serialize(task.Editions));
        cmd.Parameters.AddWithValue("@downloadUrl",     task.DownloadUrl);
        cmd.Parameters.AddWithValue("@totalBytes",      task.TotalBytes);
        cmd.Parameters.AddWithValue("@isRetailOnly",    task.IsRetailOnly ? 1 : 0);
        cmd.Parameters.AddWithValue("@state",           (int)task.State);
        cmd.Parameters.AddWithValue("@downloadedBytes", task.DownloadedBytes);
        cmd.Parameters.AddWithValue("@errorMessage",    task.ErrorMessage ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@createdAt",       task.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@updatedAt",       task.UpdatedAt.ToString("O"));
    }

    /// <summary>Maps a reader row to a <see cref="DownloadTask"/>.</summary>
    private static DownloadTask MapToTask(SqliteDataReader reader)
    {
        var totalBytes = reader.GetInt64(reader.GetOrdinal("TotalBytes"));
        var downloadedBytes = reader.GetInt64(reader.GetOrdinal("DownloadedBytes"));
        var state = (TaskState)reader.GetInt32(reader.GetOrdinal("State"));

        return new DownloadTask
        {
            Sha256          = reader.GetString(reader.GetOrdinal("Sha256")),
            LanguageCode    = reader.GetString(reader.GetOrdinal("LanguageCode")),
            Language        = reader.GetString(reader.GetOrdinal("Language")),
            Architecture    = reader.GetString(reader.GetOrdinal("Architecture")),
            EditionLoc      = reader.GetString(reader.GetOrdinal("EditionLoc")),
            Edition         = reader.GetString(reader.GetOrdinal("Edition")),
            FileName        = reader.GetString(reader.GetOrdinal("FileName")),
            Editions        = JsonSerializer.Deserialize<List<string>>(
                                  reader.IsDBNull(reader.GetOrdinal("Editions"))
                                      ? "[]"
                                      : reader.GetString(reader.GetOrdinal("Editions"))) ?? [],
            DownloadUrl     = reader.GetString(reader.GetOrdinal("DownloadUrl")),
            TotalBytes      = totalBytes,
            IsRetailOnly    = reader.GetInt32(reader.GetOrdinal("IsRetailOnly")) != 0,
            State           = state,
            DownloadedBytes = downloadedBytes,
            Progress        = state == TaskState.Completed
                ? 1.0
                : totalBytes > 0 ? Math.Clamp((double)downloadedBytes / totalBytes, 0, 1) : 0,
            ErrorMessage    = reader.IsDBNull(reader.GetOrdinal("ErrorMessage"))
                                  ? null
                                  : reader.GetString(reader.GetOrdinal("ErrorMessage")),
            CreatedAt       = DateTimeOffset.Parse(
                                  reader.GetString(reader.GetOrdinal("CreatedAt")),
                                  null,
                                  DateTimeStyles.RoundtripKind),
            UpdatedAt       = DateTimeOffset.Parse(
                                  reader.GetString(reader.GetOrdinal("UpdatedAt")),
                                  null,
                                  DateTimeStyles.RoundtripKind),
        };
    }
}
