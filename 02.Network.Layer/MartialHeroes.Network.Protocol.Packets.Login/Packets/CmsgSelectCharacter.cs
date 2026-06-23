using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.Login.Packets;

[PacketOpcode(1, 7)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct CmsgSelectCharacterSlot
{
    public const uint OpcodeId = Opcodes.CmsgSelectCharacterSlot;

    public const int WireSize = 2;

    public readonly byte SlotIndex;

    public readonly byte Mode;
}