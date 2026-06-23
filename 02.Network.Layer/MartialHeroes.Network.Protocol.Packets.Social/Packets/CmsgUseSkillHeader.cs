using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.Social.Packets;

[PacketOpcode(2, 52)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct CmsgUseSkillHeader
{
    public const uint OpcodeId = Opcodes.CmsgUseSkill;

    public const int HeaderSize = 24;

    public readonly byte SkillSlot;

    private readonly byte _pad0_0;

    private readonly byte _pad0_1;
    private readonly byte _pad0_2;

    public readonly uint AimMode;

    public readonly float AimScale;

    public readonly float AimX;

    public readonly float AimZ;

    public readonly ushort CountA;

    public readonly ushort CountB;
}