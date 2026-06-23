
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Core;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct FrameHeader
{
    public const int Size = 8;

    public uint FrameSize { get; }

    public ushort Major { get; }

    public ushort Minor { get; }

    public FrameHeader(uint frameSize, ushort major, ushort minor)
    {
        FrameSize = frameSize;
        Major = major;
        Minor = minor;
    }

    public PacketOpcode Opcode => new(Major, Minor);

    public uint PackedOpcode => ((uint)Major << 16) | Minor;

    public int PayloadLength => (int)FrameSize - Size;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FrameHeader Read(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < Size)
            throw new ArgumentOutOfRangeException(
                nameof(frame), frame.Length, $"A frame header requires at least {Size} bytes.");

        var frameSize = BinaryPrimitives.ReadUInt32LittleEndian(frame);
        var major = BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(4, 2));
        var minor = BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(6, 2));
        return new FrameHeader(frameSize, major, minor);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlySpan<byte> Payload(ReadOnlySpan<byte> frame)
    {
        return frame.Slice(Size);
    }
}