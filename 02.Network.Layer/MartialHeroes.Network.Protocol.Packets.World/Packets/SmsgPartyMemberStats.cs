using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(5, 38)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgPartyMemberStats
{
    public const uint OpcodeId = Opcodes.SmsgPartyMemberStats;

    public const int WireSize = 100;

    public readonly PrefixBuffer Prefix;

    public readonly uint MemberId;

    public readonly MemberNameBuffer MemberName;

    public readonly short StatA;

    public readonly short StatBState;

    public readonly GapABuffer GapA;

    public readonly uint StatC;

    public readonly uint StatD;

    public readonly uint StatE;

    public readonly uint StatF;

    public readonly uint StatG;

    public readonly uint StatH;

    public readonly uint StatI;

    public readonly uint StatJ;

    public readonly StatusArrayBuffer StatusArray;

    public readonly TailPadBuffer TailPad;

    [InlineArray(8)]
    public struct PrefixBuffer
    {
        private byte _element0;
    }

    [InlineArray(18)]
    public struct MemberNameBuffer
    {
        private byte _element0;
    }

    [InlineArray(2)]
    public struct GapABuffer
    {
        private byte _element0;
    }

    [InlineArray(30)]
    public struct StatusArrayBuffer
    {
        private byte _element0;
    }

    [InlineArray(2)]
    public struct TailPadBuffer
    {
        private byte _element0;
    }
}
