using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgPanelIdSubmit
{
    public const uint OpcodeId = 0x20080;

    public const int Size = 4;

    public uint PanelId;
}
