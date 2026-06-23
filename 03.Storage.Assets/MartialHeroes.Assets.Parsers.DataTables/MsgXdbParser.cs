using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers.DataTables.Models;

namespace MartialHeroes.Assets.Parsers.DataTables;

public static class MsgXdbParser
{
    private const int RecordStride = 516;

    private const int CaptionIdOffset = 0;

    private const int TextOffset = 4;
    private const int TextLength = 512;

    static MsgXdbParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static MsgXdbCatalog Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span);
    }

    public static MsgXdbCatalog Parse(ReadOnlySpan<byte> span)
    {
        if (span.Length % RecordStride != 0)
            throw new InvalidDataException(
                $"msg.xdb parse error: buffer length {span.Length} is not an exact multiple of " +
                $"the 516-byte record stride. " +
                $"spec: Docs/RE/formats/msg_xdb.md §File layout.");

        var count = span.Length / RecordStride;

        var records = new MsgXdbRecord[count];

        var cp949 = Encoding.GetEncoding(949);

        for (var i = 0; i < count; i++)
        {
            var rec = span.Slice(i * RecordStride, RecordStride);

            var captionId = BinaryPrimitives.ReadInt32LittleEndian(rec.Slice(CaptionIdOffset, 4));

            var textBuf = rec.Slice(TextOffset, TextLength);
            var nulPos = textBuf.IndexOf((byte)0);
            var text = nulPos switch
            {
                0 => string.Empty,
                < 0 => cp949.GetString(textBuf),
                _ => cp949.GetString(textBuf[..nulPos])
            };

            records[i] = new MsgXdbRecord(captionId, text);
        }

        return new MsgXdbCatalog(records);
    }
}