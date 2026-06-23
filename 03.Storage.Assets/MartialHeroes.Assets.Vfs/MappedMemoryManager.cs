using System.Buffers;

namespace MartialHeroes.Assets.Vfs;

internal sealed unsafe class MappedMemoryManager : MemoryManager<byte>
{
    private readonly int _length;
    private readonly byte* _slicePointer;

    internal MappedMemoryManager(byte* basePointer, long offset, int length)
    {
        if (basePointer is null) throw new ArgumentNullException(nameof(basePointer));
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));

        _slicePointer = basePointer + offset;
        _length = length;
    }

    public override Span<byte> GetSpan()
    {
        return new Span<byte>(_slicePointer, _length);
    }

    public override MemoryHandle Pin(int elementIndex = 0)
    {
        if ((uint)elementIndex > (uint)_length)
            throw new ArgumentOutOfRangeException(nameof(elementIndex));
        return new MemoryHandle(_slicePointer + elementIndex);
    }

    public override void Unpin()
    {
    }

    protected override void Dispose(bool disposing)
    {
    }
}