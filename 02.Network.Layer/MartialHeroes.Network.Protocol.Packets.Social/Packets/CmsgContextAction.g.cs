using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgContextAction
{
    public const uint OpcodeId = 0x2002a;

    public const int Size = 44;

    public byte Mode;
    public Rest0Buffer Rest0;
    public WordsBuffer Words;

    [InlineArray(3)]
    public struct Rest0Buffer
    {
        private byte _element0;
    }

    [InlineArray(40)]
    public struct WordsBuffer
    {
        private byte _element0;
    }

}
