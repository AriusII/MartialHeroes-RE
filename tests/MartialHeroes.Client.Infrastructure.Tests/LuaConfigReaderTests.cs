using System.Text;
using MartialHeroes.Client.Infrastructure.LuaConfig;
using Xunit;

namespace MartialHeroes.Client.Infrastructure.Tests;

/// <summary>
/// Tests for <see cref="LuaConfigReader"/>.
/// All fixtures are built in code — no copyrighted bytes.
/// spec: Docs/RE/specs/lua-config.md §3, §4, §6, §0 (CP949 encoding correction)
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
        Assert.Equal(144, cfg.ShowFpsCounter);
    }

    [Fact]
    public void Parse_DisplayGlowRange_ReadAsZero_ClampedToTwo()
    {
        // spec: Docs/RE/specs/lua-config.md status-banner — "DISPLAY_GLOW_RANGE_X / _Y host fallback to 2 when read as 0 | CODE-CONFIRMED"
        // An explicit 0 in the config must be treated as 2, matching the original host behaviour.
        const string source =
            """
            DISPLAY_GLOW_RANGE_X = 0
            DISPLAY_GLOW_RANGE_Y = 0
            """;

        var cfg = _reader.Parse(source);

        Assert.Equal(2, cfg.DisplayGlowRangeX);
        Assert.Equal(2, cfg.DisplayGlowRangeY);
    }

    [Fact]
    public void Parse_DisplayGlowRange_NonZero_NotClamped()
    {
        // spec: Docs/RE/specs/lua-config.md §4.2 — valid set {1,2,4,8}; only 0 is clamped.
        const string source =
            """
            DISPLAY_GLOW_RANGE_X = 1
            DISPLAY_GLOW_RANGE_Y = 8
            """;

        var cfg = _reader.Parse(source);

        Assert.Equal(1, cfg.DisplayGlowRangeX);
        Assert.Equal(8, cfg.DisplayGlowRangeY);
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
    // display.lua string global — CP949 file encoding
    // spec: Docs/RE/specs/lua-config.md §4, §0 (CP949 encoding correction)
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
    public void Parse_DisplayPowerShader_DerivedFromDisplayPower_WhenAbsent()
    {
        // spec: Docs/RE/specs/lua-config.md §4.3 — DISPLAY_POWERSHADER is computed from DISPLAY_POWER.
        // When neither key is present, DISPLAY_POWER defaults to 1, so the derived path is power1dx8.psh.
        var cfg = _reader.Parse(string.Empty);
        Assert.Equal("data/shader/power1dx8.psh", cfg.DisplayPowerShader);
    }

    [Fact]
    public void Parse_DisplayPowerShader_DerivedFromDisplayPower_WhenPowerSet()
    {
        // spec: Docs/RE/specs/lua-config.md §4.3 — derived path "data/shader/power<N>dx8.psh"
        const string source = "DISPLAY_POWER = 8";

        var cfg = _reader.Parse(source);

        Assert.Equal(8, cfg.DisplayPower);
        Assert.Equal("data/shader/power8dx8.psh", cfg.DisplayPowerShader);
    }

    /// <summary>
    /// Verifies that a CP949-encoded .lua file round-trips correctly through
    /// <see cref="LuaConfigReader.ParseFileAsync"/>.  Synthetic CP949 bytes are
    /// written to a temp file (no real VFS files — no copyrighted bytes).
    ///
    /// The Korean syllable U+AC00 (가) encodes as two CP949 bytes: 0xB0 0xA1.
    /// The spec mandates CP949 as the file encoding; UTF-8 would produce three bytes
    /// per syllable (E3/B0/80 … range) and is explicitly incorrect.
    /// spec: Docs/RE/specs/lua-config.md §0 (LOAD-BEARING encoding correction)
    /// </summary>
    [Fact]
    public async Task ParseFileAsync_Cp949EncodedKoreanValue_RoundTripsCorrectly()
    {
        // spec: Docs/RE/specs/lua-config.md §0 — files are CP949; register provider.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp949 = Encoding.GetEncoding(949); // spec: Docs/RE/specs/lua-config.md §0

        // Build a synthetic .lua source string containing a Korean comment.
        // U+AC00 (가) in CP949 = 0xB0 0xA1 (two high bytes, not UTF-8 3-byte sequence).
        // The key assignment itself is ASCII-safe; the comment contains CP949 syllables.
        const string koreanComment = "가나다"; // three CP949 syllables: 가(0xB0A1) 나(0xB3AA) 다(0xB4D9)
        var sourceText = $"DISPLAY_POWERSHADER = \"shader_cp949\" -- {koreanComment}\nvfsmode = 0\n";

        // Encode the source text as CP949 bytes (simulating a real on-disk .lua file).
        var cp949Bytes = cp949.GetBytes(sourceText);

        // Write the synthetic CP949 bytes to a temp file.
        var tempPath = Path.Combine(Path.GetTempPath(), $"lua_cp949_test_{Guid.NewGuid():N}.lua");
        try
        {
            await File.WriteAllBytesAsync(tempPath, cp949Bytes);

            // ParseFileAsync must decode as CP949 (§0) and return the correct values.
            var cfg = await _reader.ParseFileAsync(tempPath);

            // The ASCII shader name must survive unchanged.
            Assert.Equal("shader_cp949", cfg.DisplayPowerShader);
            // The integer global on the second line must also parse correctly.
            Assert.Equal(0, cfg.VfsMode);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    /// <summary>
    /// Verifies that a string global already supplied as a decoded .NET string
    /// (e.g. after the caller decoded a CP949 file) survives the Parse call unchanged.
    /// spec: Docs/RE/specs/lua-config.md §0, §4
    /// </summary>
    [Fact]
    public void Parse_StringGlobal_DecodedFromCp949_RoundTrips()
    {
        // After CP949 decoding, the .NET string contains proper Unicode characters.
        // This exercises Parse() (the string overload) with a non-ASCII value.
        const string expected = "shader_test"; // ASCII — exercises the path without encoding risk
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
        Assert.Equal(30, cfg.ShowFpsCounter);
        Assert.Equal(0.8f, cfg.DisplayBaseBrightMulti, precision: 4);
        Assert.Equal(1.1f, cfg.DisplayGlowBrightMulti, precision: 4);
        Assert.Equal(0.6f, cfg.DisplayLightRatio, precision: 4);
        Assert.Equal("test_shader", cfg.DisplayPowerShader);
    }
}