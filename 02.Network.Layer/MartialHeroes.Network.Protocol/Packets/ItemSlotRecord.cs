// spec: Docs/RE/structs/item.md §1 — "ItemSlotRecord — 16-byte wire/runtime unit".
//
// !!! CAPTURE-UNVERIFIED FIELD VALUE SEMANTICS !!!
// The 16-byte stride is CONFIRMED (cross-checked by the 4/149 item-panel slot path and the
// SpawnDescriptor equipment array); the per-field VALUE interpretation is partly UNVERIFIED per
// item.md §1/§1a. This is a reusable record, not a top-level packet, so it carries no [PacketOpcode].

using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

/// <summary>
/// The canonical 16-byte item-slot unit shared by all item containers. The same 16 bytes are
/// transmitted by the item-panel slot-update path (4/149, a header + a run of these records) and
/// back each equipment slot inside the local-player SpawnDescriptor's EquipTable — a 20-record,
/// 16-byte-stride array at SD+0x54 (item.md §3; this supersedes the earlier "8 records" reading).
/// spec: Docs/RE/structs/item.md §1 ("ItemSlotRecord"), §3 ("EquipTable — 20-slot equipment array").
/// CAPTURE-UNVERIFIED field value semantics.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct ItemSlotRecord
{
    /// <summary>Wire/runtime size of one slot record. spec: Docs/RE/structs/item.md §1 (16-byte unit).</summary>
    public const int WireSize = 16;

    /// <summary>
    /// 0x00 — leading dword, copied verbatim. Per item.md §1 this breaks down as <c>flags_a</c> (+0x00)
    /// and <c>flags_b</c> (+0x01) — server bytes copied but never individually dispatched — plus two
    /// CLIENT-LOCAL drag/drop flag bytes (+0x02/+0x03) the server never sends. Modelled here as one
    /// opaque dword; value semantics UNVERIFIED. spec: Docs/RE/structs/item.md §1.
    /// </summary>
    public readonly uint Word0;

    /// <summary>0x04 — id of the item's runtime actor instance; 0 = empty slot. spec: Docs/RE/structs/item.md §1 (item_actor_id).</summary>
    public readonly uint ItemActorId;

    /// <summary>
    /// 0x08 — OVERLOADED by item type (item.md §1a): stackable = quantity (1..1000); timed = low 32
    /// bits of a UNIX time_t expiry (0 = permanent). spec: Docs/RE/structs/item.md §1/§1a.
    /// </summary>
    public readonly uint ExpiryLo;

    /// <summary>
    /// 0x0c — OVERLOADED by item type (item.md §1a): upgradeable gear = running enchant-progress
    /// accumulator; timed = high 32 bits of the same time_t (0 for most items).
    /// spec: Docs/RE/structs/item.md §1/§1a.
    /// </summary>
    public readonly uint ExpiryHi;
}