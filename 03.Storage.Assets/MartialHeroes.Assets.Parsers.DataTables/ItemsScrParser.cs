using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers.DataTables.Models;

namespace MartialHeroes.Assets.Parsers.DataTables;

public static class ItemsScrParser
{
    private const int FixedBlockSize = 0x224;

    private const int OffItemName = 0x000;
    private const int ItemNameLen = 52;

    private const int OffItemUid = 0x034;

    private const int OffItemDesc = 0x038;

    private const int OffModelRefKey = 0x080;

    private const int OffAnimRefKey = 0x084;

    private const int OffOpaque0A4 = 0x0A4;

    private const int OffRecordDiscriminator = 0x0BA;

    private const int OffDispatchFlags = 0x0CD;

    private const int OffOpaque200 = 0x200;

    private const int OffOpaque21C = 0x21C;

    private const int OffEffectCount = 0x220;

    private const int EffectEntryStride = 8;

    private const int OffEffectA = 0x00;
    private const int OffEffectB = 0x02;
    private const int OffEffectC = 0x04;
    private const int OffEffectD = 0x06;

    private static readonly Encoding Cp949;

    static ItemsScrParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Cp949 = Encoding.GetEncoding(949);
    }

    public static IEnumerable<ItemsScrRecord> Parse(ReadOnlyMemory<byte> data)
    {
        var pos = 0;
        var totalLen = data.Length;

        while (pos < totalLen)
        {
            if (totalLen - pos < FixedBlockSize)
                break;

            var record = DecodeRecord(data, pos, totalLen);
            var recordStride = FixedBlockSize + record.EffectCount * EffectEntryStride;

            yield return record;

            pos += recordStride;
        }
    }

    private static ItemsScrRecord DecodeRecord(ReadOnlyMemory<byte> data, int recordOffset, int totalLen)
    {
        if ((uint)recordOffset + FixedBlockSize > (uint)totalLen)
            throw new InvalidDataException(
                $"items.scr parse error: record at offset {recordOffset}: " +
                $"fixed block requires {FixedBlockSize} bytes but only {totalLen - recordOffset} bytes remain. " +
                "spec: Docs/RE/formats/items_scr.md §1.2.");

        var fixedBlock = data.Span.Slice(recordOffset, FixedBlockSize);

        var nameBytes = fixedBlock.Slice(OffItemName, ItemNameLen);
        var nameNul = nameBytes.IndexOf((byte)0);
        var itemName = nameNul >= 0
            ? Cp949.GetString(nameBytes[..nameNul])
            : Cp949.GetString(nameBytes);

        var itemUid = BinaryPrimitives.ReadUInt32LittleEndian(fixedBlock[OffItemUid..]);

        var descRegion = fixedBlock[OffItemDesc..];
        var descNul = descRegion.IndexOf((byte)0);
        var itemDesc = descNul >= 0
            ? Cp949.GetString(descRegion[..descNul])
            : Cp949.GetString(descRegion);

        var modelRefKey = BinaryPrimitives.ReadUInt32LittleEndian(fixedBlock[OffModelRefKey..]);

        var animRefKey = BinaryPrimitives.ReadUInt32LittleEndian(fixedBlock[OffAnimRefKey..]);

        var opaque0A4 = data.Slice(recordOffset + OffOpaque0A4, 4);

        var recordDiscriminator = fixedBlock[OffRecordDiscriminator];

        var dispatchFlags = data.Slice(recordOffset + OffDispatchFlags, 4);

        var opaque200 = data.Slice(recordOffset + OffOpaque200, 4);

        var opaque21C = data.Slice(recordOffset + OffOpaque21C, 4);

        var effectCount = fixedBlock[OffEffectCount];

        var trailingSize = effectCount * EffectEntryStride;
        if (recordOffset + FixedBlockSize + trailingSize > totalLen)
            throw new InvalidDataException(
                $"items.scr parse error: record at offset {recordOffset}: " +
                $"effect_count={effectCount} requires {trailingSize} trailing bytes but only " +
                $"{totalLen - recordOffset - FixedBlockSize} bytes remain. " +
                "spec: Docs/RE/formats/items_scr.md §1.2.");

        var effects = effectCount == 0
            ? Array.Empty<ItemEffectEntry>()
            : new ItemEffectEntry[effectCount];
        var fullSpan = data.Span;
        var effectsBase = recordOffset + FixedBlockSize;
        for (var e = 0; e < effectCount; e++)
        {
            var entryBase = effectsBase + e * EffectEntryStride;
            var entry = fullSpan.Slice(entryBase, EffectEntryStride);

            var effectA = BinaryPrimitives.ReadUInt16LittleEndian(entry[..]);

            var effectB = BinaryPrimitives.ReadInt16LittleEndian(entry[OffEffectB..]);

            var effectC = BinaryPrimitives.ReadUInt16LittleEndian(entry[OffEffectC..]);

            var effectD = entry[OffEffectD];

            effects[e] = new ItemEffectEntry
            {
                EffectA = effectA,
                EffectB = effectB,
                EffectC = effectC,
                EffectD = effectD
            };
        }

        return new ItemsScrRecord
        {
            ItemName = itemName,
            ItemUid = itemUid,
            ItemDesc = itemDesc,
            ModelRefKey = modelRefKey,
            AnimRefKey = animRefKey,
            Opaque0A4 = opaque0A4,
            RecordDiscriminator = recordDiscriminator,
            DispatchFlags = dispatchFlags,
            Opaque200 = opaque200,
            Opaque21C = opaque21C,
            EffectCount = effectCount,
            Effects = effects,
            FixedBlockRaw = data.Slice(recordOffset, FixedBlockSize)
        };
    }
}