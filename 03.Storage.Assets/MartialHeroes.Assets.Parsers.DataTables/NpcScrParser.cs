using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers.DataTables.Models;

namespace MartialHeroes.Assets.Parsers.DataTables;

public static class NpcScrParser
{
    private const int RecordStride = 404;

    private const int Paragraph0Offset = 0x014;
    private const int Paragraph0Width = 64;

    private const int
        Paragraph1Offset = 0x054;

    private const int Paragraph1Width = 64;

    private const int
        Paragraph2Offset = 0x094;

    private const int Paragraph2Width = 64;

    static NpcScrParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static NpcScrRecord[] Parse(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;

        if (span.Length % RecordStride != 0)
            throw new InvalidDataException(
                $"npc.scr parse error: buffer length {span.Length} is not a multiple of " +
                $"stride {RecordStride} (NPC_SCR_RECORD_BYTES). " +
                "spec: Docs/RE/formats/config_tables.md §2.17.3.");

        var count = span.Length / RecordStride;
        var results = new NpcScrRecord[count];
        var cp949 = Encoding.GetEncoding(949);

        for (var i = 0; i < count; i++)
        {
            var recBase = i * RecordStride;
            var rec = span.Slice(recBase, RecordStride);

            var id = BinaryPrimitives.ReadUInt32LittleEndian(rec[..]);

            var idMirror = BinaryPrimitives.ReadUInt32LittleEndian(rec[0x004..]);


            var para0 = ReadNullTerminatedCp949(rec.Slice(Paragraph0Offset, Paragraph0Width), cp949);

            var para1 = ReadNullTerminatedCp949(rec.Slice(Paragraph1Offset, Paragraph1Width), cp949);

            var para2 = ReadNullTerminatedCp949(rec.Slice(Paragraph2Offset, Paragraph2Width), cp949);

            results[i] = new NpcScrRecord
            {
                Id = id,
                IdMirror = idMirror,
                Paragraph0 = para0,
                Paragraph1 = para1,
                Paragraph2 = para2,
                Raw = data.Slice(recBase, RecordStride)
            };
        }

        return results;
    }


    private static string ReadNullTerminatedCp949(ReadOnlySpan<byte> field, Encoding cp949)
    {
        var len = field.IndexOf((byte)0);
        if (len < 0) len = field.Length;
        var ccPos = field[..len].IndexOf((byte)0xCC);
        if (ccPos >= 0) len = ccPos;
        if (len == 0) return string.Empty;
        return cp949.GetString(field[..len]);
    }
}