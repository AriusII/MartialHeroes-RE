using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgQuestAction
{
    public const uint OpcodeId = 0x2001c;

    public const int Size = 12;

    public uint SubAction;
    public uint QuestId;
    public uint ParamA;
}
