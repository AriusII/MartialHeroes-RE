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
/// The only serialised resource is the single <see cref="MemoryMappedViewAccessor"/> used
/// to derive the base pointer; pointer acquisition is performed once at construction and
/// individual per-entry <see cref="MappedMemoryManager"/> instances are constructed without
/// a lock.
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
public sealed class MappedVfsArchive : IDisposable
{
    private readonly VfsDirectory              _directory;
    private readonly MemoryMappedFile?         _mappedFile;     // null iff the .vfs is zero bytes
    private readonly MemoryMappedViewAccessor? _viewAccessor;   // null iff the .vfs is zero bytes
    private volatile bool                      _disposed;

    // Private constructor — use the static factory methods.
    private MappedVfsArchive(
        VfsDirectory directory,
        MemoryMappedFile? mappedFile,
        MemoryMappedViewAccessor? viewAccessor)
    {
        _directory    = directory;
        _mappedFile   = mappedFile;
        _viewAccessor = viewAccessor;
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

        // --- Open data/data.vfs as a memory-mapped file ---
        // spec: Docs/RE/formats/pak.md §"Opening sequence" step 5: "Open data/data.vfs
        // and retain the handle."  CONFIRMED.
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
            mmf  = MemoryMappedFile.CreateFromFile(
                vfsPath,
                FileMode.Open,
                mapName: null,          // anonymous — no cross-process sharing needed
                capacity: 0,            // 0 = use actual file size
                access: MemoryMappedFileAccess.Read);

            view = mmf.CreateViewAccessor(
                offset: 0,
                size:   0,              // 0 = map entire file
                access: MemoryMappedFileAccess.Read);
        }
        catch
        {
            // Ensure the MMF is not leaked if view creation fails.
            try { mmf?.Dispose(); } catch { /* best-effort */ }
            throw;
        }

        // Both mmf and view are non-null here (the catch always rethrows).
        return new MappedVfsArchive(directory, mmf!, view!);
    }

    /// <summary>
    /// Opens and mounts an archive whose <c>data.inf</c> content is provided as a stream
    /// and whose <c>data/data.vfs</c> is a path on disk.
    /// Intended for internal/test use where the inf bytes are already in memory.
    /// </summary>
    /// <param name="infStream">
    /// Readable stream positioned at the start of the <c>data.inf</c> content.
    /// Read sequentially; not disposed by this method.
    /// </param>
    /// <param name="vfsPath">
    /// Path to the data blob file that will be memory-mapped.
    /// </param>
    internal static MappedVfsArchive OpenFromStreams(Stream infStream, string vfsPath)
    {
        var directory = VfsDirectory.Load(infStream);

        var vfsInfo = new FileInfo(vfsPath);
        if (!vfsInfo.Exists)
            throw new FileNotFoundException($"VFS data file not found: \"{vfsPath}\".", vfsPath);

        if (vfsInfo.Length == 0)
            return new MappedVfsArchive(directory, mappedFile: null, viewAccessor: null);

        var mmf = MemoryMappedFile.CreateFromFile(
            vfsPath,
            FileMode.Open,
            mapName: null,
            capacity: 0,
            access: MemoryMappedFileAccess.Read);

        MemoryMappedViewAccessor view;
        try
        {
            view = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        }
        catch
        {
            mmf.Dispose();
            throw;
        }

        return new MappedVfsArchive(directory, mmf, view);
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
        return _directory.TryFind(virtualPath.ToLowerInvariant()) is not null;
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

        // Step 1: normalize (lower-case).
        // spec: Docs/RE/formats/pak.md §"Lookup algorithm" step 1. CONFIRMED.
        string normalized = virtualPath.ToLowerInvariant();

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

        // The view accessor is non-null whenever any entry has non-zero dataSize.
        // If it is null here the .vfs file is zero bytes but the entry claims to have data —
        // that is a corrupted/invalid archive; surface a clear error.
        if (_viewAccessor is null)
            throw new InvalidDataException(
                $"VFS entry \"{e.Name}\" has dataSize={length} but data/data.vfs is empty.");

        // Steps 3-4: return a zero-copy slice backed by the memory-mapped view.
        // spec: Docs/RE/formats/pak.md §"Lookup algorithm" steps 3-4. CONFIRMED.
        // No seek+read: we construct a MappedMemoryManager whose GetSpan() returns a pointer
        // directly into the mapped region at the entry's dataOffset.
        var manager = new MappedMemoryManager(_viewAccessor, e.DataOffset, length);
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

        // Release the view accessor first, then the mapped file.
        _viewAccessor?.Dispose();
        _mappedFile?.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(MappedVfsArchive));
    }
}
