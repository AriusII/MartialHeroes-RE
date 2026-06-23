using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgSkillListSubmit
{
    public const uint OpcodeId = 0x20091;

    public uint Count;
    public RecordBuffer Record;

    [InlineArray(12)]
    public struct RecordBuffer
    {
        private byte _element0;
    }

}
