using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(5, 91)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgPvpScoreUpdate
{
    public const uint OpcodeId = Opcodes.SmsgPvpScoreUpdate;

    public const int WireSize = 124;

    public readonly byte ByteA;

    public readonly GapABuffer GapA;

    public readonly uint DwordB;

    public readonly byte ByteC;

    public readonly byte ByteD;

    public readonly GapBBuffer GapB;

    public readonly uint DwordE;

    public readonly uint DwordF;

    public readonly NamePrimaryBuffer NamePrimary;

    public readonly NameArrayBuffer NameArray;

    public readonly TailPadBuffer TailPad;

    [InlineArray(3)]
    public struct GapABuffer
    {
        private byte _element0;
    }

    [InlineArray(2)]
    public struct GapBBuffer
    {
        private byte _element0;
    }

    [InlineArray(17)]
    public struct NamePrimaryBuffer
    {
        private byte _element0;
    }

    [InlineArray(85)]
    public struct NameArrayBuffer
    {
        private byte _element0;
    }

    [InlineArray(2)]
    public struct TailPadBuffer
    {
        private byte _element0;
    }
}
