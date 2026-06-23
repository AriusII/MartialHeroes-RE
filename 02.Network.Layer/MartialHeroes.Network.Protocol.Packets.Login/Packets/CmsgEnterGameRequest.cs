
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.Login.Packets;

[PacketOpcode(1, 9)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct CmsgEnterGameRequest
{
    public const uint OpcodeId = Opcodes.CmsgEnterGameRequest;

    public const int WireSize = 40;

    public readonly byte SlotIndex;

    public readonly SessionTokenBuffer SessionToken;

    public readonly PadBuffer Pad;

    public readonly uint VersionToken;

    [InlineArray(33)]
    public struct SessionTokenBuffer
    {
        private byte _element0;
    }

    [InlineArray(2)]
    public struct PadBuffer
    {
        private byte _element0;
    }
}