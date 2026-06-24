using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(5, 127)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgStealthToggle
{
    public const uint OpcodeId = Opcodes.SmsgStealthToggle;

    public const int WireSize = 12;

    public readonly uint ActorSort;

    public readonly uint ActorId;

    public readonly byte StealthFlag;

    public readonly PaddingBuffer Padding;

    [InlineArray(3)]
    public struct PaddingBuffer
    {
        private byte _element0;
    }
}
