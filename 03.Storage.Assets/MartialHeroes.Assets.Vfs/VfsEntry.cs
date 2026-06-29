using System.Buffers.Binary;
using System.Text;

namespace MartialHeroes.Assets.Vfs;

public readonly struct VfsEntry : IComparable<VfsEntry>
{
    internal const int RecordSize = 144;
    internal const int NameCapacity = 100;
    private const int DataOffsetField = 104;
    private const int DataSizeField = 112;

    public readonly string Name;

    public readonly long DataOffset;

    public readonly long DataSize;

    private VfsEntry(string name, long dataOffset, long dataSize)
    {
        Name = name;
        DataOffset = dataOffset;
        DataSize = dataSize;
    }

    internal static VfsEntry Parse(ReadOnlySpan<byte> raw)
    {
        if (raw.Length < RecordSize)
            throw new ArgumentException(
                $"TOC record too short: expected {RecordSize}, got {raw.Length}.",
                nameof(raw));

        var nameSpan = raw[..NameCapacity];
        var nameLen = nameSpan.IndexOf((byte)0);
        if (nameLen < 0) nameLen = NameCapacity;
        var name = Encoding.Latin1.GetString(nameSpan[..nameLen]);
        name = name.ToLowerInvariant();


        var dataOffset = BinaryPrimitives.ReadInt64LittleEndian(raw[DataOffsetField..]);

        var dataSize = BinaryPrimitives.ReadInt64LittleEndian(raw[DataSizeField..]);


        return new VfsEntry(name, dataOffset, dataSize);
    }

    public int CompareTo(VfsEntry other)
    {
        return string.CompareOrdinal(Name, other.Name);
    }

    public override string ToString()
    {
        return $"VfsEntry {{ Name=\"{Name}\", DataOffset={DataOffset}, DataSize={DataSize} }}";
    }
}