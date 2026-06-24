using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(4, 40)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgMotionWeaponFxSwap
{
    public const uint OpcodeId = Opcodes.SmsgResponseSlot40;

    public const int WireSize = 164;

    public readonly HeadBuffer Head;

    public readonly byte Result;

    public readonly BodyBuffer Body;

    [InlineArray(8)]
    public struct HeadBuffer
    {
        private byte _element0;
    }

    [InlineArray(155)]
    public struct BodyBuffer
    {
        private byte _element0;
    }
}