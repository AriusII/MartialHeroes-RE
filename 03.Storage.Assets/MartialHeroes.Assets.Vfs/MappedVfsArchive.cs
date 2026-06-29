using System.IO.MemoryMappedFiles;

namespace MartialHeroes.Assets.Vfs;

public sealed unsafe class MappedVfsArchive : IDisposable
{
    private const long MinDataOffset = 24;

    private readonly VfsDirectory _directory;
    private readonly MemoryMappedFile? _mappedFile;
    private readonly MemoryMappedViewAccessor? _viewAccessor;
    private readonly long _viewLength;

    private volatile bool _disposed;

    private byte* _viewBasePointer;

    private MappedVfsArchive(
        VfsDirectory directory,
        MemoryMappedFile? mappedFile,
        MemoryMappedViewAccessor? viewAccessor,
        long viewLength)
    {
        _directory = directory;
        _mappedFile = mappedFile;
        _viewAccessor = viewAccessor;
        _viewLength = viewLength;

        if (viewAccessor is not null)
            viewAccessor.SafeMemoryMappedViewHandle.AcquirePointer(ref _viewBasePointer);
    }

    public int EntryCount
    {
        get
        {
            ThrowIfDisposed();
            return _directory.EntryCount;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_viewBasePointer is not null)
        {
            _viewAccessor!.SafeMemoryMappedViewHandle.ReleasePointer();
            _viewBasePointer = null;
        }

        _viewAccessor?.Dispose();
        _mappedFile?.Dispose();
    }

    public static MappedVfsArchive Open(string infPath, string vfsPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(infPath);
        ArgumentException.ThrowIfNullOrEmpty(vfsPath);

        VfsDirectory directory;
        using (var infStream = new FileStream(
                   infPath,
                   FileMode.Open,
                   FileAccess.Read,
                   FileShare.Read,
                   4096,
                   false))
        {
            directory = VfsDirectory.Load(infStream);
        }

        var vfsInfo = new FileInfo(vfsPath);
        if (!vfsInfo.Exists)
            throw new FileNotFoundException($"VFS data file not found: \"{vfsPath}\".", vfsPath);

        if (vfsInfo.Length == 0)
            return new MappedVfsArchive(directory, null, null, 0);

        MemoryMappedFile? mmf = null;
        MemoryMappedViewAccessor? view;
        try
        {
            mmf = MemoryMappedFile.CreateFromFile(
                vfsPath,
                FileMode.Open,
                null,
                0,
                MemoryMappedFileAccess.Read);

            view = mmf.CreateViewAccessor(
                0,
                0,
                MemoryMappedFileAccess.Read);
        }
        catch
        {
            try
            {
                mmf?.Dispose();
            }
            catch
            {
            }

            throw;
        }

        return new MappedVfsArchive(directory, mmf!, view!, vfsInfo.Length);
    }

    public bool Contains(string virtualPath)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(virtualPath);
        return _directory.TryFind(NormalizeName(virtualPath)) is not null;
    }

    private static string NormalizeName(string virtualPath)
    {
        foreach (var c in virtualPath)
            if (char.IsUpper(c))
                return virtualPath.ToLowerInvariant();

        return virtualPath;
    }

    public ReadOnlyMemory<byte> GetFileContent(string virtualPath)
    {
        var resolved = Resolve(virtualPath);
        if (resolved.Length == 0)
            return ReadOnlyMemory<byte>.Empty;

        var manager = new MappedMemoryManager(_viewBasePointer, resolved.Offset, resolved.Length);
        return manager.Memory;
    }

    public ReadOnlySpan<byte> GetFileSpan(string virtualPath)
    {
        var resolved = Resolve(virtualPath);
        if (resolved.Length == 0)
            return ReadOnlySpan<byte>.Empty;

        return new ReadOnlySpan<byte>(_viewBasePointer + resolved.Offset, resolved.Length);
    }

    private (long Offset, int Length) Resolve(string virtualPath)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(virtualPath);

        var normalized = NormalizeName(virtualPath);

        var entry = _directory.TryFind(normalized);
        if (entry is null)
            throw new FileNotFoundException(
                $"VFS entry not found: \"{virtualPath}\".", virtualPath);

        var e = entry.Value;

        if (e.DataSize >> 32 != 0)
            throw new InvalidDataException(
                $"VFS entry \"{e.Name}\": dataSize high dword is non-zero " +
                $"(value=0x{e.DataSize:X16}). Entry too large for this implementation.");

        var low = e.DataSize & 0xFFFF_FFFF;
        if (low > int.MaxValue)
            throw new InvalidDataException(
                $"VFS entry \"{e.Name}\": dataSize {low} exceeds 2 GiB; " +
                $"not representable as a single ReadOnlyMemory<byte>.");

        var length = (int)low;

        if (length == 0)
            return (e.DataOffset, 0);

        if (_viewBasePointer is null)
            throw new InvalidDataException(
                $"VFS entry \"{e.Name}\" has dataSize={length} but data/data.vfs is empty.");

        if (e.DataOffset < MinDataOffset
            || e.DataOffset > _viewLength
            || length > _viewLength - e.DataOffset)
            throw new InvalidDataException(
                $"VFS entry \"{e.Name}\" payload range [{e.DataOffset}, " +
                $"{e.DataOffset + length}) lies outside the mapped data.vfs length {_viewLength}.");

        return (e.DataOffset, length);
    }

    public ReadOnlySpan<VfsEntry> GetEntries()
    {
        ThrowIfDisposed();
        return _directory.Entries;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(MappedVfsArchive));
    }
}