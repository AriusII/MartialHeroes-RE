# Format: .arr  (NPC / monster spawn array)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file.

---

## Status

```
sample_verified: true   # stride, mob_id, world_x, world_z, spawn_type confirmed against real bytes
                        # field_1, rotation_y, unknown_5, unknown_6 — see per-field confidence below
```

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
| 2 | 2 | u16 | `field_02` | Purpose unknown. Value 67 (decimal) in all examined 28-byte samples. Candidates: mob level override, sub-type index, faction / allegiance ID, or spawn count. May alternatively be the high word of a 32-bit integer whose low word is `mob_id`; no code was observed accessing this offset as a distinct field. | UNVERIFIED |
| 4 | 4 | f32 | `world_x` | World-space X coordinate (horizontal plane). | CONFIRMED |
| 8 | 4 | f32 | `world_z` | World-space Z coordinate (horizontal plane). Y (height) is not stored; the terrain system snaps spawned entities to ground level. | CONFIRMED |
| 12 | 4 | f32 | `rotation_y` | Yaw / facing angle in radians (Y-axis rotation). Observed values cluster near π/2 (≈ 1.5708 rad, i.e. 90°), consistent with a default facing direction. This interpretation rests on sample-byte inference; no runtime code was observed reading this offset as a named field. | PARTIAL — sample inference only |
| 16 | 4 | u32 | `spawn_type` | Spawn-group link ID and spawn-type modifier. The value 7 is treated specially by the spawn-matching code: it triggers an elite / boss modifier branch (returns a 10 % bonus multiplier rather than the baseline). This field is also compared against an actor's own spawn-group ID during in-scene lookup. Both examined 28-byte samples contain 0 (standard spawn, no modifier). | CONFIRMED |
| 20 | 4 | u32 | `unknown_20` | Purpose unknown. Zero in all examined samples. Candidates: respawn delay (seconds), maximum simultaneous live count. | UNVERIFIED |
| 24 | 4 | u32 | `unknown_24` | Purpose unknown. Zero in all examined samples. Candidates: spawn radius, group size, reserved padding. | UNVERIFIED |

**Total record size: 28 bytes (0x1C). Stride is confirmed by parser iteration.**

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

## Anomaly: map 000 file (16-byte variant)

The spawn file for map 000 (`data/map000/npc000.arr`) is 16 bytes, which is **not a multiple of 28**. The runtime computes `floor(16 / 28) = 0` and reads zero records from disk; map 000 therefore has no runtime mob spawn data.

The 16-byte content closely resembles the first 16 bytes of a standard 28-byte record (fields `mob_id`, `field_02`, `world_x`, `world_z`, `rotation_y`), suggesting one of:

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

### Coordinate system

World coordinates use X (east/west) and Z (north/south) as the horizontal plane; Y (vertical) is absent from spawn records and is resolved at runtime by the terrain system. Observed coordinate magnitudes (approximately 25 000–65 000 world units) are consistent with a world extent of approximately 65 536 × 65 536 units, common for client-server MMORPGs of this generation.

### Related formats

| Format | File | Relationship |
|--------|------|--------------|
| `config_tables.md` | `data/script/mobs.scr` | Template database resolved by `mob_id` |
| `terrain.md` | `data/map<NNN>/*.ted` etc. | Y-coordinate (ground snap) source for spawned entities |
| `pak.md` | `data.inf` / `data/data.vfs` | VFS container that holds `.arr` files |

- **Glossary:** see `Docs/RE/names.yaml`
- **Provenance:** see `Docs/RE/journal.md`

---

## Known unknowns

The following fields and questions remain unresolved and must not be guessed at during implementation:

1. **`field_02` (offset 2, u16)** — Purpose not determined. Value 67 in all available 28-byte samples; must be cross-checked against `mobs.scr` (verify whether 67 matches a mob-level entry) and against map files with varied values.
2. **`rotation_y` (offset 12, f32)** — Yaw interpretation is inferred from sample-byte analysis only. No runtime code was observed reading this field directly. Verify against a sample with a clearly non-90-degree facing, or locate editor / export code that writes this field.
3. **`unknown_20` (offset 20, u32)** — Always zero in available samples; semantics unknown. Likely respawn delay or max live count, but unconfirmed.
4. **`unknown_24` (offset 24, u32)** — Always zero in available samples; semantics unknown. Likely spawn radius or group size, but unconfirmed.
5. **16-byte tool variant (map 000)** — Whether a tool-side reader exists, and what the precise field layout of the 16-byte record is, remains unverified. No tool loader was identified in the runtime client.
6. **`mob_id` vs. `npc_id` scope** — The same `.arr` format and `mob_id` field appear to be used for both NPC (non-combat) and monster spawns. Whether a flag elsewhere in the record (e.g. `field_02`) distinguishes the two populations is unknown.
7. **`spawn_type` full enumeration** — Only values 0 and 7 are observed / documented. The full set of valid values and their semantics is unknown.
