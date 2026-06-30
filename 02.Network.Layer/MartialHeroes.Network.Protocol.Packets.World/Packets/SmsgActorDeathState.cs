using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(5, 79)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgActorDeathState
{
    public const uint OpcodeId = Opcodes.SmsgActorDeathState;

    public const int WireSize = 20;

    public readonly uint LeadingDword;

    public readonly uint ActorKey;

    public readonly byte Mode;

    public readonly PaddingBuffer Pad3;

    public readonly uint SubSelector;

    public readonly uint KillerKey;

    [InlineArray(3)]
    public struct PaddingBuffer
    {
        private byte _element0;
    }
}
