using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.Login.Packets;

[PacketOpcode(3, 6)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgRenameCharResult
{
    public const uint OpcodeId = Opcodes.SmsgRenameCharResult;

    public const int WireSize = 12;

    public readonly byte Result;

    public readonly byte ErrorCode;

    public readonly PadBuffer Pad;

    public readonly float PlacementValue0;

    public readonly float PlacementValue1;

    [InlineArray(2)]
    public struct PadBuffer
    {
        private byte _element0;
    }
}