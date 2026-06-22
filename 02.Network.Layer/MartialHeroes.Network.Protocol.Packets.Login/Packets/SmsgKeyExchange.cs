// spec: Docs/RE/packets/0-0_key_exchange.yaml — opcode 0/0 (0x0), 62-byte fixed payload.
//
// VERIFICATION: routing+size confirmed (control-flow: handler reads a 54-byte RSA key blob then
// two trailing u32 scalars in that fixed order; 54 + 4 + 4 = 62). Blob internal layout confirmed.
// capture_verified: false — the two trailing scalars' VALUE meanings are capture/debugger-pending.
// ida_anchor: 263bd994 (CYCLE 7 2026-06-20, static-ida).
//
// This is the FIRST frame of the secure session. The client's inbound major-0 hardwired branch
// (NOT a switch) immediately replies with C2S 1/4 CmsgLoginCredential after parsing this.
//
// Field widths sum to size: 54 + 4 + 4 = 62. spec: Docs/RE/packets/0-0_key_exchange.yaml.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.Login.Packets;

/// <summary>
///     0/0 — RSA key exchange; the first secure-session frame (S2C, 62 bytes). Carries the server's RSA
///     public key in a 54-byte length-prefixed blob, followed by two trailing server scalars. Received over
///     the hardwired major-0 branch (NOT an inline switch). Its arrival drives the reactive C2S 1/4
///     credential reply. spec: Docs/RE/packets/0-0_key_exchange.yaml.
///     <para>
///         RSA KEY BLOB INTERNAL LAYOUT (54 bytes, CONFIRMED): a self-describing length-prefixed
///         modulus/exponent record: [2B headerA][2B headerB][u32 LE modulus-len][modulus bytes][u32 LE
///         exp-len][exp bytes]. The blob importer asserts it consumed exactly 54 bytes.
///         spec: Docs/RE/packets/0-0_key_exchange.yaml.
///     </para>
///     <para>
///         CRYPTO HAND-OFF: the modulus/exponent parse and the per-session cipher key schedule are owned
///         by Network.Crypto (specs/crypto.md). This struct owns only the framing/field layout.
///         spec: Docs/RE/specs/crypto.md §6.2.
///     </para>
///     CAPTURE-UNVERIFIED: the two trailing scalars' VALUE meanings.
/// </summary>
[PacketOpcode(0, 0)] // spec: Docs/RE/opcodes.md row 0/0 = 0x0 (major 0, hardwired branch)
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgKeyExchange
{
    /// <summary>Packed opcode 0x0 (0/0). spec: Docs/RE/opcodes.md; packets/0-0_key_exchange.yaml.</summary>
    public const uint OpcodeId = Opcodes.SmsgKeyExchange; // spec: Docs/RE/opcodes.md row 0/0

    /// <summary>Declared wire size in bytes. spec: Docs/RE/packets/0-0_key_exchange.yaml (size: 62; 54+4+4).</summary>
    public const int WireSize = 62; // spec: Docs/RE/packets/0-0_key_exchange.yaml

    // Compile-time width sum: 54 (RsaKeyBlob) + 4 (ServerScalarA) + 4 (ServerScalarB) = 62.

    /// <summary>
    ///     0x00 — 54-byte RSA public-key blob (length-prefixed modulus/exponent). Sub-layout:
    ///     [2B hdrA][2B hdrB][u32 LE modulus-len][modulus bytes][u32 LE exp-len][exp bytes].
    ///     The Network.Crypto layer imports the modulus/exponent from this blob.
    ///     spec: Docs/RE/packets/0-0_key_exchange.yaml (RsaKeyBlob @0x00, bytes[54]).
    /// </summary>
    public readonly RsaKeyBlobBuffer RsaKeyBlob; // spec: Docs/RE/packets/0-0_key_exchange.yaml offset 0x00

    /// <summary>
    ///     0x36 — first trailing server scalar (LE u32). Value meaning capture/debugger-pending.
    ///     spec: Docs/RE/packets/0-0_key_exchange.yaml (ServerScalarA @0x36, u32).
    /// </summary>
    public readonly uint ServerScalarA; // spec: Docs/RE/packets/0-0_key_exchange.yaml offset 0x36

    /// <summary>
    ///     0x3a — second trailing server scalar (LE u32). Value meaning capture/debugger-pending.
    ///     spec: Docs/RE/packets/0-0_key_exchange.yaml (ServerScalarB @0x3a, u32).
    /// </summary>
    public readonly uint ServerScalarB; // spec: Docs/RE/packets/0-0_key_exchange.yaml offset 0x3a

    /// <summary>
    ///     54-byte RSA public-key blob. Sub-layout (confirmed): [2B hdrA][2B hdrB][u32 LE mod-len][mod bytes]
    ///     [u32 LE exp-len][exp bytes]; the importer asserts exactly 54 bytes consumed.
    ///     spec: Docs/RE/packets/0-0_key_exchange.yaml (RsaKeyBlob).
    /// </summary>
    [InlineArray(54)] // spec: Docs/RE/packets/0-0_key_exchange.yaml (bytes[54])
    public struct RsaKeyBlobBuffer
    {
        private byte _element0;
    }
}