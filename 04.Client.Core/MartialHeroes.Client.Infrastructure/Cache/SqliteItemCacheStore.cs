using MartialHeroes.Client.Infrastructure.Exceptions;
using MartialHeroes.Shared.Kernel.Ids;
using Microsoft.Data.Sqlite;

namespace MartialHeroes.Client.Infrastructure.Cache;

/// <summary>
///     SQLite-backed implementation of <see cref="IItemCacheStore" />.
///     <para>
///         Schema versioning uses <c>PRAGMA user_version</c>.
///         All SQL uses parameterized queries — no value interpolation into SQL text.
///     </para>
/// </summary>
/// <remarks>
///     Pass <c>Data Source=:memory:</c> (or a shared-cache URI) for unit tests.
/// </remarks>
public sealed class SqliteItemCacheStore : IItemCacheStore
{
    // ── Schema version ────────────────────────────────────────────────────────
    private const int SchemaVersion = 1;

    private readonly string _connectionString;
    private bool _initialised;

    /// <param name="connectionString">
    ///     A <c>Microsoft.Data.Sqlite</c> connection string.
    /// </param>
    public SqliteItemCacheStore(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    // ── IAsyncDisposable ──────────────────────────────────────────────────────

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    // ── IItemCacheStore ───────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task InitialiseAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var conn = OpenConnection();
            await conn.OpenAsync(cancellationToken);
            await ApplyMigrationsAsync(conn, cancellationToken);
            _initialised = true;
        }
        catch (SqliteException ex)
        {
            throw new LocalStoreException("Failed to initialise item cache store.", ex);
        }
    }

    /// <inheritdoc />
    public async Task<CachedItemDto?> TryGetAsync(ItemId itemId, CancellationToken cancellationToken = default)
    {
        EnsureInitialised();
        try
        {
            await using var conn = OpenConnection();
            await conn.OpenAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT item_id, name, description, item_type_code " +
                "FROM item_cache WHERE item_id = @id LIMIT 1;";
            cmd.Parameters.AddWithValue("@id", (long)itemId.Value); // SQLite has no uint; store as signed int64
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return null;
            return ReadRow(reader);
        }
        catch (SqliteException ex)
        {
            throw new LocalStoreException($"Failed to read item cache for id {itemId}.", ex);
        }
    }

    /// <inheritdoc />
    public async Task UpsertAsync(CachedItemDto item, CancellationToken cancellationToken = default)
    {
        EnsureInitialised();
        ArgumentNullException.ThrowIfNull(item);
        try
        {
            await using var conn = OpenConnection();
            await conn.OpenAsync(cancellationToken);
            await UpsertCoreAsync(conn, null, item, cancellationToken);
        }
        catch (SqliteException ex)
        {
            throw new LocalStoreException($"Failed to upsert item {item.Id}.", ex);
        }
    }

    /// <inheritdoc />
    public async Task BulkUpsertAsync(IEnumerable<CachedItemDto> items, CancellationToken cancellationToken = default)
    {
        EnsureInitialised();
        ArgumentNullException.ThrowIfNull(items);
        try
        {
            await using var conn = OpenConnection();
            await conn.OpenAsync(cancellationToken);
            await using var tx = await conn.BeginTransactionAsync(cancellationToken);

            foreach (var item in items) await UpsertCoreAsync(conn, (SqliteTransaction)tx, item, cancellationToken);

            await tx.CommitAsync(cancellationToken);
        }
        catch (SqliteException ex)
        {
            throw new LocalStoreException("Bulk upsert of item cache failed.", ex);
        }
    }

    /// <inheritdoc />
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialised();
        try
        {
            await using var conn = OpenConnection();
            await conn.OpenAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM item_cache;";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException ex)
        {
            throw new LocalStoreException("Failed to clear item cache.", ex);
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private SqliteConnection OpenConnection()
    {
        return new SqliteConnection(_connectionString);
    }

    private void EnsureInitialised()
    {
        if (!_initialised)
            throw new InvalidOperationException(
                $"{nameof(SqliteItemCacheStore)} has not been initialised. " +
                $"Call {nameof(InitialiseAsync)} before any other method.");
    }

    private static async Task UpsertCoreAsync(
        SqliteConnection conn,
        SqliteTransaction? tx,
        CachedItemDto item,
        CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText =
            "INSERT INTO item_cache (item_id, name, description, item_type_code) " +
            "VALUES (@id, @name, @desc, @type) " +
            "ON CONFLICT(item_id) DO UPDATE SET " +
            "  name           = excluded.name, " +
            "  description    = excluded.description, " +
            "  item_type_code = excluded.item_type_code;";
        cmd.Parameters.AddWithValue("@id", (long)item.Id.Value);
        cmd.Parameters.AddWithValue("@name", item.Name);
        cmd.Parameters.AddWithValue("@desc", item.Description);
        cmd.Parameters.AddWithValue("@type", (int)item.ItemTypeCode);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static CachedItemDto ReadRow(SqliteDataReader r)
    {
        var rawId = (ulong)r.GetInt64(0); // cast back: stored as int64, re-interpret as uint
        return new CachedItemDto(
            new ItemId((uint)rawId),
            r.GetString(1),
            r.GetString(2),
            (byte)r.GetInt32(3));
    }

    private static async Task ApplyMigrationsAsync(SqliteConnection conn, CancellationToken ct)
    {
        var currentVersion = await GetUserVersionAsync(conn, ct);

        if (currentVersion < SchemaVersion)
        {
            // ── Migration v0 → v1 ────────────────────────────────────────────
            // Creates the item_cache table.
            // item_id is stored as INTEGER (SQLite int64) because SQLite has no
            // unsigned integer type; we cast to/from uint in C#.
            const string v1Ddl =
                """
                CREATE TABLE IF NOT EXISTS item_cache (
                    item_id        INTEGER NOT NULL PRIMARY KEY,
                    name           TEXT    NOT NULL,
                    description    TEXT    NOT NULL,
                    item_type_code INTEGER NOT NULL
                ) STRICT;
                """;

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = v1Ddl;
            await cmd.ExecuteNonQueryAsync(ct);

            await SetUserVersionAsync(conn, SchemaVersion, ct);
        }
        // Future migrations: increment SchemaVersion above and add `if (currentVersion < 2) { ... }`.
    }

    private static async Task<int> GetUserVersionAsync(SqliteConnection conn, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    private static async Task SetUserVersionAsync(SqliteConnection conn, int version, CancellationToken ct)
    {
        // PRAGMA user_version only accepts an integer literal, not a parameter.
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA user_version = {version};";
        await cmd.ExecuteNonQueryAsync(ct);
    }
}