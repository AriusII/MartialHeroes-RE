using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(4, 2)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgGameTickConfig
{
    public const uint OpcodeId = Opcodes.SmsgGameTickConfig;

    public const int WireSize = 52;

    public readonly Opaque1Buffer Opaque1;

    public readonly uint AreaId;

    public readonly float SpawnX;

    public readonly float SpawnZ;

    public readonly Opaque2Buffer Opaque2;

    public readonly ushort TradeTick;

    public readonly Opaque3Buffer Opaque3;

    public readonly uint BattleState;

    [InlineArray(8)]
    public struct Opaque1Buffer
    {
        private byte _element0;
    }

    [InlineArray(20)]
    public struct Opaque2Buffer
    {
        private byte _element0;
    }

    [InlineArray(6)]
    public struct Opaque3Buffer
    {
        private byte _element0;
    }
}