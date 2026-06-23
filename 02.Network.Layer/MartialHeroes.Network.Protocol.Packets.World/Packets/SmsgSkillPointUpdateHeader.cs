using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(4, 150)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgSkillPointUpdateHeader
{
    public const uint OpcodeId = Opcodes.SmsgSkillPointUpdate;

    public const int HeaderSize = 16;

    public readonly byte Valid;

    private readonly byte _pad0;

    private readonly byte _pad1;
    private readonly byte _pad2;

    public readonly int IdKey;

    public readonly uint Mode;

    public readonly uint Value;
}