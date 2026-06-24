using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(4, 78)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgRentalItemExpirySweep
{
    public const uint OpcodeId = Opcodes.SmsgServerTimeNotification;

    public const int WireSize = 12;

    public readonly HeadBuffer Head;

    public readonly uint ServerTime;

    [InlineArray(8)]
    public struct HeadBuffer
    {
        private byte _element0;
    }
}