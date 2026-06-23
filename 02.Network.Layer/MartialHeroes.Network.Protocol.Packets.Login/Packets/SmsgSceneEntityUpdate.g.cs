
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.Login.Packets;

[PacketOpcode(3, 4)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgSceneEntityUpdate
{
    public const uint OpcodeId = Opcodes.SmsgSceneEntityUpdate;

    public const int HeaderSize = 3;

    public readonly byte ServerId;

    public readonly byte ChannelId;

    public readonly byte SlotMask;
}
