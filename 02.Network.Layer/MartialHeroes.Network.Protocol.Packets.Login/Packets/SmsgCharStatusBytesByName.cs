using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.Login.Packets;

[PacketOpcode(3, 23)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SmsgCharStatusBytesByName
{
    public const uint OpcodeId = 0x30017;

    public const int WireSize = 28;


    public byte HasCustomText;

    public byte StatusCode;

    public Pad0Buffer Pad0;

    public CharacterNameBuffer CharacterName;

    public byte StatusValue;

    public byte Level;

    public byte Padding;

    [InlineArray(6)]
    public struct Pad0Buffer
    {
        private byte _element0;
    }

    [InlineArray(17)]
    public struct CharacterNameBuffer
    {
        private byte _element0;
    }
}