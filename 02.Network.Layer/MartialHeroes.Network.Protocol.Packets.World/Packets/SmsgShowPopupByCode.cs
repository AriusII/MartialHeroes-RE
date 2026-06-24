using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(4, 500)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgShowPopupByCode
{
    public const uint OpcodeId = Opcodes.SmsgShowPopupByCode;

    public const int WireSize = 4;

    public readonly uint PopupCode;
}
