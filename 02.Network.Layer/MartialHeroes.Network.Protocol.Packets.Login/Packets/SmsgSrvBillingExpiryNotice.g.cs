using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets;

[PacketOpcode(1, 19)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SmsgSrvBillingExpiryNotice
{
    public const uint OpcodeId = 0x10013;

    public const int Size = 22;

    public byte Dummy;
    public byte NoticeType;
    public SubscriptionCodeBuffer SubscriptionCode;
    public uint TimeLeft;

    [InlineArray(16)]
    public struct SubscriptionCodeBuffer
    {
        private byte _element0;
    }

}
