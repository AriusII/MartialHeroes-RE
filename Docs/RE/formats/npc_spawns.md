# Format: .arr  (NPC / monster spawn array)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file.

---

## Status

```
verification:   sample-verified   # stride 28, +0 id / +4 x / +8 z / +12 facing / +16 spawn_type,
                                   #   facing math, +20/+24 inert, mob.arr 20-byte stride, map000 anomaly
                                   #   all matched against the real VFS sample (43,347 entries)
ida_reverified: 2026-06-21        # re-confirmed in full on the doida.exe build (no layout corrections)
ida_anchor:     263bd994          # primary anchor; also re-verified against the active doida.exe build
evidence:       [static-ida, vfs-sample, doida.exe-reverify]
conflicts:      spawn_type enumeration is 0..11 (not {0,7}); field_02 frequently non-zero on disk
                (loader-inert verdict unaffected) — see CONFLICTS below
loader_resolved: true              # two-witness gate (IDB loader read-set + real-VFS black-box census):
                                   #   +2 / +20 / +24 confirmed inert (never consumed by the loader)
                                   #   +12 confirmed facing/orientation value; runtime applies π/2 − value (NOT +π/2)
                                   #   mob.arr (20-byte record) + mobinfo.mi: NO CLIENT LOADER (tool/editor formats)
```

> **Re-verification (active `doida.exe` build, 2026-06-21).** This spec was re-confronted against the
> currently-loaded `doida.exe` build (a different build/SHA than the pinned `263bd994` anchor) plus the
> real VFS sample. **Verdict: CONFIRMED IN FULL — no layout corrections.** Every structural and
> behavioural claim re-confirms identically: headerless 28-byte stride, sentinel slot 0, identity
> index, field offsets (+0 id / +4 x / +8 z / +12 facing / +16 spawn_type), `π/2 − value` facing math,
> the 20-byte `mob.arr` stride with no client loader, and the map-000 (16-byte) / map-207 (240-byte)
> anomalies. Three non-corrective refinements were folded in: the area-binary load coupling (the
> `.arr` is the LAST of four area binaries opened on area-enter — see §Loading sequence), the exact
> elite-modifier-A predicate (see §`spawn_type`), and this build added to the evidence anchor line.

> **CORRECTED CYCLE 1 (ida_anchor 263bd994, evidence [static-ida]):** the `.arr` is **NOT the
> live-actor source.** Live actors arrive via the **server area-entity snapshot** (a network packet)
> → an 880-byte spawn descriptor → the actor-spawn routine; the `.arr` supplies **position / facing /
> static metadata ONLY** (its facing field feeds the actor yaw as `π/2 − value`), and the actor's
> visual id resolves from the mob/npc id through the `mobs.scr` / `npc.scr` template maps + the
> boot-loaded character-table catalogue — not from the disk record. An **offline port** (no server)
> must synthesize actors from `.arr` + the visual catalogue. Ground-Y is **re-sampled from terrain
> every frame** (sentinel when the cell is not yet streamed; a later frame re-snaps). See §Runtime
> role below. [2026-06-19]

> **Two-witness re-verification (build 263bd994, 2026-06-16).** This spec was re-confronted against
> THIS build's loader (witness 1 — the area-binary loader read-set + every field consumer) AND the
> real VFS sample (witness 2 — all 58 `npc*.arr` + 52 `mob*.arr` entries read straight from the
> archive, 559 live 28-byte records censused). Every **structural** claim re-confirmed: record size 28,
> headerless, sentinel slot 0, identity index, field offsets (+0 id / +4 x / +8 z / +12 facing /
> +16 spawn_type), facing math `π/2 − value`, the 20-byte `mob.arr` stride, and the no-mob-loader
> verdict. Two **drifts** surfaced — the `spawn_type` enumeration is wider than previously documented
> ({0..11}, not {0,7}), and `field_02` is non-zero on disk in the majority of records (its loader-inert
> verdict is unaffected). A second non-multiple-of-28 anomaly was found (`map207/npc207.arr`, 240 bytes).
> The earlier "UNVERIFIED / PARTIAL" tags for `+2`, `+12`, `+20`, and `+24` remain superseded by the
> per-field notes below. The findings here are described as layout and runtime behaviour only.

---

## Identification

- **Extension:** `.arr`
- **Found in:** `.pak` VFS archive (logical path pattern: `data/map<NNN>/npc<NNN>.arr`, where `<NNN>` is the zero-padded three-digit map number)
- **Magic / signature:** none — the file has no header and no magic bytes
- **Version field:** none
- **Endianness:** little-endian throughout (x86 client)
- **Record size:** 28 bytes (0x1C) — confirmed

---

## Container structure

The `.arr` file is a **headerless flat array** of fixed-size 28-byte records. There is no file-level magic, version field, record count, or any other header. The loading runtime determines the record count by integer division:

```
record_count = floor(file_size / 28)
```

Any trailing bytes that do not fill a complete 28-byte record are ignored.

At load time the runtime prepends one **sentinel null-record** (all bytes zero) at in-memory slot 0; the file records are placed starting at slot 1. This means all valid record indices are ≥ 1; a lookup result of slot 0 or a null pointer indicates "not found."

---

## Runtime role — the `.arr` is NOT the live-actor source (CYCLE 1)

**Confidence: CODE-CONFIRMED.** This is a major correction that kills a wrong assumption ("iterate
`npc<NNN>.arr` to spawn the world's actors").

### The live-actor path is the SERVER snapshot

The shipped client does **not** iterate `npc<NNN>.arr` to build actors. Live actors arrive from the
**server**: an **area-entity snapshot** network message is parsed **per record** into an **880-byte
spawn descriptor** on the stack, and that descriptor is handed to the **actor-spawn routine**, which
constructs the actor and resolves its visual (skin / skeleton / motion). So the live-actor chain is:

```
server area-entity snapshot (network)  →  880-byte spawn descriptor  →  actor-spawn routine  →  actor + visual
```

### What the disk `.arr` actually supplies

The disk spawn array is consumed only as a **small lookup table** for position / facing / static
metadata. The **single** place the disk array reaches actor construction is the **facing field**: in
the area-snapshot handler, for the **npc record-kind**, the handler fetches the matching disk record
by index and feeds its facing float into the actor's yaw as **`π/2 − facing`**. That is the **only**
actor-construction consumer of the disk array; the remaining disk-array consumers are id / region
lookups and tools. The actor's **visual id** (which skin / skeleton / motion) is resolved from the
**mob/npc id** through the `mobs.scr` / `npc.scr` template maps and then the **boot-loaded
character-table catalogue** (actormotion / skin / bind / mot) — **not** from the disk record.

### Offline-port consequence (faithful behaviour)

Because the real client gets its live actors from the server snapshot, a port running **offline**
(no server) has **no snapshot** to drive spawns. The faithful offline behaviour is to **synthesize
actors from the `.arr`** records (position / facing / static metadata) combined with the **visual
catalogue** (resolve the visual id from the mob/npc id the same way the server path would). State of
play:

- **With a server:** the area-entity snapshot is authoritative; the `.arr` contributes **facing only**
  (npc kind), the rest of the descriptor coming from the network.
- **Offline (no server):** the `.arr` becomes the **fallback spawn source** — the only way to populate
  the world with actors — paired with the visual catalogue for appearance. This is a port-side
  accommodation, not original client behaviour, and applies only when there is no snapshot.

### Ground-Y is re-sampled from terrain every frame

The actor's Y is resolved by **sampling the terrain manager at the actor's X/Z**, and this re-snap
happens **repeatedly** — at every position-set, at every motion-apply, and per-frame for the local
player — **not once at spawn**. When the actor's terrain cell is **not yet resident** in the loaded
streaming window, the sampler returns a **sentinel / false** and the Y is simply **not applied** that
frame; a later frame fixes it once the cell streams in. This is the mechanism behind the known port
debt *"NPCs spawn at a fallback Y before async terrain finishes loading."* Whether the actor's cell is
resident at the **instant the snapshot constructs the actor** depends on the area's terrain-cell
**streaming order relative to inbound-packet processing** — a runtime property **not settleable
statically**; the original masks it via the repeated per-frame re-snap. **The sample-or-sentinel +
repeated re-snap mechanism is CONFIRMED; the spawn-vs-cell-load timing is OPEN / debugger-pending.**

---

## Record layout — 28 bytes per record

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| 0 | 2 | u16 | `mob_id` | NPC / mob template identifier; primary key used to resolve the mob template from `data/script/mobs.scr` (or `data/script/npc.scr`), and through it the actor's visual id (see §Runtime role and cross-references). Decodes as a plausible u16 in all 559 sampled records (distinct ids observed in the 10..10458 range). | sample-verified |
| 2 | 2 | u16 | `field_02` | **INERT — present but unconsumed.** The spawn loader never reads this offset as a distinct field; no runtime consumer accesses it. It is retained in the on-disk record for stride/layout purposes only. A parser must read past it but must not attach behaviour to it. (Earlier mob-level / sub-type / faction guesses are withdrawn — they were never confirmed and the field is established as unconsumed.) **Drift note:** despite being loader-inert, this slot is **non-zero on disk in the majority of records (409 of 559; 52 of 56 files)** — it actively carries content-tool data. The loader-inert verdict is unaffected (control-flow confirms no consumer reads `+2`); only the prior impression that the slot is near-empty is corrected. | sample-verified inert |
| 4 | 4 | f32 | `world_x` | World-space X coordinate (horizontal plane). Decodes as a plausible f32 in all 559 sampled records. | sample-verified |
| 8 | 4 | f32 | `world_z` | World-space Z coordinate (horizontal plane). Y (height) is not stored; the terrain system snaps spawned entities to ground level, re-sampled every frame (see §Runtime role). Decodes as a plausible f32 in all 559 sampled records. | sample-verified |
| 12 | 4 | f32 | `facing` | Facing / orientation value (radians). **The runtime applies `π/2 − stored_value` as the entity's facing** — the stored value is *subtracted from* a quarter-turn, NOT added to it. This is the one disk field that reaches live actor construction (the area-snapshot handler feeds it into the npc actor's yaw quaternion). The on-disk number is therefore a base orientation that the runtime reflects through a fixed quarter-turn at use time; do not apply the value raw and do not simply add π/2. (This supersedes the earlier sample-only "yaw clusters near π/2" reading and the earlier "+π/2" phrasing: the runtime operation is exactly `π/2 − value`, confirmed against the snapshot/spawn consume site, which then builds a yaw quaternion from the result.) **Sampled range:** the observed disk values span `[−1.5708 .. ≈6.20]` rad — the floor is exactly `−π/2`, consistent with a stored base-orientation the runtime reflects via `π/2 − value`. | sample-verified |
| 16 | 4 | u32 | `spawn_type` | Spawn-group link ID and spawn-type modifier. The value **7** is treated specially by the spawn-matching code: it triggers an elite / boss modifier branch (returns a 10 % bonus multiplier — 110 vs. baseline 100 — under an additional area/state guard in one site, and an additive elite bonus in another). This field is also compared against an actor's own spawn-group ID during in-scene lookup. **Enumeration drift (CONFLICT C1):** the on-disk value is NOT limited to `{0, 7}` — the real sample shows **at least 12 distinct values spanning `{0..11}`**. Value 7 is the loader-special elite/boss value and is common, but it is one of many spawn-group/type ids, not the only non-zero value. The offset/type/role re-confirm; only the prior observed-values enumeration was too narrow. | confirmed (offset/role); sample-verified (enumeration) |
| 20 | 4 | u32 | `field_20` | **INERT — present but unconsumed.** The spawn loader never reads this offset; no runtime consumer accesses it. Retained for stride only. (Earlier respawn-delay / max-live-count guesses are withdrawn.) Non-zero in only 1 of 559 sampled records (effectively always zero). | sample-verified inert |
| 24 | 4 | u32 | `field_24` | **INERT — present but unconsumed.** The spawn loader never reads this offset; no runtime consumer accesses it. Retained for stride only. (Earlier spawn-radius / group-size / padding guesses are withdrawn.) Zero in all 559 sampled records. | sample-verified inert |

**Total record size: 28 bytes (0x1C). Stride is two-witness confirmed (loader read-loop advances 28 bytes per record; 56 of 58 sampled `npc*.arr` files are exact multiples of 28).**

> **Inert fields (`+2`, `+20`, `+24`).** These three offsets are **present in every record but
> consumed by no part of the shipped client.** They carry whatever the content tool wrote, but the
> spawn loader steps over them. Treat them as opaque, ignored slots: read them to advance the
> stride, never branch on them. This is a two-witness result (loader read-set establishes no consumer
> touches them; the real-VFS census confirms the layout). Note the value distributions are NOT the
> basis of the inert verdict — `+2` is in fact non-zero in most records (content-tool data), while
> `+20` is non-zero in 1 of 559 and `+24` is zero in all 559; the verdict rests on control-flow, not
> on the slot happening to be zero.

> **Facing field (`+12`).** The stored radian value is a **base orientation**; the runtime applies
> **`π/2 − stored_value`** as the entity's facing (the value is subtracted from a quarter-turn, NOT
> added) and builds a yaw quaternion from the result. This is the only disk field that reaches live
> actor construction (npc kind, via the area-snapshot handler — see §Runtime role). A faithful port
> must compute `π/2 − file_value` at use time to match the original's on-screen facing — simply adding
> π/2 would mirror the orientation. This is a two-witness result; the sampled disk range is
> `[−1.5708 .. ≈6.20]` rad (floor = exactly `−π/2`), consistent with that reflect-through-a-quarter-turn
> convention.

---

## Record-count derivation

- **Record count source:** derived from file size — `floor(file_size / 28)`. There is no count stored in the file.
- **Record stride:** 28 bytes (confirmed by parser iteration and direct field access patterns).

---

## In-memory layout after load

The runtime allocates `(record_count + 1) × 28` bytes, zeroes the allocation, then reads the file records into slots 1 through `record_count`:

```
slot 0 : 28 bytes, all zero  (sentinel — always null/empty; never from file)
slot 1 : first record from file
slot 2 : second record from file
...
slot N : N-th record from file
```

A parallel index array of `(record_count + 1) × 4` bytes is allocated alongside. Each slot's index entry is set to its own slot number (identity mapping) upon successful file read. (This array is the lookup table the area-snapshot handler indexes for the npc facing override — see §Runtime role.)

---

## Loading sequence — coupled to area-enter (not an independent asset open)

The `.arr` is **not** loaded on demand as a standalone asset. It is opened as the **last** of **four
area binaries** read together by the area-load routine each time the player enters an area; the loader
first frees the previous area's geometry and spawn array, then opens and reads the four binaries in a
fixed order through the VFS file wrappers (it reads through the archive, never a raw OS file):

| Order | Logical path pattern | Read shape |
|------:|----------------------|------------|
| 1 | `data/map<NNN>/map<NNN>.bin` | fixed-size map header |
| 2 | `data/map<NNN>/regiontable<NNN>.bin` | fixed-size region table |
| 3 | `data/map<NNN>/region<NNN>.bin` | grid (width × height) + origin X / origin Z |
| 4 | `data/map<NNN>/npc<NNN>.arr` | **the spawn array (this format)** |

`<NNN>` is the same zero-padded three-digit map number in every path (driven by the current area-id
byte). The spawn-array stage performs the headerless read described in §In-memory layout after load.
The alternative path literals `tool/npc/npc<NNN>.arr` and `tool/mob/mob<NNN>.arr` exist in the binary's
path table but have **no runtime cross-reference** — confirming the "tool / editor format" verdict for
the `tool/*` and `mob*.arr` variants (no shipped-client loader reads them).

If the spawn file is missing or unreadable the loader reports a file open/read error and the area load
fails gracefully (returns a failure code) without crashing. On area-leave the same subsystem frees the
spawn array and its index array and zeroes the count, so each area-enter starts from a clean slot 0
sentinel.

The other three binaries are siblings loaded at the same area-load stage but are **separate formats**
(map / region geometry), not described here.

---

## Enumerations / flags

### `spawn_type` known values

The field is a `u32` at `+16` carrying a spawn-group link id and a spawn-type modifier. The real-VFS
sample (559 live records) shows **at least 12 distinct values spanning `{0..11}`** — the prior
`{0, 7}` enumeration was too narrow (CONFLICT C1). Only value **7** is given special treatment by the
loader/consumer code; all other values act as ordinary spawn-group/type ids.

| Value | Meaning |
|------:|---------|
| 0 | Standard spawn — no modifier (most common value in the sample) |
| 1–6, 8–11 | Ordinary spawn-group / spawn-type ids — no special loader branch; matched against an actor's own spawn-group id during in-scene lookup |
| 7 | Elite / boss modifier — the spawn-matching code returns a 10 % bonus multiplier (110 vs. baseline 100) at one site and an additive elite bonus at another. Common in the sample (second-most-frequent value after 0). |

**Exact elite-modifier predicate (refinement).** The 110-vs-100 bonus site fires only under the
full guard **`spawn_type == 7` AND current-area-id == 1 AND an area-state flag == 12** — i.e. the
10 % bonus is gated to a specific area in a specific state, not applied to every `spawn_type == 7`
record everywhere. The second site applies its additive elite bonus more directly. A third consumer
matches `spawn_type` (`+16`) against a live actor's own spawn-group field during in-scene lookup
(the spawn-group-link use of the field). A faithful port that reproduces the elite bonus must
honour the full area/state predicate, not just the `== 7` test.

Values above 11 are unobserved in the sampled archive but are not ruled out; the field is a full
`u32` and the runtime imposes no documented upper bound. A faithful port must NOT special-case any
value other than 7.

---

## Companion formats with NO client loader — `mob.arr` and `mobinfo.mi`

Two formats that sit alongside the spawn data are **never parsed by the shipped client** and must be
treated as **tool / editor formats**, not runtime asset formats:

- **`mob.arr` (the 20-byte spawn record).** A second spawn-array shape exists on disk as fixed
  20-byte records (`mob<NNN>.arr`). The two-witness pass established that **no loader in the shipped
  client reads this 20-byte format** — the only `mob…arr` path literal in the binary is the
  unreferenced `tool/mob/mob%s.arr` (no code cross-reference); there is no `data/map%s/mob%s.arr`
  runtime string and no runtime mob.arr loader. At runtime the client sources its mob data through
  `data/script/mobs.scr` instead, routing `mob_id` lookups through a template-map lookup (and through
  it the actor's visual id — see §Runtime role). The 20-byte `mob.arr` files are therefore
  content-tool / editor artefacts. The **20-byte stride is itself sample-verified** — all 52
  `mob*.arr` entries on disk are exact multiples of 20 (a 28-byte stride leaves non-zero remainders).
  A faithful port should **not** implement a runtime parser for them; their field layout is out of
  client scope and remains capture/debugger-pending (no client loader exists to read the field
  semantics).

- **`mobinfo.mi`.** Likewise has **no client loader** — see `Docs/RE/formats/mi.md`. It is a
  tool / editor format only.

Because neither format is consumed by the shipped client, their field semantics cannot be recovered
from the client and are **out-of-client-scope / DBG-pending** (see `mi.md` for `mobinfo.mi`).

---

## Anomalies: non-multiple-of-28 `npc*.arr` files

Of the 58 sampled `npc*.arr` entries, **56 are exact multiples of 28** (559 records total). Two files
break the 28-byte stride and are documented here as anomalies only — neither is a supported runtime
format.

### Anomaly A — map 000 (16-byte file)

The spawn file for map 000 (`data/map000/npc000.arr`) is **16 bytes**, which is **not a multiple of 28**.
The runtime derives a record count of `floor(16 / 28) = 0` and reads zero records from disk; map 000
therefore has no runtime mob spawn data.

The 16-byte content closely resembles the first 16 bytes of a standard 28-byte record (fields `mob_id`
at +0, `field_02` at +2, `world_x` at +4, `world_z` at +8 — the sampled file shows id ≈ 100,
`world_x` ≈ 512, `world_z` ≈ −9392, then a 4-byte word at +12), suggesting one of:

- An **editor / tool format** variant (the alternative path `tool/npc/npc<NNN>.arr` exists in the VFS
  path table but has no observed references in the runtime client code).
- A **truncated or placeholder** file left over from content authoring.

The field interpretation of the 16-byte content is UNVERIFIED; no tool-side loader was identified.

### Anomaly B — map 207 (240-byte file, mob-shaped under an npc name)

The spawn file for map 207 (`data/map207/npc207.arr`) is **240 bytes**. This is **not a multiple of 28**
(`240 mod 28 = 16`) but **is an exact multiple of 20** (`240 = 12 × 20`). The byte length is precisely
that of **twelve 20-byte (`mob.arr`-shaped) records**, strongly suggesting a **mob-shaped file
mis-placed under an `npc` name**, or a content-tool artefact.

Run through the runtime npc loader this file is degenerate: the size-derived count is
`floor(240 / 28) = 8` (the loader adds its own sentinel slot on top), and the fixed-28 read loop walks
records that do not align with the on-disk 20-byte layout — i.e. map 207's npc-spawn read yields a
partial / mis-aligned record set rather than meaningful spawns. A faithful port should treat
`npc207.arr` as anomalous data and must not assume it parses cleanly under the 28-byte record layout.

> **Corpus-scale fact (for test fixtures).** The whole client carries **56 div-28 `npc*.arr` files,
> 559 total 28-byte records**, plus the two anomalies above; and **52 `mob*.arr` files** (all 20-byte
> stride). These are useful scale figures when building reimplementation test fixtures.

---

## Cross-references

### mobs.scr / npc.scr linkage and the visual catalogue

The `mob_id` field is the primary key that links each spawn record to the mob / NPC template database stored in `data/script/mobs.scr` (or `data/script/npc.scr`). The runtime passes `mob_id` to a lookup function that returns a template struct pointer; from that struct the client reads:

- A 17-byte name string at a known struct offset — CP949 / EUC-KR encoded.
- A 17-byte map-class string at a separate struct offset — CP949 / EUC-KR encoded.
- Combat stats and display parameters at further offsets.
- The **visual catalogue id** used to select the actor's skin / skeleton / idle motion (the visual id
  is resolved from the mob/npc id via these template maps, then through the boot-loaded
  character-table catalogue — see §Runtime role; the disk `.arr` record does NOT carry the visual id).

The `mobs.scr` format is a separate analysis target (see `Docs/RE/formats/config_tables.md`).
Note that `mobs.scr` — **not** the on-disk `mob.arr` — is the client's actual runtime source of mob data.

### Coordinate system

World coordinates use X (east/west) and Z (north/south) as the horizontal plane; Y (vertical) is absent from spawn records and is resolved at runtime by the terrain system (re-sampled every frame — see §Runtime role). Observed coordinate magnitudes (approximately 25 000–65 000 world units) are consistent with a world extent of approximately 65 536 × 65 536 units, common for client-server MMORPGs of this generation.

### Related formats

| Format | File | Relationship |
|--------|------|--------------|
| `config_tables.md` | `data/script/mobs.scr` | Template database resolved by `mob_id`; the client's actual runtime mob-data source; supplies the actor visual id |
| `mi.md` | `data/ui/mobinfo.mi` | Companion mob-info file — NO client loader (tool/editor format) |
| `terrain.md` | `data/map<NNN>/*.ted` etc. | Y-coordinate (ground snap) source for spawned entities; sampled every frame |
| `world_systems.md` | — | The area-entity snapshot → spawn-descriptor → actor-spawn wiring (the live-actor path) |
| `pak.md` | `data.inf` / `data/data.vfs` | VFS container that holds `.arr` files |

- **Glossary:** see `Docs/RE/names.yaml`
- **Provenance:** see `Docs/RE/journal.md`

---

## Known unknowns

The following fields and questions remain unresolved and must not be guessed at during implementation:

1. **`mob_id` vs. `npc_id` scope** — The same `.arr` format and `mob_id` field appear to be used for both NPC (non-combat) and monster spawns. Whether a flag elsewhere distinguishes the two populations is unknown. (Note: it is **not** an inert field — `+2`, `+20`, `+24` are confirmed unconsumed.)
2. **`spawn_type` semantics beyond 7** — The on-disk enumeration is now known to span at least `{0..11}` (sample-verified), and value 7 is the loader-special elite/boss value. The per-value *meaning* of the other spawn-group/type ids (1–6, 8–11) — and whether values above 11 are valid — is not recovered from the client and remains capture/debugger-pending. The offset, type, and elite-7 role are settled.
3. **16-byte tool variant (map 000)** — Whether a tool-side reader exists, and the precise field layout of the 16-byte record, remains unverified. No tool loader was identified in the shipped client.
4. **240-byte mob-shaped variant (map 207)** — `npc207.arr` (240 bytes = 12 × 20) is a 20-byte-stride file under an npc name; the intended interpretation (mis-named mob file vs. tool artefact) and its field layout are unverified.
5. **`mob.arr` (20-byte) and `mobinfo.mi` field semantics** — Out-of-client-scope: the shipped client has no loader for either, so their field meanings cannot be recovered from the client and are capture/debugger-pending (likely permanent). The 20-byte `mob.arr` stride is itself sample-verified; only the field layout is unrecoverable. See `mi.md`.
6. **Spawn-vs-cell-load timing (Y re-snap, CYCLE 1)** — whether the actor's terrain cell is resident at the instant the server snapshot constructs the actor is a runtime property not settleable statically; the original masks any miss via the per-frame re-snap. Confirming the exact timing on a real area-enter is debugger-pending (the sample-or-sentinel + repeated re-snap mechanism itself is CONFIRMED — see §Runtime role).

> The previously-listed unknowns for `field_02` (+2), `rotation_y`/`facing` (+12), `field_20` (+20),
> and `field_24` (+24) are now **resolved** by the two-witness pass: `+2`, `+20`, `+24` are confirmed
> inert; `+12` is confirmed a facing value the runtime applies as `π/2 − value`. They are no longer
> open questions.

---

## Open conflicts (carried explicitly — not force-resolved)

- **C1 — `spawn_type` enumeration coverage.** The prior spec recorded only `{0, 7}` ("both examined
  28-byte samples contain 0"). The real archive (this build) shows **12 distinct values `{0..11}`**.
  Resolution applied: the field's **offset/type/role** (u32 at +16, elite-modifier value 7, spawn-group
  link) re-confirm and are NOT in conflict; only the *observed-values enumeration* was too narrow and
  has been widened to "at least 0..11; 7 is the loader-special elite/boss value." This is an
  enumeration-coverage conflict, not a layout conflict.
- **C2 — `field_02` (+2) value characterization.** The prior spec implied the inert `+2` slot is
  unremarkable; the sample shows it non-zero in the majority of records (409 of 559). The **loader-inert**
  finding is NOT in conflict (control-flow confirms no consumer reads `+2`); only the implied
  "near-empty" value impression drifted and has been corrected in the field note. No layout conflict.

No conflict exists on any **structural** claim: record size 28, headerless, sentinel slot 0, identity
index, field offsets (+0 id / +4 x / +8 z / +12 facing / +16 spawn_type), the facing math `π/2 − value`,
the 20-byte `mob.arr` stride, and the no-mob-loader verdict all re-confirm against build 263bd994 plus
the real VFS sample. The CYCLE 1 runtime-role correction (server snapshot is the live-actor source;
`.arr` is facing/metadata only) is a runtime-wiring clarification, not a layout change.
