using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Fixture-based tests for <see cref="GameVerParser"/>.
/// All buffers are built in-memory; no real game file is required.
/// spec: Docs/RE/formats/game_ver.md §7
/// </summary>
public sealed class GameVerParserTests
{
    // ─── helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a syntactically correct 28-byte <c>game.ver</c> buffer (7 × u32 LE).
    /// spec: Docs/RE/formats/game_ver.md §7 §Layout — "7 × u32, little-endian, 28 bytes":
    ///   CONFIRMED.
    /// </summary>
    private static byte[] BuildGameVer(
        uint f0 = 0, uint f1 = 0, uint f2 = 0, uint f3 = 0,
        uint f4 = 0, uint f5 = 0, uint f6 = 0)
    {
        byte[] buf = new byte[28]; // 7 × 4 = 28 bytes.
        // spec: Docs/RE/formats/game_ver.md §7 §Layout — field offsets 0x00–0x18: CONFIRMED.
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0x00, 4), f0); // field0 @ 0x00
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0x04, 4), f1); // field1 @ 0x04
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0x08, 4), f2); // field2 @ 0x08
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0x0C, 4), f3); // field3 @ 0x0C
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0x10, 4), f4); // field4 @ 0x10
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0x14, 4), f5); // version_source @ 0x14
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0x18, 4), f6); // field6 @ 0x18
        return buf;
    }

    // =========================================================================
    // 1. Exact 28-byte file decodes all 7 fields
    // =========================================================================

    [Fact]
    public void Parse_ValidBuffer_AllFieldsDecoded()
    {
        // spec: Docs/RE/formats/game_ver.md §7 §Layout — field offsets 0x00–0x18: CONFIRMED.
        byte[] buf = BuildGameVer(f0: 1, f1: 2, f2: 3, f3: 4, f4: 5, f5: 6, f6: 7);
        GameVerData result = GameVerParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(1u, result.Field0);
        Assert.Equal(2u, result.Field1);
        Assert.Equal(3u, result.Field2);
        Assert.Equal(4u, result.Field3);
        Assert.Equal(5u, result.Field4);
        Assert.Equal(6u, result.VersionSourceField);
        Assert.Equal(7u, result.Field6);
    }

    // =========================================================================
    // 2. VersionSourceField is the u32 at offset 0x14 (field index 5)
    // =========================================================================

    [Fact]
    public void Parse_VersionSourceField_IsAtOffset0x14()
    {
        // spec: Docs/RE/formats/game_ver.md §7 §Layout —
        //   "version_source (field5) @ 0x14 — CONFIRMED (role)".
        byte[] buf = BuildGameVer(f5: 2114u);
        GameVerData result = GameVerParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(2114u, result.VersionSourceField);
    }

    // =========================================================================
    // 3. EnterGameVersionToken formula: 10 × version_source + 9
    // =========================================================================

    /// <summary>
    /// Verifies the enter-game token formula with the observed real-client value.
    /// spec: Docs/RE/formats/game_ver.md §7 §Version token derivation —
    ///   "version_token = 10 × version_source + 9"; "with version_source = 2114 → 21149": CONFIRMED.
    /// </summary>
    [Fact]
    public void EnterGameVersionToken_WithField5_2114_Is_21149()
    {
        // spec: Docs/RE/formats/game_ver.md §7 §Version token derivation: CONFIRMED.
        byte[] buf = BuildGameVer(f5: 2114u);
        GameVerData result = GameVerParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(21149u, result.EnterGameVersionToken);
    }

    [Theory]
    [InlineData(0u, 9u)] // 10×0+9 = 9
    [InlineData(1u, 19u)] // 10×1+9 = 19
    [InlineData(2114u, 21149u)] // real-client observed value
    [InlineData(100u, 1009u)] // 10×100+9 = 1009
    public void EnterGameVersionToken_Formula_10xPluS9(uint versionSource, uint expectedToken)
    {
        // spec: Docs/RE/formats/game_ver.md §7 §Version token derivation —
        //   "version_token = 10 × version_source + 9": CONFIRMED.
        byte[] buf = BuildGameVer(f5: versionSource);
        GameVerData result = GameVerParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(expectedToken, result.EnterGameVersionToken);
    }

    // =========================================================================
    // 4. All fields round-trip correctly (endianness check)
    // =========================================================================

    [Fact]
    public void Parse_AllFields_LittleEndianRoundTrip()
    {
        // Verify that each field occupies the correct 4-byte slot and is decoded as u32 LE.
        // spec: Docs/RE/formats/game_ver.md §7 — "7 × u32, little-endian": CONFIRMED.
        byte[] buf = BuildGameVer(
            f0: 0xDEADBEEFu,
            f1: 0x01020304u,
            f2: 0xCAFEBABEu,
            f3: 0x11223344u,
            f4: 0xAABBCCDDu,
            f5: 2114u,
            f6: 0x99887766u);

        GameVerData result = GameVerParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(0xDEADBEEFu, result.Field0);
        Assert.Equal(0x01020304u, result.Field1);
        Assert.Equal(0xCAFEBABEu, result.Field2);
        Assert.Equal(0x11223344u, result.Field3);
        Assert.Equal(0xAABBCCDDu, result.Field4);
        Assert.Equal(2114u, result.VersionSourceField);
        Assert.Equal(0x99887766u, result.Field6);
    }

    // =========================================================================
    // 5. Variable-length: accept >= 7 elements aligned to 4 bytes; reject otherwise
    // =========================================================================

    [Fact]
    public void Parse_TooShort_ThrowsInvalidDataException()
    {
        // 24 bytes = only 6 u32 elements — fewer than the required minimum of 7.
        // spec: Docs/RE/formats/game_ver.md §Identification —
        //   "minimum 7 elements (28 bytes); shorter = rejected": CONFIRMED.
        byte[] buf = new byte[24]; // 6 elements — too short
        Assert.Throws<InvalidDataException>(() => GameVerParser.Parse(new ReadOnlyMemory<byte>(buf)));
    }

    [Fact]
    public void Parse_NotAlignedToFourBytes_ThrowsInvalidDataException()
    {
        // 29 bytes — not a multiple of 4 — must be rejected regardless of length.
        // spec: Docs/RE/formats/game_ver.md §Identification —
        //   "size % 4 == 0 && count >= 7": CONFIRMED.
        byte[] buf = new byte[29];
        Assert.Throws<InvalidDataException>(() => GameVerParser.Parse(new ReadOnlyMemory<byte>(buf)));
    }

    [Fact]
    public void Parse_EmptyBuffer_ThrowsInvalidDataException()
    {
        Assert.Throws<InvalidDataException>(() => GameVerParser.Parse(ReadOnlyMemory<byte>.Empty));
    }

    [Fact]
    public void Parse_LongerThan28Bytes_AlignedMultiple_IsAccepted()
    {
        // 32 bytes = 8 × u32LE — more than 7 elements; must be tolerated (trailing elements ignored).
        // spec: Docs/RE/formats/game_ver.md §Login version gate —
        //   "requires count >= 7; longer files tolerated": CONFIRMED.
        byte[] buf = new byte[32]; // 8 elements — should parse fine
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0x14, 4), 2114u); // version_source = 2114
        GameVerData result = GameVerParser.Parse(new ReadOnlyMemory<byte>(buf));

        // The 7 canonical fields decode as normal; trailing 8th element is ignored.
        Assert.Equal(2114u, result.VersionSourceField);
        Assert.Equal(21149u, result.EnterGameVersionToken);
    }

    [Fact]
    public void Parse_ExactlySevenElements_IsAccepted()
    {
        // 28 bytes = 7 × u32LE — the canonical shipping file size; must always be accepted.
        // spec: Docs/RE/formats/game_ver.md §Identification — "canonical shipping file 28 bytes": CONFIRMED.
        byte[] buf = BuildGameVer(f5: 100u);
        GameVerData result = GameVerParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(100u, result.VersionSourceField);
        Assert.Equal(1009u, result.EnterGameVersionToken); // 10×100+9 = 1009
    }

    // =========================================================================
    // 6. Zero buffer (all fields zero) — valid
    // =========================================================================

    [Fact]
    public void Parse_AllZero_ValidWithZeroToken()
    {
        byte[] buf = new byte[28]; // all zeros
        GameVerData result = GameVerParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(0u, result.VersionSourceField);
        Assert.Equal(9u, result.EnterGameVersionToken); // 10×0+9 = 9
    }
}