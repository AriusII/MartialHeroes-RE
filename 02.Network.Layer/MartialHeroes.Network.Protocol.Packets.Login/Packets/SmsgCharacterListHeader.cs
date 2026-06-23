using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.Login.Packets;

[PacketOpcode(3, 1)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgCharacterListHeader
{
    public const uint OpcodeId = Opcodes.SmsgCharacterList;

    public const int HeaderSize = 3;

    public const int SlotRecordSize = 981;

    public readonly byte ServerId;

    public readonly byte ChannelId;

    public readonly byte SlotMask;
}