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

    public readonly byte ApplyGate;

    public readonly byte Result;

    public readonly byte Action;

    public readonly byte Grade;

    public readonly GuildNameBuffer GuildName;

    public readonly byte Pad28;

    public readonly byte SortOrSlot;

    public readonly Pad30Buffer Pad30;

    public readonly long Value64A;

    public readonly ushort GuildId;

    public readonly Pad42Buffer Pad42;

    public readonly long MoneyBalance64;

    [InlineArray(8)]
    public struct HeadBuffer
    {
        private byte _element0;
    }

    [InlineArray(16)]
    public struct GuildNameBuffer
    {
        private byte _element0;
    }

    [InlineArray(2)]
    public struct Pad30Buffer
    {
        private byte _element0;
    }

    [InlineArray(2)]
    public struct Pad42Buffer
    {
        private byte _element0;
    }
}
