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
// Header layout (24 bytes, LE):
//   +0   u32  unknown_0   — UNVERIFIED (possibly magic tag)
//   +4   u32  unknown_4   — UNVERIFIED (possibly format version)
//   +8   u32  unknown_8   — UNVERIFIED (possibly reserved / flags)
//   +12  u32  entry_count — CONFIRMED: drives allocation and bulk-read size
//   +16  u32  unknown_16  — UNVERIFIED
//   +20  u32  unknown_20  — UNVERIFIED

/// <summary>
/// Parses <c>data.inf</c> and exposes a sorted, binary-searchable directory of
/// <see cref="VfsEntry"/> records.  The <c>data.inf</c> stream is read in full and
/// then released; the caller retains no file handle after construction.
/// </summary>
internal sealed class VfsDirectory
{
    // spec: Docs/RE/formats/pak.md — header constants.
    private const int HeaderSize        = 24;  // spec: Docs/RE/formats/pak.md — "24-byte header". CONFIRMED.
    private const int EntryCountOffset  = 12;  // spec: Docs/RE/formats/pak.md — entry_count @ +12. CONFIRMED.

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

        // unknown_0, unknown_4, unknown_8: UNVERIFIED — read but not consumed.
        // unknown_16, unknown_20:          UNVERIFIED — read but not consumed.
        // We do NOT validate or assert on these fields; they are opaque per the spec.

        // entry_count @ +12, u32 LE. CONFIRMED.
        uint entryCount = BinaryPrimitives.ReadUInt32LittleEndian(header[EntryCountOffset..]);

        if (entryCount == 0)
            return new VfsDirectory([]);

        // --- 2. Allocate and bulk-read the TOC ---
        // spec: Docs/RE/formats/pak.md — "Allocate 144 × entry_count bytes" then
        // "Read 144 × entry_count bytes from data.inf starting at offset 24". CONFIRMED.
        int tocByteCount = checked((int)entryCount * VfsEntry.RecordSize);
        byte[] toc = new byte[tocByteCount];
        ReadExact(stream, toc);

        // --- 3. Parse each 144-byte record ---
        VfsEntry[] entries = new VfsEntry[entryCount];
        for (int i = 0; i < (int)entryCount; i++)
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
            else         hi = mid - 1;
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
