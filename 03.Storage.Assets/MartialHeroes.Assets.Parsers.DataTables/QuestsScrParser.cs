using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers.DataTables.Models;

namespace MartialHeroes.Assets.Parsers.DataTables;

public static class QuestsScrParser
{
    private const int RecordStride = 3720;

    private const int NameOffset = 0x002;
    private const int NameWidth = 62;

    static QuestsScrParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static QuestScrRecord[] Parse(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;

        if (span.Length % RecordStride != 0)
            throw new InvalidDataException(
                $"quests.scr parse error: buffer length {span.Length} is not a multiple of " +
                $"stride {RecordStride} (QUESTS_SCR_RECORD_BYTES). " +
                "spec: Docs/RE/formats/config_tables.md §2.17.1.");

        var totalSlots = span.Length / RecordStride;
        var cp949 = Encoding.GetEncoding(949);

        var results = new List<QuestScrRecord>(Math.Min(totalSlots, 128));

        for (var i = 0; i < totalSlots; i++)
        {
            var recBase = i * RecordStride;
            var rec = span.Slice(recBase, RecordStride);

            var questId = BinaryPrimitives.ReadUInt16LittleEndian(rec[..]);
            if (questId == 0)
                continue;

            var questName = ReadNullTerminatedCp949(rec.Slice(NameOffset, NameWidth), cp949);

            results.Add(new QuestScrRecord
            {
                QuestId = questId,
                QuestName = questName,
                Raw = data.Slice(recBase, RecordStride)
            });
        }

        return results.ToArray();
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