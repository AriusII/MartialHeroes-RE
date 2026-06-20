namespace MartialHeroes.Client.Infrastructure.Settings;

/// <summary>
///     Typed client-settings record. Covers graphics, audio, keybinds, last-used
///     server, window placement, and language selection.
///     All fields are optional so a partial upsert never clobbers values the caller
///     did not touch; a <see langword="null" /> field means "not set / use default".
/// </summary>
public sealed record ClientSettingsDto
{
    // ── Graphics ──────────────────────────────────────────────────────────────

    /// <summary>Horizontal resolution in pixels. <see langword="null" /> = unset.</summary>
    public int? ResolutionWidth { get; init; }

    /// <summary>Vertical resolution in pixels. <see langword="null" /> = unset.</summary>
    public int? ResolutionHeight { get; init; }

    /// <summary>Windowed (<c>false</c>) or fullscreen (<c>true</c>). <see langword="null" /> = unset.</summary>
    public bool? Fullscreen { get; init; }

    /// <summary>Render quality preset string (e.g. "Low", "Medium", "High", "Ultra"). <see langword="null" /> = unset.</summary>
    public string? RenderQuality { get; init; }

    // ── Audio ─────────────────────────────────────────────────────────────────

    /// <summary>Master volume 0–100. <see langword="null" /> = unset.</summary>
    public int? MasterVolume { get; init; }

    /// <summary>Music volume 0–100. <see langword="null" /> = unset.</summary>
    public int? MusicVolume { get; init; }

    /// <summary>SFX volume 0–100. <see langword="null" /> = unset.</summary>
    public int? SfxVolume { get; init; }

    // ── Network / Login ───────────────────────────────────────────────────────

    /// <summary>Host-name or IP of the last-used login server. <see langword="null" /> = unset.</summary>
    public string? LastServerHost { get; init; }

    /// <summary>TCP port of the last-used login server. <see langword="null" /> = unset.</summary>
    public int? LastServerPort { get; init; }

    // ── UI / Window ───────────────────────────────────────────────────────────

    /// <summary>Window X position (desktop pixels). <see langword="null" /> = unset.</summary>
    public int? WindowX { get; init; }

    /// <summary>Window Y position (desktop pixels). <see langword="null" /> = unset.</summary>
    public int? WindowY { get; init; }

    /// <summary>IETF language tag, e.g. "en-US", "fr-FR". <see langword="null" /> = unset.</summary>
    public string? Language { get; init; }
}

/// <summary>
///     Persists and loads a named keybinding (action → key string).
/// </summary>
/// <param name="ActionName">Logical action identifier (e.g. "UseSkill1", "OpenInventory").</param>
/// <param name="KeyCode">Platform-neutral key string (e.g. "F1", "Ctrl+I").</param>
public sealed record KeybindDto(string ActionName, string KeyCode);

/// <summary>
///     Persistent, typed settings store backed by local SQLite.
///     <para>
///         All I/O is async with <see cref="CancellationToken" /> support so the client
///         never blocks the UI thread. Implementations must be safe to call from multiple
///         threads sequentially (the connection is not shared across async continuations
///         without synchronisation).
///     </para>
/// </summary>
public interface ISettingsStore : IAsyncDisposable
{
    /// <summary>
    ///     Ensures the schema is up to date and the store is ready.
    ///     Must be called once before any other method.
    /// </summary>
    Task InitialiseAsync(CancellationToken cancellationToken = default);

    // ── Scalar settings ───────────────────────────────────────────────────────

    /// <summary>
    ///     Returns all scalar settings as a single snapshot. Non-null fields are
    ///     those that were previously upserted; all others are <see langword="null" />.
    /// </summary>
    Task<ClientSettingsDto> LoadSettingsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Upserts every non-null field in <paramref name="settings" />.
    ///     Null fields are ignored (they do not overwrite existing values).
    /// </summary>
    Task SaveSettingsAsync(ClientSettingsDto settings, CancellationToken cancellationToken = default);

    // ── Keybindings ───────────────────────────────────────────────────────────

    /// <summary>
    ///     Returns all persisted keybinds. Empty enumerable if none have been saved.
    /// </summary>
    Task<IReadOnlyList<KeybindDto>> LoadKeybindsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Upserts the keybind for <paramref name="actionName" />, overwriting any
    ///     previous value for that action.
    /// </summary>
    Task SaveKeybindAsync(string actionName, string keyCode, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Replaces the entire keybind table with <paramref name="keybinds" /> atomically.
    /// </summary>
    Task ReplaceAllKeybindsAsync(IEnumerable<KeybindDto> keybinds, CancellationToken cancellationToken = default);
}