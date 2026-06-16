# Spec: Equipment Visuals — How Worn Gear Changes the Rendered Avatar

> Clean-room spec. Neutral description only — NO decompiler pseudo-code, NO binary addresses,
> NO decompiler-generated identifiers. Promoted from dirty-room runtime analysis under EU
> Software Directive 2009/24/EC Art. 6, solely to achieve interoperability. Consumed by the
> Godot presentation engineer (layer 05) and `Client.Application` (equip-result handlers).
> Every visual-part offset an engineer cites must reference this file, `specs/skinning.md`,
> `formats/mesh.md`, or `structs/item.md`.

<!--
verification: confirmed (per-part rebuild model, GID formulas, in-memory offsets, EquipTable layout
  are control-flow-confirmed); static-hypothesis (weapon-glow tier toggler — not re-located this pass;
  the `+1052` slot-value element semantics); capture/debugger-pending (the literal equip-result opcode
  pair, the on-wire framing of the slot-type / skip-visual bytes, the `weapon_effect_grade ↔ static
  enchant level` mapping, the subtype 53-vs-55 meaning, the named hand bone).
ida_reverified: 2026-06-16
ida_anchor: 263bd994
evidence: [static-ida]
conflicts: none open. RECON#F4 RESOLVED the prior off-hand-flag inversion — the off-hand node carries
  node flag = 1; node flag = 2 selects the MAIN-hand catalog columns (was stated backwards as
  "off-hand = flag 2" in the previous build's doc). All in-memory offsets re-pinned to 263bd994.
-->

---

## Status (read first)

> **Headline finding — equipping does NOT swap the body.**
> When an item is equipped, unequipped, or swapped, the avatar's visual change is a **per-part
> mesh recomposition** under **one shared skeleton**, NOT a full `skin.txt`-row body swap. The
> rendered character (the "visual" object — the renderable twin of an actor, and the base of the
> local-player object) holds a small **visual part table** with one *part-actor id* per equipment
> part. On any equip change the visual is torn down — every attached part node is destroyed — and
> then a **fixed set of part slots** is rebound: each part is looked up, a mesh resource GID is
> derived from per-character appearance fields, the skin is fetched through the engine skin
> manager, and the new part node is parented under the one shared skeleton root. Motion is then
> replayed. This is the model a faithful Godot avatar must reproduce: independent head / face /
> hair / body / weapon parts, not a single monolithic mesh.

| Area | Confidence |
|---|---|
| Equip change = full **per-part rebuild** (teardown of all part nodes, then rebind), not a skin swap | CODE-CONFIRMED |
| Visual part table at `Visual+204 (0xCC)`, 16-byte records, part-actor id at record+0 | CODE-CONFIRMED |
| Local-player rebuild rebinds part slots `{3, 4, 6, 2, 11, 14}` (+ head / face / hand-weapon builders) | CODE-CONFIRMED |
| Other-actor rebuild rebinds `{3, 4, 6, 2, 11}` — **no slot 14** (+ head / face / body builders) | CODE-CONFIRMED |
| Part slot **14 = WEAPON** | CODE-CONFIRMED |
| Weapon mesh GID uses the special base-1000 digit formula keyed on appearance fields | CODE-CONFIRMED (formula); PLAUSIBLE (digit → column meaning) |
| Non-weapon parts derive GID via `10000·(id/10000) + id%100` through the animation-catalog GID→skin map | CODE-CONFIRMED (formula); PLAUSIBLE (digit → column meaning) |
| All parts share ONE skeleton / matrix root (`Visual+1300`); each part contributes to a combined vertex/index pool | CODE-CONFIRMED |
| Weapon model attaches to a **hand bone** via the bone-attach list (`Visual+1376`); transform from `Visual+100` | CODE-CONFIRMED (structure); PLAUSIBLE (which named bone) |
| Dual / two-hand case (weapon bind class == 3) builds main-hand + off-hand nodes (**off-hand = node flag == 1; node flag == 2 selects the MAIN-hand columns**) | CODE-CONFIRMED |
| Weapon glow / enchant aura from item-actor `+231` (`weapon_effect_grade`); 4-emitter glow object; 9 tiers | STATIC-HYPOTHESIS (toggler not re-located on 263bd994 — see §8) |
| Grade values `101..109` normalize to tiers `1..9`; `0` = no glow | STATIC-HYPOTHESIS (carried; not re-proven this pass) |
| Weapon glow tooltip uses loc-strings `57019..57024` | STATIC-HYPOTHESIS (carried; not re-proven this pass) |
| Which **named bone** the weapon hangs from | PLAUSIBLE — runtime value from the loaded `.bnd`, not a literal |

> **RE-VERIFIED ON BUILD `263bd994` (2026-06-16).** The per-part rebuild model, the two rebind lists,
> the GID formulas (§3), the shared-skeleton attach tail (§4), the weapon attach / dual-hand structure
> (§5), the EquipTable layout (§6.3), and every in-memory offset in §7 were re-confronted against the
> live disassembly this pass and hold (offsets re-pinned to this build). The one substantive correction
> is the **off-hand node flag** (now flag `1`; flag `2` selects the **main-hand** columns — see §5.1).
> Several enrichments the previous doc omitted are added (the dual-purpose `+10` byte, the exact 64-bit
> catalog-key packing, the full per-class weapon-index ladder, the separate `+1052` slot-value array,
> and the per-grade joint-effect / sheathe-sound special-case) — §6.2 and §6.4.
>
> **STILL STATIC-ONLY.** There was **no live capture** in the source analysis. The literal **equip-result
> opcode pair** and the **on-wire framing** of the slot-type / skip-visual bytes (§6.2) are
> capture/debugger-pending; the **weapon-glow tier toggler** (§6.1) was *not* re-located this pass and
> is carried as a static-hypothesis from the prior build, not re-proven.

Open items are consolidated in §8.

---

## 1. The recomposition model

The renderable character object — referred to throughout as the **visual** — is the rendered twin of
an actor and also forms the base of the local-player object. It carries, among its appearance fields
(§7), a **visual part table** describing the per-part composition of the avatar. Each entry names a
*part-actor* (a lightweight actor that stands in for one equipment part: a body piece, a helmet, a
weapon, etc.).

An equip change proceeds in three phases:

1. **Teardown.** The visual's child-node list (the attached part nodes) is walked; every attached
   part node has its refcount decremented, is detached, and is released. The hierarchy is then
   re-initialised empty. This destroys **all** part nodes — confirming the avatar is a composite of
   independent parts, not a single mesh that is texture-swapped.
2. **Rebind.** A **fixed set of part slots** is rebuilt in order. For each slot, the per-part builder
   reads the slot's part-actor id from the visual part table, looks the part-actor up, derives a mesh
   resource GID (§3), fetches the skin through the engine skin manager, builds a render node, and
   parents it under the one shared skeleton root (§4). Head, face, hair, and the hand/weapon attach
   are built by dedicated builders alongside the part-slot loop.
3. **Replay motion.** The current motion variant is re-applied and playback resumes.

Because the whole part set is destroyed and rebuilt from the *current* part table, the visible effect
of equipping a new item is that one (or a few) parts are replaced while the rest are reconstructed
identically — never a wholesale body-row swap.

### 1.1 Two rebuild paths — local player vs. other actors

There are two rebuild entry points, reached from the two equip-result network handlers (§6):

| Path | Rebound part slots | Extra builders |
|---|---|---|
| **Local player** | `3, 4, 6, 2, 11, 14` (slot 14 = weapon) | head, face, and the hand/weapon-attach builder |
| **Other actor** | `3, 4, 6, 2, 11` (**no slot 14**) | head, face, body builders; then recomputes the primary/secondary skin ids |

Both paths run the teardown of §1 first. The other-actor path uses a *different* per-part binder than
the local-player path and omits part slot 14 from its fixed list. Above a per-character skin-level
threshold (a value read from the animation catalog), the local-player path binds **only** part slot 3
and skips the rest — i.e. high-tier characters use a reduced composition.

> **Part-slot ids vs. message slot-type bytes are two different enumerations.** The fixed rebuild list
> uses *visual part-slot ids* `{2, 3, 4, 6, 11, 14}` (14 = weapon). The equip-result messages carry a
> separate *slot-type* byte that is tested against `15` to gate the weapon-drawn visual refresh (§6.2;
> at `+12` in the 16-byte message, `+11` in the 20-byte one). A translation between the message slot
> type and the visual part-slot id exists in the equip-result writers but was not fully traced, and the
> on-wire framing is capture-pending; do not assume the two numbering schemes coincide.

---

## 2. The visual part table

**Confidence: CODE-CONFIRMED.**

The visual part table lives at byte offset **`+204` (`0xCC`)** within the visual object. It is an
array of **16-byte records**, indexed by part-slot id. The per-part builder reads the part-actor id
from the **first field of each record (record + 0)**:

```
part_actor_id = read_i32( visual + 204 + 16 * part_slot )
```

This table mirrors the worn-equipment block. On the equip-change path (§6.3) the client copies the
**20-slot × 16-byte** equipment block out of the local-player actor mirror (actor base `+200`) into the
global **EquipTable**, so the part table and the EquipTable stay in sync. See
`structs/spawn_descriptor.md` for the descriptor-side equip block and `structs/actor.md` for the visual
object's place in the actor hierarchy. Note the EquipTable is distinct from the flat equipped-slot
**value** array at actor `+1052` (§6.3) — three separate structures.

If a slot's part-actor exists, the builder uses the part-actor's own mesh pointers directly. If the
part-actor is absent, the builder derives a numeric mesh GID from the per-character appearance fields
(§3) and resolves it through the animation-catalog GID→skin map.

---

## 3. Part mesh GID derivation

**Confidence: CODE-CONFIRMED for the formulas; PLAUSIBLE for the exact meaning of each appearance digit.**

When a part has no live part-actor, the builder computes a mesh resource GID from the visual's
appearance fields (§7). There are two formulas: a special one for the weapon slot, and a general one
for every other part.

### 3.1 Weapon (part slot 14)

```
weapon_gid = 1000 · ( B + 10 · ( C + 10 · ( D + 10 · ( part_id / 1000000 ) ) ) )
```

where the digits come from the visual's appearance fields:

| Digit | Source field | Visual offset |
|---|---|---|
| `B` | weapon appearance / grade digit (u8) | `+150 (0x96)` |
| `C` | hair / variant id (i16) | `+168 (0xA8)` |
| `D` | class / race id (u8) | `+160 (0xA0)` |

`part_id` is the weapon part-actor id; its high digits (`part_id / 1000000`) supply the most
significant term. In effect the weapon mesh is keyed by the character's class/race/variant digits
plus the weapon's high-order part id digits, scaled into the base-1000 GID space. The mapping of each
digit to a specific `class.txt` / `skin.txt` column is **PLAUSIBLE** — it is inferred from the GID
arithmetic, not read from a labelled table.

### 3.2 Non-weapon parts

```
part_gid = 10000 · ( part_id / 10000 ) + part_id % 100
```

The result is combined with a class/variant base term derived from the same appearance fields into a
64-bit key, which is looked up in the animation-catalog **GID → skin map** (a sorted/tree map inside the
animation catalog singleton). The exact packing (CODE-CONFIRMED on build `263bd994`) is:

```
base_term = 5 · ( hair_variant + 4 · class_race ) − 24
key       = part_gid + 1,000,000,000 · ( part_slot + 100 · base_term )
```

The catalog map resolves this 64-bit key to a concrete skin resource. The digit-to-column meanings of
the appearance fields are again PLAUSIBLE; the arithmetic itself is exact.

> This GID→skin indirection is the same animation-catalog map that the `id_b ↔ .bnd` and skin-class
> chains use elsewhere; see `formats/mesh.md` (id_b ↔ skeleton bijection) and `specs/skinning.md` (bone
> addressing) for how a resolved skin then binds to its skeleton.

---

## 4. Part attach and the one shared skeleton

**Confidence: CODE-CONFIRMED.**

Every part builder ends in a common attach tail:

1. The engine skin manager (`Visual+1296`) is queried by the derived GID. If found, a render node is
   created (tagged with the literal string `"skin"`), the resolved skin is bound, a draw flag is set,
   and a composed per-node scale is applied.
2. The node is parented into the visual's hierarchy. The parent step:
   - writes the visual's **matrix / skeleton root** (`Visual+1300`) as the node's parent matrix;
   - records the visual as the node's owner;
   - **accumulates a running combined-mesh cursor** — the visual's combined vertex/bone counter
     (`Visual+1272`) advances by the skin's vertex/bone count, and the combined face/index counter
     (`Visual+1276`) advances by three times the skin's face count;
   - inserts the node into the visual's draw list.

The consequence is the central fact for the renderer: **all parts share ONE skeleton / matrix root at
`Visual+1300`**, and each part contributes to a single combined vertex/index pool. There is no
per-part skeleton; the head, face, hair, body, and weapon meshes are all skinned against the same bone
hierarchy. See `specs/skinning.md` for the bone-addressing and deform conventions that this shared
skeleton obeys.

---

## 5. Weapon-in-hand: bone attach, dual / two-hand

**Confidence: CODE-CONFIRMED for the structure and the node-flag discriminator; PLAUSIBLE for the named bone.**

The hand/weapon-attach builder (called by the local-player rebuild) is responsible for hanging the
weapon model on the skeleton:

1. It reads a hand/weapon part-actor id at `Visual+316` and looks the part-actor up.
2. It switches on the weapon's `item_subtype` (item-actor `+136`; see `structs/item.md`) to choose a
   motion/animation offset for the weapon class — distinct weapon classes shift the visual's primary
   skin/anim index by a small **per-class amount**. The **full ladder** (CODE-CONFIRMED) is:

   | `item_subtype` | index shift |
   |---|---|
   | `1`, `4`, `10` | `+2` |
   | `2`, `5`, `8`, `11` | `+3` |
   | `3`, `6`, `9`, `12` | `+4` |
   | `7` | `+2`, plus an attack-mode refresh and an **early build** branch (special-cased) |
   | `45` | `+1` |
   | `> 45` | no shift |

3. The weapon model is attached to a **hand bone** by inserting the weapon node into the visual's
   **bone-attach list** at `Visual+1376` and binding it to the resolved bone. The node's transform is
   taken from `Visual+100`. The **named bone index is a runtime value from the loaded `.bnd`
   skeleton**, not a literal — recovering which named bone (e.g. a right-hand bone) requires a sample
   `.bnd` + a trace, so it is graded PLAUSIBLE.

### 5.1 Single vs. dual / two-hand weapons

The bind class of the resolved weapon skin (a small enum on the skin object, read from the skin
header) selects the attach shape:

- **Single-piece weapon** → ONE attach node, anchored to the hand bone. Its node flag is set to the
  skin bind-class value.
- **Dual / two-piece weapon (skin bind class == 3)** → TWO attach nodes are built: a **main-hand** and
  an **off-hand** node, each anchored to the bone-attach list.
  - The **main-hand** node (built first, in the two-piece case) carries **node flag value `2`**.
  - The **off-hand** node (built second) carries **node flag value `1`**.

> **Corrected on build `263bd994` (RECON#F4).** The previous doc stated "the off-hand node is marked by
> a node flag value of 2"; this was **backwards**. The live disassembly assigns flag `2` to the **main**
> node and flag `1` to the **off-hand** node, and the animation-column selection loop (below) branches
> on `node flag == 2` to pick the **main-hand** columns. So: **off-hand = flag `1`; flag `2` selects the
> main-hand columns.** This is now CODE-CONFIRMED (it was PLAUSIBLE before).

Per attached weapon node, the per-frame animation index is then driven from the animation-catalog
motion table. The node-flag value selects which pair of catalog columns the node reads, modulo a
40-entry stride:

- **node flag == 2 (main-hand)** → attack-mode column `902`, idle-mode column `904`.
- **otherwise (off-hand / flag 1)** → attack-mode column `903`, idle-mode column `905`.

> This `bind class == 3` two-piece flag connects to the `.skn` / `.bnd` bind recovery; see
> `formats/mesh.md` and `specs/skinning.md`. The weapon's separate trail/glow ribbon (sword-light) is
> a different sub-system — see `specs/effects.md §12` — and should not be confused with the
> enchant-aura glow described in §6.

---

## 6. Weapon glow (enchant aura) and the network entry points

**Confidence: STATIC-HYPOTHESIS for the glow toggler (§6.1, not re-located on 263bd994);
CODE-CONFIRMED for the network entry points and the in-memory EquipTable (§6.2–§6.4);
CAPTURE/DEBUGGER-PENDING for the wire opcodes and framing.**

### 6.1 The enchant-aura glow

Equipping, unequipping, or swapping a **weapon-slot** item drives a weapon glow ("enchant aura"). On a
weapon-slot mutation the handler is understood to:

1. look up the currently equipped weapon actor (via the weapon-slot actor-id singleton);
2. read the weapon's **`weapon_effect_grade`** byte at item-actor **`+231` (`0xE7`)** — see
   `structs/item.md §6g`;
3. if the grade is non-zero, enable the glow at the corresponding tier; if zero, clear it.

**Grade normalisation:** stored grade values **`101..109` normalise to tiers `1..9`** (subtract 100);
a grade of **`0` means no glow**. There are exactly **nine glow tiers**.

**The glow object** is a dedicated effect object with **four emitters**: one master emitter plus three
satellite emitters. Enabling the glow toggles all four on together at the selected tier; clearing it
toggles all four off. The weapon-glow tooltip / label builder reads the same `+231` grade, normalises
it the same way, and formats a per-tier enchant-percentage label from **localisation strings
`57019..57024`** (the baked per-tier "+N%" rows).

> **Re-RE status (build `263bd994`).** The specific tier toggler (`101..109 → 1..9`, the four-emitter
> glow object, the loc-string `57019..57024` label) was **not independently re-located this pass** — so
> the whole of §6.1 is carried forward as a **STATIC-HYPOTHESIS** from the prior build, not re-confirmed
> on 263bd994. What *was* observed on this build: the UI panels (inventory / target-select) read the
> *actor's* `+231` as a simple "has-glow" boolean and route it through a window-effect call; the
> item-actor `+231` *grade* read appears to be pointer-relative rather than a literal-immediate access,
> which is why a constant-search for the toggler missed it. The clean re-RE entry point for a follow-up
> is the four-emitter glow-object constructor / the tooltip loc-string builder, **not** the `+231`
> immediate. See §8.
>
> **Related (CODE-CONFIRMED on 263bd994):** the same `+150` "weapon_gid_digit" appearance field
> (§7) also indexes a **per-grade joint-effect (XEffect) registry** in the weapon/joint-effect refresh
> path, and that refresh special-cases the local player for `item_subtype == 55` with a specific
> weapon set-flag value (a sheathe / draw-swap sound cue). So `+150` is consumed by both the GID
> formula (§3.1) and a per-grade joint-effect lookup.

> **Grade vs. static enchant level.** The runtime `weapon_effect_grade` (range `0..9` after
> normalisation) is a *compressed* tier, not the raw enchant count. Its relationship to the static
> enchant-level column in the binary item record (range `0..28`) is **CAPTURE-PENDING**; see
> `structs/item.md §6g` and §8 here.

### 6.2 Equip-result network messages (in-memory block CODE-CONFIRMED; wire opcodes CAPTURE-PENDING)

Two server→client equip-result messages drive the visual rebuild. The handler bodies, block sizes, and
the in-memory field offsets below are control-flow-confirmed on build `263bd994`; the **literal wire
opcode pair** and the **on-wire framing** of the bytes are *not* traced from the dispatch table this
pass and must be confirmed against a capture / the debugger.

| Message (role) | Block size | Success byte | Slot-type byte | On success it… |
|---|---|---|---|---|
| **Equip-item result** | **16 bytes** | `+8` | **`+12` (`0x0C`)** | applies the visual refresh, then runs the **local-player** rebuild (teardown + rebind + motion replay) — or routes to a panel |
| **Equip-change result** | **20 bytes** | `+8` | **`+11` (`0x0B`)** | dispatches the visual-slot op (see §6.4); unless the `+10` byte is set, applies the visual refresh; mirrors the **20-slot × 16-byte** equip block from the local-player actor mirror into the EquipTable (§6.3); then iterates the 20 part-actors running the **other-actor** rebuild |

> **Re-pinned slot-byte offset (build `263bd994`).** The slot-type test (`== 15`) lives at **different
> offsets in the two messages**: **`+0x0C` (12)** in the 16-byte equip-item result, **`+0x0B` (11)** in
> the 20-byte equip-change result. Any single fixed "slot byte at `0x0C`" claim is correct only for the
> 16-byte message. (This is an in-memory-block fact; the wire framing that produces these bytes is
> capture-pending.)

A slot type of **15** is the **visual-refresh / weapon-cosmetic** slot: it is passed as a boolean into
the visual-refresh routine, which queries whether the weapon is "drawn", sets or clears the
**weapon-drawn flag bit** (`& 1`) on the visual at **both** `+888` and `+1048` (§7), and then kicks the
engine render-state refresh (§6.4).

The **weapon-drawn query** reads the equipped weapon part-actor (via `Visual+604`, §7) and returns
*drawn* only when the weapon's `item_subtype` is `53` **or** `55` **and** the weapon set-flag
(item-actor `+512`) is non-zero. What distinguishes subtype `53` from `55` is unconfirmed (no label, no
capture); see §8.

### 6.3 The in-memory EquipTable mirror (CODE-CONFIRMED)

There are **three distinct equip-related in-memory structures**, which must not be conflated:

| Structure | Where | Shape | Role |
|---|---|---|---|
| **Visual part table** | `Visual+204` (`0xCC`), §2 | rec[] of 16 bytes, part-actor id at `+0` | drives the per-part mesh rebuild |
| **EquipTable** (global mirror) | a dedicated global | **20 × 16-byte** records | the canonical worn-equipment block; the equip-change result rewrites it wholesale |
| **Equipped-slot VALUE array** | actor `+1052`, element = `+1052 + 4·slot` | i32 per slot | a separate flat per-slot value array (read e.g. by the inventory move-eligibility check); element semantics (worn item id vs handle vs count) STATIC-HYPOTHESIS |

On the equip-change path the handler copies a **20-iteration, 16-byte-stride** block out of the
local-player actor mirror (the actor object base `+200`) into the global EquipTable, then walks the 20
part-actors driving the other-actor rebuild. The part table (`Visual+204`) and this EquipTable stay in
sync because they mirror the same 20-slot × 16-byte equipment block; see `structs/spawn_descriptor.md`
for the descriptor-side block.

### 6.4 The visual-slot dispatch and the render-state mirror (CODE-CONFIRMED)

The 20-byte equip-change result routes its slot mutation through a **visual-slot dispatcher** keyed on
the message's **`+10` byte**, which is **dual-purpose**:

- It selects the **visual-slot operation**: `0` = set, `1` = set-variant, `2` = release.
- It simultaneously acts as the **skip-visual-refresh gate**: the weapon-drawn visual refresh runs only
  when `+10` is zero (`if (!+10)`). So a non-zero `+10` both picks a slot op **and** suppresses the
  bit-0 weapon-drawn refresh.

When the weapon-drawn visual refresh does run, it drives a **render-state mirror**: the render-state
refresh routine re-reads the weapon-drawn query and updates component visibility flags and layout
values on two HUD sub-objects (the on-screen weapon-state toggle). This is the weapon-drawn → on-screen
render path; the presentation layer should treat the weapon-drawn flag as gating a HUD component, not
only the avatar mesh.

---

## 7. Visual object — appearance and part fields

**Confidence: as graded per row.** Offsets are within the live in-memory visual object. These are NOT
on-disk formats. They are reproduced here only to let the presentation layer reason about the
composition; the authoritative actor/visual struct map is `structs/actor.md`.

| Offset (dec / hex) | Size | Type | Field | Grade |
|---|---|---|---|---|
| `+96 / 0x60` | 1 | u8 | `is_character_skin` flag — gates the full per-part rebuild (== 1) | CODE-CONFIRMED |
| `+100 / 0x64` | — | transform | weapon-node transform source (used by the hand/weapon attach) | CODE-CONFIRMED |
| `+108 / 0x6C` | 4 | i32 | `base_skin_id` (primary anim/skin index) | CODE-CONFIRMED |
| `+112 / 0x70` | 4 | i32 | `secondary_skin_id` | CODE-CONFIRMED |
| `+150 / 0x96` | 1 | u8 | `weapon_gid_digit` (digit `B` in the weapon GID, §3.1) | PLAUSIBLE |
| `+160 / 0xA0` | 1 | u8 | `class_race` (digit `D` in the weapon GID; folded into the part base term) | PLAUSIBLE |
| `+162 / 0xA2` | 2 | i16 | `face_id` (read by the face builders) | PLAUSIBLE |
| `+168 / 0xA8` | 2 | i16 | `hair_variant` (digit `C` in the weapon GID) | PLAUSIBLE |
| `+204 / 0xCC` | 16/slot | rec[] | **visual part table** — part-actor id at record + 0 (§2) | CODE-CONFIRMED |
| `+316 / 0x13C` | 4 | i32 | `hand_weapon_part_actor_id` (read by the weapon attach, §5) | CODE-CONFIRMED |
| `+604 / 0x25C` | 4 | i32 | `weapon_part_actor_id` (read by the weapon-drawn query, §6.2) | CODE-CONFIRMED |
| `+888 / 0x378` | 1 (bit 0) | u8 | `weapon_drawn_flag` | CODE-CONFIRMED |
| `+1048 / 0x418` | 1 (bit 0) | u8 | `weapon_drawn_flag` (mirror) | CODE-CONFIRMED |
| `+1272 / 0x4F8` | 4 | i32 | combined vertex/bone cursor (accumulated per part, §4) | CODE-CONFIRMED |
| `+1276 / 0x4FC` | 4 | i32 | combined face/index cursor (×3 per part, §4) | CODE-CONFIRMED |
| `+1296 / 0x510` | 4 | ptr | engine skin manager (GID → skin) | CODE-CONFIRMED |
| `+1300 / 0x514` | — | matrix root | **shared part parent matrix / skeleton root** (§4) | CODE-CONFIRMED |
| `+1364 / 0x554`, `+1368 / 0x558` | 4 | ptr | child-node list begin / end (the part nodes torn down on rebuild) | CODE-CONFIRMED |
| `+1376 / 0x560` | — | list | **bone-attach list** (weapon / off-hand anchors, §5) | CODE-CONFIRMED |

### 7.1 Item-actor fields used by the visual lane

These corroborate `structs/item.md`; offsets are within the item-actor object.

| Offset (dec / hex) | Size | Type | Field | Grade |
|---|---|---|---|---|
| `+128 / 0x80` | 4 | ptr | part mesh pointer A (used directly when the part-actor is present) | CODE-CONFIRMED |
| `+132 / 0x84` | 4 | ptr | part mesh pointer B | CODE-CONFIRMED |
| `+136 / 0x88` | 2 | u16 | `item_subtype` (weapon class `1..12`, `45`; `53`/`55` = weapon-drawn subtypes) | CODE-CONFIRMED |
| `+231 / 0xE7` | 1 | u8 | `weapon_effect_grade` (glow tier; `101..109` → `1..9`; `0` = none) | CODE-CONFIRMED |
| `+512 / 0x200` | 4 | u32 | weapon set-flag (the weapon-drawn query also requires this != 0) | CODE-CONFIRMED |

---

## 8. Open items

| Item | Status | Impact |
|---|---|---|
| Which **named bone** the weapon hangs from | PLAUSIBLE — the bone index is a runtime value from the loaded `.bnd`; needs a sample skeleton + a trace to name the hand/weapon bone | Get it wrong and the weapon floats off the hand; resolve against a real `.bnd` before placing weapons |
| ~~Off-hand discriminator~~ | **RESOLVED on `263bd994` (RECON#F4)** — off-hand node flag = `1`; flag `2` selects the **main-hand** columns (§5.1). Was inverted in the prior doc. | Affects dual-wield; the correct flags are now in §5.1 |
| Appearance digit → column mapping (`+150 / +160 / +162 / +168`) | PLAUSIBLE — inferred from the GID arithmetic, not from a labelled table | The GID formulas (and the exact 64-bit key packing, §3.2) are exact; only the *naming* of each digit's role is uncertain |
| **Weapon-glow tier toggler** (`101..109 → 1..9`, four emitters, loc `57019..57024`) | STATIC-HYPOTHESIS — carried from the prior build, **not re-located on `263bd994`** (the `+231` grade read is pointer-relative, not a literal immediate) | Re-RE entry point = the four-emitter glow-object constructor / the tooltip loc-string builder, not the `+231` immediate |
| `weapon_effect_grade` (0..9) ↔ static enchant level (0..28) | CAPTURE-PENDING — see `structs/item.md §6g` | The glow has exactly 9 tiers, so `+231` is a compressed tier, not the raw enchant count |
| Weapon-drawn subtype semantics (`53` vs `55`) | CAPTURE-PENDING — no label, no capture (both confirmed as the two weapon-drawn subtypes; `55` + a specific set-flag triggers a local-player sheathe-sound special-case) | Distinguishes drawn/sheathable weapon kinds; not needed to compose the mesh |
| `+1052` slot-value array element meaning (worn item id vs handle vs count) | STATIC-HYPOTHESIS — confirmed as a separate flat per-slot i32 array; element semantics not pinned | Distinct from the part table (`+204`) and the EquipTable (§6.3) |
| Equip-result wire opcodes + framing (slot-type / `+10` skip byte) | CAPTURE/DEBUGGER-PENDING — the handler bodies and the in-memory block offsets (`+8`/`+11`/`+12`) are confirmed, but the literal opcode pair and on-wire framing are not traced | Confirm the opcode pair and the wire framing against a real capture before any wire claim is authoritative |

---

## 9. Godot consumer guidance

For the layer-05 avatar, the load-bearing takeaways are:

1. **Compose, don't swap.** Build the avatar as a set of independent parts (head, face, hair, body
   slots, weapon) parented under **one** `Skeleton3D` — never reload a whole-body mesh on equip. On an
   equip change, rebuild the affected part node(s) and leave the shared skeleton in place.
2. **One shared skeleton.** Every part skins against the same bones (§4); resolve each part's skin
   through its GID (§3) and skin it with the conventions in `specs/skinning.md`. Do not give parts
   their own skeletons.
3. **Weapon on a hand bone.** Attach the weapon mesh to a hand bone (a `BoneAttachment3D`-style anchor)
   rather than baking it into the body mesh; build a second off-hand node for two-piece weapons (skin
   bind class == 3). When driving the per-node animation columns, the **main-hand node carries flag `2`**
   (reads catalog columns 902/904) and the **off-hand node carries flag `1`** (reads 903/905) — see
   §5.1; do not invert this.
4. **Enchant aura is optional and tiered.** Drive a four-emitter glow from the weapon's
   `weapon_effect_grade` (`structs/item.md §6g`): tiers `1..9`, none at `0`. This is *separate* from
   the weapon-trail sword-light (`specs/effects.md §12`). Note the toggler itself is a
   STATIC-HYPOTHESIS on build `263bd994` (§6.1) — treat the four-emitter / loc-string detail as a
   working model pending a re-RE pass, not a confirmed contract.
5. **Weapon-drawn gates a HUD component too.** The weapon-drawn flag (§6.4) is mirrored into the HUD
   weapon-state toggle, not only the avatar mesh — wire it to both.

---

## 10. Cross-references

- **Skinning math + bone addressing:** `specs/skinning.md` — the deform, bind, and bone-id conventions
  the shared skeleton (§4) and the weapon bone attach (§5) obey.
- **Mesh / skeleton bytes:** `formats/mesh.md` — `.skn` / `.bnd` layout, the `id_b ↔ skeleton`
  bijection, and the bone addressing the GID→skin map (§3) resolves into.
- **Item-actor struct:** `structs/item.md` — `item_subtype` (+136), `weapon_effect_grade` (+231, §6g),
  and the set-flag (+512) read by the visual lane (§7.1).
- **Actor / visual struct:** `structs/actor.md` — authoritative layout of the visual object whose
  appearance/part fields are summarised in §7.
- **Spawn descriptor equip block:** `structs/spawn_descriptor.md` — the 20-slot × 16-byte equipment
  block the part table (§2) mirrors.
- **Effects:** `specs/effects.md` — the separate sword-light weapon-trail sub-system (§12), not to be
  confused with the enchant-aura glow (§6).
- **Glossary:** see `Docs/RE/names.yaml`.
- **Provenance:** see `Docs/RE/journal.md`.
