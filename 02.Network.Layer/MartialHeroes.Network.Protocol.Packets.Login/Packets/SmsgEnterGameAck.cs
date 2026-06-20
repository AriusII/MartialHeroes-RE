// spec: Docs/RE/packets/3-5_enter_game_response.yaml — opcode 3/5 (0x30005), 44-byte fixed block.
//
// !!! CAPTURE-UNVERIFIED STATIC LAYOUT !!!
// Every offset/size below is a static inference (capture_verified: false in the spec). The
// (major:minor) routing is dispatch-table-confirmed; the field layout is a hypothesis until a
// live capture confirms it. The exact name length within NameBlock is UNKNOWN per the spec.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.Login.Packets;

/// <summary>
///     3/5 — enter-world ACCOUNT/BILLING acknowledgement; the direct reply to C2S 1/9. Carries the
///     account/character name, a billing flag, and the account character count, and its side effect moves
///     the client to the LOADING scene. It is NOT the world snapshot (map/position/self arrive on 4/1).
///     40-byte account/billing block + trailing u32 char count = 44 bytes total.
///     spec: Docs/RE/packets/3-5_enter_game_response.yaml. CAPTURE-UNVERIFIED value semantics.
/// </summary>
[PacketOpcode(3, 5)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgEnterGameAck
{
    /// <summary>Packed opcode 0x30005 (3/5). spec: packets/3-5_enter_game_response.yaml.</summary>
    public const uint OpcodeId = Opcodes.SmsgEnterGameAck;

    /// <summary>Declared wire size in bytes. spec: packets/3-5_enter_game_response.yaml (size: 44).</summary>
    public const int WireSize = 44;

    /// <summary>
    ///     0x00 — character/account name region; a NUL-terminated ASCII string lives somewhere inside
    ///     this 28-byte block (exact length unconfirmed). spec: packets/3-5_enter_game_response.yaml.
    /// </summary>
    public readonly NameBlockBuffer NameBlock;

    /// <summary>
    ///     0x1c — billing flag (LE u32), at +28 within the leading 40-byte block; fed to the client
    ///     billing/session object. (Distinct from the 256-byte <c>BillingState</c> subscription singleton in
    ///     structs/runtime_singletons.md §3.5 — this is the packet's wire field.)
    ///     spec: Docs/RE/packets/3-5_enter_game_response.yaml (BillingFlag @0x1c, u32).
    /// </summary>
    public readonly uint BillingFlag;

    /// <summary>0x20 — unconsumed remainder of the 40-byte leading block. spec: same (BlockTail: bytes[8]).</summary>
    public readonly BlockTailBuffer BlockTail;

    /// <summary>0x28 — character count (LE u32), read by a second 4-byte read. spec: same.</summary>
    public readonly uint CharacterCount;

    /// <summary>0x00 — 28-byte name region (asciiz within). spec: packets/3-5_enter_game_response.yaml.</summary>
    [InlineArray(28)]
    public struct NameBlockBuffer
    {
        private byte _element0;
    }

    /// <summary>0x20 — 8-byte unconsumed block remainder. spec: packets/3-5_enter_game_response.yaml.</summary>
    [InlineArray(8)]
    public struct BlockTailBuffer
    {
        private byte _element0;
    }
}