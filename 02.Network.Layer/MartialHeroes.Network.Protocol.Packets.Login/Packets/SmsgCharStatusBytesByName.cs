// spec: Docs/RE/packets/3-23_char_select_status_update.yaml
//   opcode: 0x30017   major 3 / minor 23   direction: S2C   size: 28
//   CLEAN ROOM: re-implemented from a neutral spec; no decompiler output.
//
// Binary-confirmed (Phase 2b / CYCLE 11 Block A, IDB 263bd994): 3/23 = SmsgCharStatusBytesByName,
// 28-byte fixed payload (8-byte frame header + 28 bytes).  This is NOT a 12-byte char-create result.
// Create is acked via 3/7 SmsgCharManageResult (latch-clear) + a refreshed 3/1 SmsgCharacterList.
// There is NO dedicated create-result packet; 3/23 is a by-name status/level patch, NOT a create
// result. spec: Docs/RE/specs/character_creation.md §5 and §5.1; Docs/RE/opcodes.md.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.Login.Packets;

/// <summary>
///     3/23 — select-screen character level and status update (S2C, 28 bytes).
///     The server sends this to update a character's level and custom status byte by matching the
///     CP949 name string inside the lobby character slots. Also updates the local player level global
///     when the local player is already instantiated.
///     spec: Docs/RE/packets/3-23_char_select_status_update.yaml.
/// </summary>
[PacketOpcode(3, 23)] // spec: Docs/RE/opcodes.md — opcode 0x30017 (major 3, minor 23)
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SmsgCharStatusBytesByName
{
    /// <summary>Packed opcode 0x30017 (3/23). spec: Docs/RE/packets/3-23_char_select_status_update.yaml.</summary>
    public const uint OpcodeId = 0x30017; // spec: Docs/RE/opcodes.md row 3/23

    /// <summary>Declared wire size in bytes. spec: Docs/RE/packets/3-23_char_select_status_update.yaml (size: 28).</summary>
    public const int WireSize = 28; // spec: Docs/RE/packets/3-23_char_select_status_update.yaml

    // Compile-time size guard: Unsafe.SizeOf<SmsgCharStatusBytesByName>() must equal 28.
    // Fields: 1(HasCustomText) + 1(StatusCode) + 6(Pad0) + 17(CharacterName) + 1(StatusValue) + 1(Level) + 1(Padding) = 28.

    /// <summary>
    ///     0x00 — 1 if name-based status is updated, 0 for code-based status.
    ///     spec: Docs/RE/packets/3-23_char_select_status_update.yaml HasCustomText @0x00.
    /// </summary>
    public byte HasCustomText;

    /// <summary>
    ///     0x01 — status reason code used if HasCustomText is 0.
    ///     spec: Docs/RE/packets/3-23_char_select_status_update.yaml StatusCode @0x01.
    /// </summary>
    public byte StatusCode;

    /// <summary>
    ///     0x02 — alignment padding (6 bytes).
    ///     spec: Docs/RE/packets/3-23_char_select_status_update.yaml Pad0 @0x02.
    /// </summary>
    public Pad0Buffer Pad0;

    /// <summary>
    ///     0x08 — CP949 NUL-terminated character name to match against the slot roster.
    ///     spec: Docs/RE/packets/3-23_char_select_status_update.yaml CharacterName @0x08 (17 bytes).
    /// </summary>
    public CharacterNameBuffer CharacterName;

    /// <summary>
    ///     0x19 (25) — updates character status / PK / faction byte (staged in the global client status flag).
    ///     spec: Docs/RE/packets/3-23_char_select_status_update.yaml StatusValue @0x19.
    /// </summary>
    public byte StatusValue;

    /// <summary>
    ///     0x1A (26) — updates character level (staged in the local player level global).
    ///     spec: Docs/RE/packets/3-23_char_select_status_update.yaml Level @0x1A.
    /// </summary>
    public byte Level;

    /// <summary>
    ///     0x1B (27) — 1 byte padding/reserved (unused).
    ///     spec: Docs/RE/packets/3-23_char_select_status_update.yaml Padding @0x1B.
    /// </summary>
    public byte Padding;

    /// <summary>
    ///     Fixed 6-byte inline buffer (no managed string on the wire). spec:
    ///     Docs/RE/packets/3-23_char_select_status_update.yaml Pad0.
    /// </summary>
    [InlineArray(6)]
    public struct Pad0Buffer
    {
        private byte _element0;
    }

    /// <summary>
    ///     Fixed 17-byte CP949 name buffer (no managed string on the wire). spec:
    ///     Docs/RE/packets/3-23_char_select_status_update.yaml CharacterName.
    /// </summary>
    [InlineArray(17)]
    public struct CharacterNameBuffer
    {
        private byte _element0;
    }
}