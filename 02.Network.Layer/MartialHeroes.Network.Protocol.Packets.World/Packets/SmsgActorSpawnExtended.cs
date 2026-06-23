using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;
using MartialHeroes.Network.Protocol.Core.Packets;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(5, 1)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgActorSpawnExtended
{
    public const uint OpcodeId = Opcodes.SmsgActorSpawnExtended;

    public const int WireSize = 912;


    private const int
        SdHpLowOffset = 0x3C;

    private const int
        SdHpHighOffset = 0x40;


    public readonly byte Sort;

    private readonly byte _pad0_0;

    private readonly byte _pad0_1;
    private readonly byte _pad0_2;

    public readonly uint ActorId;

    public readonly byte TitleState;

    public readonly byte TitleSlot;

    public readonly byte RelationFlag;

    private readonly byte _pad1;

    public readonly SpawnDescriptorBuffer SpawnDescriptor;

    public readonly TrailerBuffer Trailer;


    public ActorSort SortKind => (ActorSort)Sort;

    public bool IsPlayerBranch => Sort == 1;

    public readonly long PlayerHpQword
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            ref readonly var descBase =
                ref Unsafe.As<SpawnDescriptorBuffer, byte>(ref Unsafe.AsRef(in SpawnDescriptor));
            var span = MemoryMarshal.CreateReadOnlySpan(in descBase, 880);
            return BinaryPrimitives.ReadInt64LittleEndian(span[SdHpLowOffset..]);
        }
    }

    [InlineArray(880)]
    public struct SpawnDescriptorBuffer
    {
        private byte _element0;
    }

    [InlineArray(20)]
    public struct TrailerBuffer
    {
        private byte _element0;
    }
}