using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers.DataTables.Models;

namespace MartialHeroes.Assets.Parsers.DataTables;

public static class DoTableParser
{

    private const int TextCommandStride = 52;


    private const int EmoticonStride = 40;


    private const int MsgInfoStride = 128;


    private const int ItemsExtraStride = 48;

    private const uint ItemsExtraSentinelId = 0x7FFFFFFF;

    private static Encoding? _cp949;

    public static TextCommandRecord[] ParseTextCommandDo(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        EnsureStride(span, TextCommandStride, "textcommand.do", "Docs/RE/formats/config_tables.md §3.1");
        var count = span.Length / TextCommandStride;
        var results = new TextCommandRecord[count];
        var cp949 = GetCp949();

        for (var i = 0; i < count; i++)
        {
            var offset = i * TextCommandStride;
            var rec = span.Slice(offset, TextCommandStride);

            var commandId = BinaryPrimitives.ReadUInt32LittleEndian(rec[..]);

            var commandName = DecodeNullTerminated(cp949, rec.Slice(4, 36));


            var argFlag = rec[44];


            var subCommandId = BinaryPrimitives.ReadUInt32LittleEndian(rec[48..]);

            results[i] = new TextCommandRecord
            {
                CommandId = commandId,
                CommandName = commandName,
                ArgumentFlag = argFlag,
                SubCommandId = subCommandId,
                Raw = data.Slice(offset, TextCommandStride)
            };
        }

        return results;
    }

    public static EmoticonRecord[] ParseEmoticonDo(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        EnsureStride(span, EmoticonStride, "emoticon.do", "Docs/RE/formats/ui_manifests.md §2.9");
        var count = span.Length / EmoticonStride;
        var results = new EmoticonRecord[count];

        for (var i = 0; i < count; i++)
        {
            var offset = i * EmoticonStride;
            var rec = span.Slice(offset, EmoticonStride);

            var emoteId = BinaryPrimitives.ReadUInt32LittleEndian(rec[..]);

            var categoryFlag = rec[4];


            var secondaryKey = BinaryPrimitives.ReadUInt32LittleEndian(rec[8..]);

            var actionLink = BinaryPrimitives.ReadUInt32LittleEndian(rec[12..]);

            var dstX = BinaryPrimitives.ReadInt32LittleEndian(rec[16..]);

            var dstY = BinaryPrimitives.ReadInt32LittleEndian(rec[20..]);

            var glyphSrcX = BinaryPrimitives.ReadInt32LittleEndian(rec[24..]);

            var glyphSrcY = BinaryPrimitives.ReadInt32LittleEndian(rec[28..]);

            var labelSrcX = BinaryPrimitives.ReadInt32LittleEndian(rec[32..]);

            var labelSrcY = BinaryPrimitives.ReadInt32LittleEndian(rec[36..]);

            results[i] = new EmoticonRecord
            {
                EmoteId = emoteId,
                CategoryFlag = categoryFlag,
                SecondaryKey = secondaryKey,
                ActionLink = actionLink,
                DstX = dstX,
                DstY = dstY,
                GlyphSrcX = glyphSrcX,
                GlyphSrcY = glyphSrcY,
                LabelSrcX = labelSrcX,
                LabelSrcY = labelSrcY,
                Raw = data.Slice(offset, EmoticonStride)
            };
        }

        return results;
    }

    public static MsgInfoRecord[] ParseMsgInfoDo(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        EnsureStride(span, MsgInfoStride, "msginfo.do", "Docs/RE/formats/config_tables.md §3.3");
        var count = span.Length / MsgInfoStride;
        var results = new MsgInfoRecord[count];
        var cp949 = GetCp949();

        for (var i = 0; i < count; i++)
        {
            var offset = i * MsgInfoStride;
            var rec = span.Slice(offset, MsgInfoStride);

            var msgId = BinaryPrimitives.ReadUInt32LittleEndian(rec[..]);

            var dialogFlag = BinaryPrimitives.ReadUInt32LittleEndian(rec[4..]);

            var textLine1 = DecodeNullTerminated(cp949, rec.Slice(8, 60));

            var textLine2 = DecodeNullTerminated(cp949, rec.Slice(68, 60));

            results[i] = new MsgInfoRecord
            {
                MessageId = msgId,
                DialogFlag = dialogFlag,
                TextLine1 = textLine1,
                TextLine2 = textLine2,
                Raw = data.Slice(offset, MsgInfoStride)
            };
        }

        return results;
    }

    public static ItemsExtraRecord[] ParseItemsExtraDo(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        EnsureStride(span, ItemsExtraStride, "items_extra.do", "Docs/RE/formats/config_tables.md §3.4");
        var count = span.Length / ItemsExtraStride;
        var results = new ItemsExtraRecord[count];

        for (var i = 0; i < count; i++)
        {
            var offset = i * ItemsExtraStride;
            var rec = span.Slice(offset, ItemsExtraStride);

            var itemId = BinaryPrimitives.ReadUInt32LittleEndian(rec[..]);

            var animScale = BinaryPrimitives.ReadSingleLittleEndian(rec[4..]);

            var attachFieldA = BinaryPrimitives.ReadInt32LittleEndian(rec[8..]);

            var attachFieldB = BinaryPrimitives.ReadInt32LittleEndian(rec[12..]);

            var attachX = BinaryPrimitives.ReadInt32LittleEndian(rec[16..]);
            var attachY = BinaryPrimitives.ReadInt32LittleEndian(rec[20..]);
            var attachZ = BinaryPrimitives.ReadInt32LittleEndian(rec[24..]);

            var rotX = BinaryPrimitives.ReadInt32LittleEndian(rec[28..]);
            var rotY = BinaryPrimitives.ReadInt32LittleEndian(rec[32..]);
            var rotZ = BinaryPrimitives.ReadInt32LittleEndian(rec[36..]);

            var field40 = BinaryPrimitives.ReadInt32LittleEndian(rec[40..]);

            var rarityTier = BinaryPrimitives.ReadUInt32LittleEndian(rec[44..]);

            results[i] = new ItemsExtraRecord
            {
                ItemId = itemId,
                IsSentinel = itemId == ItemsExtraSentinelId,
                AnimScale = animScale,
                AttachFieldA = attachFieldA,
                AttachFieldB = attachFieldB,
                AttachX = attachX,
                AttachY = attachY,
                AttachZ = attachZ,
                RotXDeg = rotX,
                RotYDeg = rotY,
                RotZDeg = rotZ,
                Field40 = field40,
                RarityTier = rarityTier,
                Raw = data.Slice(offset, ItemsExtraStride)
            };
        }

        return results;
    }


    private static void EnsureStride(ReadOnlySpan<byte> span, int stride, string fileName, string specRef)
    {
        if (span.Length % stride != 0)
            throw new InvalidDataException(
                $"{fileName} parse error: buffer length {span.Length} is not a multiple of " +
                $"stride {stride}. spec: {specRef}.");
    }

    private static Encoding GetCp949()
    {
        if (_cp949 is not null)
            return _cp949;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return _cp949 = Encoding.GetEncoding(949);
    }

    private static string DecodeNullTerminated(Encoding enc, ReadOnlySpan<byte> buf)
    {
        var end = buf.IndexOf((byte)0);
        if (end < 0) end = buf.Length;
        if (end == 0) return string.Empty;
        return enc.GetString(buf[..end]);
    }
}