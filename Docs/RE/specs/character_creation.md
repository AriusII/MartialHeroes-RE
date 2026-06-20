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
  UNVERIFIED / static-partial: the exact byte offsets of the class / gender / 5 stat values inside the
  52-byte body (interior not byte-pinned statically); whether the class byte equals the selector value
  verbatim or is remapped; the exact point-buy arithmetic and per-stat caps.
  RUNTIME-ONLY (capture / debugger-pending): the on-wire VALUE meanings of every field, the server's
  actual reply sequence after a 1/6, the progression-panel option list shown for codes 100/101, and the
  5/32 packed HP/MP / stat-dword value semantics.
ida_anchor: 263bd994
ida_reverified: 2026-06-20
evidence: [static-ida]
sample_verified: false
note: |
  IDB SHA 263bd994, CYCLE 7 (2026-06-20) — promoted from dirty-room analyst notes (Block D, lane D5).
  Records the create-character request (1/6, 52-byte appearance blob), the @BLANK@ empty-slot sentinel
  and 880-byte slot stride, the local point-buy + banned-word validation, the create-ack path (latch +
  3/7 + char-list refresh — there is NO 12-byte create-result), the explicit 3/23 correction
  (= SmsgCharStatusBytesByName, 28 bytes), and the class-evolution flow (5/32 SmsgLevelUp, 48 bytes,
  panel at levels 12/24 with progression codes 100/101).
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
> - `packets/` — the per-opcode wire field specs.
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
create screen, then sent verbatim. The leading fields are byte-pinned from observed member writes; the
interior class/gender/stat fields are present but their exact offsets within bytes 24..51 were **not**
byte-pinned by static analysis (the create-form objects are nested), so they are flagged
`static-partial`.

| body off | size | type | field | status | notes |
|---|---|---|---|---|---|
| 0x00 | 20 | char[] (CP949) | character **name** | CONFIRMED (start) | NUL-terminated; the name input is capped to 17 characters in the edit path; the body buffer is wider than the cap. All game text is CP949. |
| 0x12 (18) | 2 | u16 | appearance **FACE** index | CONFIRMED | clamped to the range **1..7**: the "face +" control increments and clamps at 7, the "face −" control decrements and clamps at 1, reset returns it to 1. |
| 0x14 (20) | 2 | u16 | appearance / **variant** word B | CONFIRMED (role static-partial) | initialised to 1; reset returns it to 1. |
| 0x16 (22) | 2 | u16 | appearance word C | CONFIRMED (role static-partial) | initialised to 0; reset returns it to 0. |
| interior (within 24..51) | — | — | **CLASS** selector + **GENDER** + **five stat point-buy values** | static-partial | the class value comes from the class selector (§3); the five stat values come from the point-buy pool (§2). The exact byte offsets inside the body are **debugger-pending** (R-1). |

Total body = **52 bytes** (CONFIRMED).

> **RUNTIME-ONLY / debugger-pending (R-1, R-2):** the precise interior byte offsets of the class,
> gender and five stat values — and whether the class byte equals the selector value verbatim or is
> remapped before packing — are not asserted here. They are best byte-pinned by reading the 52-byte
> buffer at send time in a live create.

---

## 2. Client-side validation BEFORE the request is sent

The create form performs **two independent local validations** and only emits `1/6` once both pass.
*([CONFIRMED]* that both the point-buy gate and the name checks run client-side before the send.)*

### 2.1 Local stat point-buy

The create form runs a **local stat point pool**: five editable stats are seeded to base values (four
to 10 and one to 5), and a separate pool counter holds the remaining spendable points.

- **Increment** a stat: allowed only while the pool counter is below its cap; the pool is consumed.
- **Decrement** a stat: allowed only while the pool counter is above zero; the stat is floored at its
  per-stat base, and the pool is refunded.

The effect is that the client **clamps each stat and gates the total spend against the pool locally**
— the point-buy allocation is fully validated client-side, and the resolved five stat values are
packed into the 52-byte body (§1.2).

> **UNVERIFIED:** the exact base/cap numbers and the precise point-buy arithmetic (e.g. the pool size
> and any per-stat ceiling) are static-partial; treat the "4×10 + 1×5 with a small pool" shape as
> indicative, not byte-exact.

### 2.2 Name validation — banned words + charset + non-empty

Before the request is sent, the entered name is checked locally:

- **Empty name** → rejected with a message-table prompt.
- **Banned word** → the name is tested against a banned-word table; a hit is rejected with a
  message-table prompt.
- **Disallowed characters** → the name's character set is validated; a violation is rejected with a
  message-table prompt.

*([CONFIRMED]* that an empty-name guard, a banned-word table lookup, and a charset validation all gate
the send.)* The concrete message-table string ids are catalogued with the UI text, not here.

---

## 3. Class selection — the {1,2,3,4} selector

The create-form class buttons set a **class selector** value, and the four selectable classes are the
project-canonical `SkinClassId` set (shared with `skinning.md`):

| class selector value | class | shared `SkinClassId` |
|---|---|---|
| **1** | **Musa** | 1 |
| **2** | **Salsu** | 2 |
| **3** | **Dosa** | 3 |
| **4** | **Monk** | 4 |

*([CONFIRMED]* the selector value range {1,2,3,4} and that exactly four real classes exist; a "0" case
is the no-selection / default branch.)* Each class button also selects a per-class create-screen
preview actor, a per-class create BGM cue, and a per-class name-entry prompt — those presentation
details belong to `frontend_scenes.md`.

> **UNVERIFIED (static-partial):** the selector value → class-ordinal mapping is the natural 1:1
> (1 = Musa, 2 = Salsu, 3 = Dosa, 4 = Monk per the shared `SkinClassId`). Whether the class byte
> written into the 52-byte body equals the selector value verbatim or is remapped is
> **debugger-pending (R-2)**.

The chosen class also determines the character's skill page; that class → skill-page relationship is
owned by the sibling skill lane — see `skills.md` / `skill_trees.md`.

---

## 4. The `@BLANK@` empty-slot sentinel & the 880-byte slot stride

The character-select roster is an array of fixed-size slot records supplied by the server. **Each slot
record is 880 bytes**, and the slot's **name field sits at offset 116 within the record**. *([CONFIRMED]*
the 880-byte slot stride and the name-field offset within the record.)*

An **empty slot is marked by the sentinel string `@BLANK@`** in that name field. When the player
activates a roster slot, the client compares the slot's name field against `@BLANK@`:

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
acknowledgement is observed through a **latch + the char-manage result + a refreshed character list**:

1. **The builder arms an in-flight create latch** on the network client object as the request is sent.
2. **The latch is cleared by the char-manage result `3/7` SmsgCharManageResult** (an 8-byte
   char delete/rename/select/create manage result — see `opcodes.md` / `packets/`).
3. **The character list is refreshed** through the ordinary character-list / char-status family
   (`3/1` SmsgCharacterList and the by-name status patch `3/23` of §6), so the newly created character
   appears in its slot.

*([CONFIRMED]* the in-flight latch set by the builder and cleared on the char-manage result, and that
the create round-trip is acknowledged through the existing char-list / char-status path — **not** a
bespoke create-result message.)*

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
| **R-1** | Byte-pin the 52-byte `1/6` body interior — the class byte offset, the gender/variant offset, and the five stat-value offsets — by reading the buffer at send time in a live create. | debugger-pending |
| **R-2** | Confirm whether the packed class byte equals the class selector value verbatim or is remapped. | debugger-pending |
| **R-3** | Confirm there is no dedicated 12-byte create-result; observe the actual server reply sequence after a `1/6` (expect a refreshed `3/1` char list and/or a `3/23` status patch, with the `3/7` manage result clearing the latch). | capture-pending |
| **R-4** | Confirm the class-progression panel identity and the option list it presents for codes 100 / 101. | debugger / static-deep-pending |
| **R-5** | Confirm the `5/32` value semantics (the packed HP/MP qword at body `0x14` and the stat dword pairs). | capture-pending |

---

## 8. Cross-reference map

| This spec covers | Owned elsewhere — cite, don't duplicate |
|---|---|
| The create request `1/6` and its 52-byte appearance body (§1) | `opcodes.md` (catalogue of record), `packets/` (field specs) |
| The class enum {1 Musa, 2 Salsu, 3 Dosa, 4 Monk} = `SkinClassId` (§3) | `skinning.md` (shared class/skin enum) |
| The class → skill-page relationship (§3) | `skills.md` / `skill_trees.md` (sibling lane) |
| The character-select screen, the create modal, the per-class preview actors / BGM / name prompts (§3, §4) | `frontend_scenes.md`, `frontend_layout_tables.md` |
| The create-ack family `3/7`, `3/1`, `3/23` (§5) | `opcodes.md` (catalogue of record), `packets/` (field specs) |
| The level-up / evolution opcode `5/32` and the class-form-refresh / char-property pushes (§6) | `opcodes.md`, `packets/`, `progression.md` |
