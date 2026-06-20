using System.Buffers;

namespace MartialHeroes.Assets.Vfs;

/// <summary>
///     A <see cref="MemoryManager{T}" /> that exposes a slice of an already-acquired memory-mapped region
///     as a <see cref="ReadOnlyMemory{T}" /> without copying any bytes and without itself touching the view
///     handle's ref-count.
/// </summary>
/// <remarks>
///     <para>
///         <strong>Pointer ownership.</strong> This manager does <em>not</em> acquire or release the view
///         handle. The owning <see cref="MappedVfsArchive" /> calls
///         <c>SafeMemoryMappedViewHandle.AcquirePointer</c> exactly once at open (pinning the OS mapping for
///         the archive's whole lifetime) and <c>ReleasePointer</c> exactly once at dispose. Each per-entry
///         manager merely wraps <c>basePointer + offset</c> for <paramref name="length" /> bytes. This is the
///         fix for the historic leak where a fresh manager acquired the view-handle pointer on every
///         <see cref="MappedVfsArchive.GetFileContent" /> call and was never disposed, permanently incrementing
///         the handle ref-count and preventing a clean unmap.
///     </para>
///     <para>
///         <strong>Lifetime contract.</strong> The base pointer passed to the constructor must remain valid
///         (the owning archive's view handle acquired and not yet released) for this object's whole lifetime.
///         Slices derived from it become invalid once the archive is disposed — callers must consume the
///         memory before disposing the archive (documented on <see cref="MappedVfsArchive.GetFileContent" />).
///     </para>
///     <para>
///         A <c>long</c> offset is used so entries anywhere in a multi-gigabyte data blob remain addressable:
///         the slice length itself is bounded by <see cref="int.MaxValue" /> (an entry's <c>dataSize</c> is a
///         low-32-bit field per the format), but the base offset can exceed the 31-bit range.
///     </para>
///     <para>
///         Thread safety: <see cref="GetSpan" /> returns a pointer into a read-shared memory-mapped region that
///         supports concurrent reads from multiple threads without locks. No mutable state is touched after
///         construction.
///     </para>
/// </remarks>
internal sealed unsafe class MappedMemoryManager : MemoryManager<byte>
{
    private readonly int _length; // byte count for this slice
    private readonly byte* _slicePointer; // basePointer + offset

    /// <summary>
    ///     Wraps <paramref name="length" /> bytes starting at <paramref name="offset" /> within the mapped
    ///     region addressed by <paramref name="basePointer" />. The pointer is owned by the caller
    ///     (<see cref="MappedVfsArchive" />); this object never acquires or releases it.
    /// </summary>
    internal MappedMemoryManager(byte* basePointer, long offset, int length)
    {
        if (basePointer is null) throw new ArgumentNullException(nameof(basePointer));
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));

        _slicePointer = basePointer + offset;
        _length = length;
    }

    /// <inheritdoc />
    public override Span<byte> GetSpan()
    {
        return new Span<byte>(_slicePointer, _length);
    }

    /// <inheritdoc />
    public override MemoryHandle Pin(int elementIndex = 0)
    {
        // The memory is already pinned (native / unmanaged mapping); no GC involvement.
        if ((uint)elementIndex > (uint)_length)
            throw new ArgumentOutOfRangeException(nameof(elementIndex));
        return new MemoryHandle(_slicePointer + elementIndex);
    }

    /// <inheritdoc />
    public override void Unpin()
    {
        // Nothing to do — the memory is unmanaged; GC does not move it.
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        // Nothing to release — this manager does not own the view-handle pointer (see remarks).
    }
}