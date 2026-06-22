// spec: Docs/RE/packets/5-10_combat_death.yaml — opcode 5/10 (0x5000a), 20-byte fixed block.
//
// Control-flow-confirmed on IDB SHA 263bd994 (CYCLE 12 / Phase 3): the 20-byte read, the
// (VictimId, VictimSort) and (KillerId, KillerSort) composite-key lookups, the five field offsets,
// and the DeathCause switch structure are all confirmed. Value-only residuals (death-penalty
// magnitudes, effect/sound/message ids, level threshold) are CAPTURE-PENDING — do NOT model them
// as wire fields; they are NON-blocking runtime residuals.
// spec: Docs/RE/packets/5-10_combat_death.yaml (readiness: IMPLEMENTATION-READY)

using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

/// <summary>
///     5/10 — character/actor death push. The server announces that an actor has died and who
///     killed it. The client resolves victim and killer by their composite (id, sort) keys,
///     clears combat/buff state, plays the death motion, and (for the local player) opens the
///     respawn UI and enqueues the respawn-countdown timer.
///     <para>
///         Fixed 20-byte payload (Pack=1). Width sum: 1+3+4+4+1+3+4 = 20. ✓
///     </para>
///     spec: Docs/RE/packets/5-10_combat_death.yaml.
/// </summary>
[PacketOpcode(5, 10)] // spec: Docs/RE/packets/5-10_combat_death.yaml (opcode 0x5000a = major 5, minor 10)
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgCharDeath
{
    /// <summary>Packed opcode 0x5000a (5/10). spec: Docs/RE/packets/5-10_combat_death.yaml.</summary>
    public const uint OpcodeId = Opcodes.SmsgCharDeath;

    /// <summary>Declared wire size in bytes. spec: Docs/RE/packets/5-10_combat_death.yaml (size: 20).</summary>
    public const int WireSize = 20;

    // -------------------------------------------------------------------------
    // Wire fields. Offsets are payload-relative (offset 0 = first payload byte).
    // Pack=1; widths sum exactly to WireSize = 20.
    // spec: Docs/RE/packets/5-10_combat_death.yaml (fields table).
    // -------------------------------------------------------------------------

    /// <summary>
    ///     +0x00 (u8) Dying actor sort byte — the low byte of the sort dword; 1 = player.
    ///     The id half of the victim composite key is <see cref="VictimId" /> at +0x04.
    ///     spec: Docs/RE/packets/5-10_combat_death.yaml (VictimSort, offset 0).
    /// </summary>
    public readonly byte VictimSort; // spec: 5-10_combat_death.yaml +0x00 (u8, low byte of sort dword)

    /// <summary>
    ///     +0x01 (u8[3]) Upper bytes of the victim sort dword — not consumed by the handler.
    ///     spec: Docs/RE/packets/5-10_combat_death.yaml (VictimPad, offset 1, u8[3]).
    /// </summary>
    private readonly byte _victimPad0; // spec: 5-10_combat_death.yaml +0x01..+0x03 (not consumed)

    private readonly byte _victimPad1;
    private readonly byte _victimPad2;

    /// <summary>
    ///     +0x04 (u32) Dying actor id — the id half of the victim composite key (id, VictimSort).
    ///     spec: Docs/RE/packets/5-10_combat_death.yaml (VictimId, offset 4).
    /// </summary>
    public readonly uint VictimId; // spec: 5-10_combat_death.yaml +0x04 (u32)

    /// <summary>
    ///     +0x08 (i32) Death cause / result selector, switched by the handler:
    ///     0 = normal death; 1 = PK type A; 2 = PK type B; 3 = special / no-modal.
    ///     spec: Docs/RE/packets/5-10_combat_death.yaml (DeathCause, offset 8).
    /// </summary>
    /// <remarks>
    ///     Death-penalty magnitudes (XP loss, durability, drops), effect/sound/message ids, and
    ///     the level threshold for respawn-modal mode selection are NON-blocking value residuals —
    ///     NOT modelled as wire fields.
    ///     spec: 5-10_combat_death.yaml (debugger-pending, CYCLE 12 deferred).
    /// </remarks>
    public readonly int
        DeathCause; // spec: 5-10_combat_death.yaml +0x08 (i32; 0=normal/1=PK-A/2=PK-B/3=special-no-modal)

    /// <summary>
    ///     +0x0c (u8) Killer actor sort byte — the low byte of the killer sort dword.
    ///     The id half of the killer composite key is <see cref="KillerId" /> at +0x10.
    ///     spec: Docs/RE/packets/5-10_combat_death.yaml (KillerSort, offset 12).
    /// </summary>
    public readonly byte KillerSort; // spec: 5-10_combat_death.yaml +0x0c (u8, low byte of sort dword)

    /// <summary>
    ///     +0x0d (u8[3]) Upper bytes of the killer sort dword — not consumed by the handler.
    ///     spec: Docs/RE/packets/5-10_combat_death.yaml (KillerPad, offset 13, u8[3]).
    /// </summary>
    private readonly byte _killerPad0; // spec: 5-10_combat_death.yaml +0x0d..+0x0f (not consumed)

    private readonly byte _killerPad1;
    private readonly byte _killerPad2;

    /// <summary>
    ///     +0x10 (u32) Killer actor id — the id half of the killer composite key (id, KillerSort).
    ///     spec: Docs/RE/packets/5-10_combat_death.yaml (KillerId, offset 16).
    /// </summary>
    public readonly uint KillerId; // spec: 5-10_combat_death.yaml +0x10 (u32)
}