// spec: Docs/RE/packets/5-14_ground_item_spawn.yaml — opcode 5/14 (0x5000e), 48-byte fixed block.
//
// !!! CAPTURE-UNVERIFIED STATIC LAYOUT !!!
// Every offset/size below is a static inference (capture_verified: false in the spec). The
// (major:minor) routing and the 48-byte body-read length are CODE-CONFIRMED from the handler;
// the per-field roles are hypotheses pending a live capture.
//
// NOTE on offset annotations: the YAML lists Reserved1 starting at 0x21, but accumulating the
// declared field widths gives: Sort(4)+SourceId(4)+Mode(1)+Slot(1)+Pad0(2)+TemplateId(4)+
// Reserved0(4)+Param1(4)+Param2(4)+PosX(4)+PosZ(4) = 36 bytes (0x24), not 0x21. The YAML
// offset annotation appears to be a documentation typo; the FIELD ORDER and WIDTHS (which sum
// to the confirmed 48 bytes) are authoritative. spec: Docs/RE/packets/5-14_ground_item_spawn.yaml.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets;

/// <summary>
/// 5/14 — ground item / combat-effect instance spawn. The handler reads a fixed 48-byte body;
/// the named fields drive the world-item actor placement; two reserved holes (Reserved0 at +16,
/// Reserved1 after PosZ) are not consumed by the ground-item path. Fixed 48-byte payload.
/// spec: Docs/RE/packets/5-14_ground_item_spawn.yaml. CAPTURE-UNVERIFIED layout.
/// </summary>
[PacketOpcode(5, 14)] // spec: Docs/RE/packets/5-14_ground_item_spawn.yaml (opcode 5/14 = 0x5000e)
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgGroundItemSpawn
{
    /// <summary>Packed opcode 0x5000e (5/14). spec: Docs/RE/packets/5-14_ground_item_spawn.yaml.</summary>
    public const uint OpcodeId = Opcodes.Opcodes.SmsgCombatEffectInstanceSpawn;

    /// <summary>Declared wire size in bytes. spec: Docs/RE/packets/5-14_ground_item_spawn.yaml (size: 48).</summary>
    public const int WireSize = 48; // spec: Docs/RE/packets/5-14_ground_item_spawn.yaml

    /// <summary>+0 — source/dropper sort key; low byte 2 = Mob dropper (u32). spec: Docs/RE/packets/5-14_ground_item_spawn.yaml.</summary>
    public readonly uint Sort;

    /// <summary>+4 — dropper / source actor id (u32). spec: Docs/RE/packets/5-14_ground_item_spawn.yaml.</summary>
    public readonly uint SourceId;

    /// <summary>+8 — drop mode; 0xFF forces coin template 217000501 (i8). spec: Docs/RE/packets/5-14_ground_item_spawn.yaml.</summary>
    public readonly sbyte Mode;

    /// <summary>+9 — source slot index on the dropper (u8). spec: Docs/RE/packets/5-14_ground_item_spawn.yaml.</summary>
    public readonly byte Slot;

    /// <summary>+10 — alignment bytes (2 bytes). spec: Docs/RE/packets/5-14_ground_item_spawn.yaml (Pad0: bytes[2]).</summary>
    public readonly Pad0Buffer Pad0;

    /// <summary>+12 — ground-item 3D-model / effect template id (i32). spec: Docs/RE/packets/5-14_ground_item_spawn.yaml.</summary>
    public readonly int TemplateId;

    /// <summary>+16 — reserved hole; not read by the ground-item path (4 bytes). spec: Docs/RE/packets/5-14_ground_item_spawn.yaml (Reserved0: bytes[4]).</summary>
    public readonly Reserved0Buffer Reserved0;

    /// <summary>+20 — opaque (likely quantity or flags) (i32). spec: Docs/RE/packets/5-14_ground_item_spawn.yaml.</summary>
    public readonly int Param1;

    /// <summary>+24 — world entity key for this ground item; used by CmsgPickupItem (2/15) and 5/15 (i32). spec: Docs/RE/packets/5-14_ground_item_spawn.yaml.</summary>
    public readonly int Param2;

    /// <summary>+28 — world X position (f32). spec: Docs/RE/packets/5-14_ground_item_spawn.yaml.</summary>
    public readonly float PosX;

    /// <summary>+32 — world Z position (f32). spec: Docs/RE/packets/5-14_ground_item_spawn.yaml.</summary>
    public readonly float PosZ;

    /// <summary>
    /// +36 — reserved hole (11 bytes); not read by this handler.
    /// spec: Docs/RE/packets/5-14_ground_item_spawn.yaml (Reserved1: bytes[11]).
    /// NOTE: YAML annotates this at offset 0x21 but field accumulation places it at 0x24 (36 dec).
    /// The field ORDER and WIDTHS are authoritative; offset annotation appears to be a typo.
    /// </summary>
    public readonly Reserved1Buffer Reserved1;

    /// <summary>+47 — when set, drives a one-shot "rare drop" chat line (u8). spec: Docs/RE/packets/5-14_ground_item_spawn.yaml.</summary>
    public readonly byte NoticeFlag;

    /// <summary>+10 — 2-byte alignment pad. spec: Docs/RE/packets/5-14_ground_item_spawn.yaml.</summary>
    [InlineArray(2)]
    public struct Pad0Buffer
    {
        private byte _element0;
    }

    /// <summary>+16 — 4-byte reserved hole. spec: Docs/RE/packets/5-14_ground_item_spawn.yaml.</summary>
    [InlineArray(4)]
    public struct Reserved0Buffer
    {
        private byte _element0;
    }

    /// <summary>+36 — 11-byte reserved hole. spec: Docs/RE/packets/5-14_ground_item_spawn.yaml.</summary>
    [InlineArray(11)]
    public struct Reserved1Buffer
    {
        private byte _element0;
    }
}
