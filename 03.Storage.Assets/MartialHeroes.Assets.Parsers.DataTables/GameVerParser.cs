using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.DataTables.Models;

namespace MartialHeroes.Assets.Parsers.DataTables;

/// <summary>
///     Parser for <c>data/cursor/game.ver</c> — a variable-length list of u32 LE values
///     supplying the client version number consumed by the enter-game token formula.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/game_ver.md — "variable-length list of 32-bit little-endian integers,
///     minimum 7 elements; canonical shipping file is exactly 28 bytes (7 × u32LE)": CONFIRMED.
///     <para>
///         The canonical file is exactly 28 bytes (7 elements). A file with fewer than 7 elements (less
///         than 28 bytes) is rejected. A file with more than 7 elements is tolerated — extra trailing
///         elements are counted but not individually interpreted.
///         spec: Docs/RE/formats/game_ver.md §Identification — "minimum 7 elements (28 bytes)": CONFIRMED.
///         spec: Docs/RE/formats/game_ver.md §Login version gate — "requires count >= 7; longer files tolerated":
///         CONFIRMED.
///     </para>
///     <para>
///         The enter-game token is computed as <c>10 × version_source + 9</c>, where
///         <c>version_source</c> is the u32 at offset <c>0x14</c> (field index 5).
///         With the observed <c>version_source = 2114</c> the token is <c>21149</c>.
///         spec: Docs/RE/formats/game_ver.md §Derived value — enter-game version token: CONFIRMED.
///     </para>
///     <para>ZERO rendering/engine dependencies.</para>
/// </remarks>
public static class GameVerParser
{
    // Minimum file size: 7 × 4 bytes = 28 bytes.
    // spec: Docs/RE/formats/game_ver.md §Identification — "minimum 7 elements (28 bytes)": CONFIRMED.
    private const int MinFileSize = 28;

    // Minimum number of u32 fields (also the count in the canonical shipping file).
    // spec: Docs/RE/formats/game_ver.md §Identification — "minimum 7 elements; canonical 28 bytes": CONFIRMED.
    private const int MinFieldCount = 7;

    /// <summary>
    ///     Parses a <c>game.ver</c> file from raw bytes delivered by the VFS.
    /// </summary>
    /// <param name="data">
    ///     Raw file bytes (from the VFS or a unit-test fixture).
    ///     Must be at least 28 bytes (7 × u32 LE) and must be a multiple of 4.
    ///     Longer files (more than 7 elements) are accepted.
    /// </param>
    /// <returns>A <see cref="GameVerData" /> with the 7 canonical decoded fields.</returns>
    /// <exception cref="InvalidDataException">
    ///     Thrown when the buffer is too short (fewer than 7 elements / 28 bytes) or is not
    ///     a multiple of 4 bytes.
    /// </exception>
    /// <remarks>
    ///     spec: Docs/RE/formats/game_ver.md §File layout — "variable-length u32LE list, minimum 7 elements": CONFIRMED.
    ///     spec: Docs/RE/formats/game_ver.md §7 §Layout — field offsets 0x00–0x18: CONFIRMED.
    ///     spec: Docs/RE/formats/game_ver.md §Derived value — "version_token = 10 × version_source + 9": CONFIRMED.
    /// </remarks>
    public static GameVerData Parse(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;

        // Validate: must be a non-zero multiple of 4 with at least 7 elements (28 bytes).
        // spec: Docs/RE/formats/game_ver.md §Identification —
        //   "size % 4 == 0 && count >= 7": CONFIRMED.
        // spec: Docs/RE/formats/game_ver.md §Login version gate —
        //   "requires count >= 7; longer files tolerated (>7 elements accepted)": CONFIRMED.
        if (span.Length < MinFileSize || span.Length % 4 != 0)
            throw new InvalidDataException(
                $"game.ver parse error: buffer is {span.Length} bytes; " +
                $"expected a multiple of 4 with at least {MinFieldCount} u32 elements ({MinFileSize} bytes minimum). " +
                "spec: Docs/RE/formats/game_ver.md §Identification.");

        // Read 7 × u32 LE, field by field, using BinaryPrimitives (zero-alloc).
        // spec: Docs/RE/formats/game_ver.md §7 — "7 × u32, little-endian": CONFIRMED.

        // field0 @ 0x00 — semantic UNVERIFIED.
        // spec: Docs/RE/formats/game_ver.md §7 §Layout — "field0 @ 0x00: UNVERIFIED".
        var field0 = BinaryPrimitives.ReadUInt32LittleEndian(span[..]);

        // field1 @ 0x04 — semantic UNVERIFIED.
        // spec: Docs/RE/formats/game_ver.md §7 §Layout — "field1 @ 0x04: UNVERIFIED".
        var field1 = BinaryPrimitives.ReadUInt32LittleEndian(span[0x04..]);

        // field2 @ 0x08 — semantic UNVERIFIED.
        // spec: Docs/RE/formats/game_ver.md §7 §Layout — "field2 @ 0x08: UNVERIFIED".
        var field2 = BinaryPrimitives.ReadUInt32LittleEndian(span[0x08..]);

        // field3 @ 0x0C — consumed by a separate external (non-VFS) client system-info reader
        // that seeks to byte offset 12 (= field index 3); SAMPLE-VERIFIED-as-consumed.
        // spec: Docs/RE/formats/game_ver.md §7 §Layout — "field3 @ 0x0C: SAMPLE-VERIFIED-as-consumed (external reader seeks offset 12)".
        var field3 = BinaryPrimitives.ReadUInt32LittleEndian(span[0x0C..]);

        // field4 @ 0x10 — semantic UNVERIFIED.
        // spec: Docs/RE/formats/game_ver.md §7 §Layout — "field4 @ 0x10: UNVERIFIED".
        var field4 = BinaryPrimitives.ReadUInt32LittleEndian(span[0x10..]);

        // version_source @ 0x14 (field5) — CONFIRMED role; consumed by enter-game token.
        // spec: Docs/RE/formats/game_ver.md §7 §Layout —
        //   "version_source (field5) @ 0x14 — CONFIRMED (role)".
        var versionSource = BinaryPrimitives.ReadUInt32LittleEndian(span[0x14..]);

        // field6 @ 0x18 — semantic UNVERIFIED.
        // spec: Docs/RE/formats/game_ver.md §7 §Layout — "field6 @ 0x18: UNVERIFIED".
        var field6 = BinaryPrimitives.ReadUInt32LittleEndian(span[0x18..]);

        return new GameVerData
        {
            Field0 = field0,
            Field1 = field1,
            Field2 = field2,
            Field3 = field3,
            Field4 = field4,
            VersionSourceField = versionSource,
            Field6 = field6
        };
    }
}