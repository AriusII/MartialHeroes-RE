
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(5, 14)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgGroundItemSpawn
{
    public const uint OpcodeId = Opcodes.SmsgCombatEffectInstanceSpawn;

    public const int WireSize = 48;

    public readonly uint Sort;

    public readonly uint SourceId;

    public readonly sbyte Mode;

    public readonly byte Slot;

    public readonly Pad0Buffer Pad0;

    public readonly int TemplateId;

    public readonly Reserved0Buffer Reserved0;

    public readonly int Param1;

    public readonly int Param2;

    public readonly float PosX;

    public readonly float PosZ;

    public readonly Reserved1Buffer Reserved1;

    public readonly byte NoticeFlag;

    [InlineArray(2)]
    public struct Pad0Buffer
    {
        private byte _element0;
    }

    [InlineArray(4)]
    public struct Reserved0Buffer
    {
        private byte _element0;
    }

    [InlineArray(11)]
    public struct Reserved1Buffer
    {
        private byte _element0;
    }
}