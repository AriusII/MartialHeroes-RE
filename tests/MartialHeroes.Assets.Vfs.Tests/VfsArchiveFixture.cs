using System.Buffers.Binary;
using System.Text;

namespace MartialHeroes.Assets.Vfs.Tests;

// ---------------------------------------------------------------------------
// Synthetic archive builder
//
// Produces a conformant data.inf + data/data.vfs pair in a temp directory
// according to the format spec at Docs/RE/formats/pak.md.
//
// data.inf layout (spec: Docs/RE/formats/pak.md):
//   Header — 24 bytes:
//     +0  u32 LE  unknown_0   — written as 0 (UNVERIFIED field; opaque)
//     +4  u32 LE  unknown_4   — written as 0 (UNVERIFIED)
//     +8  u32 LE  unknown_8   — written as 0 (UNVERIFIED)
//     +12 u32 LE  entry_count — CONFIRMED
//     +16 u32 LE  unknown_16  — written as 0 (UNVERIFIED)
//     +20 u32 LE  unknown_20  — written as 0 (UNVERIFIED)
//   TOC — entry_count × 144 bytes:
//     +0   char[100] name       — null-terminated, lower-case ASCII. CONFIRMED.
//     +100 u8[4]     pad_100    — zero-filled (UNVERIFIED alignment padding).
//     +104 i64 LE    dataOffset — CONFIRMED.
//     +112 i64 LE    dataSize   — CONFIRMED.
//     +120 u8[24]    pad_120    — zero-filled (UNVERIFIED).
// ---------------------------------------------------------------------------

internal sealed class SyntheticArchive : IDisposable
{
    // spec: Docs/RE/formats/pak.md — header and record sizes.
    private const int HeaderSize   = 24;   // CONFIRMED
    private const int RecordSize   = 144;  // CONFIRMED (0x90)
    private const int NameCapacity = 100;  // CONFIRMED

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
        _tempDir  = tempDir;
        InfPath   = infPath;
        VfsPath   = vfsPath;
        Entries   = entries;
    }

    /// <summary>
    /// Builds a synthetic archive in a fresh temp directory.
    /// <paramref name="entries"/> must be provided in ascending order by lower-cased name
    /// (the spec requires the TOC to be sorted for binary search).
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

        // --- Write data/data.vfs: concatenate all payloads ---
        long[] offsets = new long[sorted.Length];
        using (var vfsStream = new FileStream(vfsPath, FileMode.Create, FileAccess.Write))
        {
            long cursor = 0;
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
            // Header (24 bytes).
            Span<byte> header = stackalloc byte[HeaderSize];
            header.Clear();
            // unknown_0, unknown_4, unknown_8, unknown_16, unknown_20 → 0 (UNVERIFIED fields)
            // entry_count @ +12. spec: Docs/RE/formats/pak.md — entry_count @ +12, u32 LE. CONFIRMED.
            BinaryPrimitives.WriteUInt32LittleEndian(header[12..], (uint)sorted.Length);
            infStream.Write(header);

            // TOC records (144 bytes each).
            Span<byte> record = stackalloc byte[RecordSize];
            for (int i = 0; i < sorted.Length; i++)
            {
                record.Clear();

                // name @ +0, char[100], null-terminated, lower-case. CONFIRMED.
                byte[] nameBytes = Encoding.ASCII.GetBytes(sorted[i].Name);
                if (nameBytes.Length >= NameCapacity)
                    throw new ArgumentException(
                        $"Entry name too long (max {NameCapacity - 1} chars): {sorted[i].Name}");
                nameBytes.CopyTo(record[..NameCapacity]);
                // Remaining bytes are 0 (null terminator + padding already cleared).

                // pad_100 @ +100 — zero-filled. UNVERIFIED.

                // dataOffset @ +104, i64 LE. CONFIRMED.
                BinaryPrimitives.WriteInt64LittleEndian(record[104..], offsets[i]);

                // dataSize @ +112, i64 LE. CONFIRMED.
                BinaryPrimitives.WriteInt64LittleEndian(record[112..], sorted[i].Data.Length);

                // pad_120 @ +120 — zero-filled. UNVERIFIED.

                infStream.Write(record);
            }
        }

        return new SyntheticArchive(tempDir, infPath, vfsPath, sorted);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }
}
