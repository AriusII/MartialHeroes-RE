using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.DataTables.Models;

namespace MartialHeroes.Assets.Parsers.DataTables;

public static class XdbParser
{
    private const int ActorSizeStride = 12;


    private const int BuffIconStride = 12;


    private const int EffectScaleStride = 8;


    private const int VehicleStride = 52;

    private const int VehicleTagAOffset = 8;

    private const int VehicleTagBOffset = 12;
    private const uint VehicleTagBExpected = 0x1575A3E4u;

    private const int VehicleParam0Offset = 16;
    private const int VehicleParam1Offset = 20;

    private const int
        VehicleParam2Offset = 24;

    private const int VehicleParam3Offset = 28;

    private const int VehicleParam4Offset = 32;

    private const int VehicleParam5to8Offset = 36;


    private const int CreatureItemStride = 48;

    public static ActorSizeRecord[] ParseActorSizeXdb(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        EnsureExactStride(span, ActorSizeStride, "actor_size.xdb", "Docs/RE/formats/xdb_tables.md §1");
        var count = span.Length / ActorSizeStride;
        var results = new ActorSizeRecord[count];

        for (var i = 0; i < count; i++)
        {
            var offset = i * ActorSizeStride;
            var rec = span.Slice(offset, ActorSizeStride);

            var actorClassId = BinaryPrimitives.ReadUInt32LittleEndian(rec[..]);

            var scaleXz = BinaryPrimitives.ReadSingleLittleEndian(rec[4..]);

            var scaleY = BinaryPrimitives.ReadSingleLittleEndian(rec[8..]);

            results[i] = new ActorSizeRecord
            {
                ActorClassId = actorClassId,
                ScaleXz = scaleXz,
                ScaleY = scaleY
            };
        }

        return results;
    }


    public static BuffIconPositionRecord[] ParseBuffIconPositionXdb(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        EnsureExactStride(span, BuffIconStride, "buff_icon_position.xdb", "Docs/RE/formats/xdb_tables.md §2");
        var count = span.Length / BuffIconStride;
        var results = new BuffIconPositionRecord[count];

        for (var i = 0; i < count; i++)
        {
            var offset = i * BuffIconStride;
            var rec = span.Slice(offset, BuffIconStride);

            var buffId = BinaryPrimitives.ReadUInt32LittleEndian(rec[..]);

            var spriteX = BinaryPrimitives.ReadUInt32LittleEndian(rec[4..]);

            var spriteY = BinaryPrimitives.ReadUInt32LittleEndian(rec[8..]);

            results[i] = new BuffIconPositionRecord
            {
                BuffId = buffId,
                AtlasX = (int)spriteX,
                AtlasY = (int)spriteY
            };
        }

        return results;
    }

    public static EffectScaleRecord[] ParseEffectScaleXdb(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        EnsureExactStride(span, EffectScaleStride, "effectscale.xdb", "Docs/RE/formats/xdb_tables.md §3");
        var count = span.Length / EffectScaleStride;
        var results = new EffectScaleRecord[count];

        for (var i = 0; i < count; i++)
        {
            var offset = i * EffectScaleStride;
            var rec = span.Slice(offset, EffectScaleStride);

            var objectId = BinaryPrimitives.ReadUInt32LittleEndian(rec[..]);

            var scale = BinaryPrimitives.ReadSingleLittleEndian(rec[4..]);

            results[i] = new EffectScaleRecord
            {
                ObjectId = objectId,
                Scale = scale
            };
        }

        return results;
    }

    public static VehicleXdbRecord[] ParseVehicleXdb(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        EnsureExactStride(span, VehicleStride, "vehicle.xdb", "Docs/RE/formats/xdb_tables.md §4");
        var count = span.Length / VehicleStride;
        var results = new VehicleXdbRecord[count];

        for (var i = 0; i < count; i++)
        {
            var recBase = i * VehicleStride;
            var rec = span.Slice(recBase, VehicleStride);

            var vehicleId = BinaryPrimitives.ReadUInt32LittleEndian(rec[..]);

            var itemId = BinaryPrimitives.ReadUInt32LittleEndian(rec[4..]);

            var tagA = BinaryPrimitives.ReadUInt32LittleEndian(rec[VehicleTagAOffset..]);

            var tagB = BinaryPrimitives.ReadUInt32LittleEndian(rec[VehicleTagBOffset..]);

            var param0 = BinaryPrimitives.ReadSingleLittleEndian(rec[VehicleParam0Offset..]);

            var param1 = BinaryPrimitives.ReadSingleLittleEndian(rec[VehicleParam1Offset..]);

            var param2 = BinaryPrimitives.ReadSingleLittleEndian(rec[VehicleParam2Offset..]);
            var param3 = BinaryPrimitives.ReadSingleLittleEndian(rec[VehicleParam3Offset..]);
            var param4 = BinaryPrimitives.ReadSingleLittleEndian(rec[VehicleParam4Offset..]);

            var seatYFacing1 = BinaryPrimitives.ReadSingleLittleEndian(rec[(VehicleParam5to8Offset + 0)..]);
            var seatYFacing2 = BinaryPrimitives.ReadSingleLittleEndian(rec[(VehicleParam5to8Offset + 4)..]);
            var seatYFacing3 = BinaryPrimitives.ReadSingleLittleEndian(rec[(VehicleParam5to8Offset + 8)..]);
            var seatYFacing4 = BinaryPrimitives.ReadSingleLittleEndian(rec[(VehicleParam5to8Offset + 12)..]);

            results[i] = new VehicleXdbRecord
            {
                VehicleId = vehicleId,
                ItemId = itemId,
                TagA = tagA,
                TagB = tagB,
                Param0 = param0,
                Param1 = param1,
                Param2 = param2,
                Param3 = param3,
                Param4 = param4,
                SeatYFacing1 = seatYFacing1,
                SeatYFacing2 = seatYFacing2,
                SeatYFacing3 = seatYFacing3,
                SeatYFacing4 = seatYFacing4
            };
        }

        return results;
    }


    public static CreatureItemXdbRecord[] ParseCreatureItemXdb(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        EnsureExactStride(span, CreatureItemStride, "creature_item.xdb", "Docs/RE/formats/xdb_tables.md §5");
        var count = span.Length / CreatureItemStride;
        var results = new CreatureItemXdbRecord[count];

        for (var i = 0; i < count; i++)
        {
            var recBase = i * CreatureItemStride;
            var rec = span.Slice(recBase, CreatureItemStride);

            var creatureKey = BinaryPrimitives.ReadUInt32LittleEndian(rec[..]);

            var itemId = BinaryPrimitives.ReadUInt32LittleEndian(rec[4..]);

            var f0 = BinaryPrimitives.ReadSingleLittleEndian(rec[8..]);
            var f1 = BinaryPrimitives.ReadSingleLittleEndian(rec[12..]);
            var f2 = BinaryPrimitives.ReadSingleLittleEndian(rec[16..]);
            var f3 = BinaryPrimitives.ReadSingleLittleEndian(rec[20..]);
            var f4 = BinaryPrimitives.ReadSingleLittleEndian(rec[24..]);
            var f5 = BinaryPrimitives.ReadSingleLittleEndian(rec[28..]);

            var scaleOrRadius = BinaryPrimitives.ReadSingleLittleEndian(rec[32..]);

            var unknownU1 = BinaryPrimitives.ReadSingleLittleEndian(rec[36..]);

            var flag0 = rec[40];
            var flag1 = rec[41];
            var flag2 = rec[42];
            var flag3 = rec[43];

            var probability = BinaryPrimitives.ReadUInt32LittleEndian(rec[44..]);

            results[i] = new CreatureItemXdbRecord
            {
                CreatureKey = creatureKey,
                ItemId = itemId,
                AttachF0 = f0,
                AttachF1 = f1,
                AttachF2 = f2,
                AttachF3 = f3,
                AttachF4 = f4,
                AttachF5 = f5,
                ScaleOrRadius = scaleOrRadius,
                VisualScale = unknownU1,
                Flag0 = flag0,
                Flag1 = flag1,
                Flag2 = flag2,
                Flag3 = flag3,
                Probability = probability
            };
        }

        return results;
    }


    private static void EnsureExactStride(ReadOnlySpan<byte> span, int stride, string fileName, string specRef)
    {
        if (span.Length % stride != 0)
            throw new InvalidDataException(
                $"{fileName} parse error: buffer length {span.Length} is not an exact multiple of " +
                $"stride {stride}. spec: {specRef}.");
    }
}