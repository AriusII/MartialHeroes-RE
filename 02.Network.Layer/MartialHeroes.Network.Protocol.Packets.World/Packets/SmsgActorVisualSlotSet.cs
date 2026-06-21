// spec: Docs/RE/packets/5-12_actor_visual_slot_set.yaml — opcode 5/12 (0x5000c), 20-byte fixed block.
//
// !!! CAPTURE-UNVERIFIED STATIC LAYOUT !!!
// Every offset/size below is a static inference (capture_verified: false in the spec). The
// (major:minor) routing is dispatch-table-confirmed; the field layout is a hypothesis until a
// live capture confirms it.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

/// <summary>
///     5/12 — actor equipment/visual slot set. Updates the model visual representation of an actor's
///     equipped item, including upgrading flags and shield/weapon visibility toggles. Fixed 20-byte payload.
///     spec: Docs/RE/packets/5-12_actor_visual_slot_set.yaml. CAPTURE-UNVERIFIED layout.
/// </summary>
[PacketOpcode(5, 12)] // spec: Docs/RE/packets/5-12_actor_visual_slot_set.yaml (opcode 5/12 = 0x5000c)
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgActorVisualSlotSet
{
    /// <summary>Packed opcode 0x5000c (5/12). spec: Docs/RE/packets/5-12_actor_visual_slot_set.yaml.</summary>
    public const uint OpcodeId = Opcodes.SmsgActorVisualSlotSet;

    /// <summary>Declared wire size in bytes. spec: Docs/RE/packets/5-12_actor_visual_slot_set.yaml (size: 20).</summary>
    public const int WireSize = 20; // spec: Docs/RE/packets/5-12_actor_visual_slot_set.yaml

    /// <summary>0x00 — actor sort key, typically 1 for players (u32). spec: Docs/RE/packets/5-12_actor_visual_slot_set.yaml.</summary>
    public readonly uint ActorSort;

    /// <summary>0x04 — actor ID to update (u32). spec: Docs/RE/packets/5-12_actor_visual_slot_set.yaml.</summary>
    public readonly uint ActorId;

    /// <summary>0x08 — item database/visual ID; 0 to clear (u32). spec: Docs/RE/packets/5-12_actor_visual_slot_set.yaml.</summary>
    public readonly uint ItemId;

    /// <summary>0x0C — item upgrade level / visual glow index (u32). spec: Docs/RE/packets/5-12_actor_visual_slot_set.yaml.</summary>
    public readonly uint ItemUpgrade;

    /// <summary>0x10 — visual equipment slot index (0..19) (u8). spec: Docs/RE/packets/5-12_actor_visual_slot_set.yaml.</summary>
    public readonly byte SlotIndex;

    /// <summary>
    ///     0x11 — alignment padding to 20 bytes (3 bytes). spec: Docs/RE/packets/5-12_actor_visual_slot_set.yaml
    ///     (Padding: u8[3]).
    /// </summary>
    public readonly PaddingBuffer Padding;

    /// <summary>0x11 — 3-byte alignment pad. spec: Docs/RE/packets/5-12_actor_visual_slot_set.yaml.</summary>
    [InlineArray(3)]
    public struct PaddingBuffer
    {
        private byte _element0;
    }
}