using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(4, 81)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgActionErrorResult
{
    public const uint OpcodeId = Opcodes.SmsgActionErrorResult;

    public const int HeaderSize = 10;

    public readonly HeadBuffer Head;

    public readonly byte Status;

    public readonly byte Error;

    [InlineArray(8)]
    public struct HeadBuffer
    {
        private byte _element0;
    }
}
