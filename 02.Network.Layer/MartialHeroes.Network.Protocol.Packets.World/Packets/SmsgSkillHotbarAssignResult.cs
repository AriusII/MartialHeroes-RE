
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(4, 41)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgSkillHotbarAssignResult
{
    public const uint OpcodeId = Opcodes.SmsgSkillHotbarAssignResult;

    public const int WireSize = 24;

    public readonly uint Header;

    public readonly uint ActorId;

    public readonly byte Gate;

    public readonly byte ResultCode;

    private readonly byte _pad0;

    private readonly byte _pad1;

    public readonly int HotbarSlotEcho;

    public readonly int SkillIdEcho;

    public readonly uint SkillPointPool;
}