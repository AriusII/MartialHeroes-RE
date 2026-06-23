using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers.DataTables.Models;

namespace MartialHeroes.Assets.Parsers.DataTables;

public static class CitemsParser
{
    private const int RecordStride = 1052;


    private const int OffItemId = 0x00;

    private const int OffItemName = 0x04;
    private const int ItemNameLen = 48;

    private const int OffUnknown36 = 0x36;

    private const int OffCashPriceNx = 0x38;

    private const int OffSlotSeq2 = 0x3C;

    private const int OffItemUid = 0x48;

    private const int OffFlag4C = 0x4C;

    private const int OffDescBlock = 0x0E4;
    private const int DescParaWidth = 81;
    private const int DescParaCount = 10;

    private const int OffRemainder = 0x40E;
    private const int RemainderLen = 14;

    public static CitemsCatalog Parse(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;

        if (span.Length % RecordStride != 0)
            throw new InvalidDataException(
                $"citems.scr parse error: buffer length {span.Length} is not an exact multiple of " +
                $"stride {RecordStride}. " +
                "spec: Docs/RE/formats/items_scr.md §2.3.");

        var count = span.Length / RecordStride;

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp949 = Encoding.GetEncoding(949);

        var records = new CitemsRecord[count];

        for (var i = 0; i < count; i++)
        {
            var recBase = i * RecordStride;
            var rec = span.Slice(recBase, RecordStride);

            var itemId = BinaryPrimitives.ReadUInt32LittleEndian(rec[..]);

            var nameBytes = rec.Slice(OffItemName, ItemNameLen);
            var nameNul = nameBytes.IndexOf((byte)0);
            var itemName = nameNul >= 0
                ? cp949.GetString(nameBytes[..nameNul])
                : cp949.GetString(nameBytes);

            var unknown36 = BinaryPrimitives.ReadUInt16LittleEndian(rec[OffUnknown36..]);

            var cashPriceNx = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffCashPriceNx..]);

            var slotSeq2 = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffSlotSeq2..]);

            var itemUid = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffItemUid..]);

            var flag4C = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffFlag4C..]);

            var descParaList = new List<string>(DescParaCount);
            for (var p = 0; p < DescParaCount; p++)
            {
                var paraOff = OffDescBlock + p * DescParaWidth;
                var paraBytes = rec.Slice(paraOff, DescParaWidth);
                if (paraBytes[0] == 0x23)
                    break;
                var paraNul = paraBytes.IndexOf((byte)0);
                descParaList.Add(paraNul >= 0
                    ? cp949.GetString(paraBytes[..paraNul])
                    : cp949.GetString(paraBytes));
            }

            var descParagraphs = descParaList.ToArray();

            var remainderRaw = data.Slice(recBase + OffRemainder, RemainderLen);

            records[i] = new CitemsRecord
            {
                ItemId = itemId,
                ItemName = itemName,
                Unknown36 = unknown36,
                CashPriceNx = cashPriceNx,
                SlotSeq2 = slotSeq2,
                ItemUid = itemUid,
                Flag4C = flag4C,
                DescParagraphs = descParagraphs,
                RemainderRaw = remainderRaw
            };
        }

        return new CitemsCatalog(records);
    }
}