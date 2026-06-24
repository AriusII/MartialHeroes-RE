using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(4, 36)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgPartyMemberRemoveResult
{
    public const uint OpcodeId = Opcodes.SmsgPartyMemberRemoveResult;

    public const int WireSize = 56;

    public readonly Pad0Buffer Pad0;

    public readonly uint RequesterId;

    public readonly Pad1Buffer Pad1;

    public readonly byte Submode;

    public readonly Pad2Buffer Pad2;

    public readonly uint RemovedIdExpel;

    public readonly Pad3Buffer Pad3;

    public readonly MemberIdsBuffer MemberIds;

    public readonly uint RemovedIdLeft;

    [InlineArray(4)]
    public struct Pad0Buffer
    {
        private byte _element0;
    }

    [InlineArray(2)]
    public struct Pad1Buffer
    {
        private byte _element0;
    }

    [InlineArray(1)]
    public struct Pad2Buffer
    {
        private byte _element0;
    }

    [InlineArray(4)]
    public struct Pad3Buffer
    {
        private byte _element0;
    }

    [InlineArray(32)]
    public struct MemberIdsBuffer
    {
        private byte _element0;
    }
}
