# Buff / Debuff Subsystem Specification — clean-room neutral spec

> Clean-room neutral spec. Promoted from dirty-room analyst notes by the spec-author —
> **rewritten, never copied**. No decompiler identifiers, no binary addresses, no pseudo-code.
> This document is the **authority** for the in-world status (buff / debuff) model: the 30-slot
> per-actor buff table, the periodic tick, the buff-id effect-kind dispatch (including the
> crowd-control / status codes), the visual / icon resolution chains, and the local-player mirror
> tables the HUD buff bar reads.
>
> **Scope.** The runtime buff-slot table and its tick; the buff-slot push that fills it; how a
> `buff_id` resolves to a visual effect, a gameplay state change, and an on-screen icon. **Server
> authority is the rule:** the client owns only the *visual / motion-state* side of a buff — the
> authoritative stat magnitudes (HP/MP deltas, attack/defence modifiers, per-tick DoT/HoT numbers)
> are server-side and never computed in the client.
>
> **Cross-references.** `specs/skills.md §6` (the cast pipeline that requests skills; the skill-side
> view of the same slots — `skills.md` defers to *this* spec for the buff-table model);
> `structs/actor.md` (the buff-slot array at actor +520 and the buff-related actor state fields);
> `specs/world_systems.md` (the HUD buff bar). The wire field layout of the buff push is owned by
> `opcodes.md` / `packets/` (referenced here by canonical name and body size, not re-derived).

---

## Status and verification banner

> **verification:** control-flow-confirmed (static) on `doida.exe` IDB SHA **263bd994**, CYCLE 7
> (2026-06-20). The 30-slot count, the 12-byte slot stride, the actor +520 array base, the **4000 ms**
> tick interval, the buff-id effect-kind dispatch branches, the EffectManager visual-resolution index,
> the icon-position disk table layout, and the local-player mirror tables are all **read directly from
> the binary's structure and control flow** (no debugger, no captures this cycle). Spec-audit
> corrections applied 2026-06-24: added §3.3 nuance distinguishing the gameplay state (+1420) from the
> per-frame computed display-state (+1280 / dword idx 320).
> **ida_anchor:** f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963  **evidence:** [static-ida]
> **ida_reverified:** 2026-06-20 (SHA 263bd994, CYCLE 7); spec-audit 2026-06-24; CYCLE 14 re-anchor: 2026-06-27
> **CYCLE 14 re-anchor (f61f66a9, 2026-06-27):** 1 fact re-confirmed SAME (30-slot × 12B actor buff table at actor +0x208, 360-byte local buff-bar mirror at g_LocalBuffBarMirror; protected-range clear on death preserving slots [80..130] ∪ {13} ∪ [132..134]).
> **conflicts:** none.
>
> **RUNTIME-ONLY residue (do not treat as settled):**
> - **Per-tick effect magnitudes** (DoT / HoT numbers, any stat delta) are **RUNTIME-ONLY /
>   server-authoritative** — the client computes no buff arithmetic; it only spawns the periodic
>   *visual* and applies server-sent stat updates.
> - The **input-gating semantics of each action/motion-state value** (`actor +1420`) — i.e. exactly
>   which states forbid movement / casting — are **RUNTIME-ONLY**; the client expresses crowd-control
>   through motion-state and flag bytes, but the actual movement/cast prohibition is enforced
>   **server-side**.
> - The **buff-icon `.dds` atlas path** is **UNVERIFIED** (only the fame-buff window art surfaced this
>   cycle, see §5); the icon *positions* are confirmed (the icon-position disk table), the atlas file
>   itself is not pinned.

Confidence tags used below:

- `CONFIRMED` — read directly from the binary's structure / control flow on build 263bd994.
- `UNVERIFIED` — inferred from static structure; boundary / meaning not independently pinned.
- `RUNTIME-ONLY` — a magnitude or value-meaning settleable only by a live capture / debugger, or
  server-authoritative.

---

## 1. The per-actor buff-slot table

**Each actor carries a fixed array of 30 buff slots, 12 bytes per slot, based at actor +520 (0x208).**
`CONFIRMED` (the slot count is corroborated two independent ways — see below). The full actor-relative
placement of this array is mirrored in `structs/actor.md`; this section is the authority for the slot
*record model*.

### 1.1 Slot count and stride

The slot count of **30** and the **12-byte** stride are confirmed three ways:

- The per-tick update loop iterates slot indices 0..29 with a 12-byte stride.
- The cleanse / dispel path iterates the same 30 entries (a 30-iteration countdown, 12-byte stride).
- The local-player mirror table (§4) spans exactly 30 × 12 = 360 bytes.

### 1.2 Per-slot record (12 bytes, naturally aligned)

The record is **naturally aligned** (not byte-packed); the fields sit at slot offsets 0 / 4 / 8.

| Slot offset | Size | Type | Field | Conf | Meaning |
|---|---|---|---|---|---|
| +0x00 | 2 | u16 | `buff_id` | CONFIRMED | The effect-kind code; the dispatch key (§3) and the icon / visual lookup key (§2). Read as a 16-bit value. A value `> 0` marks the slot active (§3.4). |
| +0x02 | 2 | u16 | (pad / high half) | UNVERIFIED | Alignment pad; the id is read as 16-bit, so this is not part of the id. |
| +0x04 | 4 | i32 | `remaining_ticks` | CONFIRMED | Remaining duration in **4-second ticks** (§2). Decremented by 1 per tick; `<= 0` → release; `== 0` → clear; written `0` on removal. |
| +0x08 | 2 | u16 | `param` | CONFIRMED | Low word of the buff's 32-bit source value; used as the spawn **Value** of the per-tick periodic visual, and (for the clone buff, §3.2) as a packed summon code. |
| +0x0A | 1 | u8 | `param_hi` | CONFIRMED | High byte (third byte) of the 32-bit source value. |
| +0x0B | 1 | u8 | (pad) | UNVERIFIED | Alignment tail of the 12-byte record. |

> The 32-bit "source value" delivered by the buff push (§4) is stored across `param` (low 16 bits) and
> `param_hi`; the record is therefore effectively `{u16 buff_id; u16 pad; i32 remaining_ticks; u16 param;
> u8 param_hi; u8 pad}`. There is exactly **one** 30×12 array at actor +520 — the binary addresses the
> param field both as a direct slot offset and through an index alias, but it is the same storage.

---

## 2. Tick model — the 4000 ms heartbeat

**The buff table is advanced on a fixed 4000 ms tick.** `CONFIRMED`.

- A per-frame actor-manager update keeps a "last buff-tick" timestamp. When `(now − last_tick) > 4000
  ms`, it runs the buff-tick over the local player's slots and re-arms the timestamp to `now`. The
  same 4000 ms heartbeat also drives the skill-cooldown tick (`skills.md §4`) and one other periodic
  routine — buffs, cooldowns, and that routine share the one heartbeat.
- **Duration unit.** Because `remaining_ticks` is decremented by 1 on each 4-second tick, a server
  duration value `N` lasts approximately **N × 4 seconds**. `CONFIRMED` (mechanically).

### 2.1 Per-tick action (each slot, indices 0..29)

1. **Periodic visual.** If the slot's `buff_id` is **47** (the DoT / periodic-aura marker, §3) and
   `remaining_ticks > 0`, spawn the periodic visual effect, using the slot's `param` as the effect
   spawn Value, anchored on the actor. The spawn is gated by a local-relevance check (the actor's
   hidden/stealth flag and a "relevant for the local view" test).
2. **Decrement.** If `remaining_ticks > 1` **and** a per-actor frozen/paused guard is not set, then
   `remaining_ticks -= 1`.
3. **Release on expiry.** If `remaining_ticks == 0`, run the apply/clear-by-id dispatch (§3) for the
   slot (which undoes any associated state), then clear the slot.
4. **Clamp.** A negative `remaining_ticks` is clamped to 0.

### 2.2 No client-side DoT/HoT arithmetic

The client **never computes** per-tick HP/MP deltas or any stat magnitude from a buff. The id-47 path
spawns only the periodic **visual**; any hit-point / mana change arrives as an ordinary server stat
update. **Per-tick numeric magnitudes are RUNTIME-ONLY (server authority).** `CONFIRMED` (the absence
of any client-side buff arithmetic is confirmed).

---

## 3. buff_id → effect resolution

`buff_id` is an **effect-kind code**, not a free catalog row id. There is **no data-driven
stat-modifier buff table** in the client — the gameplay consequence of each id is a hard-coded
dispatch (§3.1–3.3). Resolution is three-pronged: a visual effect, a gameplay/state effect, and an
on-screen icon.

```
buff_id (slot +0)
  ├─ visual  : EffectManager visual-resolution map  indexed by (buff_id + 5)  → xeff effect id  [in-memory]
  ├─ gameplay: hard-coded dispatch keyed on the id code (§3.1)                                   [code]
  └─ icon    : data/script/buff_icon_position.xdb[buff_id]  → (sprite_x, sprite_y)               [disk]
```

### 3.0 Visual-effect resolution (the EffectManager map)

- On apply/clear, the dispatch reads the EffectManager singleton's in-memory visual-resolution map at
  index **`(buff_id + 5)`** to obtain an **xeff effect id**; that id is spawned as an anchored particle
  effect on apply and deactivated on clear. `CONFIRMED` (the `+5` index and the apply/deactivate pair);
  the exact element width of the map is `UNVERIFIED` — treat it as "an integer array indexed by
  `(buff_id + 5)` yielding an xeff id".
- The **per-tick** periodic visual (id-47 path, §2.1) is a separate spawn that uses the slot's `param`
  as the effect Value (not the EffectManager map). `CONFIRMED`.

### 3.1 Gameplay / status dispatch — the buff_id effect-kind enum

These are the discrete `buff_id` codes the apply/clear-by-id dispatch switches on. The **branch
behaviour is CONFIRMED** from the control flow; the **meaning labels are proposed** (`UNVERIFIED`
naming). All actor-relative state offsets cited here are detailed in `structs/actor.md`.

| id | Branch behaviour (CONFIRMED) | Proposed meaning |
|---|---|---|
| 43 | Sets action/motion-state (`+1420`) to **11**; refreshes weapon/joint fx + idle motion; clears the motion-suppress flag (`+1013`). | stance / transform-A (motion pose 11) |
| 44 | Toggles a stealth/disguise byte (`+1836`); on expiry restores the outfit from the disguise id (`+1764`) and summons by a class map. | polymorph / disguise |
| 45 | Sets a local-player status flag (UI cue) for the duration. | local-player status flag |
| 46 | Sets action/motion-state (`+1420`) to **12** while active. | stance / transform-B (motion pose 12) |
| 47 | Sets a per-actor flag (`+1837`) while active **and** drives the **periodic visual** in the tick loop (§2.1). | DoT / periodic-aura marker |
| 48 | **Dispel / cleanse:** deactivates several xeff slots; resets motion-state **11/13 → 1**; clears every slot whose `buff_id ∈ {43, 46, 47}`; clears the id-47/id-64 flags; refreshes the buff panel. | dispel / cleanse |
| 50 | Swaps the body motion by class (mount / ride or body-swap) and reapplies motion. | mount / ride or body-swap |
| 57 | On expiry sets a summon state (`+1828`) and spawns up to **3** mirror/clone actors keyed by class + the slot's `param` (`+1832` = clone count, derived from `param`). | summon / mirror-image clones |
| 64 | Sets a flag (`+1838`) only while active **and** the param `< 100` (a param-gated threshold flag). | threshold status flag |
| 131 | Sets action/motion-state (`+1420`) to **13** (transform-revert); refreshes motion; clears the motion-suppress flag (`+1013`). | transform-revert |
| other id (`> 0`, not above) | Falls to the default tail: deactivate the matching xeff; if not stealthed, spawn the anchored apply visual. | generic visual-only buff |

### 3.2 The clone / summon path (id 57)

On id-57 expiry, the dispatch sets the summon state and spawns up to three clone actors. The clone
count and the per-clone identity are derived from the slot's `param` (a packed code split into a
"per-10000" quotient and remainder) combined with the actor's class. This is the visual mirror-image /
clone effect. `CONFIRMED` (the spawn of up-to-3 clones and the param-derived keying); the precise
summon code semantics are `UNVERIFIED`.

### 3.3 Crowd-control is motion-state, not a CC bitfield

The client holds **no single crowd-control bitfield** that gates input. Crowd-control is expressed
through the actor's **action/motion-state** (`+1420`) and the flag bytes set by the dispatch above; the
actual movement / cast prohibition is enforced **server-side** (the client is told via the buff push
plus the motion-state). One protected value is pinned: **`+1420` value 8 is a death / special state the
buff dispatch never overwrites.** `CONFIRMED` (behaviour). **The precise input-gating semantics of each
`+1420` value are RUNTIME-ONLY.**

> **Two distinct actor state words — do not conflate.** The **gameplay / CC state** is at actor **+1420**
> (value 8 = death, set by the death-reset routine; the alive/can-act word is at +1424). Separately, the
> per-frame display routine computes a **display-state word at actor +1280 (dword idx 320)** each frame
> from the flag bytes: +1836 → display-state 8 (stealth), +1837 → 16 (id-47 DoT flag), +1838 → 32 (id-64
> threshold flag), +1844 → 64, anim-count > 0 → 128; the motion-lockout field at actor +1488 (dword idx
> 372) additionally contributes state 2 within a 500 ms post-action display window. This computed
> display-state drives the visual state icon only; it is **not** the `+1420` gameplay state and must not
> be used as the CC / death gate. `CONFIRMED` (both words exist; their roles are distinct).

### 3.4 UI visibility filter

A slot is shown on the buff bar only when its `buff_id > 0` and its `remaining_ticks != 0`. Two id
ranges are **hidden** (internal / system statuses, never drawn):

- ids in **80..130** (inclusive), and
- id **59**.

All other active ids render a **21×21** icon button on the buff bar; the slot's id is stored on the
button as its tooltip / click key. `CONFIRMED`.

### 3.5 Icon resolution — the on-disk position table

- **Disk table:** `data/script/buff_icon_position.xdb`, loaded at boot. **12-byte records**, keyed by
  `buff_id`:

  | Record offset | Size | Type | Field |
  |---|---|---|---|
  | +0 | 4 | u32 | `buff_id` (key) |
  | +4 | 4 | u32 | `sprite_x` |
  | +8 | 4 | u32 | `sprite_y` |

  `CONFIRMED`. `buff_id` → record → `(sprite_x, sprite_y)` on the buff icon atlas.
- **Atlas note.** The buff/state icon atlas `.dds` path is **UNVERIFIED** this cycle (the only related
  art string surfaced was the fame-buff window art); the icon *positions* above are confirmed, the
  atlas file is not pinned. `specs/skills.md §6` references a state-icon atlas for the same panel — that
  reference is the closest lead.
- **No `.scr` buff catalog.** There is **no** `itemeffect.scr` / buff-catalog `.scr` in this chain;
  the only on-disk table is the icon-position `.xdb`. `CONFIRMED` (absence).

---

## 4. The buff push and the local-player mirror tables

### 4.1 The buff-slot push

The canonical live buff-slot stream is **S2C `SmsgBuffSlotUpdate`, opcode 5/31**. `CONFIRMED`. (This
**supersedes** the earlier "4/102" framing — 4/102 is a 476-byte skill-window snapshot rebuild, a
different thing; see `skills.md §6A.4`.)

- **Body = 56 bytes.** It decodes to an **owner composite key** (an id dword plus a sub field), a
  **slot index** (i32), a **`buff_id`** (carried in a dword, used as u16), a **`remaining`** value
  (i32), and a **source** dword (→ `param` / `param_hi`).
- A push with `remaining == 0` **clears** the addressed slot.
- The exact wire field positions / widths are owned by `opcodes.md` / `packets/` (recorded here by name
  and body size only; this lane does not edit those files).

### 4.2 Local-player mirror tables

When the push targets the local player, the handler also mirrors the slot into flat global tables so the
HUD can read the buff bar without walking the actor object. `CONFIRMED`:

| Mirror | Slot-index band | Stride | Purpose |
|---|---|---|---|
| Primary buff-bar mirror | low band (0..30) | 12 bytes | The buff-bar source: 30 × 12-byte `{buff_id, remaining, source}` triples. |
| High-id band | id / slot ≥ 1,000,000 | 12 bytes | Special / system statuses → a separate panel. |

The buff-bar rebuild walks the primary mirror (30 entries, 12-byte stride), and for each entry passing
the §3.4 visibility filter it looks up the icon position (§3.5) and builds the 21×21 icon button.

---

## 5. Implementation guidance (clean-room engineers)

- **Buff-slot model** → `Client.Domain`: a per-actor `BuffSlot[30]` array of 12-byte records
  (`{u16 buff_id; u16 pad; i32 remaining_ticks; u16 param; u8 param_hi; u8 pad}`, §1.2). Re-applying a
  buff overwrites its slot (refresh-by-slot; `remaining_ticks` is the only per-slot timer — there is no
  stack counter).
- **Tick** → a single 4000 ms heartbeat that decrements every active slot by 1, runs the §3.1 dispatch
  at expiry, spawns the id-47 periodic visual, and clamps negatives to 0 (§2). **Do not implement any
  per-tick HP/MP/stat arithmetic** — magnitudes are server-authoritative (RUNTIME-ONLY).
- **Effect dispatch** → a switch on `buff_id` implementing the §3.1 branches against the actor
  state fields in `structs/actor.md`; treat the meaning labels as proposed until confirmed.
- **Crowd-control** → drive the actor's motion-state / flag bytes; do **not** invent a CC bitfield, and
  never let buff code overwrite motion-state value 8 (death/special). Movement/cast prohibition is the
  server's call.
- **Visual / icon** → resolve the xeff id from the EffectManager map at `(buff_id + 5)` (§3.0); resolve
  the icon position from `data/script/buff_icon_position.xdb` (§3.5, 12-byte `{buff_id, x, y}`); the
  buff atlas `.dds` path is a TODO until pinned.
- **Wire** → `Network.Protocol` decodes `SmsgBuffSlotUpdate` (5/31, 56-byte body) per its
  `packets/*.yaml`; cite `// spec: Docs/RE/packets/<name>.yaml` on each offset, and gate any VALUE
  assumption on a capture (RUNTIME-ONLY).

---

## 6. Cross-reference summary

| Topic | Authority |
|---|---|
| Buff-slot array placement on the actor; buff-related state fields (`+1013`, `+1420`, `+1764`, `+1828`, `+1832`, `+1836`, `+1837`, `+1838`); death fields | `structs/actor.md` |
| Cast pipeline that requests skills; skill-side view of the slots (defers here) | `specs/skills.md §6` |
| HUD buff bar | `specs/world_systems.md` |
| `SmsgBuffSlotUpdate` (5/31) wire field layout | `opcodes.md` / `packets/` |
