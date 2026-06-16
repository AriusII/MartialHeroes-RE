// FrameHeader round-trip + opcode decode. spec: Docs/RE/opcodes.md (8-byte LE header).

using System.Buffers.Binary;
using MartialHeroes.Network.Protocol;
using MartialHeroes.Network.Protocol.Opcodes;

namespace MartialHeroes.Network.Protocol.Tests;

public sealed class FrameHeaderTests
{
    [Fact] // spec: Docs/RE/opcodes.md + crypto.md §2 — size@+0 (u32 LE), major@+4, minor@+6, all LE.
    public void Read_decodes_little_endian_fields_and_opcode()
    {
        // Frame: size=48 (u32 @+0), major=5, minor=13 (the 5/13 movement opcode).
        Span<byte> frame = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(frame[..4], 48);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.Slice(4, 2), 5);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.Slice(6, 2), 13);

        FrameHeader header = FrameHeader.Read(frame);

        Assert.Equal(48u, header.FrameSize);
        Assert.Equal(5, header.Major);
        Assert.Equal(13, header.Minor);
        Assert.Equal(new PacketOpcode(5, 13), header.Opcode);
        Assert.Equal(0x5000du, header.PackedOpcode);
        Assert.Equal(Opcodes.Opcodes.SmsgActorMovementUpdate, header.PackedOpcode);
        Assert.Equal(48 - 8, header.PayloadLength);
    }

    [Fact] // spec: Docs/RE/opcodes.md + crypto.md §2 — size is a TRUE u32; bytes @+2..+3 are part of size.
    public void Read_treats_size_as_full_u32()
    {
        // A size value with non-zero high bytes (@+2..+3) must be read as part of the u32, not padding.
        // 0x00010008 = 65544 — the low word is 8 but the full value spans all four bytes.
        Span<byte> frame = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(frame[..4], 0x0001_0008u);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.Slice(4, 2), 4);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.Slice(6, 2), 1);

        FrameHeader header = FrameHeader.Read(frame);

        Assert.Equal(0x0001_0008u, header.FrameSize);
        Assert.Equal((int)0x0001_0008u - 8, header.PayloadLength);
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