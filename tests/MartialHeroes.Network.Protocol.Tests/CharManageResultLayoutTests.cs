// Layout + decode guards for the char-management S2C RESULT structs added in Phase E4-c (network
// completeness): SmsgCharManageResult (3/4), SmsgRenameCharResult (3/6), SmsgCharCreateResult (3/23).
// Each struct's runtime size must equal its spec size, every field must land at its specced offset
// (Marshal.OffsetOf), Pack=1 must hold (Unsafe.SizeOf agrees => no padding drift), and a synthetic
// byte buffer must MemoryMarshal-reinterpret to the expected per-offset values. Spec sources cited
// per assertion. CAPTURE-UNVERIFIED layouts (static inference; 3/23 size is capture-verified).
// spec: Docs/RE/specs/login_flow.md §5.4-5.6 + Docs/RE/packets/3-4_char_manage_result.yaml.

using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Packets;

namespace MartialHeroes.Network.Protocol.Tests;

public sealed class CharManageResultLayoutTests
{
    // -------------------------------------------------------------------------
    // Opcode constants reconcile against opcodes.md (packed major<<16 | minor)
    // -------------------------------------------------------------------------

    [Fact] // spec: Docs/RE/opcodes.md
    public void CharManageResult_opcode_constants_match_catalog()
    {
        Assert.Equal(0x30004u, Protocol.Opcodes.Opcodes.SmsgCharManageResult); // 3/4
        Assert.Equal(0x30006u, Protocol.Opcodes.Opcodes.SmsgRenameCharResult); // 3/6
        Assert.Equal(0x30017u, Protocol.Opcodes.Opcodes.SmsgCharCreateResult); // 3/23

        // OpcodeId mirrors the catalog constant on every struct.
        Assert.Equal(Protocol.Opcodes.Opcodes.SmsgCharManageResult, SmsgCharManageResult.OpcodeId);
        Assert.Equal(Protocol.Opcodes.Opcodes.SmsgRenameCharResult, SmsgRenameCharResult.OpcodeId);
        Assert.Equal(Protocol.Opcodes.Opcodes.SmsgCharCreateResult, SmsgCharCreateResult.OpcodeId);
    }

    // -------------------------------------------------------------------------
    // Size guards (Marshal.SizeOf == spec size; Unsafe.SizeOf agrees => Pack=1, no padding)
    // -------------------------------------------------------------------------

    [Fact] // spec: Docs/RE/packets/3-4_char_manage_result.yaml (size: 8) / login_flow.md §5.5
    public void SmsgCharManageResult_size_is_8()
    {
        Assert.Equal(8, Marshal.SizeOf<SmsgCharManageResult>());
        Assert.Equal(8, Unsafe.SizeOf<SmsgCharManageResult>());
        Assert.Equal(8, SmsgCharManageResult.WireSize);
    }

    [Fact] // spec: Docs/RE/specs/login_flow.md §5.6 (19-byte / 0x13 block)
    public void SmsgRenameCharResult_size_is_19()
    {
        Assert.Equal(19, Marshal.SizeOf<SmsgRenameCharResult>());
        Assert.Equal(19, Unsafe.SizeOf<SmsgRenameCharResult>());
        Assert.Equal(19, SmsgRenameCharResult.WireSize);
    }

    [Fact] // spec: Docs/RE/specs/login_flow.md §5.4 (12-byte block, capture-verified)
    public void SmsgCharCreateResult_size_is_12()
    {
        Assert.Equal(12, Marshal.SizeOf<SmsgCharCreateResult>());
        Assert.Equal(12, Unsafe.SizeOf<SmsgCharCreateResult>());
        Assert.Equal(12, SmsgCharCreateResult.WireSize);
    }

    // -------------------------------------------------------------------------
    // Offset guards (Marshal.OffsetOf == specced byte offset)
    // -------------------------------------------------------------------------

    [Fact] // spec: Docs/RE/packets/3-4_char_manage_result.yaml / login_flow.md §5.5
    public void SmsgCharManageResult_field_offsets()
    {
        Assert.Equal(0, (int)Marshal.OffsetOf<SmsgCharManageResult>(nameof(SmsgCharManageResult.Result)));
        Assert.Equal(1, (int)Marshal.OffsetOf<SmsgCharManageResult>(nameof(SmsgCharManageResult.Reserved1)));
        Assert.Equal(2, (int)Marshal.OffsetOf<SmsgCharManageResult>(nameof(SmsgCharManageResult.Subtype)));
        Assert.Equal(3, (int)Marshal.OffsetOf<SmsgCharManageResult>(nameof(SmsgCharManageResult.Reserved3)));
        Assert.Equal(4, (int)Marshal.OffsetOf<SmsgCharManageResult>(nameof(SmsgCharManageResult.ReadyTime)));
    }

    [Fact] // spec: Docs/RE/specs/login_flow.md §5.6
    public void SmsgRenameCharResult_field_offsets()
    {
        Assert.Equal(0, (int)Marshal.OffsetOf<SmsgRenameCharResult>(nameof(SmsgRenameCharResult.Result)));
        Assert.Equal(1, (int)Marshal.OffsetOf<SmsgRenameCharResult>(nameof(SmsgRenameCharResult.NameOrError)));
    }

    [Fact] // spec: Docs/RE/specs/login_flow.md §5.4
    public void SmsgCharCreateResult_field_offsets()
    {
        Assert.Equal(0, (int)Marshal.OffsetOf<SmsgCharCreateResult>(nameof(SmsgCharCreateResult.Result)));
        Assert.Equal(1, (int)Marshal.OffsetOf<SmsgCharCreateResult>(nameof(SmsgCharCreateResult.Code)));
        Assert.Equal(2, (int)Marshal.OffsetOf<SmsgCharCreateResult>(nameof(SmsgCharCreateResult.Pad)));
        Assert.Equal(4, (int)Marshal.OffsetOf<SmsgCharCreateResult>(nameof(SmsgCharCreateResult.Value1)));
        Assert.Equal(8, (int)Marshal.OffsetOf<SmsgCharCreateResult>(nameof(SmsgCharCreateResult.Value2)));
    }

    // -------------------------------------------------------------------------
    // Decode guards (write known bytes -> MemoryMarshal reinterpret -> read fields back)
    // -------------------------------------------------------------------------

    [Fact] // spec: Docs/RE/packets/3-4_char_manage_result.yaml / login_flow.md §5.5
    public void SmsgCharManageResult_decodes_known_bytes()
    {
        Span<byte> body = stackalloc byte[SmsgCharManageResult.WireSize];
        body[0x00] = 1; // Result = success path
        body[0x01] = 0xAA; // Reserved1
        body[0x02] = 2; // Subtype = delete-confirm
        body[0x03] = 0xBB; // Reserved3
        BinaryPrimitives.WriteUInt32LittleEndian(body[0x04..], 0x5F3A_C0DEu); // ReadyTime

        ref readonly SmsgCharManageResult p = ref MemoryMarshal.AsRef<SmsgCharManageResult>(body);
        Assert.Equal((byte)1, p.Result);
        Assert.Equal((byte)0xAA, p.Reserved1);
        Assert.Equal((byte)2, p.Subtype);
        Assert.Equal((byte)0xBB, p.Reserved3);
        Assert.Equal(0x5F3A_C0DEu, p.ReadyTime);
    }

    [Fact] // spec: Docs/RE/specs/login_flow.md §5.6 (success path: CP949 name in NameOrError)
    public void SmsgRenameCharResult_decodes_success_name()
    {
        Span<byte> body = stackalloc byte[SmsgRenameCharResult.WireSize];
        body[0x00] = 1; // Result = success
        // "hero7\0" ASCII into the 18-byte CP949 buffer at offset 1; the rest stays zero.
        body[0x01] = (byte)'h';
        body[0x02] = (byte)'e';
        body[0x03] = (byte)'r';
        body[0x04] = (byte)'o';
        body[0x05] = (byte)'7';
        // body[0x06..] = 0 (NUL terminator + padding)

        ref readonly SmsgRenameCharResult p = ref MemoryMarshal.AsRef<SmsgRenameCharResult>(body);
        Assert.Equal((byte)1, p.Result);
        Assert.Equal((byte)'h', p.NameOrError[0]);
        Assert.Equal((byte)'e', p.NameOrError[1]);
        Assert.Equal((byte)'r', p.NameOrError[2]);
        Assert.Equal((byte)'o', p.NameOrError[3]);
        Assert.Equal((byte)'7', p.NameOrError[4]);
        Assert.Equal((byte)0, p.NameOrError[5]); // NUL terminator
        Assert.Equal((byte)0, p.NameOrError[17]); // last buffer byte stays zero
    }

    [Fact] // spec: Docs/RE/specs/login_flow.md §5.6 (failure path: error code in NameOrError[0])
    public void SmsgRenameCharResult_decodes_failure_error_code()
    {
        Span<byte> body = stackalloc byte[SmsgRenameCharResult.WireSize];
        body[0x00] = 0; // Result = failure
        body[0x01] = 0xC8; // error code (range 0xC8..0xD4) lands in NameOrError[0]

        ref readonly SmsgRenameCharResult p = ref MemoryMarshal.AsRef<SmsgRenameCharResult>(body);
        Assert.Equal((byte)0, p.Result);
        Assert.Equal((byte)0xC8, p.NameOrError[0]);
    }

    [Fact] // spec: Docs/RE/specs/login_flow.md §5.4
    public void SmsgCharCreateResult_decodes_known_bytes()
    {
        Span<byte> body = stackalloc byte[SmsgCharCreateResult.WireSize];
        body[0x00] = 1; // Result = success
        body[0x01] = 3; // Code = assigned slot id (success) / or 0xC8..0xD4 on failure
        BinaryPrimitives.WriteUInt16LittleEndian(body[0x02..], 0xBEEF); // Pad
        BinaryPrimitives.WriteUInt32LittleEndian(body[0x04..], 0x11223344u); // Value1
        BinaryPrimitives.WriteUInt32LittleEndian(body[0x08..], 0x55667788u); // Value2

        ref readonly SmsgCharCreateResult p = ref MemoryMarshal.AsRef<SmsgCharCreateResult>(body);
        Assert.Equal((byte)1, p.Result);
        Assert.Equal((byte)3, p.Code);
        Assert.Equal((ushort)0xBEEF, p.Pad);
        Assert.Equal(0x11223344u, p.Value1);
        Assert.Equal(0x55667788u, p.Value2);
    }
}