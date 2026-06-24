using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(5, 65)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgGuildMemberRosterUpdate
{
    public const uint OpcodeId = Opcodes.SmsgGuildMemberRosterUpdate;

    public const int WireSize = 32;

    public readonly uint MemberKey;

    public readonly uint Sort;

    public readonly byte RankFlag;

    public readonly NameBuffer Name;

    public readonly byte NulTerm;

    public readonly short NameGate;

    public readonly byte ByteC;

    public readonly byte ByteD;

    public readonly Pad0Buffer Pad0;

    [InlineArray(16)]
    public struct NameBuffer
    {
        private byte _element0;
    }

    [InlineArray(2)]
    public struct Pad0Buffer
    {
        private byte _element0;
    }
}