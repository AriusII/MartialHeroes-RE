using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgConfirmDialogState
{
    public const uint OpcodeId = 0x2007e;

    public const int Size = 1;

    public byte State;
}
