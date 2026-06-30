using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(5, 73)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgQuestComplete
{
    public const uint OpcodeId = Opcodes.SmsgQuestComplete;

    public const int WireSize = 344;

    public readonly HeadBuffer Head;

    public readonly uint Apply;

    public readonly byte RewardState;

    public readonly BodyRemainderBuffer BodyRemainder;

    [InlineArray(8)]
    public struct HeadBuffer
    {
        private byte _element0;
    }

    [InlineArray(331)]
    public struct BodyRemainderBuffer
    {
        private byte _element0;
    }
}
