// spec: Docs/RE/packets/cmsg_char_create.yaml — opcode 1/6 (0x10006), 52-byte fixed payload.
//
// !!! CAPTURE-UNVERIFIED VALUE SEMANTICS !!!
// The (major:minor) routing is dispatch-table-confirmed and the prior "1/6 login-or-create
// collision" is RESOLVED: 1/6 is character-CREATE only (single emitter = the create-confirm action
// of the character-select window). The login credential is a SEPARATE sub-opcode 0x2B on the secure
// 1/4 frame (packets/login.yaml), never here.
//
// The 52-byte body is an embedded appearance/creation record. The field offsets are now promoted in
// cmsg_char_create.yaml; exact sex/hair/stat VALUE semantics remain capture/debugger-pending.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.Login.Packets;

/// <summary>
///     1/6 — client character-CREATE request. The 52-byte body is an embedded appearance/creation
///     record: a CP949 character name at offset 0 followed by face, sex, hair/reserved, internal class,
///     five stat dwords, and a remaining-points dword. The server replies with the S2C 3/23 create
///     result (and may refresh via 3/7).
///     spec: Docs/RE/packets/cmsg_char_create.yaml. CAPTURE-UNVERIFIED value semantics.
/// </summary>
[PacketOpcode(1, 6)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct CmsgCreateCharacter
{
    /// <summary>Packed opcode 0x10006 (1/6). spec: packets/cmsg_char_create.yaml.</summary>
    public const uint OpcodeId = Opcodes.CmsgCreateCharacter;

    /// <summary>Declared wire size in bytes. spec: packets/cmsg_char_create.yaml (size: 52).</summary>
    public const int WireSize = 52;

    /// <summary>0x00 — 18-byte CP949 name buffer. spec: packets/cmsg_char_create.yaml.</summary>
    public readonly NameBuffer Name;

    /// <summary>0x12 — face index, range 1..7. spec: packets/cmsg_char_create.yaml.</summary>
    public readonly ushort Face;

    /// <summary>0x14 — sex/gender word (semantics capture-pending). spec: packets/cmsg_char_create.yaml.</summary>
    public readonly ushort Sex;

    /// <summary>0x16 — hair/reserved word (semantics capture-pending). spec: packets/cmsg_char_create.yaml.</summary>
    public readonly ushort HairOrReserved;

    /// <summary>0x18 — internal class id after UI remap {0->4,1->1,2->3,3->2}. spec: packets/cmsg_char_create.yaml.</summary>
    public readonly ushort ClassInternalId;

    /// <summary>0x1A — reserved alignment gap. spec: packets/cmsg_char_create.yaml.</summary>
    public readonly ushort Reserved1A;

    /// <summary>0x1C..0x2F — five allocatable stat dwords. spec: packets/cmsg_char_create.yaml.</summary>
    public readonly uint Stat0;

    public readonly uint Stat1;
    public readonly uint Stat2;
    public readonly uint Stat3;
    public readonly uint Stat4;

    /// <summary>0x30 — trailing allocation-budget counter. spec: packets/cmsg_char_create.yaml.</summary>
    public readonly uint PointsRemaining;

    /// <summary>0x00 — 18-byte CP949 name buffer. spec: packets/cmsg_char_create.yaml.</summary>
    [InlineArray(18)]
    public struct NameBuffer
    {
        private byte _element0;
    }
}