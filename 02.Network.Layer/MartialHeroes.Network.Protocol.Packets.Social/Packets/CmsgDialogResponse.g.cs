using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgDialogResponse
{
    public const uint OpcodeId = 0x20003;

    public const int Size = 2;

    public ushort ResponseCode;
}
