using System.Buffers.Binary;

namespace MartialHeroes.Assets.Vfs;

internal sealed class VfsDirectory
{
    private const int HeaderSize = 24;
    private const int EntryCountOffset = 12;

    private readonly VfsEntry[] _entries;

    private VfsDirectory(VfsEntry[] entries)
    {
        _entries = entries;
    }

    public int EntryCount => _entries.Length;

    public ReadOnlySpan<VfsEntry> Entries => _entries.AsSpan();

    public static VfsDirectory Load(Stream stream)
    {
        Span<byte> header = stackalloc byte[HeaderSize];
        ReadExact(stream, header);


        var entryCount = BinaryPrimitives.ReadInt32LittleEndian(header[EntryCountOffset..]);

        if (entryCount <= 0)
            return new VfsDirectory([]);

        var tocByteCount = checked(entryCount * VfsEntry.RecordSize);
        var toc = new byte[tocByteCount];
        ReadExact(stream, toc);

        var entries = new VfsEntry[entryCount];
        for (var i = 0; i < entryCount; i++)
        {
            var recordStart = i * VfsEntry.RecordSize;
            entries[i] = VfsEntry.Parse(toc.AsSpan(recordStart, VfsEntry.RecordSize));
        }

        Array.Sort(entries);

        return new VfsDirectory(entries);
    }

    public VfsEntry? TryFind(string normalizedName)
    {
        var lo = 0;
        var hi = _entries.Length - 1;
        while (lo <= hi)
        {
            var mid = (int)(((uint)lo + (uint)hi) >> 1);
            var cmp = string.CompareOrdinal(_entries[mid].Name, normalizedName);
            if (cmp == 0) return _entries[mid];
            if (cmp < 0) lo = mid + 1;
            else hi = mid - 1;
        }

        return null;
    }

    private static void ReadExact(Stream stream, Span<byte> buffer)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var n = stream.Read(buffer[totalRead..]);
            if (n == 0)
                throw new InvalidDataException(
                    $"Unexpected end of stream while reading data.inf " +
                    $"(needed {buffer.Length} bytes, got {totalRead}).");
            totalRead += n;
        }
    }
}