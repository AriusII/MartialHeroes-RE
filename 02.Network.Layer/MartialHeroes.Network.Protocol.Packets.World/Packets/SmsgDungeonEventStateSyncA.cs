using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(5, 85)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgDungeonEventStateSyncA
{
    public const uint OpcodeId = Opcodes.SmsgDungeonEventStateSyncA;

    public const int WireSize = 20;

    public readonly HeaderBuffer Header;

    public readonly uint StateA;

    public readonly uint StateB;

    public readonly uint StateC;

    [InlineArray(8)]
    public struct HeaderBuffer
    {
        private byte _element0;
    }
}