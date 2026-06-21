<!--
verification: pets/companions/summons subsystem [confirmed-absent] — static analysis of the whole
  candidate seed set (the "creature_item" table, the "PetPanel" UI class, and the "summon" effect-id
  range) shows there is NO classic combat-pet / follower / summon-creature subsystem in this build:
  none of the three seeds resolves to a stat-bearing pet, a follower-AI loop, or a creature-spawn
  packet. The three reclassifications ARE confirmed on build 263bd994: the "PetPanel" UI slot is the
  couple/partner-pair window driven by the inbound pair-state message; creature_item.xdb is a 48-byte
  cosmetic attached-prop table (actor kind 15); the "summon" effect-ids are item-use particle ids
  delivered via the item-use-effect message; the 1-byte 2/106 tick is a cosmetic-prop keepalive.
  Field-level interpretations of the attached-prop record (the three facing-frame offset pairs being
  int vs float, the unread word, the exact pickup-branch semantics) are UNVERIFIED (static-only,
  debugger-pending).
ida_anchor: 263bd994
ida_reverified: 2026-06-20
evidence: [static-ida]
sample_verified: false
note: |
  IDB SHA 263bd994, CYCLE 7 (2026-06-20) — NEW spec. Anti-phantom record: there is no pet/summon
  gameplay subsystem. The doc reclassifies the three things that LOOK like a pet system (a "PetPanel"
  UI class, a "creature_item" data table, a "summon" effect-id range) to what they actually are, so no
  engineer builds a follower/summon feature that does not exist in the original.
-->

# Pets / Companions / Summons — CONFIRMED-ABSENT — Clean-Room Specification

> Neutral, rewritten specification promoted from dirty-room analyst notes under **EU Software
> Directive 2009/24/EC Art. 6** (decompilation permitted solely to achieve interoperability). It
> contains **no decompiler output, no pseudo-code, no legacy symbol names, and no binary virtual
> addresses**.
>
> **Spec path (cite this):** `// spec: Docs/RE/specs/pets.md`

---

## 0. Headline finding — DO NOT build a pet/summon system

**There is NO creature-pet, companion-follower, or summon-creature subsystem in this client.**
*([confirmed-absent]* via static analysis, CYCLE 7.)*

This spec exists to **stop** an engineer from implementing a phantom feature. Three things in the
binary carry pet-like or summon-like names, and each one, traced to its actual behaviour, is something
else entirely:

- there is **no pet/companion stat block**, no follower-AI loop, no feed/rename/level-up of a creature;
- there is **no `SmsgPetSpawn` / `SmsgPetDespawn` / `SmsgPetStat` opcode** and no creature-summon
  packet of any kind;
- there is **no mapping from any id into a "creature/pet" table** that would spawn a combat companion.

Do not port pet inventory, pet AI, pet combat, summon-creature casting, or a creature-companion HUD.
None of it exists in the original. The four sections below name exactly what the misleading seeds
**actually** are.

---

## 1. The "PetPanel" UI slot is the COUPLE / PARTNER window

The in-world UI registers a panel under a legacy developer class name that reads as "PetPanel"
(MainHud panel **slot 52**). Despite the name, **this panel is the couple / partner-pair window** of
the relationship system — it has nothing to do with pets. *([confirmed]* the slot index and the panel's
true role.)*

What the panel actually does:

- It **binds to a paired player actor** (an ordinary player actor — **actor kind 2**), not to a
  creature.
- It displays the **partner's name and level** and **both partners' HP** as two percentage bars
  (self HP bar and partner HP bar, each formatted as a percentage).
- Its action buttons select a **couple-relation behaviour mode** (a small set of pairing modes); they
  are **not** summon / dismiss / feed / rename controls. Pressing a mode button emits the couple
  behaviour command **C2S `2/60`** (an 8-byte body carrying the chosen mode and the partner key).
- It is populated and shown by the **inbound `5/53`** message (see §1.1). The same handler that drives
  the panel also raises the localized **couple/marriage notice messages** on relation set/clear.

> **Anti-phantom note.** The class name is the original developers' internal name and is misleading.
> The feature is the **couple/partner window**, and it belongs to the **relationship / social
> system**. See `social.md` for the relationship/partner behaviour that truly owns this window.

### 1.1 Inbound pair-state — `5/53`

The server pushes **S2C `5/53`** (a 32-byte block) to update an actor's vitals **and** its
couple/pair-relation state. On a relation set/clear it raises the couple notice text and refreshes the
couple panel of §1, binding the partner actor into it. *([confirmed]* the opcode, the 32-byte size, and
that it carries both actor vitals and pair-relation state.)* The opcode identity is catalogued in
`opcodes.md` and its field shape in `packets/`; this spec does not duplicate them.

---

## 2. `creature_item.xdb` is a COSMETIC attached-prop table (actor kind 15)

`creature_item.xdb` is a fixed-record data table loaded at boot. Each record is **48 bytes**, and the
table is indexed by a key into a lookup map. *([confirmed]* the 48-byte record size and the keyed
lookup.)*

Its purpose is **purely cosmetic**: a record describes a **visual prop attached to an owning actor** —
a small decorative companion-ornament that is placed near the owner and follows the owner's facing. The
attached prop is spawned **locally** as an actor of **kind 15** (the attached-prop class); it carries
**no stats, no combat behaviour, and no follower AI**. It is the closest thing in the binary to a
"creature item", and it is a **decoration**, not a pet.

Key points (all the load-bearing facts an engineer needs; the rest is cosmetic detail):

- Records are **48 bytes**, looked up by a numeric key. *([confirmed]*.)*
- A record names an **attached visual item id**; if that id is zero, **no prop is spawned**.
  *([confirmed]* the "zero ⇒ no spawn" gate.)*
- The prop is placed at one of **three facing-frame (X,Z) offset pairs** relative to the owner's
  facing, plus a visual sub-parameter fed to the spawn descriptor. *([confirmed]* that three offset
  pairs and a descriptor sub-parameter exist and drive placement; whether the offsets are stored as
  integers or floats is **UNVERIFIED** — static-only, debugger-pending.)*
- A small set of single-byte flags gate a periodic pulse/highlight cadence, two pickup-validation
  branches, and the tick/keepalive send; a 4-byte field holds the **tick interval** for that cadence.
  *([confirmed]* that these gate fields exist and govern a per-interval tick; the exact pickup-branch
  semantics are **UNVERIFIED**.)*
- One 4-byte field inside the 48-byte span is **not read** by either record consumer; its purpose is
  **UNVERIFIED**.

### 2.1 Record layout (offset table)

| offset | size | type | field (neutral) | confidence | notes |
|--------|------|------|-----------------|------------|-------|
| 0x00 | 4 | u32 | lookup key | confirmed | the map key the record is found by |
| 0x04 | 4 | u32 | attached visual item id | confirmed | becomes the kind-15 prop's item id; **zero ⇒ no spawn** |
| 0x08 | 4 | int/float | facing-frame offset, pair A — X | unverified (int vs float) | placement relative to owner facing |
| 0x0C | 4 | int/float | facing-frame offset, pair A — Z | unverified | |
| 0x10 | 4 | int/float | facing-frame offset, pair B — X | unverified | alternate placement frame |
| 0x14 | 4 | int/float | facing-frame offset, pair B — Z | unverified | |
| 0x18 | 4 | int/float | facing-frame offset, pair C — X | unverified | alternate placement frame |
| 0x1C | 4 | int/float | facing-frame offset, pair C — Z | unverified | |
| 0x20 | 4 | — | (unread by consumers) | unverified | inside the 48-byte span; not touched in spawn/tick |
| 0x24 | 4 | u32 | descriptor visual sub-parameter | confirmed | written into the spawn descriptor |
| 0x28 | 1 | u8 | pulse/highlight gate | confirmed | enables the pulse cadence |
| 0x29 | 1 | u8 | pickup-mode flag A | unverified | selects a pickup-validate branch |
| 0x2A | 1 | u8 | pickup-mode flag B | unverified | selects pickup path A vs B |
| 0x2B | 1 | u8 | tick-send gate | confirmed | gates the `2/106` keepalive send + effect branch |
| 0x2C | 4 | u32 | tick interval (ms) | confirmed | cadence for the pickup/effect tick |
| 0x30 | — | — | (end — 48 bytes) | | |

> This is a **data-table format**; the byte-layout half of it also belongs in `formats/` as a
> 48-byte record. It is recorded here so the pets spec is self-contained anti-phantom evidence.

---

## 3. The "summon" effect-ids are ITEM-USE PARTICLES (delivered via `4/139`)

A range of ids that looks like a "summon" set is **not** a summon system. Those ids are **item-use
effect ids** delivered through the generic **item-use-effect** path: the inbound **item-use FX message
`4/139`** (and a sibling actor-state effect path) map an item id to a **particle factory id** and a
**3D sound effect**. *([confirmed]* that these ids are item-use effect ids handled by the item-use FX
message, and that they resolve to particle + sound, not a creature.)*

There is **no lookup of any of these ids into a creature/pet table** and **no actor is spawned from
them** — they are pure **visual/audio effects keyed by item id**. Calling them "summon effect ids" is a
mischaracterization; they are ordinary item-use particles.

> The item-use FX delivery (`4/139` mapping an item id to a particle/sound) is owned by the item
> system. Cross-ref **`item.md`** (Lane 2, by name — the item-use-effect resolution) for that
> mapping; this spec only records that the "summon" ids fall inside it.

---

## 4. Opcode `2/106` is a COSMETIC-PROP KEEPALIVE (not a pet command)

The client emits **C2S `2/106`** as a **1-byte** message (a constant payload byte). It is a
**keepalive / "still have the attached prop" tick** for the cosmetic attached-prop of §2 — it is sent
each tick while the attached-prop tick-send gate is set and the world UI is active (a second code path
sends the same tick, suppressed under a particular map condition). *([confirmed]* the opcode, the
1-byte body, and that it is the cosmetic-prop tick.)*

It is **not** a pet command: it carries no creature id, no pet action, and no summon/dismiss request —
it is a fixed 1-byte heartbeat. The attached prop has **no despawn packet**; it is torn down **locally**
on re-spawn and on scene teardown.

The opcode identity is catalogued in `opcodes.md` and its (trivial) body in `packets/`; this spec does
not duplicate them.

---

## 5. Summary — what the seeds actually are

| Seed (looks like a pet/summon) | What it actually is | Confidence | Owned by |
|---|---|---|---|
| "PetPanel" UI slot 52 | the **couple / partner-pair window** (kind-2 partner actor; self + partner HP); mode buttons emit `2/60` | confirmed | `social.md` |
| inbound `5/53` | actor vitals + couple/pair-relation state; populates the couple panel; raises couple notices | confirmed | `opcodes.md`, `packets/`, `social.md` |
| `creature_item.xdb` (48-byte records) | a **cosmetic attached-prop table** spawning a kind-15 visual decoration | confirmed (some fields unverified) | this spec + `formats/` |
| "summon" effect-id range | **item-use particle ids** delivered via `4/139` (particle factory + 3D sound) | confirmed | `item.md` (Lane 2, by name) |
| `2/106` | a **1-byte cosmetic-prop keepalive** tick | confirmed | `opcodes.md`, `packets/` |
| any pet/summon **gameplay** subsystem | **does not exist** | confirmed-absent | — |

---

## 6. Cross-reference map

| Topic | Owned elsewhere — cite, don't duplicate |
|---|---|
| The couple/partner relationship behaviour behind the "PetPanel" window and the `2/60` mode command (§1) | `social.md` |
| The inbound pair-state `5/53` and the cosmetic-keepalive `2/106` opcode identities (§1.1, §4) | `opcodes.md` (catalogue of record), `packets/` (field specs) |
| The item-use-effect resolution that the "summon" effect-ids actually use, via `4/139` (§3) | `item.md` (Lane 2, by name — item-use FX) |
| The 48-byte `creature_item.xdb` record as a binary data-table format (§2) | `formats/` (48-byte record layout) |
