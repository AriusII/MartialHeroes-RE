// spec: Docs/RE/packets/5-15_ground_item_remove.yaml — opcode 5/15 (0x5000f), 16-byte fixed block.
//
// !!! CAPTURE-UNVERIFIED STATIC LAYOUT !!!
// Every offset/size below is a static inference (capture_verified: false in the spec). The
// (major:minor) routing and the 16-byte body-read length are CODE-CONFIRMED from the handler;
// the per-field roles are hypotheses pending a live capture.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets;

/// <summary>
/// 5/15 — ground item / tracked-world-object despawn. The client removes the tracked ground item
/// from its registry and optionally shows an "X picked up Y" notice for party context.
/// Fixed 16-byte payload.
/// spec: Docs/RE/packets/5-15_ground_item_remove.yaml. CAPTURE-UNVERIFIED layout.
/// </summary>
[PacketOpcode(5, 15)] // spec: Docs/RE/packets/5-15_ground_item_remove.yaml (opcode 5/15 = 0x5000f)
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgGroundItemRemove
{
    /// <summary>Packed opcode 0x5000f (5/15). spec: Docs/RE/packets/5-15_ground_item_remove.yaml.</summary>
    public const uint OpcodeId = Opcodes.Opcodes.SmsgTrackedWorldObjectRemove;

    /// <summary>Declared wire size in bytes. spec: Docs/RE/packets/5-15_ground_item_remove.yaml (size: 16).</summary>
    public const int WireSize = 16; // spec: Docs/RE/packets/5-15_ground_item_remove.yaml

    /// <summary>0x00 — picker actor sort key (u32). spec: Docs/RE/packets/5-15_ground_item_remove.yaml.</summary>
    public readonly uint Sort;

    /// <summary>0x04 — the actor that picked the item up (u32). spec: Docs/RE/packets/5-15_ground_item_remove.yaml.</summary>
    public readonly uint PickerId;

    /// <summary>0x08 — ground-item entity key to remove (matches spawn Param2) (u32). spec: Docs/RE/packets/5-15_ground_item_remove.yaml.</summary>
    public readonly uint TrackedId;

    /// <summary>0x0C — when set, shows a "X picked up Y" notice for party/relation context (u8). spec: Docs/RE/packets/5-15_ground_item_remove.yaml.</summary>
    public readonly byte NotifyFlag;

    /// <summary>0x0D — alignment padding to 16 bytes (3 bytes). spec: Docs/RE/packets/5-15_ground_item_remove.yaml (Pad0: bytes[3]).</summary>
    public readonly Pad0Buffer Pad0;

    /// <summary>0x0D — 3-byte alignment pad. spec: Docs/RE/packets/5-15_ground_item_remove.yaml.</summary>
    [InlineArray(3)]
    public struct Pad0Buffer
    {
        private byte _element0;
    }
}