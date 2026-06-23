namespace MartialHeroes.Client.Infrastructure.Settings;

public sealed record ClientSettingsDto
{
    public int? ResolutionWidth { get; init; }

    public int? ResolutionHeight { get; init; }

    public bool? Fullscreen { get; init; }

    public string? RenderQuality { get; init; }


    public int? MasterVolume { get; init; }

    public int? MusicVolume { get; init; }

    public int? SfxVolume { get; init; }


    public string? LastServerHost { get; init; }

    public int? LastServerPort { get; init; }


    public int? WindowX { get; init; }

    public int? WindowY { get; init; }

    public string? Language { get; init; }
}

public sealed record KeybindDto(string ActionName, string KeyCode);

public interface ISettingsStore : IAsyncDisposable
{
    Task InitialiseAsync(CancellationToken cancellationToken = default);


    Task<ClientSettingsDto> LoadSettingsAsync(CancellationToken cancellationToken = default);

    Task SaveSettingsAsync(ClientSettingsDto settings, CancellationToken cancellationToken = default);


    Task<IReadOnlyList<KeybindDto>> LoadKeybindsAsync(CancellationToken cancellationToken = default);

    Task SaveKeybindAsync(string actionName, string keyCode, CancellationToken cancellationToken = default);

    Task ReplaceAllKeybindsAsync(IEnumerable<KeybindDto> keybinds, CancellationToken cancellationToken = default);
}