// spec: Docs/RE/packets/4-13_local_player_state_sync.yaml — opcode 4/13 (0x4000d), 56-byte fixed block.
//
// Control-flow-confirmed on IDB SHA 263bd994 (CYCLE 12 / Phase 3): the 56-byte read, the
// (TargetSort, TargetId) local-player gate, the X/Y/Z/Heading floats, the sync-mode byte
// position, and the tail QwordValue/DwordValue1/DwordValue2/ArgValue destinations.
//
// CONFLICT RESOLUTION (CYCLE 12 / Phase 3 — binary won):
//   The sync-mode byte is at wire offset 33 (0x21), NOT 32. Wire bytes 24..32 are an
//   unconsumed reserved 9-byte gap. The prior "Mode @ offset 32" reading is REFUTED by the binary.
//   spec: Docs/RE/packets/4-13_local_player_state_sync.yaml (CONFLICT RESOLUTION note).

using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

/// <summary>
///     4/13 — local player state synchronization. Broadcasts movement coords, orientation/heading,
///     current player stance/mode, and portal check updates. Only processed if it references
///     the local player actor (TargetSort must be 1, TargetId must match local player id).
///     <para>
///         Fixed 56-byte payload (Pack=1). Width sum: 4+4+4+4+4+4+9+1+2+8+4+4+4 = 56. ✓
///     </para>
///     <para>
///         CRITICAL: Mode is at wire offset 33 (0x21) — NOT 32. Wire bytes 24..32 are an
///         unconsumed 9-byte reserved gap.
///         spec: Docs/RE/packets/4-13_local_player_state_sync.yaml (CONFLICT RESOLUTION note —
///         sync-mode byte at wire offset 33 (0x21), NOT 32).
///     </para>
///     spec: Docs/RE/packets/4-13_local_player_state_sync.yaml.
/// </summary>
[PacketOpcode(4, 13)] // spec: Docs/RE/packets/4-13_local_player_state_sync.yaml (opcode 0x4000d = major 4, minor 13)
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgLocalPlayerStateSync
{
    /// <summary>Packed opcode 0x4000d (4/13). spec: Docs/RE/packets/4-13_local_player_state_sync.yaml.</summary>
    public const uint OpcodeId = Opcodes.SmsgLocalPlayerStateSync;

    /// <summary>Declared wire size in bytes. spec: Docs/RE/packets/4-13_local_player_state_sync.yaml (size: 56).</summary>
    public const int WireSize = 56;

    // -------------------------------------------------------------------------
    // Wire fields. Offsets are payload-relative (offset 0 = first payload byte).
    // Pack=1; widths sum exactly to WireSize = 56.
    // spec: Docs/RE/packets/4-13_local_player_state_sync.yaml (fields table).
    // -------------------------------------------------------------------------

    /// <summary>
    ///     +0x00 (u32) Target actor sort key — must be 1 (player) for this packet to be processed.
    ///     spec: Docs/RE/packets/4-13_local_player_state_sync.yaml (TargetSort, offset 0).
    /// </summary>
    public readonly uint TargetSort; // spec: 4-13_local_player_state_sync.yaml +0x00 (u32)

    /// <summary>
    ///     +0x04 (u32) Target actor id — must match the local player's id for processing.
    ///     spec: Docs/RE/packets/4-13_local_player_state_sync.yaml (TargetId, offset 4).
    /// </summary>
    public readonly uint TargetId; // spec: 4-13_local_player_state_sync.yaml +0x04 (u32)

    /// <summary>
    ///     +0x08 (f32) World Y coordinate — ignored by the client (world Y is always 0).
    ///     spec: Docs/RE/packets/4-13_local_player_state_sync.yaml (Y, offset 8).
    /// </summary>
    public readonly float Y; // spec: 4-13_local_player_state_sync.yaml +0x08 (f32, ignored by client)

    /// <summary>
    ///     +0x0c (f32) Heading / yaw orientation.
    ///     spec: Docs/RE/packets/4-13_local_player_state_sync.yaml (Heading, offset 12).
    /// </summary>
    public readonly float Heading; // spec: 4-13_local_player_state_sync.yaml +0x0c (f32)

    /// <summary>
    ///     +0x10 (f32) World X coordinate.
    ///     spec: Docs/RE/packets/4-13_local_player_state_sync.yaml (X, offset 16).
    /// </summary>
    public readonly float X; // spec: 4-13_local_player_state_sync.yaml +0x10 (f32)

    /// <summary>
    ///     +0x14 (f32) World Z coordinate.
    ///     spec: Docs/RE/packets/4-13_local_player_state_sync.yaml (Z, offset 20).
    /// </summary>
    public readonly float Z; // spec: 4-13_local_player_state_sync.yaml +0x14 (f32)

    // --- Unconsumed reserved gap: wire bytes 24..32 (9 bytes). ---
    // These bytes are read into the buffer but NEVER consumed by the handler.
    // spec: Docs/RE/packets/4-13_local_player_state_sync.yaml (Reserved18, offset 24, u8[9]).

    private readonly byte _reserved18_0; // spec: 4-13_local_player_state_sync.yaml +0x18..+0x20 (9-byte reserved gap)
    private readonly byte _reserved18_1;
    private readonly byte _reserved18_2;
    private readonly byte _reserved18_3;
    private readonly byte _reserved18_4;
    private readonly byte _reserved18_5;
    private readonly byte _reserved18_6;
    private readonly byte _reserved18_7;
    private readonly byte _reserved18_8;

    /// <summary>
    ///     +0x21 / wire offset 33 (u8) Sync mode discriminator:
    ///     0 = set-position; 1 = BattleController sync; 5 = no state write.
    ///     <para>
    ///         CRITICAL CORRECTION: Mode is at wire offset 33 (0x21), NOT 32.
    ///         Wire bytes 24..32 are an unconsumed reserved gap.
    ///         spec: Docs/RE/packets/4-13_local_player_state_sync.yaml (Mode, offset 33 — CONFLICT
    ///         RESOLUTION note: sync-mode byte at wire offset 33 (0x21), NOT 32).
    ///     </para>
    /// </summary>
    public readonly byte
        Mode; // spec: 4-13_local_player_state_sync.yaml +0x21 (wire offset 33, u8; 0=set-pos/1=BC-sync/5=no-write)

    /// <summary>
    ///     +0x22 (u8[2]) Struct alignment padding to the next dword.
    ///     spec: Docs/RE/packets/4-13_local_player_state_sync.yaml (Padding, offset 34, u8[2]).
    /// </summary>
    private readonly byte _padding0; // spec: 4-13_local_player_state_sync.yaml +0x22..+0x23 (alignment padding)

    private readonly byte _padding1;

    /// <summary>
    ///     +0x24 / wire offset 36 (i64) Sync timeline / budget value stored to LocalPlayer +176.
    ///     spec: Docs/RE/packets/4-13_local_player_state_sync.yaml (QwordValue, offset 36).
    /// </summary>
    public readonly long QwordValue; // spec: 4-13_local_player_state_sync.yaml +0x24 (i64, -> LocalPlayer+176)

    /// <summary>
    ///     +0x2c / wire offset 44 (u32) Sync value stored to LocalPlayer +184.
    ///     spec: Docs/RE/packets/4-13_local_player_state_sync.yaml (DwordValue1, offset 44).
    /// </summary>
    public readonly uint DwordValue1; // spec: 4-13_local_player_state_sync.yaml +0x2c (u32, -> LocalPlayer+184)

    /// <summary>
    ///     +0x30 / wire offset 48 (u32) Sync value stored to a global sync-state dword.
    ///     spec: Docs/RE/packets/4-13_local_player_state_sync.yaml (DwordValue2, offset 48).
    /// </summary>
    public readonly uint DwordValue2; // spec: 4-13_local_player_state_sync.yaml +0x30 (u32, -> global sync-state dword)

    /// <summary>
    ///     +0x34 / wire offset 52 (u32) Passed to the post-sync update helper when Mode is nonzero.
    ///     spec: Docs/RE/packets/4-13_local_player_state_sync.yaml (ArgValue, offset 52).
    /// </summary>
    public readonly uint
        ArgValue; // spec: 4-13_local_player_state_sync.yaml +0x34 (u32, -> post-sync helper arg when Mode != 0)
}