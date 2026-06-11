// spec: Docs/RE/packets/5-13_actor_movement_update.yaml — opcode 5/13 (0x5000d), 40-byte fixed block.
//
// !!! CAPTURE-UNVERIFIED STATIC LAYOUT !!!
// Every offset/size below is a static inference (capture_verified: false in the spec). The
// (major:minor) routing is dispatch-table-confirmed; the field layout is a hypothesis until a
// live capture confirms it. Whether the Pad* regions carry real data is UNKNOWN per the spec.

using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets;

/// <summary>
/// 5/13 — actor movement update: current XZ, destination XZ, facing yaw, for interpolation.
/// Fixed 40-byte payload block, XZ-plane (world Y never sent), LE float coords.
/// spec: Docs/RE/packets/5-13_actor_movement_update.yaml. CAPTURE-UNVERIFIED layout.
/// </summary>
[PacketOpcode(5, 13)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgActorMovementUpdate
{
    /// <summary>Packed opcode 0x5000d (5/13). spec: packets/5-13_actor_movement_update.yaml.</summary>
    public const uint OpcodeId = Opcodes.Opcodes.SmsgActorMovementUpdate;

    /// <summary>Declared wire size in bytes. spec: packets/5-13_actor_movement_update.yaml (size: 40).</summary>
    public const int WireSize = 40;

    /// <summary>0x00 — actor category (1=PC, 2=Mob, 3=NPC); selects the interp branch. spec: same.</summary>
    public readonly byte Sort;

    /// <summary>0x01 — alignment padding to the next dword. spec: same (Pad0: bytes[3]).</summary>
    private readonly byte _pad0_0;
    private readonly byte _pad0_1;
    private readonly byte _pad0_2;

    /// <summary>0x04 — actor id (LE u32). spec: packets/5-13_actor_movement_update.yaml.</summary>
    public readonly uint ActorId;

    /// <summary>0x08 — facing yaw (radians-ish), fed to a yaw-quaternion builder. spec: same.</summary>
    public readonly float Yaw;

    /// <summary>0x0c — current world X (LE f32). spec: packets/5-13_actor_movement_update.yaml.</summary>
    public readonly float PosX;

    /// <summary>0x10 — current world Z (LE f32); world Y is never sent (forced 0). spec: same.</summary>
    public readonly float PosZ;

    /// <summary>0x14 — destination/move-to X (LE f32). spec: packets/5-13_actor_movement_update.yaml.</summary>
    public readonly float DestX;

    /// <summary>0x18 — destination/move-to Z (LE f32). spec: packets/5-13_actor_movement_update.yaml.</summary>
    public readonly float DestZ;

    /// <summary>0x1c — 1 = running (affects speed/anim). spec: packets/5-13_actor_movement_update.yaml.</summary>
    public readonly byte RunFlag;

    /// <summary>0x1d — alignment padding. spec: same (Pad1: bytes[3]).</summary>
    private readonly byte _pad1_0;
    private readonly byte _pad1_1;
    private readonly byte _pad1_2;

    /// <summary>0x20 — speed/scale float, ~1.0 default. spec: packets/5-13_actor_movement_update.yaml.</summary>
    public readonly float SpeedScale;

    /// <summary>0x24 — motion/action code; 5 =&gt; instant snap branch. spec: same.</summary>
    public readonly byte MotionCode;

    /// <summary>0x25 — alignment padding. spec: same (Pad2: bytes[1]).</summary>
    private readonly byte _pad2_0;

    /// <summary>0x26 — stance / secondary-state byte. spec: packets/5-13_actor_movement_update.yaml.</summary>
    public readonly byte StanceByte;

    /// <summary>0x27 — trailing padding to 40 bytes. spec: same (Pad3: bytes[1]).</summary>
    private readonly byte _pad3_0;

    /// <summary>Typed view over <see cref="Sort"/>. spec: Docs/RE/structs/actor.md.</summary>
    public ActorSort SortKind => (ActorSort)Sort;
}
