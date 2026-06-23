using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers.Texture.Models;

namespace MartialHeroes.Assets.Parsers.Texture;

public static class BgtextureLstParser
{
    private const int HeaderSize = 4;

    private const int RecordStride = 48;

    private const int RelPathFieldLen = 47;

    static BgtextureLstParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static BgtextureLstCatalog Parse(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;

        if (span.Length < HeaderSize)
            throw new InvalidDataException(
                $"bgtexture.lst parse error: buffer length {span.Length} is shorter than the " +
                $"4-byte header. spec: Docs/RE/formats/bgtexture_lst.md §Header layout.");

        var recordCount = BinaryPrimitives.ReadUInt32LittleEndian(span[..]);

        const uint MaxRecordCountExclusive = 2000;
        if (recordCount == 0 || recordCount >= MaxRecordCountExclusive)
            throw new InvalidDataException(
                $"bgtexture.lst parse error: record_count={recordCount} is outside the loader's valid " +
                $"range [1, {MaxRecordCountExclusive}) (upper bound 0x7D0). " +
                "spec: Docs/RE/formats/bgtexture_lst.md §Header layout — count validation (loader-enforced).");

        var expectedBodyLen = (long)recordCount * RecordStride;
        var expectedTotalLen = HeaderSize + expectedBodyLen;

        if (span.Length < expectedTotalLen)
            throw new InvalidDataException(
                $"bgtexture.lst parse error: record_count={recordCount} requires " +
                $"{expectedTotalLen} bytes (4 + {recordCount} × {RecordStride}) " +
                $"but buffer is only {span.Length} bytes. " +
                "spec: Docs/RE/formats/bgtexture_lst.md §Record / body layout — size formula: CONFIRMED.");

        var bodyLen = span.Length - HeaderSize;
        if (bodyLen % RecordStride != 0)
            throw new InvalidDataException(
                $"bgtexture.lst parse error: body length {bodyLen} is not an exact multiple " +
                $"of stride {RecordStride}. " +
                "spec: Docs/RE/formats/bgtexture_lst.md §Record / body layout — stride 48 bytes: CONFIRMED.");

        var count = (int)recordCount;

        var cp949 = Encoding.GetEncoding(949);
        var records = new BgtextureLstRecord[count];

        for (var i = 0; i < count; i++)
        {
            var recBase = HeaderSize + i * RecordStride;
            var rec = span.Slice(recBase, RecordStride);

            var kind = rec[0];

            var relPathSpan = rec.Slice(1, RelPathFieldLen);
            var nulPos = relPathSpan.IndexOf((byte)0);
            var pathLen = nulPos < 0 ? RelPathFieldLen : nulPos;
            var relPath = pathLen == 0 ? string.Empty : cp949.GetString(relPathSpan[..pathLen]);

            records[i] = new BgtextureLstRecord
            {
                Index = i,
                KindRaw = kind,
                RelPath = relPath
            };
        }

        return new BgtextureLstCatalog(records);
    }
}