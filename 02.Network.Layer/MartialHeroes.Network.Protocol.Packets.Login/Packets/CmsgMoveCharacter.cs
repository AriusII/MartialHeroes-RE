
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.Login.Packets;

[PacketOpcode(1, 14)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct CmsgMoveCharacter
{
    public const uint OpcodeId = Opcodes.CmsgMoveCharacter;

    public const int WireSize = 1;

    public readonly byte SlotIndex;
}