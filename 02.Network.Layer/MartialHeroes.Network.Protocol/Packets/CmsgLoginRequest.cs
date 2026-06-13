// spec: Docs/RE/packets/1-6_login_or_create.yaml — opcode 1/6 (0x10006), 52-byte fixed payload.
//
// *** UNRESOLVED OPCODE COLLISION — OPAQUE BODY, COLLISION-GATED. READ THE SPEC NOTES. ***
// Two independent ~52-byte send-sites map to 1/6: (A) the account LOGIN credential blob, and
// (B) the character-CREATE body (name + class/appearance). They may be one phase-dependent opcode
// or a mis-attribution; only a capture can disambiguate. The spec commits to NEITHER field layout
// and explicitly FORBIDS a hand-authored field split for either reading. This struct therefore
// models the body as ONE OPAQUE 52-byte buffer; the interpretation must be gated on the session
// phase at the CALL SITE, not decoded here. Do NOT add Login/Create field views to this struct
// until the collision is resolved.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets;

/// <summary>
/// 1/6 — CONTESTED login-OR-create C2S message. The 52-byte body is modelled OPAQUE because its
/// field meaning is an unresolved attribution conflict (login credential blob vs character-create
/// body). The canonical catalog name CmsgLoginRequest is preserved; this struct does NOT interpret
/// the body — interpretation is collision-gated to the call site by session phase.
/// spec: Docs/RE/packets/1-6_login_or_create.yaml. CAPTURE-UNVERIFIED, COLLISION-GATED layout.
/// </summary>
[PacketOpcode(1, 6)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct CmsgLoginRequest
{
    /// <summary>Packed opcode 0x10006 (1/6). spec: packets/1-6_login_or_create.yaml.</summary>
    public const uint OpcodeId = Opcodes.Opcodes.CmsgLoginRequest;

    /// <summary>Declared wire size in bytes. spec: packets/1-6_login_or_create.yaml (size: 52).</summary>
    public const int WireSize = 52;

    /// <summary>
    /// 0x00 — the contested 52-byte body, kept OPAQUE. Could be the login credential blob (reading
    /// A) OR the character-create body (reading B). NOT field-split here per the spec's explicit
    /// prohibition. spec: packets/1-6_login_or_create.yaml.
    /// </summary>
    public readonly BodyBuffer Body;

    /// <summary>0x00 — 52-byte opaque, collision-gated body. spec: packets/1-6_login_or_create.yaml.</summary>
    [InlineArray(52)]
    public struct BodyBuffer
    {
        private byte _element0;
    }
}