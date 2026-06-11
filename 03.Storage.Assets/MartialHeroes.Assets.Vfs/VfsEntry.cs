using System.Buffers.Binary;
using System.Text;

namespace MartialHeroes.Assets.Vfs;

// spec: Docs/RE/formats/pak.md — TOC record, 144 bytes (0x90) per entry.
//
// Layout (all fields little-endian):
//   +0   char[100]  name       — null-terminated ASCII virtual path, stored lower-case. CONFIRMED.
//   +100 u8[4]      pad_100    — alignment padding; never read. UNVERIFIED (structurally expected).
//   +104 i64 LE     dataOffset — byte offset into data/data.vfs. CONFIRMED.
//   +112 i64 LE     dataSize   — byte count; only low 32 bits are consumed (high dword must be 0). CONFIRMED.
//   +120 u8[24]     pad_120    — trailing bytes; never accessed. UNVERIFIED (possibly flags/CRC/timestamp).
//
// Total: 144 bytes = 0x90.  CONFIRMED by allocation arithmetic and stride literal in spec.

/// <summary>
/// One record from the VFS Table of Contents.
/// Parsed from a 144-byte (0x90) region in <c>data.inf</c>.
/// </summary>
/// <remarks>
/// The <c>name</c> field is decoded to a managed <see cref="string"/> at parse time and used as
/// the lookup key.  The 100-byte name field in the raw record is decoded immediately on parse;
/// no raw name buffer is retained at runtime.
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
    /// Byte offset of the payload within <c>data/data.vfs</c>.
    /// spec: Docs/RE/formats/pak.md — dataOffset @ +104, i64 LE. CONFIRMED.
    /// </summary>
    public readonly long DataOffset;

    /// <summary>
    /// Byte count of the payload.  Only the low 32 bits are meaningful;
    /// a non-zero high dword causes the original engine's read to fail.
    /// spec: Docs/RE/formats/pak.md — dataSize @ +112, i64 LE. CONFIRMED.
    /// </summary>
    public readonly long DataSize;

    private VfsEntry(string name, long dataOffset, long dataSize)
    {
        Name = name;
        DataOffset = dataOffset;
        DataSize = dataSize;
    }

    /// <summary>
    /// Parses one 144-byte record from <paramref name="raw"/>.
    /// <paramref name="raw"/> must be exactly <see cref="RecordSize"/> bytes.
    /// </summary>
    internal static VfsEntry Parse(ReadOnlySpan<byte> raw)
    {
        if (raw.Length < RecordSize)
            throw new ArgumentException(
                $"TOC record too short: expected {RecordSize}, got {raw.Length}.",
                nameof(raw));

        // Decode the null-terminated ASCII name at offset 0, length 100.
        // spec: Docs/RE/formats/pak.md — name @ +0, char[100], null-terminated, lowercased. CONFIRMED.
        ReadOnlySpan<byte> nameSpan = raw[..NameCapacity];
        int nameLen = nameSpan.IndexOf((byte)0);
        if (nameLen < 0) nameLen = NameCapacity; // no null terminator — use full width
        string name = Encoding.ASCII.GetString(nameSpan[..nameLen]);
        // Names are stored lower-case at build time; normalise at parse time as well
        // so the lookup invariant holds even for archives with mixed-case names.
        name = name.ToLowerInvariant();

        // pad_100 (4 bytes at +100): UNVERIFIED alignment padding — skipped.

        // dataOffset @ +104, i64 LE. CONFIRMED.
        long dataOffset = BinaryPrimitives.ReadInt64LittleEndian(raw[DataOffsetField..]);

        // dataSize @ +112, i64 LE.  Only low 32 bits are consumed. CONFIRMED.
        long dataSize = BinaryPrimitives.ReadInt64LittleEndian(raw[DataSizeField..]);

        // pad_120 (24 bytes at +120): UNVERIFIED — not read.

        return new VfsEntry(name, dataOffset, dataSize);
    }

    /// <inheritdoc/>
    public int CompareTo(VfsEntry other) =>
        string.CompareOrdinal(Name, other.Name);

    /// <inheritdoc/>
    public override string ToString() =>
        $"VfsEntry {{ Name=\"{Name}\", DataOffset={DataOffset}, DataSize={DataSize} }}";
}