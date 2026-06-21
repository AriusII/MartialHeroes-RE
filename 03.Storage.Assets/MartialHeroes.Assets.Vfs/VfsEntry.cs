using System.Buffers.Binary;
using System.Text;

namespace MartialHeroes.Assets.Vfs;

// spec: Docs/RE/formats/pak.md — TOC record, 144 bytes (0x90) per entry.
//
// Layout (all fields little-endian) — sample-verified (spec: Docs/RE/formats/pak.md §TOC record):
//   +0   char[100]  name             — null-terminated ASCII virtual path, stored lower-case. Sample-verified.
//   +100 u8[4]      pad_100          — alignment padding; never read. Sample-verified zero in
//                                       43,333/43,347 entries (build-tool path residue in 14, inert).
//   +104 i64 LE     dataOffset       — byte offset into data/data.vfs (entry 0 = 24, the header echo). Sample-verified.
//   +112 i64 LE     dataSize         — byte count; only low 32 bits consumed (high dword must be 0). Sample-verified.
//   +120 u64 LE     creation_time    — Windows FILETIME; source file NTFS creation time at pack. Never read by client.
//   +128 u64 LE     last_access_time — Windows FILETIME; source file NTFS last-access time. Never read by client.
//   +136 u64 LE     last_write_time  — Windows FILETIME; source file NTFS last-write time. Never read by client.
//
// Total: 144 bytes = 0x90.  Sample-verified by allocation arithmetic and stride literal in spec.

/// <summary>
///     One record from the VFS Table of Contents.
///     Parsed from a 144-byte (0x90) region in <c>data.inf</c>.
/// </summary>
/// <remarks>
///     The <c>name</c> field is decoded to a managed <see cref="string" /> at parse time and used as
///     the lookup key.  The 100-byte name field in the raw record is decoded immediately on parse;
///     no raw name buffer is retained at runtime.
/// </remarks>
public readonly struct VfsEntry : IComparable<VfsEntry>
{
    // spec: Docs/RE/formats/pak.md — field sizes and offsets.
    internal const int RecordSize = 144; // 0x90 — CONFIRMED
    internal const int NameCapacity = 100; // char[100] at +0 — CONFIRMED
    private const int DataOffsetField = 104; // i64 at +104 — CONFIRMED
    private const int DataSizeField = 112; // i64 at +112 — CONFIRMED

    /// <summary>Lower-cased ASCII virtual path (the binary-search key).</summary>
    public readonly string Name;

    /// <summary>
    ///     Byte offset of the payload within <c>data/data.vfs</c>.
    ///     spec: Docs/RE/formats/pak.md — dataOffset @ +104, i64 LE. CONFIRMED.
    /// </summary>
    public readonly long DataOffset;

    /// <summary>
    ///     Byte count of the payload.  Only the low 32 bits are meaningful;
    ///     a non-zero high dword causes the original engine's read to fail.
    ///     spec: Docs/RE/formats/pak.md — dataSize @ +112, i64 LE. CONFIRMED.
    /// </summary>
    public readonly long DataSize;

    private VfsEntry(string name, long dataOffset, long dataSize)
    {
        Name = name;
        DataOffset = dataOffset;
        DataSize = dataSize;
    }

    /// <summary>
    ///     Parses one 144-byte record from <paramref name="raw" />.
    ///     <paramref name="raw" /> must be exactly <see cref="RecordSize" /> bytes.
    /// </summary>
    internal static VfsEntry Parse(ReadOnlySpan<byte> raw)
    {
        if (raw.Length < RecordSize)
            throw new ArgumentException(
                $"TOC record too short: expected {RecordSize}, got {raw.Length}.",
                nameof(raw));

        // Decode the null-terminated ASCII name at offset 0, length 100.
        // spec: Docs/RE/formats/pak.md — name @ +0, char[100], null-terminated, lowercased. CONFIRMED.
        var nameSpan = raw[..NameCapacity];
        var nameLen = nameSpan.IndexOf((byte)0);
        if (nameLen < 0) nameLen = NameCapacity; // no null terminator — use full width
        var name = Encoding.ASCII.GetString(nameSpan[..nameLen]);
        // Names are stored lower-case at build time; normalise at parse time as well
        // so the lookup invariant holds even for archives with mixed-case names.
        name = name.ToLowerInvariant();

        // pad_100 (4 bytes at +100): alignment padding — skipped. Sample-verified zero in 43,333/43,347
        // entries (build-tool path residue in 14, inert since never consumed).

        // dataOffset @ +104, i64 LE. CONFIRMED.
        var dataOffset = BinaryPrimitives.ReadInt64LittleEndian(raw[DataOffsetField..]);

        // dataSize @ +112, i64 LE.  Only low 32 bits are consumed. CONFIRMED.
        var dataSize = BinaryPrimitives.ReadInt64LittleEndian(raw[DataSizeField..]);

        // Trailing 24 bytes at +120: three Windows FILETIME values (creation @ +120, last-access @ +128,
        // last-write @ +136), recorded by the build tool from source-file NTFS metadata. Never read by
        // the client. Sample-verified. spec: Docs/RE/formats/pak.md §TOC record.

        return new VfsEntry(name, dataOffset, dataSize);
    }

    /// <inheritdoc />
    public int CompareTo(VfsEntry other)
    {
        return string.CompareOrdinal(Name, other.Name);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"VfsEntry {{ Name=\"{Name}\", DataOffset={DataOffset}, DataSize={DataSize} }}";
    }
}