// FrameHeader round-trip + opcode decode. spec: Docs/RE/opcodes.md (8-byte LE header).

using System.Buffers.Binary;
using MartialHeroes.Network.Protocol;
using MartialHeroes.Network.Protocol.Opcodes;

namespace MartialHeroes.Network.Protocol.Tests;

public sealed class FrameHeaderTests
{
    [Fact] // spec: Docs/RE/opcodes.md — size@+0, high@+2, major@+4, minor@+6, all LE.
    public void Read_decodes_little_endian_fields_and_opcode()
    {
        // Frame: size=48, sizeHigh=0, major=5, minor=13 (the 5/13 movement opcode).
        Span<byte> frame = stackalloc byte[8];
        BinaryPrimitives.WriteUInt16LittleEndian(frame[..2], 48);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.Slice(2, 2), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.Slice(4, 2), 5);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.Slice(6, 2), 13);

        FrameHeader header = FrameHeader.Read(frame);

        Assert.Equal(48, header.FrameSize);
        Assert.Equal(0, header.SizeHigh);
        Assert.Equal(5, header.Major);
        Assert.Equal(13, header.Minor);
        Assert.Equal(new PacketOpcode(5, 13), header.Opcode);
        Assert.Equal(0x5000du, header.PackedOpcode);
        Assert.Equal(Opcodes.Opcodes.SmsgActorMovementUpdate, header.PackedOpcode);
        Assert.Equal(48 - 8, header.PayloadLength);
    }

    [Fact] // spec: Docs/RE/opcodes.md — payload begins at frame +8.
    public void Payload_returns_zero_copy_slice_after_header()
    {
        Span<byte> frame = stackalloc byte[12];
        // header bytes [0..7], payload [8..11]
        frame[8] = 0xAA;
        frame[9] = 0xBB;
        frame[10] = 0xCC;
        frame[11] = 0xDD;

        ReadOnlySpan<byte> payload = FrameHeader.Payload(frame);

        Assert.Equal(4, payload.Length);
        Assert.Equal(0xAA, payload[0]);
        Assert.Equal(0xDD, payload[3]);
    }

    [Fact]
    public void Read_throws_when_frame_shorter_than_header()
    {
        byte[] tooShort = new byte[7];
        Assert.Throws<ArgumentOutOfRangeException>(() => FrameHeader.Read(tooShort));
    }

    [Fact] // spec: Docs/RE/opcodes.md — packed = (major << 16) | minor.
    public void PacketOpcode_packs_and_unpacks()
    {
        var op = new PacketOpcode(5, 13);
        Assert.Equal(0x5000du, op.Packed);
        Assert.Equal(op, PacketOpcode.FromPacked(0x5000d));
    }
}