using MartialHeroes.Client.Infrastructure.LuaConfig;
using Xunit;

namespace MartialHeroes.Client.Infrastructure.Tests;

/// <summary>
/// Tests for <see cref="LuaConfigReader"/>.
/// All fixtures are built in code — no copyrighted bytes.
/// spec: Docs/RE/specs/lua-config.md §3, §4, §6, §5.2 (N-B4-2)
/// </summary>
public sealed class LuaConfigReaderTests
{
    private readonly LuaConfigReader _reader = new();

    // ────────────────────────────────────────────────────────────────────────
    // Boot flags — game.lua globals (spec: Docs/RE/specs/lua-config.md §3)
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_BootFlags_AreParsedAsFullIntegers()
    {
        // spec: Docs/RE/specs/lua-config.md §3 — each flag read as full int, not clamped bool
        const string source =
            """
            vfsmode = 1
            launcher = 0
            debugmode = 1
            """;

        var cfg = _reader.Parse(source);

        Assert.Equal(1, cfg.VfsMode);
        Assert.Equal(0, cfg.Launcher);
        Assert.Equal(1, cfg.DebugMode);
    }

    [Fact]
    public void Parse_BootFlags_DefaultToOneWhenAbsent()
    {
        // spec: Docs/RE/specs/lua-config.md §3 — "each defaults to 1 (true) if the read branch is skipped"
        var cfg = _reader.Parse(string.Empty);

        Assert.Equal(1, cfg.VfsMode);
        Assert.Equal(1, cfg.Launcher);
        Assert.Equal(1, cfg.DebugMode);
    }

    [Fact]
    public void Parse_BootFlag_ReturnsFullIntegerNotClampedBool()
    {
        // spec: Docs/RE/specs/lua-config.md §6 — "returns a full signed integer … not clamped 0/1"
        // A non-0/1 value must survive round-trip unchanged.
        const string source = "vfsmode = 42";

        var cfg = _reader.Parse(source);

        Assert.Equal(42, cfg.VfsMode);
    }

    // ────────────────────────────────────────────────────────────────────────
    // uiconfig.lua — NEW_SERVER_INDEX
    // spec: Docs/RE/specs/lua-config.md §2.3 table, §6
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_NewServerIndex_ParsedCorrectly()
    {
        // spec: Docs/RE/specs/lua-config.md §2.3
        const string source = "NEW_SERVER_INDEX = 3";

        var cfg = _reader.Parse(source);

        Assert.Equal(3, cfg.NewServerIndex);
    }

    [Fact]
    public void Parse_NewServerIndex_DefaultsToOneWhenAbsent()
    {
        var cfg = _reader.Parse(string.Empty);
        Assert.Equal(1, cfg.NewServerIndex);
    }

    // ────────────────────────────────────────────────────────────────────────
    // display.lua integer globals (spec: Docs/RE/specs/lua-config.md §4)
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_DisplayIntegers_ParsedCorrectly()
    {
        // spec: Docs/RE/specs/lua-config.md §4
        const string source =
            """
            DISPLAY_GLOW_RANGE_X = 4
            DISPLAY_GLOW_RANGE_Y = 6
            DISPLAY_FRAMERATE = 144
            """;

        var cfg = _reader.Parse(source);

        Assert.Equal(4, cfg.DisplayGlowRangeX);
        Assert.Equal(6, cfg.DisplayGlowRangeY);
        Assert.Equal(144, cfg.DisplayFramerate);
    }

    // ────────────────────────────────────────────────────────────────────────
    // display.lua float globals (spec: Docs/RE/specs/lua-config.md §4)
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_DisplayFloats_ParsedCorrectly()
    {
        // spec: Docs/RE/specs/lua-config.md §4 (float reader sibling)
        const string source =
            """
            DISPLAY_BASE_BRIGHT_MULTI = 0.75
            DISPLAY_GLOW_BRIGHT_MULTI = 1.25
            DISPLAY_LIGHT_RATIO = 0.5
            """;

        var cfg = _reader.Parse(source);

        Assert.Equal(0.75f, cfg.DisplayBaseBrightMulti, precision: 4);
        Assert.Equal(1.25f, cfg.DisplayGlowBrightMulti, precision: 4);
        Assert.Equal(0.5f, cfg.DisplayLightRatio, precision: 4);
    }

    // ────────────────────────────────────────────────────────────────────────
    // display.lua string global — UTF-8 decode (N-B4-2)
    // spec: Docs/RE/specs/lua-config.md §4, §5.2
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_DisplayPowerShader_IsReadAsString()
    {
        // spec: Docs/RE/specs/lua-config.md §4 — DISPLAY_POWERSHADER is a string global
        const string source = """DISPLAY_POWERSHADER = "power_shader_v2" """;

        var cfg = _reader.Parse(source);

        Assert.Equal("power_shader_v2", cfg.DisplayPowerShader);
    }

    [Fact]
    public void Parse_DisplayPowerShader_SingleQuoteVariant_IsReadAsString()
    {
        // Lua accepts both "…" and '…' string literals.
        const string source = "DISPLAY_POWERSHADER = 'glow_effect'";

        var cfg = _reader.Parse(source);

        Assert.Equal("glow_effect", cfg.DisplayPowerShader);
    }

    [Fact]
    public void Parse_DisplayPowerShader_DefaultsToEmptyWhenAbsent()
    {
        var cfg = _reader.Parse(string.Empty);
        Assert.Equal(string.Empty, cfg.DisplayPowerShader);
    }

    /// <summary>
    /// Verifies that the string value survives unchanged when the source is
    /// already supplied as a UTF-8 decoded string — confirming the N-B4-2 contract
    /// that callers must decode .lua files as UTF-8 before handing to the reader.
    /// spec: Docs/RE/specs/lua-config.md §5.2 (N-B4-2)
    /// </summary>
    [Fact]
    public void Parse_StringGlobal_SurvivedUtf8Decode_RoundTrips()
    {
        // The fixture string contains non-ASCII to prove encoding is not mangled.
        // A real .lua file would be read via File.ReadAllTextAsync(…, Encoding.UTF8).
        // spec: Docs/RE/specs/lua-config.md §5.2 (N-B4-2)
        const string expected = "héros_shader"; // UTF-8 text, not CP949
        var source = $"""DISPLAY_POWERSHADER = "{expected}" """;

        var cfg = _reader.Parse(source);

        Assert.Equal(expected, cfg.DisplayPowerShader);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Line-scanner edge cases
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_CommentsAndBlankLines_AreIgnored()
    {
        const string source =
            """
            -- This is a Lua line comment
            vfsmode = 0

            -- Another comment
            launcher = 1
            """;

        var cfg = _reader.Parse(source);

        Assert.Equal(0, cfg.VfsMode);
        Assert.Equal(1, cfg.Launcher);
    }

    [Fact]
    public void Parse_InlineComment_IsStripped()
    {
        // spec: Docs/RE/specs/lua-config.md §2.1 — trailing -- comments must be ignored
        const string source = "vfsmode = 0 -- disable VFS in dev mode";

        var cfg = _reader.Parse(source);

        Assert.Equal(0, cfg.VfsMode);
    }

    [Fact]
    public void Parse_LastAssignmentWins_WhenKeyAppearsMultipleTimes()
    {
        // In Lua, later assignments overwrite earlier ones; the scanner follows the same rule.
        const string source =
            """
            vfsmode = 1
            vfsmode = 0
            """;

        var cfg = _reader.Parse(source);

        Assert.Equal(0, cfg.VfsMode);
    }

    [Fact]
    public void Parse_UnrecognisedKeys_AreIgnoredWithoutError()
    {
        // cpp_load calls and unknown globals must not throw.
        const string source =
            """
            cpp_load("data/script/extra.lua")
            some_unknown_global = 99
            vfsmode = 0
            """;

        var cfg = _reader.Parse(source);

        Assert.Equal(0, cfg.VfsMode);
    }

    [Fact]
    public void Parse_AllKnownGlobals_ParsedFromOneString()
    {
        // Integration: all known config globals in a single source block.
        const string source =
            """
            vfsmode = 0
            launcher = 0
            debugmode = 1
            NEW_SERVER_INDEX = 2
            DISPLAY_GLOW_RANGE_X = 3
            DISPLAY_GLOW_RANGE_Y = 4
            DISPLAY_FRAMERATE = 30
            DISPLAY_BASE_BRIGHT_MULTI = 0.8
            DISPLAY_GLOW_BRIGHT_MULTI = 1.1
            DISPLAY_LIGHT_RATIO = 0.6
            DISPLAY_POWERSHADER = "test_shader"
            """;

        var cfg = _reader.Parse(source);

        Assert.Equal(0, cfg.VfsMode);
        Assert.Equal(0, cfg.Launcher);
        Assert.Equal(1, cfg.DebugMode);
        Assert.Equal(2, cfg.NewServerIndex);
        Assert.Equal(3, cfg.DisplayGlowRangeX);
        Assert.Equal(4, cfg.DisplayGlowRangeY);
        Assert.Equal(30, cfg.DisplayFramerate);
        Assert.Equal(0.8f, cfg.DisplayBaseBrightMulti, precision: 4);
        Assert.Equal(1.1f, cfg.DisplayGlowBrightMulti, precision: 4);
        Assert.Equal(0.6f, cfg.DisplayLightRatio, precision: 4);
        Assert.Equal("test_shader", cfg.DisplayPowerShader);
    }
}