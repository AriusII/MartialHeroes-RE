using System.Buffers.Binary;
using System.Text;

namespace MartialHeroes.Assets.Vfs.Tests;

// ---------------------------------------------------------------------------
// Synthetic archive builder
//
// Produces a conformant data.inf + data/data.vfs pair in a temp directory
// according to the format spec at Docs/RE/formats/pak.md.
//
// data.inf layout (spec: Docs/RE/formats/pak.md §Header / §TOC record — sample-verified):
//   Header — 24 bytes:
//     +0  char[8] magic       — null-padded ASCII "VFS001" (read-and-discarded by the client).
//     +8  u32 LE  field_08    — scalar, role unknown; NOT the count. Written as 39 (sample value).
//     +12 u32 LE  entry_count — the only consumed field.
//     +16 u64 LE  total_blob_size — data.vfs byte length (read-and-discarded; integrity cross-check).
//   TOC — entry_count × 144 bytes:
//     +0   char[100] name             — null-terminated, lower-case ASCII.
//     +100 u8[4]     pad_100          — alignment padding (zero).
//     +104 i64 LE    dataOffset       — payload offset within data.vfs (entry 0 = 24, the header echo).
//     +112 i64 LE    dataSize         — payload byte count (low 32 bits consumed).
//     +120 u64 LE    creation_time    — Windows FILETIME, never read by the client.
//     +128 u64 LE    last_access_time — Windows FILETIME, never read by the client.
//     +136 u64 LE    last_write_time  — Windows FILETIME, never read by the client.
//
// The data.vfs blob leads with a verbatim 24-byte echo of the header, so every payload begins at
// offset >= 24 (entry 0 = 24). The builder writes this echo by default so the fixture is a faithful
// sample of a real archive.
// ---------------------------------------------------------------------------

internal sealed class SyntheticArchive : IDisposable
{
    // spec: Docs/RE/formats/pak.md — header and record sizes.
    private const int HeaderSize = 24; // sample-verified
    private const int RecordSize = 144; // sample-verified (0x90)
    private const int NameCapacity = 100; // sample-verified

    // spec: Docs/RE/formats/pak.md §Header — magic "VFS001" (null-padded to 8 bytes), field_08 = 39.
    private static readonly byte[] Vfs001Magic = [(byte)'V', (byte)'F', (byte)'S', (byte)'0', (byte)'0', (byte)'1', 0, 0];
    private const uint SampleFieldO8 = 39; // sample value of field_08

    // Non-zero FILETIME-shaped sample values for the three trailing TOC timestamps. Arbitrary but
    // plausible (~2004-era 100-ns FILETIME magnitudes); the parser must read-and-discard them.
    private const ulong SampleCreationTime = 0x01C4A0000000_0000UL;
    private const ulong SampleAccessTime = 0x01C4A1000000_0000UL;
    private const ulong SampleWriteTime = 0x01C4A2000000_0000UL;

    public string InfPath { get; }
    public string VfsPath { get; }

    /// <summary>The entries written into the archive, in the order they appear on disk.</summary>
    public IReadOnlyList<(string Name, byte[] Data)> Entries { get; }

    private readonly string _tempDir;

    private SyntheticArchive(
        string tempDir,
        string infPath,
        string vfsPath,
        IReadOnlyList<(string Name, byte[] Data)> entries)
    {
        _tempDir = tempDir;
        InfPath = infPath;
        VfsPath = vfsPath;
        Entries = entries;
    }

    /// <summary>
    /// Builds a synthetic archive in a fresh temp directory. The data.vfs blob leads with the 24-byte
    /// header echo (so payloads start at offset 24, matching a real archive), the header carries the
    /// real <c>VFS001</c> magic, and each TOC record carries non-zero FILETIME-shaped trailing bytes.
    /// </summary>
    public static SyntheticArchive Build(params (string Name, byte[] Data)[] entries)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        string dataDir = Path.Combine(tempDir, "data");
        Directory.CreateDirectory(dataDir);

        string infPath = Path.Combine(tempDir, "data.inf");
        string vfsPath = Path.Combine(dataDir, "data.vfs");

        // Sort entries ascending by lower-cased name (spec requirement).
        var sorted = entries
            .Select(e => (Name: e.Name.ToLowerInvariant(), e.Data))
            .OrderBy(e => e.Name, StringComparer.Ordinal)
            .ToArray();

        long[] offsets = new long[sorted.Length];

        // --- Write data/data.vfs: leading 24-byte header echo, then the concatenated payloads ---
        // spec: Docs/RE/formats/pak.md §"data.vfs leads with a verbatim 24-byte header echo".
        using (var vfsStream = new FileStream(vfsPath, FileMode.Create, FileAccess.Write))
        {
            // Header echo (only present when there is at least one entry — a zero-entry archive has a
            // zero-byte .vfs so the OS-mapping edge case is still exercised by the empty case).
            long cursor = 0;
            if (sorted.Length > 0)
            {
                Span<byte> echo = stackalloc byte[HeaderSize];
                WriteHeader(echo, sorted.Length);
                vfsStream.Write(echo);
                cursor = HeaderSize;
            }

            for (int i = 0; i < sorted.Length; i++)
            {
                offsets[i] = cursor;
                vfsStream.Write(sorted[i].Data);
                cursor += sorted[i].Data.Length;
            }
        }

        // --- Write data.inf ---
        using (var infStream = new FileStream(infPath, FileMode.Create, FileAccess.Write))
        {
            // Header (24 bytes) — same bytes echoed at data.vfs offset 0.
            Span<byte> header = stackalloc byte[HeaderSize];
            WriteHeader(header, sorted.Length);
            infStream.Write(header);

            // TOC records (144 bytes each).
            Span<byte> record = stackalloc byte[RecordSize];
            for (int i = 0; i < sorted.Length; i++)
            {
                record.Clear();

                // name @ +0, char[100], null-terminated, lower-case.
                byte[] nameBytes = Encoding.ASCII.GetBytes(sorted[i].Name);
                if (nameBytes.Length >= NameCapacity)
                    throw new ArgumentException(
                        $"Entry name too long (max {NameCapacity - 1} chars): {sorted[i].Name}");
                nameBytes.CopyTo(record[..NameCapacity]);

                // pad_100 @ +100 — zero (alignment padding).

                // dataOffset @ +104, i64 LE.
                BinaryPrimitives.WriteInt64LittleEndian(record[104..], offsets[i]);

                // dataSize @ +112, i64 LE.
                BinaryPrimitives.WriteInt64LittleEndian(record[112..], sorted[i].Data.Length);

                // Three Windows FILETIME values @ +120/+128/+136 — non-zero, never read by the client.
                BinaryPrimitives.WriteUInt64LittleEndian(record[120..], SampleCreationTime);
                BinaryPrimitives.WriteUInt64LittleEndian(record[128..], SampleAccessTime);
                BinaryPrimitives.WriteUInt64LittleEndian(record[136..], SampleWriteTime);

                infStream.Write(record);
            }
        }

        return new SyntheticArchive(tempDir, infPath, vfsPath, sorted);
    }

    // Writes the 24-byte header (magic + field_08 + entry_count + total-blob-size placeholder).
    // spec: Docs/RE/formats/pak.md §Header — char[8] magic; u32 field_08; u32 entry_count; u64 blob_size.
    private static void WriteHeader(Span<byte> header, int entryCount)
    {
        header.Clear();
        Vfs001Magic.CopyTo(header); // magic @ +0
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..], SampleFieldO8); // field_08 @ +8
        BinaryPrimitives.WriteUInt32LittleEndian(header[12..], (uint)entryCount); // entry_count @ +12
        // total_blob_size @ +16/+20 (u64) — left zero; read-and-discarded by the client.
    }

    /// <summary>
    /// Writes a raw <c>data.inf</c> + <c>data/data.vfs</c> pair from caller-supplied bytes, bypassing the
    /// conformant builder. For exercising malformed / edge-case headers and TOC records that the normal
    /// <see cref="Build"/> cannot express (e.g. a name with no null terminator, or a high-dword dataSize).
    /// </summary>
    public static SyntheticArchive BuildRaw(byte[] infBytes, byte[] vfsBytes)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        string dataDir = Path.Combine(tempDir, "data");
        Directory.CreateDirectory(dataDir);

        string infPath = Path.Combine(tempDir, "data.inf");
        string vfsPath = Path.Combine(dataDir, "data.vfs");

        File.WriteAllBytes(infPath, infBytes);
        File.WriteAllBytes(vfsPath, vfsBytes);

        return new SyntheticArchive(tempDir, infPath, vfsPath, []);
    }

    /// <summary>
    /// Builds a 24-byte header (with the real <c>VFS001</c> magic) for raw test archives.
    /// spec: Docs/RE/formats/pak.md §Header.
    /// </summary>
    public static byte[] MakeHeader(int entryCount)
    {
        byte[] header = new byte[HeaderSize];
        WriteHeader(header, entryCount);
        return header;
    }

    /// <summary>
    /// Builds one 144-byte TOC record from explicit field bytes for raw test archives.
    /// <paramref name="nameField"/> is copied into the 100-byte name field verbatim (no null is added,
    /// so callers can express a name that fills all 100 bytes).
    /// spec: Docs/RE/formats/pak.md §TOC record.
    /// </summary>
    public static byte[] MakeRecord(ReadOnlySpan<byte> nameField, long dataOffset, long dataSize)
    {
        if (nameField.Length > NameCapacity)
            throw new ArgumentException($"Name field exceeds {NameCapacity} bytes.", nameof(nameField));

        byte[] record = new byte[RecordSize];
        nameField.CopyTo(record.AsSpan(0, NameCapacity));
        BinaryPrimitives.WriteInt64LittleEndian(record.AsSpan(104), dataOffset);
        BinaryPrimitives.WriteInt64LittleEndian(record.AsSpan(112), dataSize);
        return record;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            /* best-effort cleanup */
        }
    }
}
