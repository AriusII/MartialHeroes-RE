// spec: Docs/RE/packets/5-5_actor_state_event.yaml — opcode 5/5 (0x50005), 32-byte fixed block.
//
// !!! CAPTURE-UNVERIFIED STATIC LAYOUT !!!
// Every offset/size below is a static inference (capture_verified: false in the spec). The
// (major:minor) routing is dispatch-table-confirmed; the field layout is a hypothesis until a
// live capture confirms it.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets;

/// <summary>
/// 5/5 — actor state event. Triggers visual effects, custom name updates, stance changes,
/// and 3D audio cues for the target actor. Fixed 32-byte payload.
/// spec: Docs/RE/packets/5-5_actor_state_event.yaml. CAPTURE-UNVERIFIED layout.
/// </summary>
[PacketOpcode(5, 5)] // spec: Docs/RE/packets/5-5_actor_state_event.yaml (opcode 5/5 = 0x50005)
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgActorStateEvent
{
    /// <summary>Packed opcode 0x50005 (5/5). spec: Docs/RE/packets/5-5_actor_state_event.yaml.</summary>
    public const uint OpcodeId = Opcodes.Opcodes.SmsgActorStateEvent;

    /// <summary>Declared wire size in bytes. spec: Docs/RE/packets/5-5_actor_state_event.yaml (size: 32).</summary>
    public const int WireSize = 32; // spec: Docs/RE/packets/5-5_actor_state_event.yaml

    /// <summary>0x00 — target actor sort key (u32). spec: Docs/RE/packets/5-5_actor_state_event.yaml.</summary>
    public readonly uint TargetSort;

    /// <summary>0x04 — target actor ID (u32). spec: Docs/RE/packets/5-5_actor_state_event.yaml.</summary>
    public readonly uint TargetId;

    /// <summary>0x08 — event source/actor ID (u32). spec: Docs/RE/packets/5-5_actor_state_event.yaml.</summary>
    public readonly uint ActorId;

    /// <summary>
    /// 0x0C — target custom title or name string (20 bytes, CP949, NUL-terminated within).
    /// spec: Docs/RE/packets/5-5_actor_state_event.yaml (Name: char[20]).
    /// </summary>
    public readonly NameBuffer Name;

    /// <summary>0x0C — 20-byte CP949 name field. spec: Docs/RE/packets/5-5_actor_state_event.yaml.</summary>
    [InlineArray(20)]
    public struct NameBuffer
    {
        private byte _element0;
    }
}