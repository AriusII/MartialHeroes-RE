using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers.DataTables.Models;

namespace MartialHeroes.Assets.Parsers.DataTables;

public static class AutoQuestionParser
{
    private const int RecordStride = 92;


    private const int OffQuestionId = 0x00;

    private const int OffTextBlock = 0x04;
    private const int TextBlockLen = 84;


    public static AutoQuestionRecord[] Parse(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;

        if (span.Length % RecordStride != 0)
            throw new InvalidDataException(
                $"autoquestion_cl.scr parse error: buffer length {span.Length} is not an exact multiple of " +
                $"stride {RecordStride}. " +
                "spec: Docs/RE/formats/events_scr.md §2.1.");

        var count = span.Length / RecordStride;

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp949 = Encoding.GetEncoding(949);

        var records = new AutoQuestionRecord[count];

        for (var i = 0; i < count; i++)
        {
            var rec = span.Slice(i * RecordStride, RecordStride);

            var questionId = BinaryPrimitives.ReadUInt32LittleEndian(rec[..]);

            var block = rec.Slice(OffTextBlock, TextBlockLen);

            var firstNul = block.IndexOf((byte)0);
            string questionText;
            string answerPrompt;

            if (firstNul < 0)
            {
                questionText = cp949.GetString(block);
                answerPrompt = string.Empty;
            }
            else
            {
                questionText = cp949.GetString(block[..firstNul]);

                var remaining = block[(firstNul + 1)..];
                var secondNul = remaining.IndexOf((byte)0);
                answerPrompt = secondNul >= 0
                    ? cp949.GetString(remaining[..secondNul])
                    : cp949.GetString(remaining);
            }

            records[i] = new AutoQuestionRecord
            {
                QuestionId = questionId,
                QuestionText = questionText,
                AnswerPrompt = answerPrompt
            };
        }

        return records;
    }
}