using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Parsers;

/// <summary>
/// Parser for <c>data/cursor/game.ver</c> — a 28-byte binary file containing 7 × u32 LE
/// that supplies the client version number consumed by the enter-game token formula.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/config_tables.md §7 — "flat array of 7 × u32, little-endian
///   (no header, no magic, no record-count prefix — the field count is fixed at 7)": CONFIRMED.
/// <para>
/// Size: exactly <b>28 bytes</b>.  Any buffer shorter or longer than 28 bytes is rejected.
/// spec: Docs/RE/formats/config_tables.md §7 §Identification — "Size: exactly 28 bytes": CONFIRMED.
/// </para>
/// <para>
/// The enter-game token is computed as <c>10 × version_source + 9</c>, where
/// <c>version_source</c> is the u32 at offset <c>0x14</c> (field index 5).
/// With the observed <c>version_source = 2114</c> the token is <c>21149</c>.
/// spec: Docs/RE/formats/config_tables.md §7 §Version token derivation: CONFIRMED.
/// </para>
/// <para>ZERO rendering/engine dependencies.</para>
/// </remarks>
public static class GameVerParser
{
    // Fixed file size: 7 × 4 bytes = 28 bytes.
    // spec: Docs/RE/formats/config_tables.md §7 §Identification — "Size: exactly 28 bytes": CONFIRMED.
    private const int FileSize = 28;

    // Number of u32 fields.
    // spec: Docs/RE/formats/config_tables.md §7 — "7 × u32, little-endian": CONFIRMED.
    private const int FieldCount = 7;

    /// <summary>
    /// Parses a <c>game.ver</c> file from raw bytes delivered by the VFS.
    /// </summary>
    /// <param name="data">
    /// Raw file bytes (from the VFS or a unit-test fixture).
    /// Must be exactly 28 bytes.
    /// </param>
    /// <returns>A <see cref="GameVerData"/> with all 7 decoded fields.</returns>
    /// <exception cref="InvalidDataException">
    /// Thrown when the buffer length is not exactly 28 bytes.
    /// </exception>
    /// <remarks>
    /// spec: Docs/RE/formats/config_tables.md §7 §Layout — field offsets 0x00–0x18: CONFIRMED.
    /// spec: Docs/RE/formats/config_tables.md §7 §Version token derivation —
    ///   "version_token = 10 × version_source + 9": CONFIRMED.
    /// </remarks>
    public static GameVerData Parse(ReadOnlyMemory<byte> data)
    {
        ReadOnlySpan<byte> span = data.Span;

        // Validate exact size.
        // spec: Docs/RE/formats/config_tables.md §7 §Identification —
        //   "Size: exactly 28 bytes": CONFIRMED.
        if (span.Length != FileSize)
            throw new InvalidDataException(
                $"game.ver parse error: expected exactly {FileSize} bytes " +
                $"but buffer is {span.Length} bytes. " +
                "spec: Docs/RE/formats/config_tables.md §7 §Identification.");

        // Read 7 × u32 LE, field by field, using BinaryPrimitives (zero-alloc).
        // spec: Docs/RE/formats/config_tables.md §7 — "7 × u32, little-endian": CONFIRMED.

        // field0 @ 0x00 — semantic UNVERIFIED.
        // spec: Docs/RE/formats/config_tables.md §7 §Layout — "field0 @ 0x00: UNVERIFIED".
        uint field0 = BinaryPrimitives.ReadUInt32LittleEndian(span[0x00..]);

        // field1 @ 0x04 — semantic UNVERIFIED.
        // spec: Docs/RE/formats/config_tables.md §7 §Layout — "field1 @ 0x04: UNVERIFIED".
        uint field1 = BinaryPrimitives.ReadUInt32LittleEndian(span[0x04..]);

        // field2 @ 0x08 — semantic UNVERIFIED.
        // spec: Docs/RE/formats/config_tables.md §7 §Layout — "field2 @ 0x08: UNVERIFIED".
        uint field2 = BinaryPrimitives.ReadUInt32LittleEndian(span[0x08..]);

        // field3 @ 0x0C — possibly a build number; semantic UNVERIFIED.
        // spec: Docs/RE/formats/config_tables.md §7 §Layout — "field3 @ 0x0C: UNVERIFIED".
        uint field3 = BinaryPrimitives.ReadUInt32LittleEndian(span[0x0C..]);

        // field4 @ 0x10 — semantic UNVERIFIED.
        // spec: Docs/RE/formats/config_tables.md §7 §Layout — "field4 @ 0x10: UNVERIFIED".
        uint field4 = BinaryPrimitives.ReadUInt32LittleEndian(span[0x10..]);

        // version_source @ 0x14 (field5) — CONFIRMED role; consumed by enter-game token.
        // spec: Docs/RE/formats/config_tables.md §7 §Layout —
        //   "version_source (field5) @ 0x14 — CONFIRMED (role)".
        uint versionSource = BinaryPrimitives.ReadUInt32LittleEndian(span[0x14..]);

        // field6 @ 0x18 — semantic UNVERIFIED.
        // spec: Docs/RE/formats/config_tables.md §7 §Layout — "field6 @ 0x18: UNVERIFIED".
        uint field6 = BinaryPrimitives.ReadUInt32LittleEndian(span[0x18..]);

        return new GameVerData
        {
            Field0 = field0,
            Field1 = field1,
            Field2 = field2,
            Field3 = field3,
            Field4 = field4,
            VersionSourceField = versionSource,
            Field6 = field6,
        };
    }
}
