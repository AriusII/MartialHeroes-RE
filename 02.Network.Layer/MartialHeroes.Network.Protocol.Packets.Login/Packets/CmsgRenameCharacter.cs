
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.Login.Packets;

[PacketOpcode(1, 13)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct CmsgRenameCharacter
{
    public const uint OpcodeId = Opcodes.CmsgRenameCharacter;

    public const int WireSize = 18;

    public readonly byte SlotIndex;

    public readonly NewNameBuffer NewName;

    [InlineArray(17)]
    public struct NewNameBuffer
    {
        private byte _element0;
    }
}