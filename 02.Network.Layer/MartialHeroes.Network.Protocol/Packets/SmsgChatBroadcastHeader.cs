// spec: Docs/RE/packets/5-7_chat_broadcast.yaml — opcode 5/7 (0x50007), VARIABLE-LENGTH packet.
//
// !!! CAPTURE-UNVERIFIED STATIC LAYOUT !!!
// Every offset/size below is a static inference (capture_verified: false in the spec). The
// (major:minor) routing is dispatch-table-confirmed; the field layout is a hypothesis until a
// live capture confirms it.
//
// THIS IS THE FIXED 36-BYTE HEADER ONLY. The variable text body that follows the header — its
// length encoding is unconfirmed (length-prefixed vs NUL-terminated vs rest-of-frame per the
// spec) — is NOT modelled as a struct; the caller (Client.Application) hand-codes it. No fixed
// WireSize / size assertion: this packet has no fixed total size.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets;

/// <summary>
/// 5/7 — FIXED 36-byte header of a server chat broadcast (sender identity + routing). Followed on the
/// wire by a variable-length message text body the caller hand-codes.
/// spec: Docs/RE/packets/5-7_chat_broadcast.yaml. CAPTURE-UNVERIFIED layout. VARIABLE-LENGTH packet.
/// </summary>
[PacketOpcode(5, 7)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgChatBroadcastHeader
{
    /// <summary>Packed opcode 0x50007 (5/7). spec: packets/5-7_chat_broadcast.yaml.</summary>
    public const uint OpcodeId = Opcodes.Opcodes.SmsgChatBroadcast;

    /// <summary>Size of the FIXED header in bytes. spec: packets/5-7_chat_broadcast.yaml (36-byte header).</summary>
    public const int HeaderSize = 36;

    /// <summary>0x00 — sender actor sort (low byte is the real sort). spec: packets/5-7_chat_broadcast.yaml.</summary>
    public readonly byte SenderSort;

    /// <summary>0x01 — alignment padding to the next dword. spec: same (Pad0: bytes[3]).</summary>
    private readonly byte _pad0_0;

    private readonly byte _pad0_1;
    private readonly byte _pad0_2;

    /// <summary>0x04 — sender actor id (LE u32). spec: packets/5-7_chat_broadcast.yaml.</summary>
    public readonly uint SenderId;

    /// <summary>0x08 — context id (target / room / whisper-peer id). spec: packets/5-7_chat_broadcast.yaml.</summary>
    public readonly uint ContextId;

    /// <summary>0x0c — reserved / sub-field. spec: packets/5-7_chat_broadcast.yaml (Reserved0C).</summary>
    public readonly byte Reserved0C;

    /// <summary>0x0d — chat verb / sub-command. spec: packets/5-7_chat_broadcast.yaml.</summary>
    public readonly byte SubCommand;

    /// <summary>0x0e — channel (6 or 7 routed as whisper). spec: packets/5-7_chat_broadcast.yaml.</summary>
    public readonly byte Channel;

    /// <summary>0x0f — reserved. spec: packets/5-7_chat_broadcast.yaml (Reserved0F).</summary>
    public readonly byte Reserved0F;

    /// <summary>
    /// 0x10 — sender display name, NUL-terminated CP949 in a fixed 20-byte buffer (0x10..0x23). Never a
    /// managed string on the wire. spec: packets/5-7_chat_broadcast.yaml.
    /// </summary>
    public readonly SenderNameBuffer SenderName;

    /// <summary>Typed view over the low byte of <see cref="SenderSort"/>. spec: Docs/RE/structs/actor.md.</summary>
    public ActorSort SenderSortKind => (ActorSort)SenderSort;

    /// <summary>0x10 — 20-byte CP949 sender-name buffer (NUL-terminated). spec: packets/5-7_chat_broadcast.yaml.</summary>
    [InlineArray(20)]
    public struct SenderNameBuffer
    {
        private byte _element0;
    }
}
