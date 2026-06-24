using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(4, 61)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgGuildStateChangeResult
{
    public const uint OpcodeId = Opcodes.SmsgGuildStateChangeResult;

    public const int WireSize = 52;

    public readonly HeadBuffer Head;

    public readonly byte Gate;

    public readonly byte Result;

    public readonly byte Action;

    public readonly GuildNameBuffer GuildName;

    public readonly TailBuffer Tail;

    [InlineArray(8)]
    public struct HeadBuffer
    {
        private byte _element0;
    }

    [InlineArray(17)]
    public struct GuildNameBuffer
    {
        private byte _element0;
    }

    [InlineArray(24)]
    public struct TailBuffer
    {
        private byte _element0;
    }
}
