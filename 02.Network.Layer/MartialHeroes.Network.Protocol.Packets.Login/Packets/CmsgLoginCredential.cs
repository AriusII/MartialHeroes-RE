// spec: Docs/RE/packets/login.yaml — opcode 1/4 (0x10004), VARIABLE-LENGTH, crypto-dependent.
//
// THIS IS NOT A Pack=1 STRUCT. spec: Docs/RE/packets/login.yaml explicitly states:
//   "This is a VARIABLE-LENGTH, CRYPTO-DEPENDENT carrier, NOT a fixed Pack=1 struct."
//   "The fields below document the pre-cipher payload shape for the engineer; do NOT codegen a Pack=1 struct."
//
// The 1/4 payload is assembled by Network.Crypto (see specs/crypto.md §6):
//   build pre-image + RSA  ->  per-dword XOR 0x29 whitening  ->  byte cipher  ->  LZ4  ->  send
//
// Pre-image layout (spec: Docs/RE/packets/login.yaml):
//   0x00 (1B) SubOpcode    : constant 0x2B (43). DEBUGGER-VERIFIED.
//   0x01 (4B) AccountLength: strlen(account)+1 (NUL counted), u32 LE. Range: [2,20).
//   0x05 (N)  Account      : CP949 NUL-terminated; N = AccountLength.
//   +... (4B) PinLength    : OPTIONAL (PIN-gated). strlen(pin)+1, u32 LE.
//   +... (M)  PIN          : OPTIONAL (PIN-gated). NUL-terminated; M = PinLength.
// Then: [u32 LE ciphertext-length][big-endian RSA digit bytes]  -- the RSA-encrypted password.
//
// Whitening (spec: Docs/RE/packets/login.yaml, Docs/RE/specs/crypto.md §6.4):
//   Whole 1/4 payload XORed per-dword with key 0x29 (selector 0x40, complement-test false).
//
// Password staging (spec: Docs/RE/packets/login.yaml):
//   M = zero-filled buffer of PAD_WIDTH=17 bytes; password bytes copied in (no NUL). STATIC-CONFIRMED.
//   PKCS#1 v1.5 type-2 padding applied; RSA encrypt; length-prefixed digit array appended.

namespace MartialHeroes.Network.Protocol.Packets.Login.Packets;

/// <summary>
///     Constants for the 1/4 CmsgLoginCredential secure-auth-reply builder. This is NOT a
///     <c>[StructLayout(Pack=1)]</c> struct — the payload is variable-length and crypto-dependent.
///     The frame carries two back-to-back regions: (1) a plaintext pre-image led by
///     <see cref="CredentialSubOpcode" /> (the account + optional PIN length-prefixed), then (2) the RSA
///     PKCS#1 v1.5 ciphertext of the password, framed as [u32 LE len][BE digit bytes]. The whole payload
///     is then per-dword whitened with <see cref="WhiteningKey" /> before entering the normal outbound
///     send chain. spec: Docs/RE/packets/login.yaml; Docs/RE/specs/crypto.md §6.
/// </summary>
public static class CmsgLoginCredential
{
    /// <summary>Packed opcode 0x10004 (major 1, minor 4). spec: Docs/RE/packets/login.yaml.</summary>
    public const uint OpcodeId = 0x10004; // spec: Docs/RE/opcodes.md row 1/4

    /// <summary>
    ///     Payload sub-opcode byte at payload offset 0: constant 0x2B (43). DEBUGGER-VERIFIED.
    ///     spec: Docs/RE/packets/login.yaml (SubOpcode @0x00).
    /// </summary>
    public const byte CredentialSubOpcode = 0x2B; // spec: Docs/RE/packets/login.yaml SubOpcode

    /// <summary>
    ///     RSA plaintext M zero-fill pad width: 17 bytes (0x11). STATIC-CONFIRMED. The password is
    ///     copied into a zero-filled 17-byte buffer; the RSA encrypt consumes the whole buffer as M.
    ///     spec: Docs/RE/packets/login.yaml (PAD WIDTH = 17, STATIC-CONFIRMED).
    /// </summary>
    public const int PasswordPadWidth = 17; // spec: Docs/RE/packets/login.yaml

    /// <summary>
    ///     Maximum account name length (exclusive): strlen(account) must be &lt; 20 (0x14).
    ///     spec: Docs/RE/packets/login.yaml (AccountLength gate: strlen &lt; 0x14).
    /// </summary>
    public const int AccountMaxLength = 20; // spec: Docs/RE/packets/login.yaml

    /// <summary>
    ///     Per-dword whitening key applied to the whole 1/4 payload (XOR 0x29, selector 0x40,
    ///     complement-test false). Applied BEFORE the normal byte-cipher + LZ4 send chain.
    ///     spec: Docs/RE/packets/login.yaml (WHITENING + SEND); Docs/RE/specs/crypto.md §6.4.
    /// </summary>
    public const byte WhiteningKey = 0x29; // spec: Docs/RE/packets/login.yaml; Docs/RE/specs/crypto.md §6.4

    /// <summary>
    ///     Whitening selector byte (0x40). The complement-test for the selector is FALSE, so the key
    ///     is used as-is (<see cref="WhiteningKey" />). spec: Docs/RE/specs/crypto.md §6.4.
    /// </summary>
    public const byte WhiteningSelector = 0x40; // spec: Docs/RE/specs/crypto.md §6.4
}