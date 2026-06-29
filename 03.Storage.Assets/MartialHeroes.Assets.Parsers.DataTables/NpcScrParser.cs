using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers.DataTables.Models;

namespace MartialHeroes.Assets.Parsers.DataTables;

public static class NpcScrParser
{
    private const int RecordStride = 404;

    private const int NameSlotCount = 6;
    private const int NameSlotWidth = 64;
    private const int NameSlot0Offset = 0x014;

    private const int KindOffset = 0x022;
    private const int JobOffset = 0x034;

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
                $"stride {RecordStride}.");

        var count = span.Length / RecordStride;
        var results = new NpcScrRecord[count];
        var cp949 = Encoding.GetEncoding(949);

        for (var i = 0; i < count; i++)
        {
            var recBase = i * RecordStride;
            var rec = span.Slice(recBase, RecordStride);

            var id = BinaryPrimitives.ReadUInt32LittleEndian(rec[..]);

            var kind = rec[KindOffset];

            var job = BinaryPrimitives.ReadInt16LittleEndian(rec[JobOffset..]);

            var nameSlots = new string[NameSlotCount];
            for (var s = 0; s < NameSlotCount; s++)
            {
                var slotBase = NameSlot0Offset + s * NameSlotWidth;
                nameSlots[s] = ReadNameSlot(rec.Slice(slotBase, NameSlotWidth), cp949);
            }

            results[i] = new NpcScrRecord
            {
                Id = id,
                Kind = kind,
                Job = job,
                NameSlots = nameSlots,
                Raw = data.Slice(recBase, RecordStride)
            };
        }

        return results;
    }


    private static string ReadNameSlot(ReadOnlySpan<byte> field, Encoding cp949)
    {
        if (field.Length > 0 && field[0] == (byte)'0')
            return string.Empty;

        var len = field.IndexOf((byte)0);
        if (len < 0) len = field.Length;
        if (len == 0) return string.Empty;
        return cp949.GetString(field[..len]);
    }
}