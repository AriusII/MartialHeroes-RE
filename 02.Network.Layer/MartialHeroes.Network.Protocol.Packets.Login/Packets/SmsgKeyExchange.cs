
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.Login.Packets;

[PacketOpcode(0, 0)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgKeyExchange
{
    public const uint OpcodeId = Opcodes.SmsgKeyExchange;

    public const int WireSize = 62;


    public readonly RsaKeyBlobBuffer RsaKeyBlob;

    public readonly uint ServerScalarA;

    public readonly uint ServerScalarB;

    [InlineArray(54)]
    public struct RsaKeyBlobBuffer
    {
        private byte _element0;
    }
}