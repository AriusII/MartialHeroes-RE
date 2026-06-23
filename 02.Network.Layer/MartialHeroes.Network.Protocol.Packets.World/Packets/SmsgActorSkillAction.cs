using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(5, 52)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgActorSkillAction
{
    public const uint OpcodeId = Opcodes.SmsgActorSkillAction;

    public const int HeaderSize = 24;

    public const int TargetRecordStride = 36;

    public const int TargetSubKeyOffset = 0x00;

    public const int TargetKeyOffset = 0x04;


    public readonly byte CasterSort;

    public readonly Pad0Buffer Pad0;

    public readonly uint CasterId;

    public readonly byte CastFlag;

    public readonly byte BasicSelector;

    public readonly Pad1Buffer Pad1;

    public readonly uint SkillId;

    public readonly byte ActionCode;

    public readonly Pad2Buffer Pad2;

    public readonly byte TargetCount;

    public readonly Pad3Buffer Pad3;


    [InlineArray(3)]
    public struct Pad0Buffer
    {
        private byte _element0;
    }

    [InlineArray(2)]
    public struct Pad1Buffer
    {
        private byte _element0;
    }

    [InlineArray(3)]
    public struct Pad2Buffer
    {
        private byte _element0;
    }

    [InlineArray(3)]
    public struct Pad3Buffer
    {
        private byte _element0;
    }
}