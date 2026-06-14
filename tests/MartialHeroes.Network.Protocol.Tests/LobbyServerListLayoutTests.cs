// Layout + decode guards for the LOBBY (login-server) protocol records (NOT a (major:minor)
// opcode — a separate synchronous TCP surface). Each struct's runtime size must equal its spec
// byte count, Pack=1 must hold (no padding drift), and a synthetic DECOMPRESSED server-list buffer
// must iterate `count` entries to the expected per-field values via the zero-alloc reader.
// spec: Docs/RE/packets/lobby.yaml. CAPTURE-UNVERIFIED layouts (capture_verified: false).

using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Packets;

namespace MartialHeroes.Network.Protocol.Tests;

public sealed class LobbyServerListLayoutTests
{
    // -------------------------------------------------------------------------
    // Size guards (Marshal.SizeOf == spec size; Unsafe.SizeOf agrees => Pack=1, no padding)
    // -------------------------------------------------------------------------

    [Fact] // spec: Docs/RE/packets/lobby.yaml (8-byte wrapper: u32 size + u16 count + u16 reserved)
    public void LobbyFrameWrapper_size_is_8()
    {
        Assert.Equal(8, Marshal.SizeOf<LobbyFrameWrapper>());
        Assert.Equal(8, Unsafe.SizeOf<LobbyFrameWrapper>());
        Assert.Equal(8, LobbyFrameWrapper.WireSize);
    }

    [Fact] // spec: Docs/RE/packets/lobby.yaml (8-byte entry: i16 id/status/population/flag)
    public void LobbyServerEntry_size_is_8()
    {
        Assert.Equal(8, Marshal.SizeOf<LobbyServerEntry>());
        Assert.Equal(8, Unsafe.SizeOf<LobbyServerEntry>());
        Assert.Equal(8, LobbyServerEntry.WireSize);
    }

    [Fact] // spec: Docs/RE/packets/lobby.yaml (30-byte ASCII endpoint token)
    public void LobbyChannelEndpoint_size_is_30()
    {
        Assert.Equal(30, Marshal.SizeOf<LobbyChannelEndpointToken>());
        Assert.Equal(30, Unsafe.SizeOf<LobbyChannelEndpointToken>());
        Assert.Equal(30, LobbyChannelEndpointToken.WireSize);
    }

    // -------------------------------------------------------------------------
    // Offset guards (Marshal.OffsetOf == specced byte offset)
    // -------------------------------------------------------------------------

    [Fact] // spec: Docs/RE/packets/lobby.yaml (size@+0, count@+4, reserved@+6)
    public void LobbyFrameWrapper_field_offsets()
    {
        Assert.Equal(0x00, (int)Marshal.OffsetOf<LobbyFrameWrapper>(nameof(LobbyFrameWrapper.Size)));
        Assert.Equal(0x04, (int)Marshal.OffsetOf<LobbyFrameWrapper>(nameof(LobbyFrameWrapper.Count)));
        Assert.Equal(0x06, (int)Marshal.OffsetOf<LobbyFrameWrapper>(nameof(LobbyFrameWrapper.Reserved)));
    }

    [Fact] // spec: Docs/RE/packets/lobby.yaml (id@+0, status@+2, population@+4, flag@+6)
    public void LobbyServerEntry_field_offsets()
    {
        Assert.Equal(0x00, (int)Marshal.OffsetOf<LobbyServerEntry>(nameof(LobbyServerEntry.Id)));
        Assert.Equal(0x02, (int)Marshal.OffsetOf<LobbyServerEntry>(nameof(LobbyServerEntry.Status)));
        Assert.Equal(0x04, (int)Marshal.OffsetOf<LobbyServerEntry>(nameof(LobbyServerEntry.Population)));
        Assert.Equal(0x06, (int)Marshal.OffsetOf<LobbyServerEntry>(nameof(LobbyServerEntry.Flag)));
    }

    // -------------------------------------------------------------------------
    // Wrapper decode
    // -------------------------------------------------------------------------

    [Fact] // spec: Docs/RE/packets/lobby.yaml (COMMON LOBBY FRAME WRAPPER)
    public void LobbyFrameWrapper_decodes_known_bytes()
    {
        Span<byte> buf = stackalloc byte[LobbyFrameWrapper.WireSize];
        BinaryPrimitives.WriteUInt32LittleEndian(buf[0x00..], 0x20u); // Size = 32 total
        BinaryPrimitives.WriteUInt16LittleEndian(buf[0x04..], 3); // Count = 3 entries
        BinaryPrimitives.WriteUInt16LittleEndian(buf[0x06..], 0xBEEF); // Reserved (unused)

        LobbyFrameWrapper w = LobbyFrameWrapper.Read(buf);
        Assert.Equal(0x20u, w.Size);
        Assert.Equal((ushort)3, w.Count);
        Assert.Equal((ushort)0xBEEF, w.Reserved);
        Assert.Equal(0x20 - 8, w.PayloadLength); // Size - 8
    }

    // -------------------------------------------------------------------------
    // Server-list iteration: synthetic decompressed buffer -> `count` entries decode correctly
    // -------------------------------------------------------------------------

    [Fact] // spec: Docs/RE/packets/lobby.yaml (RECORD SHAPE A; count is in the wrapper)
    public void LobbyServerListReader_iterates_count_entries()
    {
        const int count = 3;
        // 8-byte wrapper + count * 8-byte entries (the wrapper's "size" measures the COMPRESSED
        // frame; the reader works on the DECOMPRESSED buffer and bounds by count + available bytes).
        Span<byte> buf = stackalloc byte[LobbyFrameWrapper.WireSize + (count * LobbyServerEntry.WireSize)];
        BinaryPrimitives.WriteUInt32LittleEndian(buf[0x00..], 0); // Size (compressed; irrelevant here)
        BinaryPrimitives.WriteUInt16LittleEndian(buf[0x04..], count); // Count = 3
        BinaryPrimitives.WriteUInt16LittleEndian(buf[0x06..], 0); // Reserved

        // Three distinct entries. id/status/population/flag at +0/+2/+4/+6 within each 8-byte slot.
        WriteEntry(buf, 0, id: 100, status: 0, population: 1300, flag: 1); // available + numeric heavy
        WriteEntry(buf, 1, id: 42, status: 3, population: 24, flag: 0); // special-3 == 24 branch
        WriteEntry(buf, 2, id: -7, status: 17, population: 0, flag: 0); // caption-array, signed id

        var reader = new LobbyServerListReader(buf);
        Assert.Equal(count, reader.Count);

        ref readonly LobbyServerEntry e0 = ref reader[0];
        Assert.Equal((short)100, e0.Id);
        Assert.Equal((short)0, e0.Status);
        Assert.Equal((short)1300, e0.Population);
        Assert.Equal((short)1, e0.Flag);

        ref readonly LobbyServerEntry e1 = ref reader[1];
        Assert.Equal((short)42, e1.Id);
        Assert.Equal((short)3, e1.Status);
        Assert.Equal((short)24, e1.Population);
        Assert.Equal((short)0, e1.Flag);

        ref readonly LobbyServerEntry e2 = ref reader[2];
        Assert.Equal((short)-7, e2.Id); // signed id round-trips
        Assert.Equal((short)17, e2.Status);
        Assert.Equal((short)0, e2.Population);
        Assert.Equal((short)0, e2.Flag);
    }

    [Fact] // spec: Docs/RE/packets/lobby.yaml — reader clamps count to the bytes actually available.
    public void LobbyServerListReader_clamps_count_to_available_bytes()
    {
        // Wrapper claims 5 entries but only 2 entries' worth of bytes follow.
        Span<byte> buf = stackalloc byte[LobbyFrameWrapper.WireSize + (2 * LobbyServerEntry.WireSize)];
        BinaryPrimitives.WriteUInt16LittleEndian(buf[0x04..], 5); // Count = 5 (over-claim)
        WriteEntry(buf, 0, id: 1, status: 0, population: 0, flag: 0);
        WriteEntry(buf, 1, id: 2, status: 0, population: 0, flag: 0);

        var reader = new LobbyServerListReader(buf);
        Assert.Equal(2, reader.Count); // clamped to available, not the over-claimed 5
        Assert.Equal((short)1, reader[0].Id);
        Assert.Equal((short)2, reader[1].Id);

        // Index past the clamped count throws — a ref struct can't be captured in a lambda, so
        // probe with an explicit try/catch instead of Assert.Throws.
        bool threw = false;
        try
        {
            _ = reader[2].Id;
        }
        catch (ArgumentOutOfRangeException)
        {
            threw = true;
        }

        Assert.True(threw);
    }

    [Fact] // spec: Docs/RE/packets/lobby.yaml — an empty list (count 0) iterates zero entries.
    public void LobbyServerListReader_handles_empty_list()
    {
        Span<byte> buf = stackalloc byte[LobbyFrameWrapper.WireSize]; // wrapper only, count = 0
        var reader = new LobbyServerListReader(buf);
        Assert.Equal(0, reader.Count);
    }

    // -------------------------------------------------------------------------
    // Channel endpoint: first 30 bytes copied verbatim as an opaque ASCII token
    // -------------------------------------------------------------------------

    [Fact] // spec: Docs/RE/packets/lobby.yaml (RECORD SHAPE B — char[30] endpoint token)
    public void LobbyChannelEndpoint_copies_30_bytes_verbatim()
    {
        Span<byte> buf = stackalloc byte[LobbyChannelEndpointToken.WireSize];
        ReadOnlySpan<byte> token = "127.0.0.1:9000\0"u8;
        token.CopyTo(buf);

        ref readonly LobbyChannelEndpointToken ep = ref MemoryMarshal.AsRef<LobbyChannelEndpointToken>(buf);
        Assert.Equal((byte)'1', ep.Endpoint[0]);
        Assert.Equal((byte)'2', ep.Endpoint[1]);
        Assert.Equal((byte)':', ep.Endpoint[9]);
        Assert.Equal((byte)'9', ep.Endpoint[10]);
        Assert.Equal((byte)0, ep.Endpoint[14]); // NUL within the field
        Assert.Equal((byte)0, ep.Endpoint[29]); // trailing zero-fill
    }

    private static void WriteEntry(Span<byte> buf, int index, short id, short status, short population, short flag)
    {
        Span<byte> slot = buf.Slice(
            LobbyFrameWrapper.WireSize + (index * LobbyServerEntry.WireSize),
            LobbyServerEntry.WireSize);
        BinaryPrimitives.WriteInt16LittleEndian(slot[0x00..], id);
        BinaryPrimitives.WriteInt16LittleEndian(slot[0x02..], status);
        BinaryPrimitives.WriteInt16LittleEndian(slot[0x04..], population);
        BinaryPrimitives.WriteInt16LittleEndian(slot[0x06..], flag);
    }
}
