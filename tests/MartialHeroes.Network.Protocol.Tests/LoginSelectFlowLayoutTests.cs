// Layout + decode guards for the login/select-flow packet structs added in Phase E4 (network
// end-to-end). Each struct's runtime size must equal its spec size, every field must land at its
// specced offset (Marshal.OffsetOf), Pack=1 must hold (no padding drift), and a synthetic byte
// buffer must MemoryMarshal-reinterpret to the expected per-offset values. Spec sources cited per
// assertion. CAPTURE-UNVERIFIED layouts (capture_verified: false / static inference).

using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Opcodes;
using MartialHeroes.Network.Protocol.Packets;

namespace MartialHeroes.Network.Protocol.Tests;

public sealed class LoginSelectFlowLayoutTests
{
    // -------------------------------------------------------------------------
    // Opcode constants reconcile against opcodes.md (packed major<<16 | minor)
    // -------------------------------------------------------------------------

    [Fact] // spec: Docs/RE/opcodes.md
    public void LoginSelectFlow_opcode_constants_match_catalog()
    {
        // Fully qualified: the `Opcodes` static class lives inside the same-named namespace.
        Assert.Equal(0x10006u, Protocol.Opcodes.Opcodes.CmsgLoginRequest); // 1/6
        Assert.Equal(0x10007u, Protocol.Opcodes.Opcodes.CmsgSelectCharacter); // 1/7
        Assert.Equal(0x1000du, Protocol.Opcodes.Opcodes.CmsgRenameCharacter); // 1/13
        Assert.Equal(0x1000eu, Protocol.Opcodes.Opcodes.CmsgDeleteCharacter); // 1/14
        Assert.Equal(0x30007u, Protocol.Opcodes.Opcodes.SmsgCharSpawnResult); // 3/7

        // OpcodeId mirrors the catalog constant on every struct.
        Assert.Equal(Protocol.Opcodes.Opcodes.CmsgLoginRequest, CmsgLoginRequest.OpcodeId);
        Assert.Equal(Protocol.Opcodes.Opcodes.CmsgSelectCharacter, CmsgSelectCharacter.OpcodeId);
        Assert.Equal(Protocol.Opcodes.Opcodes.CmsgRenameCharacter, CmsgRenameCharacter.OpcodeId);
        Assert.Equal(Protocol.Opcodes.Opcodes.CmsgDeleteCharacter, CmsgDeleteCharacter.OpcodeId);
        Assert.Equal(Protocol.Opcodes.Opcodes.SmsgCharSpawnResult, SmsgCharSpawnResult.OpcodeId);
    }

    // -------------------------------------------------------------------------
    // Size guards (Marshal.SizeOf == spec size; Unsafe.SizeOf agrees => Pack=1, no padding)
    // -------------------------------------------------------------------------

    [Fact] // spec: Docs/RE/packets/1-7_select_character.yaml (size: 2)
    public void CmsgSelectCharacter_size_is_2()
    {
        Assert.Equal(2, Marshal.SizeOf<CmsgSelectCharacter>());
        Assert.Equal(2, Unsafe.SizeOf<CmsgSelectCharacter>());
        Assert.Equal(2, CmsgSelectCharacter.WireSize);
    }

    [Fact] // spec: Docs/RE/specs/login_flow.md §5.3 (16-byte block, opcode 3/7)
    public void SmsgCharSpawnResult_size_is_16()
    {
        Assert.Equal(16, Marshal.SizeOf<SmsgCharSpawnResult>());
        Assert.Equal(16, Unsafe.SizeOf<SmsgCharSpawnResult>());
        Assert.Equal(16, SmsgCharSpawnResult.WireSize);
    }

    [Fact] // spec: Docs/RE/packets/1-13_rename_character.yaml (size: 18)
    public void CmsgRenameCharacter_size_is_18()
    {
        Assert.Equal(18, Marshal.SizeOf<CmsgRenameCharacter>());
        Assert.Equal(18, Unsafe.SizeOf<CmsgRenameCharacter>());
        Assert.Equal(18, CmsgRenameCharacter.WireSize);
    }

    [Fact] // spec: Docs/RE/packets/1-14_delete_character.yaml (size: 1)
    public void CmsgDeleteCharacter_size_is_1()
    {
        Assert.Equal(1, Marshal.SizeOf<CmsgDeleteCharacter>());
        Assert.Equal(1, Unsafe.SizeOf<CmsgDeleteCharacter>());
        Assert.Equal(1, CmsgDeleteCharacter.WireSize);
    }

    [Fact] // spec: Docs/RE/packets/1-6_login_or_create.yaml (size: 52, OPAQUE collision-gated)
    public void CmsgLoginRequest_size_is_52()
    {
        Assert.Equal(52, Marshal.SizeOf<CmsgLoginRequest>());
        Assert.Equal(52, Unsafe.SizeOf<CmsgLoginRequest>());
        Assert.Equal(52, CmsgLoginRequest.WireSize);
    }

    // -------------------------------------------------------------------------
    // Offset guards (Marshal.OffsetOf == specced byte offset)
    // -------------------------------------------------------------------------

    [Fact] // spec: Docs/RE/packets/1-7_select_character.yaml
    public void CmsgSelectCharacter_field_offsets()
    {
        Assert.Equal(0, (int)Marshal.OffsetOf<CmsgSelectCharacter>(nameof(CmsgSelectCharacter.SlotIndex)));
        Assert.Equal(1, (int)Marshal.OffsetOf<CmsgSelectCharacter>(nameof(CmsgSelectCharacter.StateFlag)));
    }

    [Fact] // spec: Docs/RE/specs/login_flow.md §5.3
    public void SmsgCharSpawnResult_field_offsets()
    {
        Assert.Equal(0, (int)Marshal.OffsetOf<SmsgCharSpawnResult>(nameof(SmsgCharSpawnResult.Result)));
        Assert.Equal(1, (int)Marshal.OffsetOf<SmsgCharSpawnResult>(nameof(SmsgCharSpawnResult.Slot)));
        Assert.Equal(2, (int)Marshal.OffsetOf<SmsgCharSpawnResult>(nameof(SmsgCharSpawnResult.Pad)));
        Assert.Equal(4, (int)Marshal.OffsetOf<SmsgCharSpawnResult>(nameof(SmsgCharSpawnResult.SpawnParam1)));
        Assert.Equal(8, (int)Marshal.OffsetOf<SmsgCharSpawnResult>(nameof(SmsgCharSpawnResult.SpawnParam2)));
        Assert.Equal(12, (int)Marshal.OffsetOf<SmsgCharSpawnResult>(nameof(SmsgCharSpawnResult.SpawnParam3)));
    }

    [Fact] // spec: Docs/RE/packets/1-13_rename_character.yaml
    public void CmsgRenameCharacter_field_offsets()
    {
        Assert.Equal(0, (int)Marshal.OffsetOf<CmsgRenameCharacter>(nameof(CmsgRenameCharacter.NewName)));
    }

    [Fact] // spec: Docs/RE/packets/1-14_delete_character.yaml
    public void CmsgDeleteCharacter_field_offsets()
    {
        Assert.Equal(0, (int)Marshal.OffsetOf<CmsgDeleteCharacter>(nameof(CmsgDeleteCharacter.SlotIndex)));
    }

    [Fact] // spec: Docs/RE/packets/1-6_login_or_create.yaml
    public void CmsgLoginRequest_field_offsets()
    {
        Assert.Equal(0, (int)Marshal.OffsetOf<CmsgLoginRequest>(nameof(CmsgLoginRequest.Body)));
    }

    // -------------------------------------------------------------------------
    // Decode guards (write known bytes -> MemoryMarshal reinterpret -> read fields back)
    // -------------------------------------------------------------------------

    [Fact] // spec: Docs/RE/packets/1-7_select_character.yaml
    public void CmsgSelectCharacter_decodes_known_bytes()
    {
        Span<byte> body = stackalloc byte[CmsgSelectCharacter.WireSize];
        body[0x00] = 3; // SlotIndex
        body[0x01] = 0xA5; // StateFlag

        ref readonly CmsgSelectCharacter p = ref MemoryMarshal.AsRef<CmsgSelectCharacter>(body);
        Assert.Equal((byte)3, p.SlotIndex);
        Assert.Equal((byte)0xA5, p.StateFlag);
    }

    [Fact] // spec: Docs/RE/specs/login_flow.md §5.3
    public void SmsgCharSpawnResult_decodes_known_bytes()
    {
        Span<byte> body = stackalloc byte[SmsgCharSpawnResult.WireSize];
        body[0x00] = 1; // Result = proceed
        body[0x01] = 4; // Slot
        BinaryPrimitives.WriteUInt16LittleEndian(body[0x02..], 0xBEEF); // Pad
        BinaryPrimitives.WriteUInt32LittleEndian(body[0x04..], 0x11111111u); // SpawnParam1
        BinaryPrimitives.WriteUInt32LittleEndian(body[0x08..], 0x22222222u); // SpawnParam2
        BinaryPrimitives.WriteUInt32LittleEndian(body[0x0c..], 0x33333333u); // SpawnParam3

        ref readonly SmsgCharSpawnResult p = ref MemoryMarshal.AsRef<SmsgCharSpawnResult>(body);
        Assert.Equal((byte)1, p.Result);
        Assert.Equal((byte)4, p.Slot);
        Assert.Equal((ushort)0xBEEF, p.Pad);
        Assert.Equal(0x11111111u, p.SpawnParam1);
        Assert.Equal(0x22222222u, p.SpawnParam2);
        Assert.Equal(0x33333333u, p.SpawnParam3);
    }

    [Fact] // spec: Docs/RE/packets/1-13_rename_character.yaml
    public void CmsgRenameCharacter_decodes_known_bytes()
    {
        Span<byte> body = stackalloc byte[CmsgRenameCharacter.WireSize];
        // "hero7\0" ASCII into the 18-byte CP949 buffer; the rest stays zero.
        body[0x00] = (byte)'h';
        body[0x01] = (byte)'e';
        body[0x02] = (byte)'r';
        body[0x03] = (byte)'o';
        body[0x04] = (byte)'7';
        // body[0x05..] = 0 (NUL terminator + padding)

        ref readonly CmsgRenameCharacter p = ref MemoryMarshal.AsRef<CmsgRenameCharacter>(body);
        Assert.Equal((byte)'h', p.NewName[0]);
        Assert.Equal((byte)'e', p.NewName[1]);
        Assert.Equal((byte)'r', p.NewName[2]);
        Assert.Equal((byte)'o', p.NewName[3]);
        Assert.Equal((byte)'7', p.NewName[4]);
        Assert.Equal((byte)0, p.NewName[5]); // NUL terminator
        Assert.Equal((byte)0, p.NewName[17]); // last padding byte stays zero
    }

    [Fact] // spec: Docs/RE/packets/1-14_delete_character.yaml
    public void CmsgDeleteCharacter_decodes_known_bytes()
    {
        Span<byte> body = stackalloc byte[CmsgDeleteCharacter.WireSize];
        body[0x00] = 2; // SlotIndex

        ref readonly CmsgDeleteCharacter p = ref MemoryMarshal.AsRef<CmsgDeleteCharacter>(body);
        Assert.Equal((byte)2, p.SlotIndex);
    }

    [Fact] // spec: Docs/RE/packets/1-6_login_or_create.yaml (OPAQUE body — round-trips verbatim)
    public void CmsgLoginRequest_opaque_body_roundtrips()
    {
        Span<byte> body = stackalloc byte[CmsgLoginRequest.WireSize];
        for (int i = 0; i < body.Length; i++)
        {
            body[i] = (byte)(i + 1); // distinct non-zero per byte
        }

        ref readonly CmsgLoginRequest p = ref MemoryMarshal.AsRef<CmsgLoginRequest>(body);
        // The opaque 52-byte body is preserved byte-for-byte; no field interpretation is applied.
        for (int i = 0; i < CmsgLoginRequest.WireSize; i++)
        {
            Assert.Equal((byte)(i + 1), p.Body[i]);
        }
    }
}