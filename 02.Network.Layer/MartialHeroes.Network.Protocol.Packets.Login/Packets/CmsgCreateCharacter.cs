// spec: Docs/RE/packets/cmsg_char_create.yaml — opcode 1/6 (0x10006), 52-byte fixed payload.
//
// !!! CAPTURE-UNVERIFIED VALUE SEMANTICS !!!
// The (major:minor) routing is dispatch-table-confirmed and the prior "1/6 login-or-create
// collision" is RESOLVED: 1/6 is character-CREATE only (single emitter = the create-confirm action
// of the character-select window). The login credential is a SEPARATE sub-opcode 0x2B on the secure
// 1/4 frame (packets/login.yaml), never here.
//
// The 52-byte body is an embedded appearance/creation record. The field offsets are byte-pinned
// (CYCLE 11 / Block A, IDB 263bd994, CORROBORATED). Wire VALUE semantics of the appearance words
// and per-stat semantic order remain capture/debugger-pending.
//
// Width sum check: 18 + 2 + 2 + 2 + 2 + 2 + (4*5) + 4 = 52. spec: packets/cmsg_char_create.yaml.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.Login.Packets;

/// <summary>
///     1/6 — client character-CREATE request. The 52-byte body is an embedded appearance/creation
///     record: a CP949 character name at offset 0 followed by face, two appearance words, internal
///     class id, an alignment pad, five stat dwords, and a remaining-points dword.
///     <para>
///         The create is acknowledged via the 8-byte <c>3/7 SmsgCharManageResult</c> (which clears the
///         in-flight create latch) and a refreshed <c>3/1 SmsgCharacterList</c> character-list push.
///         There is <b>no</b> dedicated 12-byte create-result message; <c>3/23</c> is
///         <c>SmsgCharStatusBytesByName</c> (a 28-byte by-name status/level patch) — it is NOT a
///         create result.
///     </para>
///     spec: Docs/RE/packets/cmsg_char_create.yaml; Docs/RE/specs/character_creation.md §5.
///     CAPTURE-UNVERIFIED value semantics.
/// </summary>
[PacketOpcode(1, 6)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct CmsgCreateCharacter
{
    /// <summary>Packed opcode 0x10006 (1/6). spec: Docs/RE/packets/cmsg_char_create.yaml.</summary>
    public const uint OpcodeId = Opcodes.CmsgCreateCharacter;

    /// <summary>Declared wire size in bytes. spec: Docs/RE/packets/cmsg_char_create.yaml (size: 52).</summary>
    public const int WireSize = 52; // spec: Docs/RE/packets/cmsg_char_create.yaml (size: 52)

    // Width sum: 18 + 2 + 2 + 2 + 2 + 2 + 4 + 4 + 4 + 4 + 4 + 4 = 52.
    // spec: Docs/RE/packets/cmsg_char_create.yaml notes (CYCLE 11 / Block A, IDB 263bd994).

    /// <summary>
    ///     0x00 — 18-byte CP949 name buffer, NUL-padded; min 2 chars, cap 17+NUL. spec:
    ///     Docs/RE/packets/cmsg_char_create.yaml Name @0x00.
    /// </summary>
    public readonly NameBuffer Name;

    /// <summary>0x12 — face index, range 1..7. spec: Docs/RE/packets/cmsg_char_create.yaml Face @0x12.</summary>
    public readonly ushort Face;

    /// <summary>
    ///     0x14 — appearance word A (seeded to 1 when the form opens; appears class-implied; semantics
    ///     UNVERIFIED — capture/debugger-pending). spec: Docs/RE/packets/cmsg_char_create.yaml AppearanceA @0x14.
    /// </summary>
    public readonly ushort AppearanceA;

    /// <summary>
    ///     0x16 — appearance word B (zeroed when the form opens; semantics UNVERIFIED — capture/debugger-pending).
    ///     spec: Docs/RE/packets/cmsg_char_create.yaml AppearanceB @0x16.
    /// </summary>
    public readonly ushort AppearanceB;

    /// <summary>
    ///     0x18 — internal class id after UI remap {0->4,1->1,2->3,3->2}. spec: Docs/RE/packets/cmsg_char_create.yaml
    ///     ClassInternalId @0x18.
    /// </summary>
    public readonly ushort ClassInternalId;

    /// <summary>
    ///     0x1A — 2-byte alignment gap so the stat block starts dword-aligned at 0x1C; not written on the create path —
    ///     model as reserved/zero. spec: Docs/RE/packets/cmsg_char_create.yaml Reserved1A @0x1A.
    /// </summary>
    public readonly Reserved1ABuffer Reserved1A;

    /// <summary>
    ///     0x1C — allocatable stat 0 (init 10; floor 10; clamp [10,15]). spec: Docs/RE/packets/cmsg_char_create.yaml
    ///     Stat0 @0x1C.
    /// </summary>
    public readonly uint Stat0;

    /// <summary>0x20 — allocatable stat 1 (init 10). spec: Docs/RE/packets/cmsg_char_create.yaml Stat1 @0x20.</summary>
    public readonly uint Stat1;

    /// <summary>0x24 — allocatable stat 2 (init 10). spec: Docs/RE/packets/cmsg_char_create.yaml Stat2 @0x24.</summary>
    public readonly uint Stat2;

    /// <summary>0x28 — allocatable stat 3 (init 10). spec: Docs/RE/packets/cmsg_char_create.yaml Stat3 @0x28.</summary>
    public readonly uint Stat3;

    /// <summary>0x2C — allocatable stat 4 (init 10). spec: Docs/RE/packets/cmsg_char_create.yaml Stat4 @0x2C.</summary>
    public readonly uint Stat4;

    /// <summary>
    ///     0x30 — trailing allocation-budget counter (init 5); invariant sum(Stat0..4)+PointsRemaining=55. spec:
    ///     Docs/RE/packets/cmsg_char_create.yaml PointsRemaining @0x30.
    /// </summary>
    public readonly uint PointsRemaining;

    /// <summary>
    ///     Fixed 18-byte CP949 name buffer (no managed string on the wire). The first NUL terminates the
    ///     name within; callers slice and decode as CP949. spec: Docs/RE/packets/cmsg_char_create.yaml Name @0x00.
    /// </summary>
    [InlineArray(18)]
    public struct NameBuffer
    {
        private byte _element0;
    }

    /// <summary>
    ///     2-byte alignment pad at 0x1A (reserved/zero; actual on-wire bytes are capture-pending).
    ///     spec: Docs/RE/packets/cmsg_char_create.yaml Reserved1A @0x1A.
    /// </summary>
    [InlineArray(2)]
    public struct Reserved1ABuffer
    {
        private byte _element0;
    }
}