using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.Effects.Models;

namespace MartialHeroes.Assets.Parsers.Effects;

internal static class LstNameReader
{
    private const int HeaderSize = 4;

    public static EffectNameManifest Read(ReadOnlySpan<byte> span, int recordStride, string fileLabel)
    {
        if (span.Length < HeaderSize)
            throw new InvalidDataException(
                $"{fileLabel} parse error: buffer length {span.Length} is shorter than the 4-byte count header.");

        var entryCount = BinaryPrimitives.ReadUInt32LittleEndian(span);

        var expectedTotal = HeaderSize + entryCount * recordStride;
        if (expectedTotal > span.Length)
            throw new InvalidDataException(
                $"{fileLabel} parse error: entry_count={entryCount} requires {expectedTotal} bytes " +
                $"(4 + {entryCount} × {recordStride}) but buffer is only {span.Length}.");

        var count = (int)entryCount;
        var encoding = Cp949Encoding.Instance;
        var entries = new EffectNameEntry[count];

        for (var i = 0; i < count; i++)
        {
            var record = span.Slice(HeaderSize + i * recordStride, recordStride);
            var nul = record.IndexOf((byte)0);
            var len = nul < 0 ? recordStride : nul;
            var name = len == 0 ? string.Empty : encoding.GetString(record[..len]);
            entries[i] = new EffectNameEntry { Index = i, Name = name };
        }

        return new EffectNameManifest(entries);
    }
}

public static class XeffectLstParser
{
    private const int RecordStride = 30;

    public static EffectNameManifest Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span);
    }

    public static EffectNameManifest Parse(ReadOnlySpan<byte> span)
    {
        return LstNameReader.Read(span, RecordStride, "xeffect.lst");
    }
}

public static class BmplistLstParser
{
    private const int RecordStride = 30;

    public static EffectNameManifest Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span);
    }

    public static EffectNameManifest Parse(ReadOnlySpan<byte> span)
    {
        return LstNameReader.Read(span, RecordStride, "bmplist.lst");
    }
}

public static class XobjLstParser
{
    private const int RecordStride = 34;

    public static EffectNameManifest Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span);
    }

    public static EffectNameManifest Parse(ReadOnlySpan<byte> span)
    {
        return LstNameReader.Read(span, RecordStride, "xobj.lst");
    }
}