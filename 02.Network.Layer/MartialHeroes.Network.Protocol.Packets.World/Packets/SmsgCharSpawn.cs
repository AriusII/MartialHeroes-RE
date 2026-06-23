
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;
using MartialHeroes.Network.Protocol.Core.Packets;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(5, 3)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgCharSpawn
{
    public const uint OpcodeId = Opcodes.SmsgCharSpawn;

    public const int WireSize = 908;


    private const int DescriptorOffset = 0x008;

    private const int TrailerOffset = DescriptorOffset + 0x370;


    public const int TrailerCloneByteOffset = 0;

    public const int TrailerSecondaryNameOffset = 1;

    public const int TrailerSecondaryNameLength = 17;

    public const int TrailerRelationByteOffset = 18;

    public const int TrailerTailByteOffset = 19;


    public readonly uint Sort;

    public readonly uint ActorId;

    public readonly SpawnDescriptorBuffer SpawnDescriptor;

    public readonly TrailerBuffer Trailer;


    public ActorSort SortKind => (ActorSort)(byte)Sort;

    public byte TrailerCloneByte
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            ref readonly var b = ref Unsafe.As<TrailerBuffer, byte>(ref Unsafe.AsRef(in Trailer));
            return Unsafe.Add(ref Unsafe.AsRef(in b), TrailerCloneByteOffset);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly ReadOnlySpan<byte> TrailerSecondaryName()
    {
        ref readonly var b = ref Unsafe.As<TrailerBuffer, byte>(ref Unsafe.AsRef(in Trailer));
        return MemoryMarshal.CreateReadOnlySpan(
            in Unsafe.Add(ref Unsafe.AsRef(in b), TrailerSecondaryNameOffset),
            TrailerSecondaryNameLength);
    }

    public byte TrailerRelationByte
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            ref readonly var b = ref Unsafe.As<TrailerBuffer, byte>(ref Unsafe.AsRef(in Trailer));
            return Unsafe.Add(ref Unsafe.AsRef(in b), TrailerRelationByteOffset);
        }
    }

    public byte TrailerTailByte
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            ref readonly var b = ref Unsafe.As<TrailerBuffer, byte>(ref Unsafe.AsRef(in Trailer));
            return Unsafe.Add(ref Unsafe.AsRef(in b), TrailerTailByteOffset);
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