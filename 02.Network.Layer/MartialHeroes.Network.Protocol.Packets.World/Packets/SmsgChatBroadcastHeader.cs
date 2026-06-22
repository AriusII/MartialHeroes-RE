// spec: Docs/RE/packets/5-7_chat_broadcast.yaml — opcode 5/7 (0x50007), VARIABLE-LENGTH packet.
// spec: Docs/RE/specs/chat.md §3 (channel model) §8.2 (S2C header layout).
//
// CONTROL-FLOW CONFIRMED (static IDA, IDB SHA 263bd994): the 36-byte fixed header layout (the
// handler reads a 0x24-byte block), the sender-name buffer at +0x10 (20 bytes), the channel byte at
// +0x0E, and the body framing ([u32 BodyLength][BodyLength CP949 bytes]) are all confirmed.
// Body endianness (u32 BodyLength) is capture/debugger-pending (non-blocking; implement LE).
// spec: Docs/RE/packets/5-7_chat_broadcast.yaml.
//
// CYCLE 11 (2026-06-22): body framing RESOLVED: past the 36-byte header the body is exactly ONE
// length-prefixed segment [u32 BodyLength][BodyLength CP949 bytes]. The client NUL-appends; NUL is
// NOT part of the counted length. This SUPERSEDES the old "body = rest of frame" hypothesis.
//   frame.size = 8 (frame header) + 36 (chat header) + 4 (BodyLength word) + BodyLength
// RD residual: u32 BodyLength endianness = debugger-pending; implement LE (platform default).
// spec: Docs/RE/packets/5-7_chat_broadcast.yaml (BODY framing CORRECTED, CYCLE 11).
//
// CHANNEL COLOUR CONSTANTS (spec: Docs/RE/specs/chat.md §3):
//   Code  7 = pink    0xFFFF797C  (NOT red — confirmed CYCLE 11 counter-walk; red is codes 16/17)
//   Code  9 = pink    0xFFFF797C  (GM/system, shares the pink colour with code 7)
//   Code 10 = yellow  0xFFFFFF00  (S2C-only event notice, message-template 49079)
//   Codes 16/17 = red/orange 0xFFFF4040 (S2C-only notice tail)
//   Code < 100 → chat-log ring; code > 100 → floating notice system (not the log).
//
// LAYOUT: Σ = 4+4+4+1+1+1+1+20 = 36 bytes (matches handler 0x24-byte read). ✓
//
// THIS IS THE FIXED 36-BYTE HEADER ONLY. The variable text body is hand-coded by the caller using
// the body-framing constants below. No fixed total WireSize.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;
using MartialHeroes.Network.Protocol.Core.Packets;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

/// <summary>
///     5/7 — FIXED 36-byte header of a server chat broadcast (sender identity + routing + channel).
///     Followed on the wire by a <c>[u32 BodyLength][BodyLength CP949 bytes]</c> body segment (CYCLE 11).
///     spec: Docs/RE/packets/5-7_chat_broadcast.yaml; Docs/RE/specs/chat.md §3 §8.2.
///     CONTROL-FLOW CONFIRMED (IDB SHA 263bd994). VARIABLE-LENGTH packet.
/// </summary>
/// <remarks>
///     <b>Body framing (CYCLE 11, RESOLVED):</b> after the 36-byte header comes exactly one
///     length-prefixed segment: a <c>u32 BodyLength</c> byte count followed by exactly that many CP949
///     bytes. The client appends its own NUL (not counted). Frame total size:
///     <c>8 (frame header) + 36 (chat header) + 4 (BodyLength word) + BodyLength</c>.
///     u32 BodyLength endianness is capture-pending; implement as LE.
///     spec: Docs/RE/packets/5-7_chat_broadcast.yaml (BODY framing CORRECTED, CYCLE 11).
/// </remarks>
[PacketOpcode(5, 7)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgChatBroadcastHeader
{
    /// <summary>Packed opcode 0x50007 (5/7). spec: Docs/RE/packets/5-7_chat_broadcast.yaml.</summary>
    public const uint OpcodeId = Opcodes.SmsgChatBroadcast;

    /// <summary>
    ///     Size of the FIXED header in bytes (handler reads 0x24 = 36 bytes).
    ///     Σ = 4+4+4+1+1+1+1+20 = 36. spec: Docs/RE/packets/5-7_chat_broadcast.yaml (36-byte header). Σ-verified.
    /// </summary>
    public const int HeaderSize = 36; // spec: Docs/RE/packets/5-7_chat_broadcast.yaml — Σ 4+4+4+1+1+1+1+20=36

    /// <summary>
    ///     Byte offset of the u32 BodyLength word relative to packet payload start (= 36 = 0x24).
    ///     The body text immediately follows.
    ///     spec: Docs/RE/packets/5-7_chat_broadcast.yaml (body starts at payload +0x24, CYCLE 11).
    /// </summary>
    public const int
        BodyLengthOffset = HeaderSize; // spec: Docs/RE/packets/5-7_chat_broadcast.yaml (BODY framing, CYCLE 11)

    // --- S2C channel colour constants (spec: Docs/RE/specs/chat.md §3) ---
    // Colours are 32-bit ARGB (0xAARRGGBB).

    /// <summary>
    ///     Channel 0 (say/normal) log colour: white 0xFFFFFFFF.
    ///     spec: Docs/RE/specs/chat.md §3 (code 0, CONFIRMED).
    /// </summary>
    public const uint ColourSay = 0xFFFFFFFF; // spec: Docs/RE/specs/chat.md §3 code 0

    /// <summary>
    ///     Channel 1 (whisper) log colour: lavender 0xFFCC99FF.
    ///     spec: Docs/RE/specs/chat.md §3 (code 1, CONFIRMED — label corrected from "shout").
    /// </summary>
    public const uint ColourWhisper = 0xFFCC99FF; // spec: Docs/RE/specs/chat.md §3 code 1

    /// <summary>
    ///     Channel 2 (party) log colour: cyan 0xFF00FFFF.
    ///     spec: Docs/RE/specs/chat.md §3 (code 2, CONFIRMED).
    /// </summary>
    public const uint ColourParty = 0xFF00FFFF; // spec: Docs/RE/specs/chat.md §3 code 2

    /// <summary>
    ///     Channel 3 (guild) log colour: green 0xFF33FF66.
    ///     spec: Docs/RE/specs/chat.md §3 (code 3, CONFIRMED).
    /// </summary>
    public const uint ColourGuild = 0xFF33FF66; // spec: Docs/RE/specs/chat.md §3 code 3

    /// <summary>
    ///     Channel 6 (Misia / world-shout) log colour: yellow 0xFFFFFF00.
    ///     spec: Docs/RE/specs/chat.md §3 (code 6, CONFIRMED — label refined from earlier "shout").
    /// </summary>
    public const uint ColourMisia = 0xFFFFFF00; // spec: Docs/RE/specs/chat.md §3 code 6

    /// <summary>
    ///     Channel 7 (special Misia) log colour: pink 0xFFFF797C.
    ///     NOT red — confirmed CYCLE 11 counter-walk (a dirty mis-read was caught and reverted).
    ///     Red belongs to S2C-only codes 16/17. spec: Docs/RE/specs/chat.md §3 (code 7, CONFIRMED).
    /// </summary>
    public const uint ColourSpecialMisia = 0xFFFF797C; // spec: Docs/RE/specs/chat.md §3 code 7 = pink (NOT red)

    /// <summary>
    ///     Channel 9 (GM/system) log colour: pink 0xFFFF797C (shares colour with code 7).
    ///     spec: Docs/RE/specs/chat.md §3 (code 9, CONFIRMED — label corrected from "whisper").
    /// </summary>
    public const uint ColourGmSystem = 0xFFFF797C; // spec: Docs/RE/specs/chat.md §3 code 9 = pink

    /// <summary>
    ///     S2C-only channel 10: yellow event-notice colour 0xFFFFFF00, message-template id 49079.
    ///     Routes to floating notice system (code > 100 rule does NOT apply here — code 10 routes to
    ///     the chat-log ring per §6.3). spec: Docs/RE/specs/chat.md §8.2 (code 10 = yellow 49079 notice).
    /// </summary>
    public const uint ColourNoticeYellow = 0xFFFFFF00; // spec: Docs/RE/specs/chat.md §8.2 code 10

    /// <summary>
    ///     S2C-only channels 16/17: red/orange notice colour 0xFFFF4040.
    ///     spec: Docs/RE/specs/chat.md §8.2 (codes 16/17 = red/orange notice tail). S2C-only.
    /// </summary>
    public const uint ColourNoticeRed = 0xFFFF4040; // spec: Docs/RE/specs/chat.md §8.2 codes 16/17

    /// <summary>
    ///     Channel 15 (alliance) log colour: blue 0xFF82C4FF.
    ///     spec: Docs/RE/specs/chat.md §3 (code 15, CONFIRMED).
    /// </summary>
    public const uint ColourAlliance = 0xFF82C4FF; // spec: Docs/RE/specs/chat.md §3 code 15

    // --- Wire fields (Pack=1 sequential; offsets are packet-payload-relative) ---

    /// <summary>
    ///     0x00 — sender composite-key word A (LE u32); low byte = sender sort selector.
    ///     spec: Docs/RE/packets/5-7_chat_broadcast.yaml (SenderKeyA @0x00, u32, CONFIRMED).
    /// </summary>
    public readonly uint SenderKeyA; // spec: Docs/RE/packets/5-7_chat_broadcast.yaml @0x00 SenderKeyA u32

    /// <summary>
    ///     0x04 — sender composite-key word B / sender actor id (LE u32).
    ///     spec: Docs/RE/packets/5-7_chat_broadcast.yaml (SenderId @0x04, u32).
    /// </summary>
    public readonly uint SenderId; // spec: Docs/RE/packets/5-7_chat_broadcast.yaml @0x04 SenderId u32

    /// <summary>
    ///     0x08 — context id (target / room / whisper-peer id; channel-0 gates on == 301).
    ///     spec: Docs/RE/packets/5-7_chat_broadcast.yaml (ContextId @0x08, u32, CONFIRMED).
    /// </summary>
    public readonly uint ContextId; // spec: Docs/RE/packets/5-7_chat_broadcast.yaml @0x08 ContextId u32

    /// <summary>
    ///     0x0C — reserved / sub-field (1 byte). spec: Docs/RE/packets/5-7_chat_broadcast.yaml (Reserved0C @0x0C).
    /// </summary>
    public readonly byte Reserved0C; // spec: Docs/RE/packets/5-7_chat_broadcast.yaml @0x0C Reserved0C u8

    /// <summary>
    ///     0x0D — chat verb / sub-command; drives a 0/1/2 branch immediately after the header read.
    ///     spec: Docs/RE/packets/5-7_chat_broadcast.yaml (SubCommand @0x0D, CONFIRMED).
    /// </summary>
    public readonly byte SubCommand; // spec: Docs/RE/packets/5-7_chat_broadcast.yaml @0x0D SubCommand u8

    /// <summary>
    ///     0x0E — channel code; drives the routing + colour ladder (see chat.md §3 and the colour
    ///     constants above). Codes &lt; 100 → chat-log ring; codes &gt; 100 → floating notice system.
    ///     spec: Docs/RE/packets/5-7_chat_broadcast.yaml (Channel @0x0E, CONFIRMED); Docs/RE/specs/chat.md §3.
    /// </summary>
    public readonly byte Channel; // spec: Docs/RE/packets/5-7_chat_broadcast.yaml @0x0E Channel u8

    /// <summary>
    ///     0x0F — reserved (1 byte). spec: Docs/RE/packets/5-7_chat_broadcast.yaml (Reserved0F @0x0F).
    /// </summary>
    public readonly byte Reserved0F; // spec: Docs/RE/packets/5-7_chat_broadcast.yaml @0x0F Reserved0F u8

    /// <summary>
    ///     0x10 — sender display name (fixed 20-byte CP949 buffer, NUL-terminated, 0x10..0x23).
    ///     Never a managed string on the wire. Decode with CP949 at a higher layer.
    ///     spec: Docs/RE/packets/5-7_chat_broadcast.yaml (SenderName @0x10, bytes[20]).
    /// </summary>
    public readonly SenderNameBuffer
        SenderName; // spec: Docs/RE/packets/5-7_chat_broadcast.yaml @0x10 SenderName bytes[20]

    // --- Typed accessors ---

    /// <summary>
    ///     Low byte of <see cref="SenderKeyA" /> — actor sort selector (1=PC, 2=Mob/NPC).
    ///     spec: Docs/RE/packets/5-7_chat_broadcast.yaml (SenderKeyA low byte = sort); Docs/RE/structs/actor.md.
    /// </summary>
    public ActorSort SenderSortKind => (ActorSort)(byte)SenderKeyA; // spec: Docs/RE/structs/actor.md (sort low byte)

    /// <summary>
    ///     Low byte of <see cref="SenderKeyA" /> as a raw <see langword="byte" /> — actor sort selector
    ///     (1=PC, 2=Mob/NPC). Provided for API compatibility; prefer <see cref="SenderSortKind" /> for
    ///     typed dispatch. spec: Docs/RE/packets/5-7_chat_broadcast.yaml (SenderKeyA low byte = sort).
    /// </summary>
    public byte SenderSort => (byte)SenderKeyA; // spec: Docs/RE/packets/5-7_chat_broadcast.yaml (SenderKeyA low byte)

    /// <summary>
    ///     Returns <see langword="true" /> when <see cref="Channel" /> routes to the chat-log ring
    ///     (channel &lt; 100), <see langword="false" /> when it routes to the floating notice system
    ///     (channel &gt; 100). Codes 100 and 110 are dropped by the client.
    ///     spec: Docs/RE/specs/chat.md §6.3 (channel routing).
    /// </summary>
    public bool IsLogChannel => Channel < 100; // spec: Docs/RE/specs/chat.md §6.3

    /// <summary>0x10 — 20-byte CP949 sender-name buffer (NUL-terminated). spec: Docs/RE/packets/5-7_chat_broadcast.yaml.</summary>
    [InlineArray(20)]
    public struct SenderNameBuffer
    {
        private byte _element0;
    }
}