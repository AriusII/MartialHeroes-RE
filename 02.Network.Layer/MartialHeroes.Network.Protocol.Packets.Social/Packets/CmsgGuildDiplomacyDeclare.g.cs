using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgGuildDiplomacyDeclare
{
    public const uint OpcodeId = 0x20051;

    public const int Size = 18;

    public byte Action;
    public GuildNameBuffer GuildName;

    [InlineArray(17)]
    public struct GuildNameBuffer
    {
        private byte _element0;
    }

}
