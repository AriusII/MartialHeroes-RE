// spec: Docs/RE/structs/item.md — "ItemSlotRecord — 16-byte wire/runtime unit".
//
// !!! CAPTURE-UNVERIFIED STATIC LAYOUT !!!
// The 16-byte stride is CONFIRMED (cross-checked by the 4/149 item-panel slot path and the
// SpawnDescriptor equipment array); the per-field interpretation is partly UNVERIFIED per item.md.
// This is a reusable record, not a top-level packet, so it carries no [PacketOpcode].

using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

/// <summary>
/// The canonical 16-byte item-slot unit. The same bytes are transmitted by the item-panel slot-update
/// path (4/149) and back each equipment slot inside the SpawnDescriptor (8 records, 16-byte stride at
/// SD+0x54). spec: Docs/RE/structs/item.md ("ItemSlotRecord"); Docs/RE/structs/spawn_descriptor.md
/// ("Equipment slot array"). CAPTURE-UNVERIFIED field interpretation.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct ItemSlotRecord
{
    /// <summary>Wire/runtime size of one slot record. spec: Docs/RE/structs/item.md (16-byte unit).</summary>
    public const int WireSize = 16;

    /// <summary>0x00 — first dword, copied verbatim (template id or flags; not dispatched). UNVERIFIED. spec: item.md.</summary>
    public readonly uint Word0;

    /// <summary>0x04 — actor id of the item-actor instance. spec: Docs/RE/structs/item.md (item_actor_id).</summary>
    public readonly uint ItemActorId;

    /// <summary>0x08 — low 32 bits of a UNIX time_t expiry; 0 = permanent. spec: Docs/RE/structs/item.md.</summary>
    public readonly uint ExpiryLo;

    /// <summary>0x0c — high 32 bits of the same expiry time_t; 0 for most items. spec: Docs/RE/structs/item.md.</summary>
    public readonly uint ExpiryHi;
}
