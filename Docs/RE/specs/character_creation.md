<!--
verification: character-creation-and-class-evolution [mixed-confidence];
  CONFIRMED on build 263bd994 (static control-flow): the create request is opcode 1/6 with a
  52-byte appearance body (literal 0x34 append at the builder); the in-flight create latch is set by
  the builder and cleared on the char-manage result; the create-ack path is latch + 3/7 + character-list
  refresh (NO dedicated 12-byte create-result message); 3/23 is SmsgCharStatusBytesByName (28-byte
  by-name status/level patch), NOT a create-result; @BLANK@ is the empty-slot sentinel compared against
  the server roster name field at slot-record stride 880; the create form runs a local point-buy pool
  and a local banned-word + charset name check before sending; the class selector takes {1,2,3,4} =
  Musa / Salsu / Dosa / Monk; class-evolution is driven by S2C 5/32 SmsgLevelUp (48-byte body) which
  pops a class-progression panel at level 12 (progression code 100) and level 24 (progression code 101).
  CONFIRMED CYCLE 11 / Block A (static, re-derived + counter-checked): the FULL 52-byte 1/6 interior is
  now byte-pinned and little-endian (name[18]@0, Face@0x12, AppearanceA@0x14, AppearanceB@0x16,
  ClassInternalId@0x18, pad[2]@0x1A, Stat0..4 u32@0x1C..0x2C, PointsRemaining u32@0x30); the UI class
  button index is REMAPPED to the internal class id by {0->4,1->1,2->3,3->2} (explicit per-branch
  constants, not arithmetic); the point-buy is a 5-point budget with all five stats opening at 10, a
  per-stat floor of 10, invariant sum(stats)+points=55; the per-class description strings come from the
  data/script/npc.scr keyed-node table (NOT the message catalogue), read at record offsets +20/+84/+148;
  the per-class create-form BGM cues are 910062000/910063000/910064000/910065000 on the category-0 music
  slot (replace, never overlay).
  CORRECTED CYCLE 11 / Block A (binary-won): the earlier "the fifth stat opens with a smaller floor"
  reading is REFUTED -- all five stats open at 10 with floor 10; the value "5" is the points budget,
  not a low-floor sixth stat.
  REFINED CYCLE 12 (2026-06-22, build 263bd994): (a) the per-stat ceiling 15 is IMPLICIT (budget
  exhaustion at 10+5=15), not an explicit max-15 compare; the only explicit per-stat bound is floor 10,
  keyed on ClassInternalId in {1,2,3,4}; (b) @BLANK@ name field at offset 116 belongs to the per-slot
  actor/pointer object (the this+6220 array), NOT at offset 116 within the 880-byte roster record
  (this+568 block); these are two separate structures; (c) BOTH create latches documented -- (A) the
  select-window net-busy latch (createBlob+0x34) cleared then set before send, and (B) the
  create-specific in-flight marker on the network client object set inside the 1/6 send and cleared
  by the 3/7 result.
  UNVERIFIED / capture-pending: the VALUE semantics of the two appearance words at 0x14/0x16 (sex vs
  hair vs reserved), the actual on-wire bytes of the 0x1A pad (possible stale residue), the per-stat
  semantic order within the five-stat run, the server's actual reply sequence after a 1/6, the
  progression-panel option list shown for codes 100/101, and the 5/32 packed HP/MP / stat-dword value
  semantics.
ida_anchor: 263bd994
ida_reverified: 2026-06-22
evidence: [static-ida]
sample_verified: false
note: |
  IDB SHA 263bd994. CYCLE 7 (2026-06-20) promoted the create flow from dirty-room notes. CYCLE 11 /
  Block A (2026-06-22, static-only) byte-pinned the full 52-byte 1/6 interior, confirmed the
  {0->4,1->1,2->3,3->2} class remap and the 5-point point-buy (all stats floor 10), added the
  data/script/npc.scr class-description binding and the per-class create BGM cues, and corrected the
  "fifth stat smaller floor" misreading (binary-won). Records the @BLANK@ empty-slot sentinel and
  880-byte slot stride, the local point-buy + banned-word validation, the create-ack path (latches +
  3/7 + char-list refresh -- there is NO 12-byte create-result), the 3/23 correction
  (= SmsgCharStatusBytesByName, 28 bytes), and the class-evolution flow (5/32 SmsgLevelUp, 48 bytes,
  panel at levels 12/24 with progression codes 100/101). CYCLE 12 (2026-06-22): refined per-stat
  ceiling as implicit (budget exhaustion), separated the two slot structures (@BLANK@ name@116 in
  actor/pointer object vs 880-byte server roster record), and documented both create latches.
-->

# Character Creation, Appearance & Class Evolution — Clean-Room Specification

> Neutral, rewritten specification promoted from dirty-room analyst notes under **EU Software
> Directive 2009/24/EC Art. 6** (decompilation permitted solely to achieve interoperability). It
> contains **no decompiler output, no pseudo-code, no legacy symbol names, and no binary virtual
> addresses**.
>
> **Spec path (cite this):** `// spec: Docs/RE/specs/character_creation.md`
>
> **Scope.** How the client creates a character from the character-select screen and how the
> class-progression ("evolution") milestones fire in-game: the **create-character request `1/6`** and
> its 52-byte appearance body, the **local point-buy + name validation** done *before* the request is
> sent, the **`@BLANK@` empty-slot sentinel** that offers creation, the **create-ack path** (no
> dedicated create-result message), and the **class-evolution flow** (`5/32` at levels 12 and 24).
> The opcode framing/dispatch, the wire field specs, and the character-select screen layout are owned
> by neighbours and cited, not duplicated:
> - `opcodes.md` — the 8-byte wire frame header + the opcode catalogue of record.
> - `packets/` — the per-opcode wire field specs (`packets/cmsg_char_create.yaml` is the `1/6` field spec).
> - `frontend_scenes.md` / `frontend_layout_tables.md` — the character-select screen, the create
>   modal, and the in-scene preview actors.
> - `skinning.md` — the class enum {1 Musa, 2 Salsu, 3 Dosa, 4 Monk} shared as `SkinClassId`.
> - `skills.md` / `skill_trees.md` (sibling lane) — the class → skill-page relationship.
> - `progression.md` — the level/experience progression this spec's evolution milestones sit on.

---

## 1. Create-character request — outbound `1/6` CmsgCreateCharacter

A new character is created by sending **C2S `1/6` CmsgCreateCharacter**, an opcode whose body is a
**52-byte appearance blob** (`0x34` bytes). *([CONFIRMED]* the opcode `1/6` and the 52-byte body
length — the builder appends a literal `0x34` bytes from a contiguous form-object region.)*

### 1.1 Wire frame

```
[u32 size][u16 major = 1][u16 minor = 6][ ...52-byte appearance body... ]
```

The standard 8-byte frame header precedes a body of exactly **52 bytes** (`opcodes.md §Wire frame
header`).

### 1.2 The 52-byte appearance body (createBlob)

The body is the contiguous "create form" region, assembled member-by-member as the player edits the
create screen, then sent verbatim. The full interior is now byte-pinned: the create-form per-control
stores, the stat steppers, and the wire append were independently re-derived and cross-checked against
the window's slot block-copy layout (CYCLE 11 / Block A), so the class, appearance and stat offsets are
**CONFIRMED** rather than the earlier `static-partial`. All multi-byte fields are stored at native
word/dword widths with no byte swap → **little-endian**.

| body off | size | type | field | status | notes |
|---|---|---|---|---|---|
| 0x00 | 18 | char[18] (CP949) | character **name** | CONFIRMED | NUL-terminated; the name input is capped to 17 characters + terminator in the edit path; minimum length 2. All game text is CP949. |
| 0x12 (18) | 2 | u16 | appearance **FACE** index | CONFIRMED | initialised to 1; clamped to **1..7**: "face +" increments and clamps at 7, "face −" decrements and clamps at 1, reset returns it to 1. |
| 0x14 (20) | 2 | u16 | **AppearanceA** word | CONFIRMED offset (semantics UNVERIFIED) | initialised to 1; not user-stepped on the create path (appears class-implied). The sex-vs-appearance meaning is capture-pending. |
| 0x16 (22) | 2 | u16 | **AppearanceB** word | CONFIRMED offset (semantics UNVERIFIED) | initialised to 0; appearance index or reserved/pad — capture-pending. |
| 0x18 (24) | 2 | u16 | **ClassInternalId** | CONFIRMED | the **INTERNAL** class id — a remap of the UI class-button index (§3); the preview-actor visual switch keys on this value. |
| 0x1A (26) | 2 | char[2] | **Reserved1A** (pad) | CONFIRMED offset (bytes UNVERIFIED) | alignment gap so the stat run starts dword-aligned at 0x1C; not written on the create path — model as reserved/zero (actual on-wire bytes, possibly stale residue, are capture-pending). |
| 0x1C (28) | 4 | u32 | **Stat0** | CONFIRMED | initialised to 10 (§2.1). |
| 0x20 (32) | 4 | u32 | **Stat1** | CONFIRMED | initialised to 10. |
| 0x24 (36) | 4 | u32 | **Stat2** | CONFIRMED | initialised to 10. |
| 0x28 (40) | 4 | u32 | **Stat3** | CONFIRMED | initialised to 10. |
| 0x2C (44) | 4 | u32 | **Stat4** | CONFIRMED | initialised to 10. |
| 0x30 (48) | 4 | u32 | **PointsRemaining** | CONFIRMED | the trailing allocation budget, initialised to 5 (§2.1). |

Total body = **52 bytes** (CONFIRMED). Width sum: 18 + 2 + 2 + 2 + 2 + 2 + 4×5 + 4 = 52.

> **CONFIRMED this cycle (was R-1/R-2):** the interior byte offsets of the class and five stat values
> are now pinned, and the class word is a **remap** of the selector (§3). The remaining capture-pending
> items are only the *value semantics* of AppearanceA/AppearanceB (0x14/0x16), the actual bytes of the
> Reserved1A pad (0x1A), and the per-stat semantic order — none of which change the byte layout.

---

## 2. Client-side validation BEFORE the request is sent

The create form performs **two independent local validations** and only emits `1/6` once both pass.
*([CONFIRMED]* that both the point-buy gate and the name checks run client-side before the send.)*

### 2.1 Local stat point-buy

The create form runs a **local stat point pool**: five editable stats are each seeded to **10**, and a
separate budget counter holds the remaining spendable points, seeded to **5**. *([CONFIRMED]* the
arithmetic below, byte-pinned from the stepper handlers, CYCLE 11 / Block A.)*

- **Increment** a stat: allowed only while the budget is **> 0**; the budget is decremented and the
  stat incremented.
- **Decrement** a stat: allowed only while the budget is **< 5** *and* the stat is strictly above its
  floor; the budget is incremented and the stat decremented.
- **Per-stat floor = 10** — this is the only **explicit** per-stat bound enforced by the stepper logic.
  The floor-10 compare is keyed on `ClassInternalId ∈ {1, 2, 3, 4}` (all four create-form classes
  satisfy this membership check).
- **Per-stat effective ceiling = 15** — this ceiling is **implicit**, not enforced by an explicit
  per-stat max-15 compare. A stat can reach at most `10 (seed) + 5 (full budget) = 15` because the
  increment path is gated on `budget > 0` and the budget is seeded to 5; once the budget is exhausted
  the increment is blocked. There is **no separate `stat <= 15` guard** in the stepper.
- **Invariant:** `sum(Stat0..Stat4) + PointsRemaining = 55` at all times.

The effect is that the client **enforces the floor explicitly and the ceiling implicitly via budget
exhaustion**, gates the total spend against the budget locally, and packs the resolved five stat values
+ the remaining-budget counter into the 52-byte body (§1.2).

> **CORRECTION (binary-won, CYCLE 11 / Block A):** the earlier "four stats at 10 and one at 5 with a
> smaller floor" reading is **REFUTED**. All five stats open at **10** with floor **10**; the value
> **5** is the **points budget**, not a low-floor stat.

### 2.2 Name validation — banned words + charset + non-empty

Before the request is sent, the entered name is checked locally:

- **Empty / too-short name** → rejected (minimum length 2) with a message-table prompt.
- **Banned word** → the name is tested against a banned-word table; a hit is rejected with a
  message-table prompt.
- **Disallowed characters** → the name's character set is validated; a violation is rejected with a
  message-table prompt.
- **Placeholder caption** → the form also rejects the default placeholder caption.

*([CONFIRMED]* that a length guard, a banned-word table lookup, a charset validation, and a placeholder
check all gate the send.)* The concrete message-table string ids are catalogued with the UI text, not
here. Only on passing all checks is the validated name string-copied to body offset 0 and the send fired.

Before the send is emitted the **select-window net-busy latch** (latch A, held at `createBlob+0x34`)
is checked and set: the send path verifies the latch is **clear** (not already in-flight), then sets it
to mark the operation as busy. This is a *select-window-level* guard preventing double-submission from
the create modal; it is distinct from the network-client-level create marker (§5). *([CONFIRMED]* the
clear-then-set discipline on the select-window net-busy latch before the 1/6 send.)*

---

## 3. Class selection — the UI button → internal-class remap

The create-form class buttons set a **class selector** value. *([CONFIRMED]* CYCLE 11 / Block A: the UI
class-button index is **remapped** to the internal class id before it is written into the 52-byte body,
by explicit per-branch constants — not a verbatim copy and not arithmetic.)*

| UI class-button index | → internal class id (body `0x18`) | class | shared `SkinClassId` |
|---|---|---|---|
| **0** | **4** | **Monk** | 4 |
| **1** | **1** | **Musa** | 1 |
| **2** | **3** | **Dosa** | 3 |
| **3** | **2** | **Salsu** | 2 |

The four selectable internal classes are the project-canonical `SkinClassId` set {1 Musa, 2 Salsu,
3 Dosa, 4 Monk} (shared with `skinning.md`); a "no-selection" default branch also exists. The preview
actor's visual switch keys on the **internal** id (body `0x18`), so the remap above is the binding the
preview and the wire both honour.

### 3.1 Per-class create-form presentation (CONFIRMED)

Selecting a class button also drives three presentation effects, all keyed off the same button index:

- **Description strings** — three description labels are filled from the **`data/script/npc.scr`**
  keyed-node table (a record table keyed by a small integer, **not** the message catalogue), reading
  three 64-byte string slots at record offsets **+20 / +84 / +148**. The button index selects the record
  key by the mapping {0→1, 1→2, 2→4, 3→3}. *(CONFIRMED mechanism; the npc.scr record format is owned by
  `formats/` — cite, don't duplicate.)*
- **Per-class create BGM** — a per-class music cue (**910062000 / 910063000 / 910064000 / 910065000**
  for the four buttons) plays on the **category-0 music slot**, replacing the scene BGM rather than
  overlaying it. *(CONFIRMED immediates.)*
- **Close-up preview actor** — the create form spawns a single close-up preview actor for the selected
  class (the lineup/preview machinery is owned by `frontend_scenes.md` / `rendering.md`).

The chosen class also determines the character's skill page; that class → skill-page relationship is
owned by the sibling skill lane — see `skills.md` / `skill_trees.md`.

---

## 4. The `@BLANK@` empty-slot sentinel — two distinct structures

The character-select roster involves **two separate structures** with different roles; conflating them
is a common error:

**Structure A — the per-slot actor/pointer object (the `this+6220` array).**
The select screen maintains a fixed-length array of per-slot actor/pointer objects. Each object in
this array holds a **name field at offset 116** within the object. *([CONFIRMED]* the name-field offset
of 116 is within the actor/pointer object, not within the server-supplied roster record.)*

**Structure B — the 880-byte (0x370) roster record (the `this+568` block).**
The server-supplied character list arrives as a sequence of **880-byte roster records**. On entering
the select screen these records are block-copied into the `this+568` storage region. The 880-byte
figure is the **stride of this server record**, not the stride of the actor/pointer object above.
*([CONFIRMED]* the 880-byte server record stride and the block-copy into `this+568`.)*

> **Important:** the name field at offset 116 belongs to **Structure A** (the actor/pointer object).
> Do **not** assert that the name sits at offset 116 within the 880-byte Structure B roster record —
> these are separate objects with separate layouts.

An **empty slot is marked by the sentinel string `@BLANK@`** in the name field (Structure A, offset
116). When the player activates a roster slot, the client compares that name field against `@BLANK@`:

- **Name equals `@BLANK@`** → the slot is **empty**: instead of entering the game, the client records
  the chosen slot index and opens the **character-create modal** (offering creation in that slot).
- **Name is not `@BLANK@`** → the slot holds a real character: the client proceeds to the enter-game
  request for that slot.

*([CONFIRMED]* the role of `@BLANK@` as the empty-character-slot sentinel and the empty-slot →
open-create-modal branch.)* `@BLANK@` is a **client-side** comparison against the server-supplied
roster name field; the server populates the roster, and the client interprets the sentinel.

---

## 5. The create-ack path — there is NO 12-byte create-result

After `1/6` is sent, the client does **not** wait on a dedicated create-result packet. The
acknowledgement is observed through **two independent latches + the char-manage result + a refreshed
character list**. There are **two distinct create latches** that together guard the operation:

**Latch A — select-window net-busy latch (`createBlob+0x34`).**
Owned by the select window. Checked and set **before** the 1/6 send (clear → set, §2.2). This is a
double-submission guard at the UI layer — if it is already set when the send path is entered, the send
is suppressed. *([CONFIRMED]* the clear-then-set gating discipline on this latch.)*

**Latch B — network-client create in-flight marker.**
Owned by the network client object. **Set inside the 1/6 send** (as part of the create builder
dispatching the request). **Cleared when `3/7` SmsgCharManageResult is received** — the char-manage
result handler (an 8-byte char delete/rename/select/create manage result; see `opcodes.md` / `packets/`)
clears this marker, signalling that the create round-trip is complete. *([CONFIRMED]* the in-flight
marker set inside the send and cleared by the 3/7 result.)*

The full ack sequence:

1. **Latch A checked and set** (select-window net-busy) before send — prevents double-submission.
2. **1/6 sent; Latch B set** (network-client in-flight marker) inside the create builder.
3. **`3/7` received → Latch B cleared** (the char-manage result handler).
4. **The character list is refreshed** through the ordinary character-list / char-status family
   (`3/1` SmsgCharacterList and the by-name status patch `3/23` of §5.1), so the newly created
   character appears in its slot.

*([CONFIRMED]* both latches and that the create round-trip is acknowledged through the existing
char-list / char-status path — **not** a bespoke create-result message.)*

> **RUNTIME-ONLY (R-3):** the exact server reply sequence after a `1/6` (which of `3/7`, `3/1`, `3/23`
> arrive and in what order) is capture-pending.

### 5.1 Correction — `3/23` is `SmsgCharStatusBytesByName`, not a create-result

An earlier working hypothesis treated **`3/23`** as a 12-byte "create result". **The binary refutes
this.** The inbound major-3 dispatcher routes `3/23` to **`SmsgCharStatusBytesByName`**, a **28-byte**
by-name character-status patch — there is **no** 12-byte create-result opcode at all. *([CONFIRMED]*
the `3/23 → SmsgCharStatusBytesByName` routing and the 28-byte body read.)*

`3/23` carries a **character-name key** (matched against the roster names) plus two trailing status
bytes — a **flag byte** and the **character level byte** — which are written into the matching roster
record (and into the local-player globals when the key is the local player). It is a **status/level
patch keyed by name**, consistent with the prior cartography finding that `3/23` is a by-name status
message. The wire field table for `3/23` lives in `packets/` and the opcode identity in `opcodes.md`;
do **not** model `3/23` as a create-result.

| `3/23` body off | size | type | field | notes |
|---|---|---|---|---|
| 0x00 | 17 | char[] (CP949) | character-name key | matched against the roster names to find the target record |
| (trailing) | 1 | u8 | status flag byte | written into the matched roster record (and the local flag global when it is the local player) |
| (trailing) | 1 | u8 | character **level** byte | written into the matched roster record (and the local-player level global when it is the local player) |

*([CONFIRMED]* opcode, 28-byte read, the name key, and the two trailing status/level bytes; the exact
interior offsets of the two trailing bytes within the 28-byte read are static-partial.)*

---

## 6. Class evolution — inbound `5/32` SmsgLevelUp (panel at levels 12 / 24)

Class progression ("evolution") is **driven by the level-up message**, not by a dedicated class-select
packet. The server announces a level gain with **S2C `5/32` SmsgLevelUp**, a **48-byte** body
(`0x30`). *([CONFIRMED]* the `5/32 → SmsgLevelUp` routing and the 48-byte body read.)*

### 6.1 `5/32` SmsgLevelUp — 48-byte body

The body identifies the actor that levelled, the new level, and a set of vitals/stat values applied to
that actor. The new-level field is the **evolution trigger** (§6.2).

| body off | size | type | field | applied to | status |
|---|---|---|---|---|---|
| 0x00 | 4 | int | actor key (low word of composite) | actor lookup | CONFIRMED (offset) |
| 0x04 | 4 | int | actor key (high word of composite) | actor lookup | CONFIRMED (offset) |
| 0x08 | 2 | u16 | **NEW LEVEL** (the 12 / 24 trigger) | actor level field; local-level global | CONFIRMED |
| 0x0C | 4 | i32 | stat / experience dword A | global | CONFIRMED (offset) |
| 0x10 | 4 | i32 | stat / experience dword B | global | CONFIRMED (offset) |
| 0x14 | 8 | qword | packed HP / MP pair | actor vitals; vitals global | CONFIRMED (offset); value packing RUNTIME-ONLY |
| 0x1C | 4 | dword | vital / stamina dword | actor; global | CONFIRMED (offset) |
| 0x20 | 8 | qword | stat dword pair C | global | CONFIRMED (offset) |
| 0x28 | 8 | qword | stat dword pair D | global | CONFIRMED (offset) |

Total = **48 bytes** (CONFIRMED).

> **RUNTIME-ONLY (R-5):** the concrete value semantics of the packed HP/MP qword at `0x14` and the
> stat dword pairs are capture/debugger-pending; only the offsets/sizes are asserted here.

### 6.2 The evolution trigger — progression codes 100 / 101 at levels 12 / 24

Inside `5/32`, **only when the levelling actor is the local player** (and after applying the new
level / HP / MP and playing the level-up effect + sound), the new-level value is tested against the two
evolution milestones:

- **New level == 12** → open the **class-progression panel** populated with **progression code 100**
  (the first evolution) and mark it enabled.
- **New level == 24** → open the **same class-progression panel** populated with **progression code
  101** (the second evolution) and mark it enabled.

*([CONFIRMED]* the level-12 and level-24 thresholds and the distinct progression codes 100 / 101.)*
The panel populate step resolves a UI record from the progression code via a lookup table; if a record
exists for the code, the panel is shown with that record's label text and the code is stored on the
panel; if no record matches the code, the panel stays hidden.

Two important facts about the trigger:

- **`5/32` does not itself carry the chosen new class.** It is the *level event* that **pops the
  selection panel** at the two evolution milestones. The player then makes a selection in that panel.
- **The class-tier change is server-authoritative.** After the selection, the new class form arrives
  through the ordinary actor-class-form refresh push and the character-property push (catalogued in
  `opcodes.md` / `packets/` and applied per `progression.md` / the actor-state specs) — the client does
  not decide the new class locally.

> **RUNTIME-ONLY / static-partial (R-4):** the concrete option list the progression panel presents for
> codes 100 / 101 (which class-evolution choices are offered), and the exact UI-slot identity of the
> panel, are not asserted here — they are static-deep / debugger-pending.

---

## 7. Open items (capture / debugger-pending)

| id | item | status |
|---|---|---|
| ~~R-1~~ | ~~Byte-pin the 52-byte `1/6` body interior.~~ **RESOLVED** (CYCLE 11 / Block A) — full interior pinned in §1.2. | resolved |
| ~~R-2~~ | ~~Confirm the class remap.~~ **RESOLVED** — UI index → internal id = {0→4, 1→1, 2→3, 3→2} (§3). | resolved |
| **R-2a** | Confirm the VALUE semantics of the two appearance words at body `0x14` / `0x16` (sex vs hair vs reserved) and the actual on-wire bytes of the `0x1A` pad. | capture-pending |
| **R-3** | Confirm there is no dedicated 12-byte create-result; observe the actual server reply sequence after a `1/6` (expect a refreshed `3/1` char list and/or a `3/23` status patch, with the `3/7` manage result clearing the latch). | capture-pending |
| **R-4** | Confirm the class-progression panel identity and the option list it presents for codes 100 / 101. | debugger / static-deep-pending |
| **R-5** | Confirm the `5/32` value semantics (the packed HP/MP qword at body `0x14` and the stat dword pairs). | capture-pending |

---

## 8. Cross-reference map

| This spec covers | Owned elsewhere — cite, don't duplicate |
|---|---|
| The create request `1/6` and its 52-byte appearance body (§1) | `opcodes.md` (catalogue of record), `packets/cmsg_char_create.yaml` (field spec) |
| The class enum {1 Musa, 2 Salsu, 3 Dosa, 4 Monk} = `SkinClassId` and the UI→internal remap (§3) | `skinning.md` (shared class/skin enum), `frontend_scenes.md` (the class strip) |
| The per-class npc.scr description binding + create BGM cues (§3.1) | `frontend_scenes.md`, `formats/` (npc.scr record format) |
| The class → skill-page relationship (§3) | `skills.md` / `skill_trees.md` (sibling lane) |
| The character-select screen, the create modal, the per-class preview actors / BGM / name prompts (§3, §4) | `frontend_scenes.md`, `frontend_layout_tables.md` |
| The create-ack family `3/7`, `3/1`, `3/23` (§5) | `opcodes.md` (catalogue of record), `packets/` (field specs) |
| The level-up / evolution opcode `5/32` and the class-form-refresh / char-property pushes (§6) | `opcodes.md`, `packets/`, `progression.md` |
