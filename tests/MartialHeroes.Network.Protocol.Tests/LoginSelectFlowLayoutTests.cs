// Layout + decode guards for the CharacterMgmt C2S request structs (major 1, minors 0/6/7/9/13/14)
// implemented from the freshly-promoted cmsg_char_* / cmsg_logout specs. Each struct's runtime size
// must equal its spec size, every field must land at its specced offset (Marshal.OffsetOf), Pack=1
// must hold (no padding drift), and a synthetic byte buffer must MemoryMarshal-reinterpret to the
// expected per-offset values. Spec sources cited per assertion. CAPTURE-UNVERIFIED layouts
// (capture_verified: false / static inference).

using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol;
using MartialHeroes.Network.Protocol.Packets;
using MartialHeroes.Network.Protocol.Routing;

namespace MartialHeroes.Network.Protocol.Tests;

public sealed class LoginSelectFlowLayoutTests
{
    // -------------------------------------------------------------------------
    // Opcode constants reconcile against opcodes.md (packed major<<16 | minor)
    // -------------------------------------------------------------------------

    [Fact] // spec: Docs/RE/opcodes.md
    public void CharacterMgmt_opcode_constants_match_catalog()
    {
        // Fully qualified: the `Opcodes` static class lives inside the same-named namespace.
        Assert.Equal(0x10000u, Protocol.Opcodes.Opcodes.CmsgLogout); // 1/0
        Assert.Equal(0x10006u, Protocol.Opcodes.Opcodes.CmsgCreateCharacter); // 1/6
        Assert.Equal(0x10007u, Protocol.Opcodes.Opcodes.CmsgSelectCharacter); // 1/7
        Assert.Equal(0x10009u, Protocol.Opcodes.Opcodes.CmsgEnterGameRequest); // 1/9
        Assert.Equal(0x1000du, Protocol.Opcodes.Opcodes.CmsgRenameCharacter); // 1/13
        Assert.Equal(0x1000eu, Protocol.Opcodes.Opcodes.CmsgMoveCharacter); // 1/14

        // OpcodeId mirrors the catalog constant on every struct.
        Assert.Equal(Protocol.Opcodes.Opcodes.CmsgLogout, CmsgLogout.OpcodeId);
        Assert.Equal(Protocol.Opcodes.Opcodes.CmsgCreateCharacter, CmsgCreateCharacter.OpcodeId);
        Assert.Equal(Protocol.Opcodes.Opcodes.CmsgSelectCharacter, CmsgSelectCharacter.OpcodeId);
        Assert.Equal(Protocol.Opcodes.Opcodes.CmsgEnterGameRequest, CmsgEnterGameRequest.OpcodeId);
        Assert.Equal(Protocol.Opcodes.Opcodes.CmsgRenameCharacter, CmsgRenameCharacter.OpcodeId);
        Assert.Equal(Protocol.Opcodes.Opcodes.CmsgMoveCharacter, CmsgMoveCharacter.OpcodeId);
    }

    // -------------------------------------------------------------------------
    // Size guards (Unsafe.SizeOf == spec size; Marshal.SizeOf agrees => Pack=1, no padding)
    // -------------------------------------------------------------------------

    [Fact] // spec: Docs/RE/packets/cmsg_logout.yaml (size: 0 payload; struct floor = 1 byte)
    public void CmsgLogout_payload_size_is_0()
    {
        Assert.Equal(0, CmsgLogout.WireSize);
        // An empty Pack=1 struct has a 1-byte layout floor; no payload byte is sent/read.
        Assert.Equal(1, Unsafe.SizeOf<CmsgLogout>());
        Assert.Equal(1, Marshal.SizeOf<CmsgLogout>());
    }

    [Fact] // spec: Docs/RE/packets/cmsg_char_create.yaml (size: 52)
    public void CmsgCreateCharacter_size_is_52()
    {
        Assert.Equal(52, Marshal.SizeOf<CmsgCreateCharacter>());
        Assert.Equal(52, Unsafe.SizeOf<CmsgCreateCharacter>());
        Assert.Equal(52, CmsgCreateCharacter.WireSize);
    }

    [Fact] // spec: Docs/RE/packets/cmsg_char_select.yaml (size: 2)
    public void CmsgSelectCharacter_size_is_2()
    {
        Assert.Equal(2, Marshal.SizeOf<CmsgSelectCharacter>());
        Assert.Equal(2, Unsafe.SizeOf<CmsgSelectCharacter>());
        Assert.Equal(2, CmsgSelectCharacter.WireSize);
    }

    [Fact] // spec: Docs/RE/packets/cmsg_char_enter.yaml (size: 40)
    public void CmsgEnterGameRequest_size_is_40()
    {
        Assert.Equal(40, Marshal.SizeOf<CmsgEnterGameRequest>());
        Assert.Equal(40, Unsafe.SizeOf<CmsgEnterGameRequest>());
        Assert.Equal(40, CmsgEnterGameRequest.WireSize);
    }

    [Fact] // spec: Docs/RE/packets/cmsg_char_rename.yaml (size: 18)
    public void CmsgRenameCharacter_size_is_18()
    {
        Assert.Equal(18, Marshal.SizeOf<CmsgRenameCharacter>());
        Assert.Equal(18, Unsafe.SizeOf<CmsgRenameCharacter>());
        Assert.Equal(18, CmsgRenameCharacter.WireSize);
    }

    [Fact] // spec: Docs/RE/packets/cmsg_char_move.yaml (size: 1)
    public void CmsgMoveCharacter_size_is_1()
    {
        Assert.Equal(1, Marshal.SizeOf<CmsgMoveCharacter>());
        Assert.Equal(1, Unsafe.SizeOf<CmsgMoveCharacter>());
        Assert.Equal(1, CmsgMoveCharacter.WireSize);
    }

    // -------------------------------------------------------------------------
    // Offset guards (Marshal.OffsetOf == specced byte offset)
    // -------------------------------------------------------------------------

    [Fact] // spec: Docs/RE/packets/cmsg_char_create.yaml
    public void CmsgCreateCharacter_field_offsets()
    {
        Assert.Equal(0x00, (int)Marshal.OffsetOf<CmsgCreateCharacter>(nameof(CmsgCreateCharacter.Name)));
        Assert.Equal(0x12, (int)Marshal.OffsetOf<CmsgCreateCharacter>(nameof(CmsgCreateCharacter.Face)));
        Assert.Equal(0x14, (int)Marshal.OffsetOf<CmsgCreateCharacter>(nameof(CmsgCreateCharacter.Sex)));
        Assert.Equal(0x16, (int)Marshal.OffsetOf<CmsgCreateCharacter>(nameof(CmsgCreateCharacter.HairOrReserved)));
        Assert.Equal(0x18, (int)Marshal.OffsetOf<CmsgCreateCharacter>(nameof(CmsgCreateCharacter.ClassInternalId)));
        Assert.Equal(0x1A, (int)Marshal.OffsetOf<CmsgCreateCharacter>(nameof(CmsgCreateCharacter.Reserved1A)));
        Assert.Equal(0x1C, (int)Marshal.OffsetOf<CmsgCreateCharacter>(nameof(CmsgCreateCharacter.Stat0)));
        Assert.Equal(0x20, (int)Marshal.OffsetOf<CmsgCreateCharacter>(nameof(CmsgCreateCharacter.Stat1)));
        Assert.Equal(0x24, (int)Marshal.OffsetOf<CmsgCreateCharacter>(nameof(CmsgCreateCharacter.Stat2)));
        Assert.Equal(0x28, (int)Marshal.OffsetOf<CmsgCreateCharacter>(nameof(CmsgCreateCharacter.Stat3)));
        Assert.Equal(0x2C, (int)Marshal.OffsetOf<CmsgCreateCharacter>(nameof(CmsgCreateCharacter.Stat4)));
        Assert.Equal(0x30, (int)Marshal.OffsetOf<CmsgCreateCharacter>(nameof(CmsgCreateCharacter.PointsRemaining)));
    }

    [Fact] // spec: Docs/RE/packets/cmsg_char_select.yaml
    public void CmsgSelectCharacter_field_offsets()
    {
        Assert.Equal(0, (int)Marshal.OffsetOf<CmsgSelectCharacter>(nameof(CmsgSelectCharacter.SlotIndex)));
        Assert.Equal(1, (int)Marshal.OffsetOf<CmsgSelectCharacter>(nameof(CmsgSelectCharacter.Mode)));
    }

    [Fact] // spec: Docs/RE/packets/cmsg_char_enter.yaml (1 + 33 + 2 + 4 = 40)
    public void CmsgEnterGameRequest_field_offsets()
    {
        Assert.Equal(0x00, (int)Marshal.OffsetOf<CmsgEnterGameRequest>(nameof(CmsgEnterGameRequest.SlotIndex)));
        Assert.Equal(0x01, (int)Marshal.OffsetOf<CmsgEnterGameRequest>(nameof(CmsgEnterGameRequest.SessionToken)));
        Assert.Equal(0x22, (int)Marshal.OffsetOf<CmsgEnterGameRequest>(nameof(CmsgEnterGameRequest.Pad)));
        Assert.Equal(0x24, (int)Marshal.OffsetOf<CmsgEnterGameRequest>(nameof(CmsgEnterGameRequest.VersionToken)));
    }

    [Fact] // spec: Docs/RE/packets/cmsg_char_rename.yaml (slot @0 + 17-byte name @1)
    public void CmsgRenameCharacter_field_offsets()
    {
        Assert.Equal(0, (int)Marshal.OffsetOf<CmsgRenameCharacter>(nameof(CmsgRenameCharacter.SlotIndex)));
        Assert.Equal(1, (int)Marshal.OffsetOf<CmsgRenameCharacter>(nameof(CmsgRenameCharacter.NewName)));
    }

    [Fact] // spec: Docs/RE/packets/cmsg_char_move.yaml
    public void CmsgMoveCharacter_field_offsets()
    {
        Assert.Equal(0, (int)Marshal.OffsetOf<CmsgMoveCharacter>(nameof(CmsgMoveCharacter.SlotIndex)));
    }

    // -------------------------------------------------------------------------
    // Decode guards (write known bytes -> MemoryMarshal reinterpret -> read fields back)
    // -------------------------------------------------------------------------

    [Fact] // spec: Docs/RE/packets/cmsg_char_select.yaml
    public void CmsgSelectCharacter_decodes_known_bytes()
    {
        Span<byte> body = stackalloc byte[CmsgSelectCharacter.WireSize];
        body[0x00] = 3; // SlotIndex
        body[0x01] = 1; // Mode = 1 (delete; 0 = select/view) — delete overloads 1/7. spec: cmsg_char_select.yaml

        ref readonly CmsgSelectCharacter p = ref MemoryMarshal.AsRef<CmsgSelectCharacter>(body);
        Assert.Equal((byte)3, p.SlotIndex);
        Assert.Equal((byte)1, p.Mode);
    }

    [Fact] // spec: Docs/RE/packets/cmsg_char_enter.yaml
    public void CmsgEnterGameRequest_decodes_known_bytes()
    {
        Span<byte> body = stackalloc byte[CmsgEnterGameRequest.WireSize];
        body[0x00] = 4; // SlotIndex
        // "v1.2.3\0" ASCII into the 33-byte session token; rest stays zero.
        body[0x01] = (byte)'v';
        body[0x02] = (byte)'1';
        body[0x03] = (byte)'.';
        body[0x04] = (byte)'2';
        // Pad @0x22..0x23 stays zero.
        BinaryPrimitives.WriteUInt32LittleEndian(body[0x24..], 0xABCD1234u); // VersionToken

        ref readonly CmsgEnterGameRequest p = ref MemoryMarshal.AsRef<CmsgEnterGameRequest>(body);
        Assert.Equal((byte)4, p.SlotIndex);
        Assert.Equal((byte)'v', p.SessionToken[0]);
        Assert.Equal((byte)'1', p.SessionToken[1]);
        Assert.Equal((byte)0, p.SessionToken[32]); // last token byte stays zero
        Assert.Equal((byte)0, p.Pad[0]);
        Assert.Equal((byte)0, p.Pad[1]);
        Assert.Equal(0xABCD1234u, p.VersionToken);
    }

    [Fact] // spec: Docs/RE/packets/cmsg_char_rename.yaml
    public void CmsgRenameCharacter_decodes_known_bytes()
    {
        Span<byte> body = stackalloc byte[CmsgRenameCharacter.WireSize];
        body[0x00] = 2; // SlotIndex
        // "hero7\0" ASCII into the 17-byte CP949 name buffer @0x01; the rest stays zero.
        body[0x01] = (byte)'h';
        body[0x02] = (byte)'e';
        body[0x03] = (byte)'r';
        body[0x04] = (byte)'o';
        body[0x05] = (byte)'7';
        // body[0x06..] = 0 (NUL terminator + padding)

        ref readonly CmsgRenameCharacter p = ref MemoryMarshal.AsRef<CmsgRenameCharacter>(body);
        Assert.Equal((byte)2, p.SlotIndex);
        Assert.Equal((byte)'h', p.NewName[0]);
        Assert.Equal((byte)'e', p.NewName[1]);
        Assert.Equal((byte)'r', p.NewName[2]);
        Assert.Equal((byte)'o', p.NewName[3]);
        Assert.Equal((byte)'7', p.NewName[4]);
        Assert.Equal((byte)0, p.NewName[5]); // NUL terminator
        Assert.Equal((byte)0, p.NewName[16]); // last padding byte stays zero
    }

    [Fact] // spec: Docs/RE/packets/cmsg_char_move.yaml
    public void CmsgMoveCharacter_decodes_known_bytes()
    {
        Span<byte> body = stackalloc byte[CmsgMoveCharacter.WireSize];
        body[0x00] = 2; // SlotIndex

        ref readonly CmsgMoveCharacter p = ref MemoryMarshal.AsRef<CmsgMoveCharacter>(body);
        Assert.Equal((byte)2, p.SlotIndex);
    }

    [Fact] // spec: Docs/RE/packets/cmsg_char_create.yaml
    public void CmsgCreateCharacter_decodes_known_bytes()
    {
        Span<byte> body = stackalloc byte[CmsgCreateCharacter.WireSize];
        body[0] = (byte)'m';
        body[1] = (byte)'h';
        BinaryPrimitives.WriteUInt16LittleEndian(body[0x12..], 7);
        BinaryPrimitives.WriteUInt16LittleEndian(body[0x14..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(body[0x16..], 2);
        BinaryPrimitives.WriteUInt16LittleEndian(body[0x18..], 4);
        BinaryPrimitives.WriteUInt32LittleEndian(body[0x1C..], 10);
        BinaryPrimitives.WriteUInt32LittleEndian(body[0x20..], 11);
        BinaryPrimitives.WriteUInt32LittleEndian(body[0x24..], 12);
        BinaryPrimitives.WriteUInt32LittleEndian(body[0x28..], 13);
        BinaryPrimitives.WriteUInt32LittleEndian(body[0x2C..], 14);
        BinaryPrimitives.WriteUInt32LittleEndian(body[0x30..], 5);

        ref readonly CmsgCreateCharacter p = ref MemoryMarshal.AsRef<CmsgCreateCharacter>(body);
        Assert.Equal((byte)'m', p.Name[0]);
        Assert.Equal((byte)'h', p.Name[1]);
        Assert.Equal((ushort)7, p.Face);
        Assert.Equal((ushort)1, p.Sex);
        Assert.Equal((ushort)2, p.HairOrReserved);
        Assert.Equal((ushort)4, p.ClassInternalId);
        Assert.Equal(10u, p.Stat0);
        Assert.Equal(11u, p.Stat1);
        Assert.Equal(12u, p.Stat2);
        Assert.Equal(13u, p.Stat3);
        Assert.Equal(14u, p.Stat4);
        Assert.Equal(5u, p.PointsRemaining);
    }

    // -------------------------------------------------------------------------
    // Wire-size routing map: every major-1 char-mgmt request resolves to its struct size.
    // -------------------------------------------------------------------------

    [Theory] // spec: Docs/RE/opcodes.md + the cmsg_char_* / cmsg_logout specs.
    [InlineData(0x10000u, 0)] // 1/0  logout (header-only)
    [InlineData(0x10006u, 52)] // 1/6  create
    [InlineData(0x10007u, 2)] // 1/7  select
    [InlineData(0x10009u, 40)] // 1/9  enter
    [InlineData(0x1000du, 18)] // 1/13 rename
    [InlineData(0x1000eu, 1)] // 1/14 move
    public void CharacterMgmt_requests_resolve_to_struct_size(uint opcode, int expectedSize)
    {
        Assert.True(PacketWireSizes.TryGet(opcode, out int size, out bool isVar));
        Assert.Equal(expectedSize, size);
        Assert.False(isVar); // all six are fixed-size
    }
}