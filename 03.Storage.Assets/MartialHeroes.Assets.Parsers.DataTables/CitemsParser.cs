using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers.DataTables.Models;

namespace MartialHeroes.Assets.Parsers.DataTables;

public static class CitemsParser
{
    private const int RecordStride = 0x41C;

    private const int OffItemId = 0x00;

    private const int OffItemName = 0x04;
    private const int ItemNameLen = 48;

    private const int OffUnknown36 = 0x36;

    private const int OffItemCategory = 0x37;

    private const int OffCashPriceNx = 0x38;

    private const int OffSlotSeq2 = 0x3C;

    private const int OffIconIdA = 0x40;

    private const int OffIconIdB = 0x48;

    private const int OffFlag4C = 0x4C;

    private const int OffDescBlock = 0x0E4;
    private const int DescParaWidth = 81;
    private const int DescParaCount = 10;

    private const int OffRemainderUnmapped = 0x40E;
    private const int RemainderLen = 10;

    private const int OffTailFlag418 = 0x418;

    private static readonly Encoding Cp949;

    static CitemsParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Cp949 = Encoding.GetEncoding(949);
    }

    public static CitemsCatalog Parse(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;

        if (span.Length % RecordStride != 0)
            throw new InvalidDataException(
                $"citems.scr: buffer length {span.Length} is not an exact multiple of stride {RecordStride}.");

        var count = span.Length / RecordStride;
        var records = new CitemsRecord[count];

        for (var i = 0; i < count; i++)
        {
            var recBase = i * RecordStride;
            var rec = span.Slice(recBase, RecordStride);

            var itemId = BinaryPrimitives.ReadUInt32LittleEndian(rec[..]);

            var nameBytes = rec.Slice(OffItemName, ItemNameLen);
            var nameNul = nameBytes.IndexOf((byte)0);
            var itemName = nameNul >= 0
                ? Cp949.GetString(nameBytes[..nameNul])
                : Cp949.GetString(nameBytes);

            var unknown36 = rec[OffUnknown36];

            var itemCategory = rec[OffItemCategory];

            var cashPriceNx = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffCashPriceNx..]);

            var slotSeq2 = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffSlotSeq2..]);

            var iconIdA = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffIconIdA..]);

            var iconIdB = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffIconIdB..]);

            var flag4C = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffFlag4C..]);

            var descParaList = new List<string>(DescParaCount);
            for (var p = 0; p < DescParaCount; p++)
            {
                var paraOff = OffDescBlock + p * DescParaWidth;
                var paraBytes = rec.Slice(paraOff, DescParaWidth);
                if (paraBytes[0] == (byte)'#')
                    break;
                var paraNul = paraBytes.IndexOf((byte)0);
                descParaList.Add(paraNul >= 0
                    ? Cp949.GetString(paraBytes[..paraNul])
                    : Cp949.GetString(paraBytes));
            }

            var descParagraphs = descParaList.ToArray();

            var remainderRaw = data.Slice(recBase + OffRemainderUnmapped, RemainderLen);

            var tailFlag418 = rec[OffTailFlag418];

            records[i] = new CitemsRecord
            {
                ItemId = itemId,
                ItemName = itemName,
                Unknown36 = unknown36,
                ItemCategory = itemCategory,
                CashPriceNx = cashPriceNx,
                SlotSeq2 = slotSeq2,
                IconIdA = iconIdA,
                IconIdB = iconIdB,
                Flag4C = flag4C,
                DescParagraphs = descParagraphs,
                RemainderRaw = remainderRaw,
                TailFlag418 = tailFlag418
            };
        }

        return new CitemsCatalog(records);
    }
}