// spec: Docs/RE/packets/2-7_whisper.yaml — opcode 2/7 (0x20007), VARIABLE-LENGTH packet.
//
// !!! CAPTURE-UNVERIFIED VALUE SEMANTICS !!!
// The 19-byte (0x13) prefix SHAPE is control-flow CONFIRMED from the client's chat send path; the
// wire VALUE semantics (the channel-code table and the @0x01 selector's meaning) stay
// capture/debugger-pending (capture_verified: false in the spec).
//
// THIS IS THE FIXED 19-BYTE PREFIX ONLY. The variable text body — a length-prefixed block
// [u32 textLength][text bytes] — is NOT modelled as a struct; the caller (Client.Application)
// hand-codes it. No fixed WireSize / size assertion: this packet has no fixed size.
//
// textLength for 2/7 EXCLUDES the terminating NUL: it is strlen(text), NOT strlen+1. (Contrast 3/21
// CmsgChatChannel, whose length prefix INCLUDES the NUL.) The text is strncpy-capped at 0x77 = 119
// bytes at the send site, CP949 — never a managed UTF-16 string on the wire.
// spec: Docs/RE/packets/2-7_whisper.yaml (TEXT BODY — EXCLUDES NUL, cap 119).

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets;

/// <summary>
/// 2/7 — FIXED 19-byte (0x13) prefix of the client chat send (CmsgChat). A SINGLE opcode carries every
/// everyday chat channel (say/party/guild/shout/alliance/whisper); the builder always writes a uniform
/// prefix — a channel byte, a 1-byte selector, then a 17-byte name area (zeroed for non-whisper
/// channels, the recipient name for whisper) — followed on the wire by a length-prefixed CP949 text
/// body the caller hand-codes. spec: Docs/RE/packets/2-7_whisper.yaml. CAPTURE-UNVERIFIED value
/// semantics. VARIABLE-LENGTH packet.
/// </summary>
[PacketOpcode(2, 7)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct CmsgWhisperHeader
{
    /// <summary>Packed opcode 0x20007 (2/7). spec: packets/2-7_whisper.yaml.</summary>
    public const uint OpcodeId = Opcodes.Opcodes.CmsgWhisper;

    /// <summary>Size of the FIXED prefix in bytes. spec: packets/2-7_whisper.yaml (uniform 19-byte (0x13) prefix).</summary>
    public const int HeaderSize = 19;

    /// <summary>0x00 — channel code selecting say/party/guild/shout/alliance/whisper (VALUE capture-pending). spec: packets/2-7_whisper.yaml.</summary>
    public readonly byte Channel;

    /// <summary>0x01 — sub-selector byte the builder writes after the channel; meaning capture-pending. spec: packets/2-7_whisper.yaml.</summary>
    public readonly byte Selector;

    /// <summary>
    /// 0x02 — 17-byte name area: the recipient character name for whisper (CP949, NUL-padded),
    /// all-zero for non-whisper channels. ALWAYS present (no broadcast-vs-whisper length split):
    /// the builder memsets 17 bytes then strncpy's up to 16 name bytes into it. CONTROL-FLOW CONFIRMED.
    /// spec: Docs/RE/packets/2-7_whisper.yaml (TargetName: bytes[17] @0x02).
    /// </summary>
    public readonly TargetNameBuffer TargetName;

    /// <summary>0x02 — 17-byte name area (NUL-padded, CP949). spec: packets/2-7_whisper.yaml.</summary>
    [InlineArray(17)]
    public struct TargetNameBuffer
    {
        private byte _element0;
    }
}