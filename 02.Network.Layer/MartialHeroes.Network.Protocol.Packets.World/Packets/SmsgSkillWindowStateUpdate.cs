using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(4, 102)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgSkillWindowStateUpdate
{
    public const uint OpcodeId = Opcodes.SmsgSkillWindowStateUpdate;

    public const int WireSize = 476;

    public const int StatBlockSize = 116;

    public const int BuffRecordCount = 30;

    public const int BuffRecordStride = 12;

    public readonly StatBlockBuffer StatBlock;


    public readonly uint Buff00Id;

    public readonly int Buff00X;

    public readonly int Buff00Y;

    public readonly uint Buff01Id;

    public readonly int Buff01X;
    public readonly int Buff01Y;

    public readonly uint Buff02Id;

    public readonly int Buff02X;
    public readonly int Buff02Y;

    public readonly uint Buff03Id;

    public readonly int Buff03X;
    public readonly int Buff03Y;

    public readonly uint Buff04Id;

    public readonly int Buff04X;
    public readonly int Buff04Y;

    public readonly uint Buff05Id;

    public readonly int Buff05X;
    public readonly int Buff05Y;

    public readonly uint Buff06Id;

    public readonly int Buff06X;
    public readonly int Buff06Y;

    public readonly uint Buff07Id;

    public readonly int Buff07X;
    public readonly int Buff07Y;

    public readonly uint Buff08Id;

    public readonly int Buff08X;
    public readonly int Buff08Y;

    public readonly uint Buff09Id;

    public readonly int Buff09X;
    public readonly int Buff09Y;

    public readonly uint Buff10Id;

    public readonly int Buff10X;
    public readonly int Buff10Y;

    public readonly uint Buff11Id;

    public readonly int Buff11X;
    public readonly int Buff11Y;

    public readonly uint Buff12Id;

    public readonly int Buff12X;
    public readonly int Buff12Y;

    public readonly uint Buff13Id;

    public readonly int Buff13X;
    public readonly int Buff13Y;

    public readonly uint Buff14Id;

    public readonly int Buff14X;
    public readonly int Buff14Y;

    public readonly uint Buff15Id;

    public readonly int Buff15X;
    public readonly int Buff15Y;

    public readonly uint Buff16Id;

    public readonly int Buff16X;
    public readonly int Buff16Y;

    public readonly uint Buff17Id;

    public readonly int Buff17X;
    public readonly int Buff17Y;

    public readonly uint Buff18Id;

    public readonly int Buff18X;
    public readonly int Buff18Y;

    public readonly uint Buff19Id;

    public readonly int Buff19X;
    public readonly int Buff19Y;

    public readonly uint Buff20Id;

    public readonly int Buff20X;
    public readonly int Buff20Y;

    public readonly uint Buff21Id;

    public readonly int Buff21X;
    public readonly int Buff21Y;

    public readonly uint Buff22Id;

    public readonly int Buff22X;
    public readonly int Buff22Y;

    public readonly uint Buff23Id;

    public readonly int Buff23X;
    public readonly int Buff23Y;

    public readonly uint Buff24Id;

    public readonly int Buff24X;
    public readonly int Buff24Y;

    public readonly uint Buff25Id;

    public readonly int Buff25X;
    public readonly int Buff25Y;

    public readonly uint Buff26Id;

    public readonly int Buff26X;
    public readonly int Buff26Y;

    public readonly uint Buff27Id;

    public readonly int Buff27X;
    public readonly int Buff27Y;

    public readonly uint Buff28Id;

    public readonly int Buff28X;
    public readonly int Buff28Y;

    public readonly uint Buff29Id;

    public readonly int Buff29X;
    public readonly int Buff29Y;

    [InlineArray(116)]
    public struct StatBlockBuffer
    {
        private byte _element0;
    }
}