using System.IO.MemoryMappedFiles;

namespace MartialHeroes.Assets.Vfs;

// spec: Docs/RE/formats/pak.md — two-file scheme and lookup algorithm.
//
// Archive consists of:
//   data.inf        — index / TOC (read once, file closed after parse)
//   data/data.vfs   — data blob  (memory-mapped for process lifetime)
//
// Lookup (spec §"Lookup algorithm"):
//   1. Normalize: lower-case the caller-supplied path.
//   2. Binary search the sorted TOC by lowercased name.
//   3. Return a zero-copy slice of the memory-mapped data.vfs view.
//   No decompression.  No decryption.  CONFIRMED by spec.
//
// PORT CHOICE — read mechanism (spec: Docs/RE/formats/pak.md §"ReadFile, not a memory-mapped view"):
//   The ORIGINAL client delivers each entry's payload via malloc + a global-critical-section-guarded
//   SetFilePointerEx + ReadFile into a fresh heap buffer (no memory-mapped view of the data blob; the
//   binary's lone MapViewOfFile site is an unrelated anti-tamper module-image check). THIS port instead
//   opens data/data.vfs as a single read-only memory-mapped view and returns each entry as a zero-copy
//   ReadOnlyMemory slice of that view. Both deliver byte-for-byte identical payloads — the divergence is
//   the transfer mechanism, chosen here because zero-copy mapping aligns with the project's zero-alloc
//   data-path mandate and removes the per-read heap copy + shared-handle seek lock. This is a deliberate
//   functional-equivalence port choice, NOT a claim that the original is memory-mapped.

/// <summary>
/// Mounts a pair of VFS archive files (<c>data.inf</c> + <c>data/data.vfs</c>),
/// parses the Table of Contents once at mount time, and exposes each contained file
/// as a zero-copy <see cref="ReadOnlyMemory{T}"/> slice of the memory-mapped data blob.
/// </summary>
/// <remarks>
/// <para>
/// The <c>data.inf</c> index file is fully read at construction and the file handle is
/// immediately released.  The <c>data/data.vfs</c> data blob is opened as a
/// <see cref="MemoryMappedFile"/> and the handle is kept alive until <see cref="Dispose"/>
/// is called.
/// </para>
/// <para>
/// <strong>Thread safety:</strong> concurrent calls to <see cref="GetFileContent"/> from
/// multiple threads are safe.  The TOC index is read-only after construction.  The
/// underlying OS memory mapping supports concurrent read-shared page access without locks.
/// A single <see cref="MappedMemoryManager"/> wrapping the whole view is created once at open;
/// each <see cref="GetFileContent"/> call merely returns a slice of its
/// <see cref="MemoryManager{T}.Memory"/>, so no per-read allocation pins the view handle and the
/// acquired pointer is released exactly once at <see cref="Dispose"/>.
/// </para>
/// <para>
/// <strong>Lifetime:</strong> every <see cref="ReadOnlyMemory{byte}"/> returned by
/// <see cref="GetFileContent"/> is backed by the memory-mapped view.  Callers must not
/// retain those memory slices after <see cref="Dispose"/> is called.
/// </para>
/// <para>
/// <strong>Empty VFS:</strong> if <c>data/data.vfs</c> is a zero-byte file (e.g. an archive
/// with no entries or only zero-size entries), the memory-mapped file is not created, because
/// the OS does not support mapping a zero-length file.  Any entry whose <c>dataSize</c> is
/// non-zero in such an archive would be an error in the archive itself.
/// </para>
/// </remarks>
public sealed unsafe class MappedVfsArchive : IDisposable
{
    private readonly VfsDirectory _directory;
    private readonly MemoryMappedFile? _mappedFile; // null iff the .vfs is zero bytes
    private readonly MemoryMappedViewAccessor? _viewAccessor; // null iff the .vfs is zero bytes

    // Base pointer into the whole memory-mapped view, acquired ONCE at open via AcquirePointer and
    // released ONCE at Dispose. null iff the .vfs is zero bytes (no view). Each GetFileContent slices
    // this single long-lived acquisition — no per-read AcquirePointer/pin (the historic leak).
    private byte* _viewBasePointer;

    private volatile bool _disposed;

    // Private constructor — use the static factory methods.
    private MappedVfsArchive(
        VfsDirectory directory,
        MemoryMappedFile? mappedFile,
        MemoryMappedViewAccessor? viewAccessor)
    {
        _directory = directory;
        _mappedFile = mappedFile;
        _viewAccessor = viewAccessor;

        if (viewAccessor is not null)
        {
            // Acquire the mapped-view pointer exactly once for the archive's whole lifetime. This pins
            // the OS mapping (increments the SafeMemoryMappedViewHandle ref-count) until Dispose calls
            // ReleasePointer, so the per-entry slices returned by GetFileContent stay valid and the
            // handle's ref-count returns to its baseline on Dispose (enabling a clean unmap).
            viewAccessor.SafeMemoryMappedViewHandle.AcquirePointer(ref _viewBasePointer);
        }
    }

    /// <summary>
    /// Opens and mounts an archive from its two component files.
    /// </summary>
    /// <param name="infPath">
    /// Path to the index file (typically <c>data.inf</c>).
    /// </param>
    /// <param name="vfsPath">
    /// Path to the data blob (typically <c>data/data.vfs</c>).
    /// </param>
    /// <returns>A mounted <see cref="MappedVfsArchive"/> ready for reads.</returns>
    /// <exception cref="FileNotFoundException">
    /// Thrown when either file does not exist.
    /// </exception>
    /// <exception cref="InvalidDataException">
    /// Thrown when <c>data.inf</c> is malformed.
    /// </exception>
    public static MappedVfsArchive Open(string infPath, string vfsPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(infPath);
        ArgumentException.ThrowIfNullOrEmpty(vfsPath);

        // --- Parse data.inf (read once; handle released immediately after) ---
        // spec: Docs/RE/formats/pak.md §"Opening sequence" steps 1-4. CONFIRMED.
        VfsDirectory directory;
        using (var infStream = new FileStream(
                   infPath,
                   FileMode.Open,
                   FileAccess.Read,
                   FileShare.Read,
                   bufferSize: 4096,
                   useAsync: false))
        {
            directory = VfsDirectory.Load(infStream);
        }

        // --- Open data/data.vfs and retain it for the archive's lifetime ---
        // spec: Docs/RE/formats/pak.md §"Opening sequence" step 5: "Open data/data.vfs and retain the
        // handle." CONFIRMED. PORT CHOICE: the original retains an OS file handle for ReadFile-based
        // reads; this port retains a memory-mapped view of the same blob (see the file header note).
        //
        // Edge-case: the OS refuses to memory-map a zero-byte file.  Real .vfs archives are
        // never empty, but synthetic test archives may be.  If the file is zero bytes, skip
        // mapping; any non-empty-content reads will fail at GetFileContent() with a clear
        // diagnostic.
        var vfsInfo = new FileInfo(vfsPath);
        if (!vfsInfo.Exists)
            throw new FileNotFoundException($"VFS data file not found: \"{vfsPath}\".", vfsPath);

        if (vfsInfo.Length == 0)
            return new MappedVfsArchive(directory, mappedFile: null, viewAccessor: null);

        MemoryMappedFile? mmf = null;
        MemoryMappedViewAccessor? view = null;
        try
        {
            mmf = MemoryMappedFile.CreateFromFile(
                vfsPath,
                FileMode.Open,
                mapName: null, // anonymous — no cross-process sharing needed
                capacity: 0, // 0 = use actual file size
                access: MemoryMappedFileAccess.Read);

            view = mmf.CreateViewAccessor(
                offset: 0,
                size: 0, // 0 = map entire file
                access: MemoryMappedFileAccess.Read);
        }
        catch
        {
            // Ensure the MMF is not leaked if view creation fails.
            try
            {
                mmf?.Dispose();
            }
            catch
            {
                /* best-effort */
            }

            throw;
        }

        // Both mmf and view are non-null here (the catch always rethrows).
        return new MappedVfsArchive(directory, mmf!, view!);
    }

    /// <summary>
    /// Number of entries in the archive directory.
    /// </summary>
    public int EntryCount
    {
        get
        {
            ThrowIfDisposed();
            return _directory.EntryCount;
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> if the archive contains an entry whose name matches
    /// <paramref name="virtualPath"/> (case-insensitive).
    /// </summary>
    public bool Contains(string virtualPath)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(virtualPath);
        return _directory.TryFind(NormalizeName(virtualPath)) is not null;
    }

    // Lower-cases the caller path for the lookup, but allocates a new string ONLY when the path is not
    // already all-lower-case (the common case — VFS TOC names are stored lower-case and all in-repo
    // callers build lower-case paths). For an already-lower path the original instance is returned, so
    // the dominant lookup path performs no per-call string allocation.
    // spec: Docs/RE/formats/pak.md §"Lookup algorithm" step 1 — normalize (lower-case) before search.
    private static string NormalizeName(string virtualPath)
    {
        foreach (char c in virtualPath)
        {
            if (char.IsUpper(c))
                return virtualPath.ToLowerInvariant();
        }

        return virtualPath;
    }

    /// <summary>
    /// Returns the raw bytes of the entry identified by <paramref name="virtualPath"/> as a
    /// zero-copy <see cref="ReadOnlyMemory{T}"/> slice of the memory-mapped data blob.
    /// </summary>
    /// <param name="virtualPath">
    /// Virtual path of the entry.  Case-insensitive; will be lower-cased before lookup.
    /// </param>
    /// <returns>
    /// A <see cref="ReadOnlyMemory{byte}"/> backed by the memory-mapped file.
    /// <para>
    /// <strong>No bytes are copied.</strong>  The returned memory refers directly to the
    /// mapped pages of <c>data/data.vfs</c>.  The OS demand-pages the relevant region on
    /// first access.
    /// </para>
    /// </returns>
    /// <exception cref="ObjectDisposedException">The archive has been disposed.</exception>
    /// <exception cref="FileNotFoundException">
    /// No entry with the given name exists in the directory.
    /// </exception>
    /// <exception cref="InvalidDataException">
    /// The entry's <c>dataSize</c> high dword is non-zero (oversized entry not supported).
    /// spec: Docs/RE/formats/pak.md — "Only the low 32 bits are consumed; a non-zero high
    /// dword causes the read to fail." CONFIRMED.
    /// </exception>
    public ReadOnlyMemory<byte> GetFileContent(string virtualPath)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(virtualPath);

        // Step 1: normalize (lower-case). NormalizeName allocates only when the path actually contains
        // an upper-case char; an already-lower path (the common case) is searched with no allocation.
        // spec: Docs/RE/formats/pak.md §"Lookup algorithm" step 1. CONFIRMED.
        string normalized = NormalizeName(virtualPath);

        // Step 2: binary search.
        // spec: Docs/RE/formats/pak.md §"Lookup algorithm" step 2. CONFIRMED.
        VfsEntry? entry = _directory.TryFind(normalized);
        if (entry is null)
            throw new FileNotFoundException(
                $"VFS entry not found: \"{virtualPath}\".", virtualPath);

        VfsEntry e = entry.Value;

        // Validate that the high dword of dataSize is zero.
        // spec: Docs/RE/formats/pak.md — dataSize high dword must be 0. CONFIRMED.
        if ((e.DataSize >> 32) != 0)
            throw new InvalidDataException(
                $"VFS entry \"{e.Name}\": dataSize high dword is non-zero " +
                $"(value=0x{e.DataSize:X16}). Entry too large for this implementation.");

        int length = (int)(e.DataSize & 0xFFFF_FFFF);

        if (length == 0)
            return ReadOnlyMemory<byte>.Empty;

        // The base pointer is non-null whenever a view exists. If it is null here the .vfs file is
        // zero bytes but the entry claims to have data — a corrupted/invalid archive; surface a clear
        // error.
        if (_viewBasePointer is null)
            throw new InvalidDataException(
                $"VFS entry \"{e.Name}\" has dataSize={length} but data/data.vfs is empty.");

        // Steps 3-4: return a zero-copy slice over the single, long-lived mapped-view pointer acquired
        // at open. PORT CHOICE: where the original seeks+ReadFiles into a fresh heap buffer (see the
        // file header note + pak.md §"ReadFile, not a memory-mapped view"), this port returns a
        // ReadOnlyMemory addressing the mapped region directly at the entry's dataOffset.
        // spec: Docs/RE/formats/pak.md §"Lookup algorithm" steps 3-4 (binary search → locate payload).
        // The per-entry MappedMemoryManager does NOT pin the view handle; only the archive does, once.
        // NOTE: a MemoryManager<byte> must be a reference type (it backs Memory<T>), so one instance is
        // allocated per call. It is tiny and does not pin/leak. Caching managers per entry to remove this
        // allocation is DEFERRED: it would add a keyed cache + locking that would complicate the
        // documented lock-free concurrent-read contract for negligible benefit.
        var manager = new MappedMemoryManager(_viewBasePointer, e.DataOffset, length);
        return manager.Memory;
    }

    /// <summary>
    /// Returns a span over all directory entries in sorted order.
    /// Intended for diagnostics and testing; not a hot path.
    /// </summary>
    public ReadOnlySpan<VfsEntry> GetEntries()
    {
        ThrowIfDisposed();
        return _directory.Entries;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Release the single pointer acquired at open (balances the one AcquirePointer in the ctor),
        // then the view accessor, then the mapped file. Releasing the pointer first returns the view
        // handle's ref-count to its baseline so the accessor can actually unmap.
        if (_viewBasePointer is not null)
        {
            _viewAccessor!.SafeMemoryMappedViewHandle.ReleasePointer();
            _viewBasePointer = null;
        }

        _viewAccessor?.Dispose();
        _mappedFile?.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(MappedVfsArchive));
    }
}