using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgUseSkill
{
    public const uint OpcodeId = 0x20034;

    public byte SkillSlot;
    public byte Selector;
    public Pad0Buffer Pad0;
    public uint AimMode;
    public float AimScale;
    public float AimX;
    public float AimZ;
    public ushort CountA;
    public ushort CountB;

    [InlineArray(2)]
    public struct Pad0Buffer
    {
        private byte _element0;
    }

}
