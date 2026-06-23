using MartialHeroes.Client.Infrastructure.Exceptions;
using Microsoft.Data.Sqlite;

namespace MartialHeroes.Client.Infrastructure.Settings;

public sealed class SqliteSettingsStore : ISettingsStore
{
    private const int SchemaVersion = 1;

    private readonly string _connectionString;
    private bool _initialised;

    public SqliteSettingsStore(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }


    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }


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
            throw new LocalStoreException("Failed to initialise settings store.", ex);
        }
    }

    public async Task<ClientSettingsDto> LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialised();
        try
        {
            await using var conn = OpenConnection();
            await conn.OpenAsync(cancellationToken);

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT key, value FROM client_settings;";
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var key = reader.GetString(0);
                    var val = reader.GetString(1);
                    values[key] = val;
                }
            }

            return new ClientSettingsDto
            {
                ResolutionWidth = ParseNullableInt(values, "resolution_width"),
                ResolutionHeight = ParseNullableInt(values, "resolution_height"),
                Fullscreen = ParseNullableBool(values, "fullscreen"),
                RenderQuality = values.GetValueOrDefault("render_quality"),
                MasterVolume = ParseNullableInt(values, "master_volume"),
                MusicVolume = ParseNullableInt(values, "music_volume"),
                SfxVolume = ParseNullableInt(values, "sfx_volume"),
                LastServerHost = values.GetValueOrDefault("last_server_host"),
                LastServerPort = ParseNullableInt(values, "last_server_port"),
                WindowX = ParseNullableInt(values, "window_x"),
                WindowY = ParseNullableInt(values, "window_y"),
                Language = values.GetValueOrDefault("language")
            };
        }
        catch (SqliteException ex)
        {
            throw new LocalStoreException("Failed to load settings.", ex);
        }
    }

    public async Task SaveSettingsAsync(ClientSettingsDto settings, CancellationToken cancellationToken = default)
    {
        EnsureInitialised();
        ArgumentNullException.ThrowIfNull(settings);

        var pairs = new List<(string Key, string Value)>(12);

        if (settings.ResolutionWidth.HasValue)
            pairs.Add(("resolution_width", settings.ResolutionWidth.Value.ToString()));
        if (settings.ResolutionHeight.HasValue)
            pairs.Add(("resolution_height", settings.ResolutionHeight.Value.ToString()));
        if (settings.Fullscreen.HasValue) pairs.Add(("fullscreen", settings.Fullscreen.Value ? "1" : "0"));
        if (settings.RenderQuality is not null) pairs.Add(("render_quality", settings.RenderQuality));
        if (settings.MasterVolume.HasValue) pairs.Add(("master_volume", settings.MasterVolume.Value.ToString()));
        if (settings.MusicVolume.HasValue) pairs.Add(("music_volume", settings.MusicVolume.Value.ToString()));
        if (settings.SfxVolume.HasValue) pairs.Add(("sfx_volume", settings.SfxVolume.Value.ToString()));
        if (settings.LastServerHost is not null) pairs.Add(("last_server_host", settings.LastServerHost));
        if (settings.LastServerPort.HasValue) pairs.Add(("last_server_port", settings.LastServerPort.Value.ToString()));
        if (settings.WindowX.HasValue) pairs.Add(("window_x", settings.WindowX.Value.ToString()));
        if (settings.WindowY.HasValue) pairs.Add(("window_y", settings.WindowY.Value.ToString()));
        if (settings.Language is not null) pairs.Add(("language", settings.Language));

        if (pairs.Count == 0) return;

        try
        {
            await using var conn = OpenConnection();
            await conn.OpenAsync(cancellationToken);
            await using var tx = await conn.BeginTransactionAsync(cancellationToken);

            await using var cmd = conn.CreateCommand();
            cmd.Transaction = (SqliteTransaction)tx;
            cmd.CommandText =
                "INSERT INTO client_settings (key, value) VALUES (@key, @value) " +
                "ON CONFLICT(key) DO UPDATE SET value = excluded.value;";

            var pKey = cmd.Parameters.Add("@key", SqliteType.Text);
            var pVal = cmd.Parameters.Add("@value", SqliteType.Text);

            foreach (var (key, value) in pairs)
            {
                pKey.Value = key;
                pVal.Value = value;
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);
        }
        catch (SqliteException ex)
        {
            throw new LocalStoreException("Failed to save settings.", ex);
        }
    }

    public async Task<IReadOnlyList<KeybindDto>> LoadKeybindsAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialised();
        try
        {
            await using var conn = OpenConnection();
            await conn.OpenAsync(cancellationToken);

            var result = new List<KeybindDto>();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT action_name, key_code FROM keybinds ORDER BY action_name;";
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                result.Add(new KeybindDto(reader.GetString(0), reader.GetString(1)));

            return result;
        }
        catch (SqliteException ex)
        {
            throw new LocalStoreException("Failed to load keybinds.", ex);
        }
    }

    public async Task SaveKeybindAsync(string actionName, string keyCode, CancellationToken cancellationToken = default)
    {
        EnsureInitialised();
        ArgumentException.ThrowIfNullOrWhiteSpace(actionName);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyCode);

        try
        {
            await using var conn = OpenConnection();
            await conn.OpenAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "INSERT INTO keybinds (action_name, key_code) VALUES (@action, @key) " +
                "ON CONFLICT(action_name) DO UPDATE SET key_code = excluded.key_code;";
            cmd.Parameters.AddWithValue("@action", actionName);
            cmd.Parameters.AddWithValue("@key", keyCode);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException ex)
        {
            throw new LocalStoreException("Failed to save keybind.", ex);
        }
    }

    public async Task ReplaceAllKeybindsAsync(IEnumerable<KeybindDto> keybinds,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialised();
        ArgumentNullException.ThrowIfNull(keybinds);

        try
        {
            await using var conn = OpenConnection();
            await conn.OpenAsync(cancellationToken);
            await using var tx = await conn.BeginTransactionAsync(cancellationToken);

            await using (var del = conn.CreateCommand())
            {
                del.Transaction = (SqliteTransaction)tx;
                del.CommandText = "DELETE FROM keybinds;";
                await del.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var ins = conn.CreateCommand())
            {
                ins.Transaction = (SqliteTransaction)tx;
                ins.CommandText = "INSERT INTO keybinds (action_name, key_code) VALUES (@action, @key);";
                var pAction = ins.Parameters.Add("@action", SqliteType.Text);
                var pKey = ins.Parameters.Add("@key", SqliteType.Text);

                foreach (var kb in keybinds)
                {
                    pAction.Value = kb.ActionName;
                    pKey.Value = kb.KeyCode;
                    await ins.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            await tx.CommitAsync(cancellationToken);
        }
        catch (SqliteException ex)
        {
            throw new LocalStoreException("Failed to replace keybinds.", ex);
        }
    }


    private SqliteConnection OpenConnection()
    {
        return new SqliteConnection(_connectionString);
    }

    private void EnsureInitialised()
    {
        if (!_initialised)
            throw new InvalidOperationException(
                $"{nameof(SqliteSettingsStore)} has not been initialised. " +
                $"Call {nameof(InitialiseAsync)} before any other method.");
    }

    private static async Task ApplyMigrationsAsync(SqliteConnection conn, CancellationToken ct)
    {
        var currentVersion = await GetUserVersionAsync(conn, ct);

        if (currentVersion < SchemaVersion)
        {
            const string v1Ddl =
                """
                CREATE TABLE IF NOT EXISTS client_settings (
                    key   TEXT NOT NULL PRIMARY KEY,
                    value TEXT NOT NULL
                ) STRICT;

                CREATE TABLE IF NOT EXISTS keybinds (
                    action_name TEXT NOT NULL PRIMARY KEY,
                    key_code    TEXT NOT NULL
                ) STRICT;
                """;

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = v1Ddl;
            await cmd.ExecuteNonQueryAsync(ct);

            await SetUserVersionAsync(conn, SchemaVersion, ct);
        }
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
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA user_version = {version};";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static int? ParseNullableInt(Dictionary<string, string> d, string key)
    {
        return d.TryGetValue(key, out var raw) && int.TryParse(raw, out var v) ? v : null;
    }

    private static bool? ParseNullableBool(Dictionary<string, string> d, string key)
    {
        return d.TryGetValue(key, out var raw) ? raw == "1" : null;
    }
}