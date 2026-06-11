using System.Buffers;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MartialHeroes.Assets.Vfs;

/// <summary>
/// A <see cref="MemoryManager{T}"/> that exposes a slice of a
/// <see cref="MemoryMappedViewAccessor"/>'s unmanaged backing store as a
/// <see cref="ReadOnlyMemory{T}"/> without copying any bytes.
/// </summary>
/// <remarks>
/// Lifetime contract: the <see cref="MemoryMappedViewAccessor"/> passed to the constructor
/// must remain open for the entire lifetime of this object.  The owning
/// <see cref="MappedVfsArchive"/> guarantees this by retaining the accessor until it is
/// disposed, at which point it throws <see cref="ObjectDisposedException"/> on any new call to
/// <see cref="MappedVfsArchive.GetFileContent"/>.  Existing <see cref="ReadOnlyMemory{byte}"/>
/// instances derived from this manager become invalid after the archive is disposed — callers
/// must consume the memory before disposing the archive.
///
/// Thread safety: <see cref="GetSpan"/> returns a pointer into a memory-mapped region that
/// supports concurrent reads from multiple threads without locks (the OS provides read-shared
/// page mapping).  No mutable state is touched after construction.
/// </remarks>
internal sealed unsafe class MappedMemoryManager : MemoryManager<byte>
{
    private readonly SafeMemoryMappedViewHandle _handle;
    private readonly long _offset; // byte offset within the mapped view
    private readonly int _length; // byte count for this slice

    private byte* _basePointer; // acquired via AcquirePointer; released in Dispose
    private bool _disposed;

    internal MappedMemoryManager(
        MemoryMappedViewAccessor accessor,
        long offset,
        int length)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));

        _handle = accessor.SafeMemoryMappedViewHandle;
        _offset = offset;
        _length = length;

        // Pin: AcquirePointer increments the SafeHandle ref-count so the OS mapping
        // cannot be released under us while the pointer is live.
        _handle.AcquirePointer(ref _basePointer);
    }

    /// <inheritdoc/>
    public override Span<byte> GetSpan()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new Span<byte>(_basePointer + _offset, _length);
    }

    /// <inheritdoc/>
    public override MemoryHandle Pin(int elementIndex = 0)
    {
        // The memory is already pinned (native / unmanaged mapping); no GC involvement.
        if ((uint)elementIndex > (uint)_length)
            throw new ArgumentOutOfRangeException(nameof(elementIndex));
        return new MemoryHandle(_basePointer + _offset + elementIndex);
    }

    /// <inheritdoc/>
    public override void Unpin()
    {
        // Nothing to do — the memory is unmanaged; GC does not move it.
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (_disposed) return;
        _disposed = true;
        if (_basePointer != null)
        {
            _handle.ReleasePointer();
            _basePointer = null;
        }
    }
}