using MartialHeroes.Client.Infrastructure.Settings;
using Xunit;

namespace MartialHeroes.Client.Infrastructure.Tests;

/// <summary>
/// Round-trip tests for <see cref="SqliteSettingsStore"/> using an in-memory
/// SQLite database so no real file is created.
/// </summary>
public sealed class SettingsStoreTests
{
    // Use a shared-cache in-memory URI so multiple connections inside the same
    // test method share the same in-memory database.
    private static string InMemoryConnectionString(string testName) =>
        $"Data Source=file:{testName}?mode=memory&cache=shared";

    [Fact]
    public async Task SaveAndLoad_ScalarSettings_RoundTripCorrectly()
    {
        await using var store = new SqliteSettingsStore(InMemoryConnectionString("settings_scalar"));
        await store.InitialiseAsync();

        var original = new ClientSettingsDto
        {
            ResolutionWidth  = 1920,
            ResolutionHeight = 1080,
            Fullscreen       = true,
            RenderQuality    = "High",
            MasterVolume     = 80,
            MusicVolume      = 50,
            SfxVolume        = 70,
            LastServerHost   = "login.mh.local",
            LastServerPort   = 9000,
            WindowX          = 100,
            WindowY          = 200,
            Language         = "fr-FR",
        };

        await store.SaveSettingsAsync(original);
        var loaded = await store.LoadSettingsAsync();

        Assert.Equal(1920,             loaded.ResolutionWidth);
        Assert.Equal(1080,             loaded.ResolutionHeight);
        Assert.True(loaded.Fullscreen);
        Assert.Equal("High",           loaded.RenderQuality);
        Assert.Equal(80,               loaded.MasterVolume);
        Assert.Equal(50,               loaded.MusicVolume);
        Assert.Equal(70,               loaded.SfxVolume);
        Assert.Equal("login.mh.local", loaded.LastServerHost);
        Assert.Equal(9000,             loaded.LastServerPort);
        Assert.Equal(100,              loaded.WindowX);
        Assert.Equal(200,              loaded.WindowY);
        Assert.Equal("fr-FR",          loaded.Language);
    }

    [Fact]
    public async Task SaveSettings_PartialUpsert_DoesNotClobberExistingValues()
    {
        await using var store = new SqliteSettingsStore(InMemoryConnectionString("settings_partial"));
        await store.InitialiseAsync();

        // Write first batch.
        await store.SaveSettingsAsync(new ClientSettingsDto
        {
            ResolutionWidth  = 1280,
            ResolutionHeight = 720,
            Language         = "en-US",
        });

        // Write second batch — only overrides Language.
        await store.SaveSettingsAsync(new ClientSettingsDto { Language = "de-DE" });

        var loaded = await store.LoadSettingsAsync();
        Assert.Equal(1280,    loaded.ResolutionWidth);   // from first write
        Assert.Equal(720,     loaded.ResolutionHeight);  // from first write
        Assert.Equal("de-DE", loaded.Language);           // updated by second write
    }

    [Fact]
    public async Task LoadSettings_BeforeSave_ReturnsAllNullFields()
    {
        await using var store = new SqliteSettingsStore(InMemoryConnectionString("settings_empty"));
        await store.InitialiseAsync();

        var loaded = await store.LoadSettingsAsync();

        Assert.Null(loaded.ResolutionWidth);
        Assert.Null(loaded.ResolutionHeight);
        Assert.Null(loaded.Fullscreen);
        Assert.Null(loaded.RenderQuality);
        Assert.Null(loaded.MasterVolume);
        Assert.Null(loaded.Language);
    }

    [Fact]
    public async Task SaveAndLoad_Keybinds_RoundTripCorrectly()
    {
        await using var store = new SqliteSettingsStore(InMemoryConnectionString("settings_keybinds"));
        await store.InitialiseAsync();

        await store.SaveKeybindAsync("UseSkill1",      "F1");
        await store.SaveKeybindAsync("OpenInventory",  "I");
        await store.SaveKeybindAsync("ToggleMap",      "M");

        var keybinds = await store.LoadKeybindsAsync();

        Assert.Equal(3, keybinds.Count);
        Assert.Contains(keybinds, k => k.ActionName == "UseSkill1"     && k.KeyCode == "F1");
        Assert.Contains(keybinds, k => k.ActionName == "OpenInventory" && k.KeyCode == "I");
        Assert.Contains(keybinds, k => k.ActionName == "ToggleMap"     && k.KeyCode == "M");
    }

    [Fact]
    public async Task SaveKeybind_Upsert_OverwritesExistingKey()
    {
        await using var store = new SqliteSettingsStore(InMemoryConnectionString("settings_keybind_upsert"));
        await store.InitialiseAsync();

        await store.SaveKeybindAsync("UseSkill1", "F1");
        await store.SaveKeybindAsync("UseSkill1", "F2"); // overwrite

        var keybinds = await store.LoadKeybindsAsync();

        var entry = Assert.Single(keybinds);
        Assert.Equal("F2", entry.KeyCode);
    }

    [Fact]
    public async Task ReplaceAllKeybinds_ClearsOldAndWritesNew()
    {
        await using var store = new SqliteSettingsStore(InMemoryConnectionString("settings_replace_keybinds"));
        await store.InitialiseAsync();

        await store.SaveKeybindAsync("OldAction", "X");

        var newBinds = new[]
        {
            new KeybindDto("UseSkill1", "F1"),
            new KeybindDto("UseSkill2", "F2"),
        };
        await store.ReplaceAllKeybindsAsync(newBinds);

        var loaded = await store.LoadKeybindsAsync();

        Assert.Equal(2, loaded.Count);
        Assert.DoesNotContain(loaded, k => k.ActionName == "OldAction");
        Assert.Contains(loaded, k => k.ActionName == "UseSkill1" && k.KeyCode == "F1");
        Assert.Contains(loaded, k => k.ActionName == "UseSkill2" && k.KeyCode == "F2");
    }

    [Fact]
    public async Task InitialiseAsync_IsIdempotent()
    {
        // Calling InitialiseAsync twice must not throw or corrupt the schema.
        await using var store = new SqliteSettingsStore(InMemoryConnectionString("settings_idem"));
        await store.InitialiseAsync();
        await store.InitialiseAsync(); // second call

        await store.SaveSettingsAsync(new ClientSettingsDto { Language = "ja-JP" });
        var loaded = await store.LoadSettingsAsync();
        Assert.Equal("ja-JP", loaded.Language);
    }
}
