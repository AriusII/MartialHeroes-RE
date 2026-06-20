// spec: Docs/RE/packets/5-52_actor_skill_action.yaml — opcode 5/52 (0x50034), variable-length.
//
// !!! CAPTURE-UNVERIFIED STATIC LAYOUT !!!
// Routing, the 24-byte header read, the 36-byte record stride, and the corrected header
// offsets (SkillId@0x0C, ActionCode@0x10, TargetCount@0x14) are CONTROL-FLOW-CONFIRMED
// (static IDA, anchor 263bd994). Per-record field VALUE meanings and the pad-vs-subfield
// split at @0x11..0x13 / @0x15..0x17 are capture/debugger-pending.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets;

/// <summary>
/// 5/52 — actor skill action / combat result. Variable-length S2C message: a fixed 24-byte
/// header followed by <see cref="TargetCount"/> × 36-byte target records.
/// <para>
/// This struct models ONLY the fixed 24-byte header (payload 0x00..0x17). The target records
/// begin at payload offset <see cref="HeaderSize"/> and must be consumed from the raw span
/// by the handler (stride = <see cref="TargetRecordStride"/>).
/// </para>
/// spec: Docs/RE/packets/5-52_actor_skill_action.yaml. CAPTURE-UNVERIFIED layout (routing +
/// header offsets + stride CONFIRMED; field value semantics capture/debugger-pending).
/// </summary>
[PacketOpcode(5, 52)] // spec: Docs/RE/packets/5-52_actor_skill_action.yaml (opcode 5/52 = 0x50034)
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgActorSkillAction
{
    /// <summary>
    /// Packed opcode 0x50034 (5/52). spec: Docs/RE/packets/5-52_actor_skill_action.yaml.
    /// </summary>
    public const uint OpcodeId = Opcodes.Opcodes.SmsgActorSkillAction;

    /// <summary>
    /// Fixed header size in bytes (payload 0x00..0x17). The first 36-byte target record
    /// begins at payload offset <c>HeaderSize</c>.
    /// spec: Docs/RE/packets/5-52_actor_skill_action.yaml (header 24 bytes, CONFIRMED).
    /// </summary>
    public const int HeaderSize = 24; // spec: Docs/RE/packets/5-52_actor_skill_action.yaml

    /// <summary>
    /// Per-target record stride (36 bytes). Records begin at payload +0x18 = <see cref="HeaderSize"/>.
    /// <para>
    /// DAMAGE FIELD AMBIGUITY (CAPTURE-PENDING): the 64-bit visible-damage accumulator sits
    /// at record +0x14 (low dword) / +0x18 (high dword) per static disasm of the add/adc
    /// handler loop (handlers.md §20.3 + the yaml notes). An earlier reading placed it at
    /// record +0x10/+0x14 — that reading is REFUTED by the corrected disasm (yaml §RECORD
    /// OFFSET CORRECTION). Do NOT bake a damage decode into this struct; the handler reads
    /// it raw from the span so a capture can confirm without a code change.
    /// </para>
    /// spec: Docs/RE/packets/5-52_actor_skill_action.yaml (stride 36 = 0x24, CONFIRMED).
    /// </summary>
    public const int TargetRecordStride = 36; // spec: Docs/RE/packets/5-52_actor_skill_action.yaml

    /// <summary>
    /// Byte offset of the target actor composite sub-key byte within a target record.
    /// Agreed across both specs (handlers.md §17.11 and the yaml).
    /// spec: Docs/RE/packets/5-52_actor_skill_action.yaml (record +0x00, u8, sub-key).
    /// </summary>
    public const int TargetSubKeyOffset = 0x00; // spec: Docs/RE/packets/5-52_actor_skill_action.yaml

    /// <summary>
    /// Byte offset of the target actor composite key dword within a target record.
    /// Agreed across both specs (handlers.md §17.11 and the yaml).
    /// spec: Docs/RE/packets/5-52_actor_skill_action.yaml (record +0x04, u32, key).
    /// </summary>
    public const int TargetKeyOffset = 0x04; // spec: Docs/RE/packets/5-52_actor_skill_action.yaml

    // -------------------------------------------------------------------------
    // Fixed 24-byte header fields (Pack=1, sequential). Offsets are payload-relative.
    // spec: Docs/RE/packets/5-52_actor_skill_action.yaml (fields list).
    // -------------------------------------------------------------------------

    /// <summary>
    /// +0x00 (u8) Caster actor sort.
    /// spec: Docs/RE/packets/5-52_actor_skill_action.yaml (CasterSort @0x00).
    /// </summary>
    public readonly byte CasterSort; // spec: Docs/RE/packets/5-52_actor_skill_action.yaml (+0x00, u8)

    /// <summary>
    /// +0x01..+0x03 (3 bytes) Padding to align CasterId at +0x04.
    /// spec: Docs/RE/packets/5-52_actor_skill_action.yaml (Pad0 @0x01, bytes[3]).
    /// </summary>
    public readonly Pad0Buffer Pad0; // spec: Docs/RE/packets/5-52_actor_skill_action.yaml (+0x01, bytes[3])

    /// <summary>
    /// +0x04 (u32) Caster actor id.
    /// spec: Docs/RE/packets/5-52_actor_skill_action.yaml (CasterId @0x04).
    /// </summary>
    public readonly uint CasterId; // spec: Docs/RE/packets/5-52_actor_skill_action.yaml (+0x04, u32)

    /// <summary>
    /// +0x08 (u8) Cast flag. 0 = cancel/idle branch; nonzero = active cast.
    /// spec: Docs/RE/packets/5-52_actor_skill_action.yaml (CastFlag @0x08).
    /// </summary>
    public readonly byte CastFlag; // spec: Docs/RE/packets/5-52_actor_skill_action.yaml (+0x08, u8)

    /// <summary>
    /// +0x09 (u8) Basic/alias selector. 0xFF = basic attack.
    /// spec: Docs/RE/packets/5-52_actor_skill_action.yaml (BasicSelector @0x09).
    /// </summary>
    public readonly byte BasicSelector; // spec: Docs/RE/packets/5-52_actor_skill_action.yaml (+0x09, u8)

    /// <summary>
    /// +0x0A..+0x0B (2 bytes) Padding to align SkillId at +0x0C.
    /// spec: Docs/RE/packets/5-52_actor_skill_action.yaml (Pad1 @0x0A, bytes[2]).
    /// </summary>
    public readonly Pad1Buffer Pad1; // spec: Docs/RE/packets/5-52_actor_skill_action.yaml (+0x0A, bytes[2])

    /// <summary>
    /// +0x0C (u32) Skill id / entity key resolved against the client skill/entity table.
    /// spec: Docs/RE/packets/5-52_actor_skill_action.yaml (SkillId @0x0C, CONFIRMED).
    /// </summary>
    public readonly uint SkillId; // spec: Docs/RE/packets/5-52_actor_skill_action.yaml (+0x0C, u32, CONFIRMED)

    /// <summary>
    /// +0x10 (u8) Action code. 0 = single target; 0xC8..0xCB = motion sub-ops; 0xCC = AoE.
    /// spec: Docs/RE/packets/5-52_actor_skill_action.yaml (ActionCode @0x10, CONFIRMED).
    /// </summary>
    public readonly byte ActionCode; // spec: Docs/RE/packets/5-52_actor_skill_action.yaml (+0x10, u8, CONFIRMED)

    /// <summary>
    /// +0x11..+0x13 (3 bytes) Pad-vs-subfield UNRESOLVED — may be true padding or carry
    /// sub-fields (e.g. secondary skill arg / hit flags). Capture-pending.
    /// spec: Docs/RE/packets/5-52_actor_skill_action.yaml (Pad2 @0x11, bytes[3], UNRESOLVED).
    /// </summary>
    public readonly Pad2Buffer Pad2; // spec: Docs/RE/packets/5-52_actor_skill_action.yaml (+0x11, bytes[3], UNRESOLVED)

    /// <summary>
    /// +0x14 (u8) Number of 36-byte target records that follow. Bounded (0, 0x28] = at most 40.
    /// spec: Docs/RE/packets/5-52_actor_skill_action.yaml (TargetCount @0x14, CONFIRMED).
    /// </summary>
    public readonly byte TargetCount; // spec: Docs/RE/packets/5-52_actor_skill_action.yaml (+0x14, u8, CONFIRMED)

    /// <summary>
    /// +0x15..+0x17 (3 bytes) Pad-vs-subfield UNRESOLVED — may be true padding or carry
    /// sub-fields. Capture-pending.
    /// spec: Docs/RE/packets/5-52_actor_skill_action.yaml (Pad3 @0x15, bytes[3], UNRESOLVED).
    /// </summary>
    public readonly Pad3Buffer Pad3; // spec: Docs/RE/packets/5-52_actor_skill_action.yaml (+0x15, bytes[3], UNRESOLVED)

    // -------------------------------------------------------------------------
    // Inline pad buffers (Pack=1 sequential layout requires explicit gap structs).
    // -------------------------------------------------------------------------

    /// <summary>3-byte pad at +0x01. spec: Docs/RE/packets/5-52_actor_skill_action.yaml.</summary>
    [InlineArray(3)]
    public struct Pad0Buffer
    {
        private byte _element0;
    }

    /// <summary>2-byte pad at +0x0A. spec: Docs/RE/packets/5-52_actor_skill_action.yaml.</summary>
    [InlineArray(2)]
    public struct Pad1Buffer
    {
        private byte _element0;
    }

    /// <summary>3-byte pad at +0x11 (UNRESOLVED). spec: Docs/RE/packets/5-52_actor_skill_action.yaml.</summary>
    [InlineArray(3)]
    public struct Pad2Buffer
    {
        private byte _element0;
    }

    /// <summary>3-byte pad at +0x15 (UNRESOLVED). spec: Docs/RE/packets/5-52_actor_skill_action.yaml.</summary>
    [InlineArray(3)]
    public struct Pad3Buffer
    {
        private byte _element0;
    }
}