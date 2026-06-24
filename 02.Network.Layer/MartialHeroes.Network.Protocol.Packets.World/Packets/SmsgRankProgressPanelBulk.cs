using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(5, 77)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgRankProgressPanelBulk
{
    public const uint OpcodeId = Opcodes.SmsgRankProgressPanelBulk;

    public const int WireSize = 400;

    public readonly HeaderPad0Buffer HeaderPad0;

    public readonly uint ActorKey;

    public readonly NameArrayABuffer NameArrayA;

    public readonly FlagsABuffer FlagsA;

    public readonly TitleStringBuffer TitleString;

    public readonly byte TitleFlag;

    public readonly NameArrayBBuffer NameArrayB;

    public readonly FlagsBBuffer FlagsB;

    public readonly TailPendingBuffer TailPending;

    [InlineArray(4)]
    public struct HeaderPad0Buffer
    {
        private byte _element0;
    }

    [InlineArray(136)]
    public struct NameArrayABuffer
    {
        private byte _element0;
    }

    [InlineArray(8)]
    public struct FlagsABuffer
    {
        private byte _element0;
    }

    [InlineArray(17)]
    public struct TitleStringBuffer
    {
        private byte _element0;
    }

    [InlineArray(204)]
    public struct NameArrayBBuffer
    {
        private byte _element0;
    }

    [InlineArray(12)]
    public struct FlagsBBuffer
    {
        private byte _element0;
    }

    [InlineArray(14)]
    public struct TailPendingBuffer
    {
        private byte _element0;
    }
}