using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(4, 75)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgProductPurchaseResultPanel
{
    public const uint OpcodeId = Opcodes.SmsgResponseSlot75;

    public const int WireSize = 184;

    public readonly HeadBuffer Head;

    public readonly byte Success;

    public readonly byte FailSub;

    public readonly BodyBuffer Body;

    [InlineArray(8)]
    public struct HeadBuffer
    {
        private byte _element0;
    }

    [InlineArray(174)]
    public struct BodyBuffer
    {
        private byte _element0;
    }
}