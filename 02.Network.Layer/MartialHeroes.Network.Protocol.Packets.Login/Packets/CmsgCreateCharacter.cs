using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.Login.Packets;

[PacketOpcode(1, 6)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct CmsgCreateCharacter
{
    public const uint OpcodeId = Opcodes.CmsgCreateCharacter;

    public const int WireSize = 52;


    public readonly NameBuffer Name;

    public readonly ushort Face;

    public readonly ushort AppearanceA;

    public readonly ushort AppearanceB;

    public readonly ushort ClassInternalId;

    public readonly Reserved1ABuffer Reserved1A;

    public readonly uint Stat0;

    public readonly uint Stat1;

    public readonly uint Stat2;

    public readonly uint Stat3;

    public readonly uint Stat4;

    public readonly uint PointsRemaining;

    [InlineArray(18)]
    public struct NameBuffer
    {
        private byte _element0;
    }

    [InlineArray(2)]
    public struct Reserved1ABuffer
    {
        private byte _element0;
    }
}