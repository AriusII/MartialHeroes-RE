# Format: .arr  (NPC / monster spawn array)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file.

---

## Status

```
sample_verified: true   # stride, mob_id, world_x, world_z, spawn_type confirmed against real bytes
loader_resolved: true   # CAMPAIGN VFS-MASTERY two-witness gate (loader read-set + black-box runtime):
                        #   +2 / +20 / +24 CONFIRMED inert (never consumed by the loader)
                        #   +12 CONFIRMED facing/orientation value; runtime adds a quarter-turn (π/2) on use
                        #   mob.arr (20-byte record) + mobinfo.mi: NO CLIENT LOADER (tool/editor formats)
```

> **CAMPAIGN VFS-MASTERY note (two-witness: loader read-set + black-box runtime).** A reconciliation
> pass that compared the spawn loader's actual read-set against runtime black-box behaviour settled
> the previously-open `npc.arr` fields. The earlier "UNVERIFIED / PARTIAL" tags for `+2`, `+12`,
> `+20`, and `+24` are superseded by the per-field notes below. The findings here are described as
> layout and runtime behaviour only.

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

## Record layout — 28 bytes per record

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| 0 | 2 | u16 | `mob_id` | NPC / mob template identifier; primary key used to resolve the mob template from `data/script/mobs.scr` (or `data/script/npc.scr`). See cross-references. | CONFIRMED |
| 2 | 2 | u16 | `field_02` | **INERT — present but unconsumed.** The spawn loader never reads this offset as a distinct field; no runtime consumer accesses it. It is retained in the on-disk record for stride/layout purposes only. A parser must read past it but must not attach behaviour to it. (Earlier mob-level / sub-type / faction guesses are withdrawn — they were never confirmed and the field is now established as unconsumed.) | CONFIRMED inert |
| 4 | 4 | f32 | `world_x` | World-space X coordinate (horizontal plane). | CONFIRMED |
| 8 | 4 | f32 | `world_z` | World-space Z coordinate (horizontal plane). Y (height) is not stored; the terrain system snaps spawned entities to ground level. | CONFIRMED |
| 12 | 4 | f32 | `facing` | Facing / orientation value (radians). **The runtime adds a quarter-turn (π/2) to this stored value before using it** — i.e. the applied facing is `stored_value + π/2`. The on-disk number is therefore a base orientation that the runtime rotates by a fixed quarter-turn at use time; do not apply the value raw. (This supersedes the earlier sample-only "yaw clusters near π/2" reading: the π/2 is a runtime-added rotation, not a coincidence in the data.) | CONFIRMED |
| 16 | 4 | u32 | `spawn_type` | Spawn-group link ID and spawn-type modifier. The value 7 is treated specially by the spawn-matching code: it triggers an elite / boss modifier branch (returns a 10 % bonus multiplier rather than the baseline). This field is also compared against an actor's own spawn-group ID during in-scene lookup. Both examined 28-byte samples contain 0 (standard spawn, no modifier). | CONFIRMED |
| 20 | 4 | u32 | `field_20` | **INERT — present but unconsumed.** The spawn loader never reads this offset; no runtime consumer accesses it. Retained for stride only. (Earlier respawn-delay / max-live-count guesses are withdrawn.) | CONFIRMED inert |
| 24 | 4 | u32 | `field_24` | **INERT — present but unconsumed.** The spawn loader never reads this offset; no runtime consumer accesses it. Retained for stride only. (Earlier spawn-radius / group-size / padding guesses are withdrawn.) | CONFIRMED inert |

**Total record size: 28 bytes (0x1C). Stride is confirmed by parser iteration.**

> **Inert fields (`+2`, `+20`, `+24`).** These three offsets are **present in every record but
> consumed by no part of the shipped client.** They carry whatever the content tool wrote, but the
> spawn loader steps over them. Treat them as opaque, ignored slots: read them to advance the
> stride, never branch on them. This is a CONFIRMED two-witness result (loader read-set + runtime),
> not a "value happens to be zero in samples" inference.

> **Facing field (`+12`).** The stored radian value is a **base orientation**; the runtime applies
> a fixed **quarter-turn (π/2) rotation on top of it** before facing the entity. A faithful port must
> add π/2 to the file value at use time to match the original's on-screen facing.

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

A parallel index array of `(record_count + 1) × 4` bytes is allocated alongside. Each slot's index entry is set to its own slot number (identity mapping) upon successful file read.

---

## Enumerations / flags

### `spawn_type` known values

| Value | Meaning |
|------:|---------|
| 0 | Standard spawn — no modifier |
| 7 | Elite / boss modifier — spawn-matching code returns a 10 % bonus multiplier (value 110 vs. baseline 100) for stats or rewards |

All other values are unobserved in available samples.

---

## Companion formats with NO client loader — `mob.arr` and `mobinfo.mi`

Two formats that sit alongside the spawn data are **never parsed by the shipped client** and must be
treated as **tool / editor formats**, not runtime asset formats:

- **`mob.arr` (the 20-byte spawn record).** A second spawn-array shape exists on disk as fixed
  20-byte records (`mob<NNN>.arr`). The CAMPAIGN VFS-MASTERY two-witness pass established that **no
  loader in the shipped client reads this 20-byte format.** At runtime the client sources its mob
  data through `data/script/mobs.scr` instead. The 20-byte `mob.arr` files are therefore content-tool
  / editor artefacts. A faithful port should **not** implement a runtime parser for them; their field
  layout is out of client scope.

- **`mobinfo.mi`.** Likewise has **no client loader** — see `Docs/RE/formats/mi.md`. It is a
  tool / editor format only.

Because neither format is consumed by the shipped client, their field semantics cannot be recovered
from the client and are **out-of-client-scope / DBG-pending** (see `mi.md` for `mobinfo.mi`).

---

## Anomaly: map 000 file (16-byte variant)

The spawn file for map 000 (`data/map000/npc000.arr`) is 16 bytes, which is **not a multiple of 28**. The runtime computes `floor(16 / 28) = 0` and reads zero records from disk; map 000 therefore has no runtime mob spawn data.

The 16-byte content closely resembles the first 16 bytes of a standard 28-byte record (fields `mob_id`, `field_02`, `world_x`, `world_z`), suggesting one of:

- An **editor / tool format** variant (an alternative path `tool/npc/npc<NNN>.arr` exists in the VFS path table but has no observed references in the runtime client code).
- A **truncated or placeholder** file left over from content authoring.

This 16-byte layout is **not a supported runtime format**. It is documented here as an anomaly only. No tool-side loader code was identified; the field interpretation of the 16-byte content is entirely UNVERIFIED.

---

## Cross-references

### mobs.scr linkage

The `mob_id` field is the primary key that links each spawn record to the mob / NPC template database stored in `data/script/mobs.scr` (or `data/script/npc.scr`). The runtime passes `mob_id` to a lookup function that returns a template struct pointer; from that struct the client reads:

- A 17-byte name string at a known struct offset — CP949 / EUC-KR encoded.
- A 17-byte map-class string at a separate struct offset — CP949 / EUC-KR encoded.
- Combat stats and display parameters at further offsets.

The `mobs.scr` format is a separate analysis target (see `Docs/RE/formats/config_tables.md`).
Note that `mobs.scr` — **not** the on-disk `mob.arr` — is the client's actual runtime source of mob data.

### Coordinate system

World coordinates use X (east/west) and Z (north/south) as the horizontal plane; Y (vertical) is absent from spawn records and is resolved at runtime by the terrain system. Observed coordinate magnitudes (approximately 25 000–65 000 world units) are consistent with a world extent of approximately 65 536 × 65 536 units, common for client-server MMORPGs of this generation.

### Related formats

| Format | File | Relationship |
|--------|------|--------------|
| `config_tables.md` | `data/script/mobs.scr` | Template database resolved by `mob_id`; the client's actual runtime mob-data source |
| `mi.md` | `data/ui/mobinfo.mi` | Companion mob-info file — NO client loader (tool/editor format) |
| `terrain.md` | `data/map<NNN>/*.ted` etc. | Y-coordinate (ground snap) source for spawned entities |
| `pak.md` | `data.inf` / `data/data.vfs` | VFS container that holds `.arr` files |

- **Glossary:** see `Docs/RE/names.yaml`
- **Provenance:** see `Docs/RE/journal.md`

---

## Known unknowns

The following fields and questions remain unresolved and must not be guessed at during implementation:

1. **`mob_id` vs. `npc_id` scope** — The same `.arr` format and `mob_id` field appear to be used for both NPC (non-combat) and monster spawns. Whether a flag elsewhere distinguishes the two populations is unknown. (Note: it is **not** an inert field — `+2`, `+20`, `+24` are confirmed unconsumed.)
2. **`spawn_type` full enumeration** — Only values 0 and 7 are observed / documented. The full set of valid values and their semantics is unknown.
3. **16-byte tool variant (map 000)** — Whether a tool-side reader exists, and the precise field layout of the 16-byte record, remains unverified. No tool loader was identified in the shipped client.
4. **`mob.arr` (20-byte) and `mobinfo.mi` field semantics** — Out-of-client-scope: the shipped client has no loader for either, so their field meanings cannot be recovered from the client and are DBG-pending (likely permanent). See `mi.md`.

> The previously-listed unknowns for `field_02` (+2), `rotation_y`/`facing` (+12), `field_20` (+20),
> and `field_24` (+24) are now **resolved** by the CAMPAIGN VFS-MASTERY two-witness pass: `+2`, `+20`,
> `+24` are CONFIRMED inert; `+12` is CONFIRMED a facing value to which the runtime adds π/2. They are
> no longer open questions.
