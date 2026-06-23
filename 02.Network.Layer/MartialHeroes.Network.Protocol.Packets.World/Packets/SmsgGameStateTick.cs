
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(4, 1)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgGameStateTick
{
    public const uint OpcodeId = Opcodes.SmsgGameStateTick;

    public const int WireSize = 0x238C;

    public const int WorldEntrySeedSize = SpawnZOffset + sizeof(float);

    public const int FormOffset = 0x0000;

    public const byte WorldEntryForm = 1;

    public const int AreaIdOffset = 0x000C;

    public const int SpawnXOffset = 0x2374;

    public const int SpawnZOffset = 0x2378;



    public const int TableAOffset = 24;

    public const int TableASize = 3088;

    public const int TableARecordStride = 16;

    public const int TableACapacity = 193;

    public const int TableASweepCount = 120;

    public const int TableARecordActorIdOffset = 4;

    public const int
        TableARecordKeepGuardOffset =
            8;

    public const int TableARecordAuxOffset = 12;


    public const int TableBOffset = 3112;

    public const int TableBSize = 4044;

    public const int TableBActorSlotCount = 240;

    public const int TableBActorSlotsBytes = 3840;


    public const int HotbarOffset = 7156;

    public const int HotbarSize = 1920;

    public const int HotbarSlotCount = 240;

    public const int HotbarSlotStride = 8;

    public const int
        HotbarSlotEntryKeyOffset = 0;

    public const int HotbarSlotCountOffset = 4;

    public const int
        HotbarSkillCategoryValue =
            5;

    public readonly PayloadBuffer Payload;

    public static bool TryReadWorldEntrySeed(ReadOnlySpan<byte> payload, out SmsgGameStateTickSeed seed)
    {
        seed = default;

        if (payload.Length < WorldEntrySeedSize || payload[FormOffset] != WorldEntryForm) return false;

        seed = new SmsgGameStateTickSeed(
            payload[FormOffset],
            BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(AreaIdOffset, sizeof(int))),
            BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(SpawnXOffset, sizeof(float))),
            BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(SpawnZOffset, sizeof(float))));
        return true;
    }

    [InlineArray(WireSize)]
    public struct PayloadBuffer
    {
        private byte _element0;
    }
}

public readonly record struct SmsgGameStateTickSeed(byte Form, int AreaId, float SpawnX, float SpawnZ);