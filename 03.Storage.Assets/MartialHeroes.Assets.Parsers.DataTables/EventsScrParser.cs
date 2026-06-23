using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.DataTables.Models;

namespace MartialHeroes.Assets.Parsers.DataTables;

public static class EventsScrParser
{
    private const int RecordStride = 520;


    private const int OffEventId = 0x00;

    private const int OffEventType = 0x04;

    private const int OffDayCount = 0x06;


    private const int OffModeFlag = 0x64;


    private const int OffRateArray = 0x68;
    private const int RateArrayCount = 50;

    private const int OffActorArray = 0x130;
    private const int ActorArrayCount = 52;


    public static EventsScrRecord[] Parse(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;

        if (span.Length % RecordStride != 0)
            throw new InvalidDataException(
                $"events.scr parse error: buffer length {span.Length} is not an exact multiple of " +
                $"stride {RecordStride}. " +
                "spec: Docs/RE/formats/events_scr.md §1.1.");

        var count = span.Length / RecordStride;
        var records = new EventsScrRecord[count];

        Span<uint> rateBuf = stackalloc uint[RateArrayCount];
        Span<uint> actorBuf = stackalloc uint[ActorArrayCount];

        for (var i = 0; i < count; i++)
        {
            var recBase = i * RecordStride;
            var rec = span.Slice(recBase, RecordStride);

            var eventId = BinaryPrimitives.ReadUInt32LittleEndian(rec[..]);

            var eventType = BinaryPrimitives.ReadUInt16LittleEndian(rec[OffEventType..]);

            var dayCount = BinaryPrimitives.ReadUInt16LittleEndian(rec[OffDayCount..]);


            var modeFlag = BinaryPrimitives.ReadUInt16LittleEndian(rec[OffModeFlag..]);


            var rateCount = 0;
            for (var k = 0; k < RateArrayCount; k++)
            {
                var v = BinaryPrimitives.ReadUInt32LittleEndian(
                    rec[(OffRateArray + k * 4)..]);
                if (v == 0) break;
                rateBuf[rateCount++] = v;
            }

            IReadOnlyList<uint> rateArray = rateCount == 0
                ? Array.Empty<uint>()
                : rateBuf[..rateCount].ToArray();

            var actorCount = 0;
            for (var k = 0; k < ActorArrayCount; k++)
            {
                var v = BinaryPrimitives.ReadUInt32LittleEndian(
                    rec[(OffActorArray + k * 4)..]);
                if (v == 0) break;
                actorBuf[actorCount++] = v;
            }

            IReadOnlyList<uint> actorArray = actorCount == 0
                ? Array.Empty<uint>()
                : actorBuf[..actorCount].ToArray();


            records[i] = new EventsScrRecord
            {
                EventId = eventId,
                EventType = eventType,
                DayCount = dayCount,
                ModeFlag = modeFlag,
                RateArray = rateArray,
                ActorArray = actorArray,
                Raw = data.Slice(recBase, RecordStride)
            };
        }

        return records;
    }
}