// spec: Docs/RE/packets/cmsg_char_create.yaml — opcode 1/6 (0x10006), 52-byte fixed payload.
//
// !!! CAPTURE-UNVERIFIED STATIC LAYOUT — OPAQUE BODY (intra-payload field split deferred) !!!
// The (major:minor) routing is dispatch-table-confirmed and the prior "1/6 login-or-create
// collision" is RESOLVED: 1/6 is character-CREATE only (single emitter = the create-confirm action
// of the character-select window). The login credential is a SEPARATE sub-opcode 0x2B on the secure
// 1/4 frame (packets/login.yaml), never here.
//
// The 52-byte body is an embedded appearance/creation record. STATIC-HIGH facts: total body size is
// 52 bytes (0x34) and the CP949 character NAME is at body offset 0. The intra-payload field offsets
// below the name (class/face/the four stats/sex/hair) are STATIC MEDIUM/LOW and capture-unverified,
// so per the spec the body is modelled as ONE OPAQUE 52-byte buffer; do NOT hand-author a field
// split until a capture/debugger read pins the offsets.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets;

/// <summary>
/// 1/6 — client character-CREATE request. The 52-byte body is an embedded appearance/creation
/// record: a CP949 character name at offset 0 followed by class/face/four allocatable stats/
/// appearance. Only the name-at-0 and the total 52-byte size are high-confidence; the remaining
/// intra-payload field map is capture-unverified, so the body is kept OPAQUE here. The server
/// replies with the S2C 3/6 / 3/23 create result.
/// spec: Docs/RE/packets/cmsg_char_create.yaml. CAPTURE-UNVERIFIED, OPAQUE-body layout.
/// </summary>
[PacketOpcode(1, 6)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct CmsgCreateCharacter
{
    /// <summary>Packed opcode 0x10006 (1/6). spec: packets/cmsg_char_create.yaml.</summary>
    public const uint OpcodeId = Opcodes.Opcodes.CmsgCreateCharacter;

    /// <summary>Declared wire size in bytes. spec: packets/cmsg_char_create.yaml (size: 52).</summary>
    public const int WireSize = 52;

    /// <summary>
    /// 0x00 — the 52-byte appearance/creation record, kept OPAQUE. The CP949 character name begins at
    /// byte 0 (NUL-terminated within a reserved region); the class/face/four-stats/appearance split
    /// below the name is capture-unverified and intentionally NOT field-split here per the spec.
    /// spec: packets/cmsg_char_create.yaml.
    /// </summary>
    public readonly BodyBuffer Body;

    /// <summary>0x00 — 52-byte opaque appearance/creation body (CP949 name at offset 0). spec: packets/cmsg_char_create.yaml.</summary>
    [InlineArray(52)]
    public struct BodyBuffer
    {
        private byte _element0;
    }
}