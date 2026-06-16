using System.Buffers.Binary;

namespace MartialHeroes.Assets.Vfs;

// spec: Docs/RE/formats/pak.md — index file layout (data.inf).
//
// Opening sequence (spec §"Two-file scheme"):
//   1. Read the 24-byte header from data.inf; extract entry_count at offset 12.
//   2. Read 144 × entry_count bytes from data.inf starting at offset 24.
//   3. Close data.inf.
//   4. Keep data/data.vfs open for seeks.
//
// Header layout (24 bytes, LE) — sample-verified (spec: Docs/RE/formats/pak.md §Header):
//   In struct terms: char magic[8] = "VFS001\0\0"; u32 field_08; u32 entry_count; u64 total_blob_size;
//   +0   char[8] magic           — null-padded ASCII "VFS001". Read-and-discarded; the client asserts
//                                   no magic (bug-compatible: tolerate any value here). Sample-verified.
//   +8   u32     field_08        — small scalar (=39 in the reference archive); role unknown, NOT the
//                                   entry count. Read-and-discarded. Sample-verified value.
//   +12  u32     entry_count     — the ONLY consumed field: drives the 144 × entry_count allocation and
//                                   the TOC bulk-read size. Sample-verified.
//   +16  u32     total_blob_size — low dword of a u64 (with +20). = exact data/data.vfs byte length in
//   +20  u32     (hi dword)        the reference archive; read-and-discarded (integrity cross-check
//                                   only). Sample-verified.

/// <summary>
/// Parses <c>data.inf</c> and exposes a sorted, binary-searchable directory of
/// <see cref="VfsEntry"/> records.  The <c>data.inf</c> stream is read in full and
/// then released; the caller retains no file handle after construction.
/// </summary>
internal sealed class VfsDirectory
{
    // spec: Docs/RE/formats/pak.md — header constants.
    private const int HeaderSize = 24; // spec: Docs/RE/formats/pak.md — "24-byte header". CONFIRMED.
    private const int EntryCountOffset = 12; // spec: Docs/RE/formats/pak.md — entry_count @ +12. CONFIRMED.

    /// <summary>Sorted array of entries — the binary-search target.</summary>
    private readonly VfsEntry[] _entries;

    /// <summary>Number of entries in the directory.</summary>
    public int EntryCount => _entries.Length;

    private VfsDirectory(VfsEntry[] entries) => _entries = entries;

    /// <summary>
    /// Reads and parses <c>data.inf</c> from <paramref name="stream"/>.
    /// The stream is read sequentially from its current position; it is not disposed.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// Thrown when the stream is too short or a TOC record cannot be decoded.
    /// </exception>
    public static VfsDirectory Load(Stream stream)
    {
        // --- 1. Read the 24-byte header ---
        // spec: Docs/RE/formats/pak.md — "Read the 24-byte header from data.inf". CONFIRMED.
        Span<byte> header = stackalloc byte[HeaderSize];
        ReadExact(stream, header);

        // magic @ +0 (char[8] "VFS001"), field_08 @ +8, total_blob_size @ +16/+20: all read as part of
        // the 24-byte bulk read but NOT consumed. We do NOT validate or assert on them — the original
        // client asserts none (not even the magic), so a bug-compatible reader tolerates any value here.
        // spec: Docs/RE/formats/pak.md §Header (24 bytes). Sample-verified read-and-discarded.

        // entry_count @ +12. Read as a SIGNED i32 to mirror the original mount routine, which treats
        // the count as a signed integer and guards the search loops with a "count <= 0" check. A
        // non-positive count therefore yields an empty directory (rather than the previous behaviour
        // where a high-bit-set u32 would overflow the checked multiply below).
        // spec: Docs/RE/formats/pak.md §Header — entry_count @ +12. Sample-verified.
        // spec: Docs/RE/specs/vfs_overview.md §Mount — "treated as a signed i32 ... count <= 0 guard".
        int entryCount = BinaryPrimitives.ReadInt32LittleEndian(header[EntryCountOffset..]);

        if (entryCount <= 0)
            return new VfsDirectory([]);

        // --- 2. Allocate and bulk-read the TOC ---
        // spec: Docs/RE/formats/pak.md — "Allocate 144 × entry_count bytes" then
        // "Read 144 × entry_count bytes from data.inf starting at offset 24". CONFIRMED.
        // The multiply is overflow-checked (mirrors the original's overflow-checked allocation).
        int tocByteCount = checked(entryCount * VfsEntry.RecordSize);
        byte[] toc = new byte[tocByteCount];
        ReadExact(stream, toc);

        // --- 3. Parse each 144-byte record ---
        VfsEntry[] entries = new VfsEntry[entryCount];
        for (int i = 0; i < entryCount; i++)
        {
            int recordStart = i * VfsEntry.RecordSize;
            entries[i] = VfsEntry.Parse(toc.AsSpan(recordStart, VfsEntry.RecordSize));
        }

        // --- 4. Sort ascending by name for binary search ---
        // spec: Docs/RE/formats/pak.md — "TOC must be sorted ascending by lowercased name at
        // build time; this is assumed by the binary-search implementation". CONFIRMED.
        // We sort here defensively in case the archive was produced by non-conforming tooling.
        Array.Sort(entries);

        // data.inf is no longer needed — caller may close the stream.
        return new VfsDirectory(entries);
    }

    /// <summary>
    /// Binary-searches for <paramref name="normalizedName"/> (must already be lower-case).
    /// Returns <see langword="null"/> if not found.
    /// </summary>
    /// <remarks>
    /// Allocation-free on the hot path; no LINQ.
    /// spec: Docs/RE/formats/pak.md — "Binary search the TOC array in ascending order
    /// using a byte-for-byte string comparison on the 100-byte name field". CONFIRMED.
    /// </remarks>
    public VfsEntry? TryFind(string normalizedName)
    {
        int lo = 0;
        int hi = _entries.Length - 1;
        while (lo <= hi)
        {
            int mid = (int)(((uint)lo + (uint)hi) >> 1);
            int cmp = string.CompareOrdinal(_entries[mid].Name, normalizedName);
            if (cmp == 0) return _entries[mid];
            if (cmp < 0) lo = mid + 1;
            else hi = mid - 1;
        }

        return null;
    }

    /// <summary>
    /// Enumerates all directory entries in sorted order.
    /// For diagnostics and test assertions; not a hot path.
    /// </summary>
    public ReadOnlySpan<VfsEntry> Entries => _entries.AsSpan();

    // Reads exactly <paramref name="buffer"/>.Length bytes from <paramref name="stream"/>,
    // throwing <see cref="InvalidDataException"/> on a premature end-of-stream.
    private static void ReadExact(Stream stream, Span<byte> buffer)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int n = stream.Read(buffer[totalRead..]);
            if (n == 0)
                throw new InvalidDataException(
                    $"Unexpected end of stream while reading data.inf " +
                    $"(needed {buffer.Length} bytes, got {totalRead}).");
            totalRead += n;
        }
    }
}