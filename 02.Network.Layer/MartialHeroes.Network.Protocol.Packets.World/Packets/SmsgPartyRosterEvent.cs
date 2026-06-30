using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(5, 21)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgPartyRosterEvent
{
    public const uint OpcodeId = Opcodes.SmsgPartyRosterEvent;

    public const int WireSize = 12;

    public readonly byte Event;

    public readonly byte MemberSlot;

    public readonly TrailingPadBuffer TrailingPad;

    [InlineArray(10)]
    public struct TrailingPadBuffer
    {
        private byte _element0;
    }
}