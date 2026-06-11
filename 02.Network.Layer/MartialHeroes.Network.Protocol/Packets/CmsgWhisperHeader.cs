// spec: Docs/RE/packets/2-7_whisper.yaml — opcode 2/7 (0x20007), VARIABLE-LENGTH packet.
//
// !!! CAPTURE-UNVERIFIED STATIC LAYOUT !!!
// Every offset/size below is a static inference (capture_verified: false in the spec). The
// (major:minor) routing is dispatch-table-confirmed; the field layout is a hypothesis until a
// live capture confirms it. Only the 16-byte TargetName is high-confidence per the spec.
//
// THIS IS THE FIXED 19-BYTE HEADER ONLY. The variable text body — a length-prefixed block
// [u32 textLength][text bytes] (length includes the terminating NUL, text <= 119 chars) — is NOT
// modelled as a struct; the caller (Client.Application) hand-codes it. No fixed WireSize / size
// assertion: this packet has no fixed size.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets;

/// <summary>
/// 2/7 — FIXED 19-byte header of the client whisper (named-target private message). Followed on the
/// wire by a length-prefixed text body the caller hand-codes.
/// spec: Docs/RE/packets/2-7_whisper.yaml. CAPTURE-UNVERIFIED layout. VARIABLE-LENGTH packet.
/// </summary>
[PacketOpcode(2, 7)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct CmsgWhisperHeader
{
    /// <summary>Packed opcode 0x20007 (2/7). spec: packets/2-7_whisper.yaml.</summary>
    public const uint OpcodeId = Opcodes.Opcodes.CmsgWhisper;

    /// <summary>Size of the FIXED header in bytes. spec: packets/2-7_whisper.yaml (19-byte header).</summary>
    public const int HeaderSize = 19;

    /// <summary>0x00 — channel / message sub-type selector. spec: packets/2-7_whisper.yaml.</summary>
    public readonly byte ChannelType;

    /// <summary>0x01 — flag byte. spec: packets/2-7_whisper.yaml.</summary>
    public readonly byte Flag;

    /// <summary>
    /// 0x02 — recipient character name, NUL-padded into a fixed 16-byte buffer. HIGH CONFIDENCE.
    /// spec: packets/2-7_whisper.yaml.
    /// </summary>
    public readonly TargetNameBuffer TargetName;

    /// <summary>0x12 — trailing byte rounding the header out to 19 bytes. spec: packets/2-7_whisper.yaml.</summary>
    public readonly byte HeaderTail;

    /// <summary>0x02 — 16-byte target-name buffer (NUL-padded). spec: packets/2-7_whisper.yaml.</summary>
    [InlineArray(16)]
    public struct TargetNameBuffer
    {
        private byte _element0;
    }
}