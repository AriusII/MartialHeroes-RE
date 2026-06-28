<!--
verification: skill-tree STRUCTURE (the learn/prerequisite/tier graph, the trainer learn-gate, the
  learn & hotbar-bind opcodes, the class→stance `.do`-page selection, and the absence of any respec
  subsystem) is static-control-flow-confirmed on build 263bd994;
  per-class skill COUNTS and exact tree DEPTH are data-driven (live in `skills.scr` + the `.do` files)
  and are sample-pending — not statically countable from code;
  the inner field split of the 12-byte learn entry, any server-arbitrated learn cost, and the
  meaning of the unread `skills.scr` bytes are RUNTIME-ONLY (capture/debugger-pending).
ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
ida_reverified: 2026-06-20 (SHA 263bd994, CYCLE 7); CYCLE 14 re-anchor: 2026-06-27
evidence: [static-ida]
sample_verified: false
note: |
  IDB SHA 263bd994, CYCLE 7 (2026-06-20) — new spec for the skill-TREE / prerequisite / learn-gate
  structure (the GlobalCategory class-page key, the PrerequisiteSkillId[3] edge array at +1280, the
  TierByte at +520, the trainer JOB band 2549..2553 → rank 1..5 learn-gate, the learn opcode 2/145
  and the hotbar-bind opcode 2/41, the class→stance `.do`-page selection, and the confirmed ABSENCE
  of any skill-reset/respec subsystem in the client). The skill *execution / effects* spec is owned
  by `specs/skills.md` and is cross-referenced here, not duplicated.
cycle14: |
  CYCLE 14 re-anchor (f61f66a9, 2026-06-27): 1 fact re-confirmed SAME (skillneedset.scr 4-byte
  prerequisite->dependent edge DAG; skills.scr prerequisite array @+1280, 3-entry; on-disk sidecar
  and record formats unaffected by the build delta).
-->

# Skill Trees — Learn Order, Prerequisites & the Trainer Gate — Clean-Room Specification

> Neutral, rewritten specification promoted from dirty-room analyst notes under **EU Software
> Directive 2009/24/EC Art. 6** (decompilation permitted solely to achieve interoperability). It
> contains **no decompiler output, no pseudo-code, no legacy symbol names, and no binary virtual
> addresses**.
>
> **Spec path (cite this):** `// spec: Docs/RE/specs/skill_trees.md`
>
> **Scope.** How skills are organised into per-class **trees**, how a skill's **prerequisites** and
> **rank tier** define the learn order, how a **skill-trainer NPC** gates which rank a player may
> learn, the two outbound opcodes that **commit a learn** and **bind a skill to the hotbar**, and how
> the active class + stance selects the `.do` page that populates the visible skill list. Skill
> *execution* (cast gate, targeting, effects) is owned by `specs/skills.md` (Lane 2) and is **not**
> duplicated here. The opcode framing/catalogue and the wire field layouts are owned by neighbours and
> cited, not duplicated:
> - `specs/skills.md` — skill execution, the cast gate, and the full `skills.scr` gameplay/combat
>   offset table (this spec re-states only the **tree-defining** subset).
> - `opcodes.md` — the 8-byte wire frame header + the opcode catalogue (`2/145`, `2/41`).
> - `packets/` — the wire field specs for the learn / hotbar-bind bodies.
> - `formats/scr.md` — the on-disk `skills.scr` record format.
> - `specs/character_creation.md` / `specs/skinning.md` — the class enum {1 Musa, 2 Salsu, 3 Dosa,
>   4 Monk} (shared as `SkinClassId`).

---

## 1. The tree model — what a "tree" is

The skill system is a **stance / `.do`-driven action catalogue**, **not** a classic point-buy talent
grid. There is **no client-side skill-point currency** (see §5). A "tree" is the directed graph over
the master skill catalogue, formed entirely from columns of the `skills.scr` record:

- **Node** — one `skills.scr` record, identified by its **SkillId**.
- **Class page (the page filter)** — skills are grouped by **GlobalCategory**, the per-class /
  per-discipline page key. The skill window enumerates a page by matching `record.GlobalCategory ==
  page.id`. The window exposes **9 category pages** (page index gated `category ≤ 8`).
- **Prerequisite edge (the upstream tree edge)** — each record carries a **3-entry
  PrerequisiteSkillId array**: skill *B* is learnable only when one of *B*'s three prerequisite ids is
  already owned. This array **is** the literal tree edge ("must already own skill X").
- **Rank-chain edge (the forward / upgrade edge)** — the **TierByte** orders the rank forms of a
  skill chain (a skill at tier *T* chains to tier *T+1*); the trainer JOB band (§3) unlocks ranks
  1→5 along that chain.

So tree depth has **two axes**: *prerequisite depth* (chains of prerequisite references) and *rank
depth* (the five tiers unlocked by the trainer bands). *([confirmed]* that the GlobalCategory page
filter and the prerequisite array form the tree, and that the tier byte plus the trainer bands form
the rank axis.)*

> **Per-class skill counts and exact tree depth are data-driven** — they live in a real `skills.scr`
> sample plus the `.do` page files and are **not** statically countable from code. The *structure*
> (class page × prerequisite edge × five rank tiers) is `[confirmed]`; the *counts/depth* are
> **sample-pending**.

---

## 2. `skills.scr` — the tree-defining record columns

The master skill catalogue is `data/script/skills.scr`. Each record is a **1504-byte fixed base
block** followed by a **sub-row count** and then **`N` × 8-byte sub-rows** (the per-rank variant
rows). At runtime the loader copies the 1504-byte base and appends a trailing pointer to the sub-row
array, and the 8-byte disk sub-rows are expanded to 12-byte runtime rows. The catalogue is indexed by
**SkillId**, with a secondary index by **GlobalCategory** so the UI can enumerate a class/family page
without scanning every id. The full on-disk record format is owned by `formats/scr.md`; the full
gameplay/combat offset table is owned by `specs/skills.md`. This spec re-states only the
**tree-relevant** columns:

| offset | size / type | field (canonical) | tree role | confidence |
|------:|-------------|-------------------|-----------|------------|
| +0 | u32 | **SkillId** | catalogue key / node identity | CONFIRMED |
| +520 | u8 | **TierByte** | the rank-tier of this chain form; orders the rank chain (tier *T* → *T+1*); the trainer rank tier compares against it | CONFIRMED |
| +4 | u32 | **GlobalCategory** | the per-class **page key** (which class tab the skill appears under); secondary catalogue index; the UI matches a page by `record.GlobalCategory == page.id` | CONFIRMED |
| +1280 | u32 × 3 | **PrerequisiteSkillId[0..2]** | **the prerequisite tree edge** — "must already own one of these three skills first"; the learn-gate scans all three entries | CONFIRMED |

*(Offsets are listed lowest-first within the table where it clarifies the layout; the canonical
field-order semantics are unchanged.)*

> **No learn-cost column, no max-rank column, and no explicit "learnable vs inherent" boolean** was
> found at a confirmed read site inside the base record. Maximum rank is expressed *implicitly* by the
> per-rank sub-row count `N` plus the TierByte chain; any learn cost is **server-arbitrated** (§4,
> §5). It is **UNVERIFIED** whether any of the unread base-record bytes encode a cost or an innate
> flag — promote only if a real sample shows a stable separating byte.

---

## 3. The learn-gate — the skill-trainer NPC

A player learns a skill by interacting with a **skill-trainer NPC** (reached through the standard
actor-interaction path; the trainer NPC kind opens the **skill-confirm panel**). The trainer reads
its own **JOB id** and maps a JOB-id band to a **rank** and a player **level window**; the gate is
also checked against the player's current rank. The decisive mapping:

| trainer JOB id | level window `[lo, hi)` | grants rank |
|---:|:---:|:---:|
| 2549 | 2 .. 7 | 1 |
| 2550 | 7 .. 12 | 2 |
| 2551 | 12 .. 17 | 3 |
| 2552 | 17 .. 21 | 4 |
| 2553 | 21 .. 24 | 5 |

*([confirmed]* the JOB band `2549..2553`, its monotonic mapping to ranks `1..5`, and the per-rank
level windows.)* When the player's current rank does not match the rank the trainer grants, the
client refuses and shows a rank-mismatch notice (a small contiguous block of message-table entries
reserved for this case). The trainer gate is therefore purely **class page + level-band rank +
prerequisite** — there is no point pool to spend.

The confirm panel accumulates a list of **candidate skill entries** (each a 12-byte row, matching the
runtime sub-row width); the panel's commit button submits that pending list to the server (§4.1),
while a separate button binds an already-known skill to the hotbar (§4.2).

---

## 4. The learn & bind opcodes (outbound)

Both bodies are catalogued in `opcodes.md` and field-specced under `packets/`; this section records
only the **tree-side semantics**.

### 4.1 Commit a learn — **C2S `2/145`**

The confirm panel's **commit-learn** button submits the pending candidate list with **outbound
opcode `2/145`**. The body is:

```
u32   count                       — number of skill entries that follow
entry[count] × 12 bytes           — the learned skill-rank rows (12 B = the runtime sub-row width)
```

*([confirmed]* opcode `2/145`, the leading `u32 count`, and the `count × 12-byte` entry array.)*

> **RUNTIME-ONLY:** the **inner field split of each 12-byte entry** (which bytes are the skill id, the
> rank, and any flag) is **not asserted** statically — it is the runtime sub-row layout and stays
> capture/debugger-pending (read it live or from the server reply).

### 4.2 Bind a skill to the hotbar — **C2S `2/41`**

A skill that is already known is bound into a hotbar slot with **outbound opcode `2/41`**. The body is
a **single 12-byte record** describing the slot, the skill, and an extra field. This is the generic
HUD-slot select (it places a known skill into the hotbar); it is **not** the learn itself. *([confirmed]*
opcode `2/41` and the single 12-byte record body.)*

---

## 5. Respec / reset — **ABSENT in the client (CONFIRMED by exhaustion)**

**The client has no skill-reset / respec subsystem.** This is `[confirmed]` by exhaustion:

- **No respec/reset string** exists in the client's string set (no "respec", "reset", "skill point",
  nor the Korean equivalents in the skill UI cluster).
- **No skill-point currency** is read anywhere on the learn or cast path — the client never subtracts
  a skill point. The trainer gates purely by class page, level-band rank, and prerequisite.
- **No reset / respec outbound opcode** exists among the named skill/HUD outbound builders. The
  skill-confirm panel and the in-game skill panel expose only the **commit-learn** (`2/145`) and the
  **hotbar-bind** (`2/41`) paths plus cancel/close — none is a skill-reset.

Any learn **cost** (if one applies at all — e.g. gold or level) is **server-arbitrated** and is
**RUNTIME-ONLY** (capture/debugger-pending). If a respec exists anywhere, it is a server-side admin /
item path with **no dedicated client opcode**.

> **Port directive:** do **not** invent a respec/reset opcode, a skill-point pool, or a reset-cost
> flow in the re-implementation. None exists in the client.

---

## 6. Learnable vs inherent — structural, not a flag

There is **no boolean column** separating learnable from innate skills; the distinction is
**structural**:

- **Learnable / trained** skills are those reachable through the trainer band: they carry a **rank
  tier (TierByte)**, are committed via **`2/145`**, and are then bound to the hotbar via **`2/41`**.
  The trainer gates them by JOB-band rank and prerequisite.
- **Inherent / innate** skills are catalogue records the player always has, with **no prerequisite and
  no rank tier**, granted directly by the class/stance `.do` set (most notably the basic-attack
  record). These never flow through `2/145`.

*([confirmed]* the structural split; **UNVERIFIED** whether any unread base-record byte carries an
explicit innate flag — promote only if a sample shows one.)*

---

## 7. Class × stance → the `.do` page selection

Which skill **set** is visible is chosen by the active **class** and **stance**, which together pick a
`.do` page file. The selection is keyed on the class/stance state:

- **class index** ∈ {1, 2, 3, 4} = {Musa, Salsu, Dosa, Monk} (the shared `SkinClassId`; see
  `specs/character_creation.md` / `specs/skinning.md`),
- **stance type** ∈ {0, 1, 2},
- a **stance tier** that selects among the per-stance variants.

A class→file selector resolves these to one of roughly **twelve `.do` slots** (4 classes × 3 stances).
The selected `.do` page supplies the **visible page rows**; each row references a `skills.scr` record
by id, and that record's GlobalCategory page (§1) and prerequisite array (§2) then govern the learn
order **within** the visible set.

| class index | class | stance slots (0 / 1 / 2) |
|---:|:---:|:---:|
| 1 | Musa | three `.do` slots (+ tier variants) |
| 2 | Salsu | three `.do` slots |
| 3 | Dosa | three `.do` slots |
| 4 | Monk | three `.do` slots |

*([confirmed]* that class index {1..4} × stance {0..2} selects the `.do` page set that populates the
visible skill list.)* The exact contents of each `.do` page (and therefore the per-class skill counts
and tree depth) are **data-driven** and **sample-pending**. The `.do` page format itself is described
where the stance/skill-list pipeline is owned (`specs/skills.md` / `specs/skinning.md`); it is not
re-derived here.

---

## 8. What this spec does NOT assert (RUNTIME-ONLY residue)

- **The inner field split of the 12-byte learn entry** (`2/145` body) — skill-id / rank / flag
  byte assignment — is **capture/debugger-pending**.
- **Any learn cost** (gold or level) is **server-arbitrated** — there is no client cost field.
- **Per-class skill counts and exact tree depth** are **sample-pending** (they require parsing a real
  `skills.scr` + `.do` sample).
- **Whether any unread base-record byte encodes a learn cost or an innate flag** is **UNVERIFIED**.

---

## 9. Cross-reference map

| This spec covers | Owned elsewhere — cite, don't duplicate |
|---|---|
| The tree structure: GlobalCategory page, PrerequisiteSkillId[3] edge, TierByte rank chain (§1–§2) | `specs/skills.md` (full record offset table + cast pipeline), `formats/scr.md` (on-disk `skills.scr` format) |
| The learn opcode `2/145` and the hotbar-bind opcode `2/41` (§4) | `opcodes.md` (catalogue of record), `packets/` (field specs) |
| The class enum {1 Musa, 2 Salsu, 3 Dosa, 4 Monk} (§7) | `specs/character_creation.md`, `specs/skinning.md` (shared `SkinClassId`) |
| The class × stance `.do`-page set that populates the visible skill list (§7) | `specs/skills.md`, `specs/skinning.md` (stance/`.do` pipeline) |
