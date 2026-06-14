# Format: .scr  (item master databases — `items.scr` regular items, `citems.scr` cash-shop items)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file
> with `// spec: Docs/RE/formats/items_scr.md`.

---

## Status

```
items.scr:   framing CONFIRMED   # fixed 548-byte (0x224) record + optional 8-byte effect tail;
                                  # stride = 0x224 + 8*effect_count; 90,937 records; EOF-terminated.
                                  # Stat-field ROLES (beyond the item-type tag) remain UNVERIFIED.
citems.scr:  CONFIRMED           # fixed stride 1052 B × 512 records; name@0x04, 6×81-byte desc paragraphs.
```

The `items.scr` framing (record stride, termination, and the leading name/uid/effect-count fields)
is now CONFIRMED on two independent grounds: a full-file black-box walk of all 90,937 records
(EOF-clean, zero residual) and the runtime loader's own cursor arithmetic (a single fixed-size read
of the leading block followed by a count-driven tail read). The per-record numeric stat ROLES inside
the fixed block — apart from the packed item-type tag — are still UNVERIFIED; they are best assigned
by a later cross-family debugger pass that dumps complete fixed records across weapon / armour /
consumable / quest families. The `citems.scr` schema is fully fixed-stride and boundary-confirmed
across all 512 records.

Both files were classified by black-box byte observation of the user-supplied reference VFS, with the
`items.scr` framing corroborated by the runtime loader. These are two distinct schemas that happen to
share the same extension; they are NOT interchangeable, and a parser must dispatch on the logical
filename.

---

## Identification

- **Extension:** `.scr`
- **Found in:** `.pak` VFS archive — `data/script/items.scr` and `data/script/citems.scr`
- **Magic / signature:** none — neither file has a magic signature, version field, or file-level header. The first record begins at byte 0.
- **Version field:** none
- **Endianness:** little-endian throughout (x86 client)
- **Text encoding:** CP949 (EUC-KR), NUL-terminated and zero-padded inside fixed buffers
- **Record count:**
  - `items.scr` — not stored; obtained only by walking the variable-stride record sequence to EOF (90,937 records observed). See §1.3.
  - `citems.scr` — fixed 512 (derived from `file_size / stride`). See §2.3.

---

## 1. items.scr — regular item master database

The regular item database. Each entry is a **fixed 548-byte (0x224) record** carrying a CP949 name,
a CP949 description, a numeric region, and a one-byte effect count, **optionally followed** by a
trailing array of fixed 8-byte effect/upgrade entries. The whole leading block is a single fixed-size
read in the runtime loader; the only per-record size variability comes from the trailing array.

### 1.1 File-level structure

- **No file-level header.** The first record begins at byte 0; there is no magic, version, or
  record-count field. CONFIRMED.
- **No leading count.** The number of records is not stored anywhere; parsing terminates at EOF
  (or on a short read of the leading block). CONFIRMED — the full-file walk lands exactly on EOF
  with zero residual bytes.
- Records are a contiguous sequence with no inter-record padding beyond the per-record trailing
  array.

### 1.2 Record framing — CONFIRMED model

Each logical item is **one** record, laid out as:

```
[ fixed 548-byte (0x224) block ][ effect_count × 8 bytes of trailing effect entries ]
```

- The leading block is always exactly **548 bytes (0x224)**.
- `effect_count` is a u8 stored **inside** the leading block at offset **+0x220**.
- The trailing effect array, present only when `effect_count > 0`, begins at **+0x224** (immediately
  after the leading block) and is `effect_count × 8` bytes long.
- **Per-record stride = `0x224 + 8 * effect_count`.** There is NO description-width term in the
  stride. CONFIRMED.

Observed `effect_count` distribution across all 90,937 records: value `0` in 89,520 records, `1` in
1,256, `2` in 153, `3` in 8. Maximum observed `effect_count` = 3. (The runtime loader reads this byte
as an unbounded u8; treat values up to 255 as legal, even though only 0..3 were seen.)

The "records vary by ~8 bytes" effect seen in earlier passes is fully explained: a record with one
more effect entry is exactly 8 bytes larger. There is NO variable-width description buffer driving
the stride. The description is a fixed-extent field inside the 548-byte block.

### 1.3 Record count and stride

- **Record count source:** none stored. The count is obtained only by walking records to EOF,
  advancing by `0x224 + 8 * effect_count` each step. Observed total: **90,937 records**. CONFIRMED
  (full-file walk is EOF-clean; the runtime loader likewise loops until EOF / short read).
- **Record stride:** variable — `0x224 + 8 * effect_count` per record (`effect_count` = u8 at
  +0x220). CONFIRMED.

A parser walks the file by: read the 548-byte block, read `effect_count` at +0x220, read
`effect_count × 8` trailing bytes, repeat until EOF.

### 1.4 Fixed-record field layout (offsets within the 548-byte block)

All offsets are absolute within the 548-byte (0x224) record block. Endianness: little-endian.

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| 0x000 | 52 | CP949[] | `item_name` | Fixed window 0x000..0x033 (52 bytes); CP949 text, NUL-terminated then zero-padded. Enchant variants of one base item carry a `+N` suffix inside this buffer. | CONFIRMED (90,937/90,937) |
| 0x034 | 4 | u32 | `item_uid` | Per-record id; increments by 1 within a family (e.g. across enchant levels). Observed high-byte families: 0x0B,0x0C,0x0D,0x0E,0x0F,0x10,0x11. | CONFIRMED (90,937/90,937) |
| 0x038 | variable extent within block | CP949[] | `item_desc` | CP949 description, NUL-terminated, beginning at +0x038 and bounded by the fixed block. Its exact end offset within the block is UNVERIFIED. | CONFIRMED present; exact extent UNVERIFIED |
| 0x080 | 4 | u32 | `template_ref` (inferred) | Non-zero for weapon families (carries a UID-shaped value); zero for non-weapon families. Identical across enchant variants of one base item. Role UNVERIFIED. | PARTIAL; role UNVERIFIED |
| 0x084 | 4 | u32 | `template_ref_b` (inferred) | Second UID-shaped reference; identical across enchant variants of one base item. Varies by item template/category. Role UNVERIFIED. | PARTIAL; role UNVERIFIED |
| 0x0A4 | 4 | f32 | numeric stat (inferred) | Reads as a plausible float for weapons (e.g. small magnitudes such as 1.0, 4.0, 40.0); zero for non-weapon families. Candidate attack/speed stat. Semantic UNVERIFIED. | PARTIAL; semantic UNVERIFIED |
| 0x0B8 | 4 | u32 | `item_type_tag` (inferred) | Packed value that tracks item category: e.g. one value for swords, another for tier-2 weapons/armour, another for skills/books, another for consumables. Strong candidate for an item-type / equip-slot indicator. Role UNVERIFIED. | PARTIAL; role UNVERIFIED |
| 0x200 | 4 | u32 | sequential template/index ref (inferred) | Sequential u32 that increases across weapon/armour tiers; zero for some special/event families. Candidate base-stats or template index. Role UNVERIFIED. | UNVERIFIED |
| 0x21C | 4 | u32 | rare-nonzero field | Zero in the vast majority of records; non-zero in a small subset. Meaning UNVERIFIED. | UNVERIFIED |
| 0x220 | 1 | u8 | `effect_count` | Count of trailing 8-byte effect/upgrade entries; drives the per-record stride (see §1.2). | CONFIRMED (90,937/90,937) |
| 0x221 | 3 | bytes | reserved / padding | Not individually sampled; assumed zero. | UNVERIFIED |

Region +0x038..+0x07F (between the description start and the first inferred reference field) and the
remaining bytes of the block that are not tabled above are not yet mapped. See Known Unknowns.

### 1.5 Trailing effect entries — on-disk 8-byte layout

Present only when `effect_count > 0`. `effect_count` entries follow the fixed block, each **8 bytes
on disk**. The runtime loader expands each on-disk entry into a wider in-memory record; the on-disk
field layout (as the loader reads it) is:

| Offset (in entry) | Size | Type | Field | Notes | Confidence |
|------------------:|-----:|------|-------|-------|------------|
| 0x00 | 2 | u16 | `effect_a` | Carried into the runtime entry. Role UNVERIFIED. | PLAUSIBLE (read shape); role UNVERIFIED |
| 0x02 | 2 | s16 | `effect_b` | Signed; sign-extended into a 32-bit runtime field. Role UNVERIFIED. | PLAUSIBLE (signedness); role UNVERIFIED |
| 0x04 | 2 | u16 | `effect_c` | Carried into the runtime entry. Role UNVERIFIED. | PLAUSIBLE (read shape); role UNVERIFIED |
| 0x06 | 1 | u8 | `effect_d` | Carried into the runtime entry. Role UNVERIFIED. | PLAUSIBLE (read shape); role UNVERIFIED |
| 0x07 | 1 | bytes | (pad / unused) | Not carried into the runtime entry. | PLAUSIBLE |

- **Entry count source:** `effect_count` (u8 at fixed offset +0x220 of the leading block).
- **Entry stride:** 8 bytes on disk.

The semantic meaning of each effect field (e.g. enchant delta, stat bonus type/value) is UNVERIFIED;
candidate per-enchant stat deltas may live here (they were NOT found at the fixed block offsets probed
across an enchant series). A cross-family debugger pass is the way to assign these roles.

### 1.6 items.scr — Known Unknowns

- Exact end offset of the `item_desc` field within the 548-byte block.
- Layout of region +0x038..+0x07F beyond the description text.
- Roles of `template_ref` (+0x080), `template_ref_b` (+0x084), the f32 at +0x0A4, the packed
  `item_type_tag` (+0x0B8), the sequential ref at +0x200, and the rare-nonzero field at +0x21C —
  all single/few-family inference, no confirmed semantics.
- Semantic layout of the trailing 8-byte effect entries (§1.5); which field, if any, carries
  per-enchant stat deltas.
- Reserved bytes at +0x221..+0x223.

### 1.7 SUPERSEDED / REFUTED — do not reintroduce

Two earlier models of `items.scr` were proven wrong and must NOT be revived. They are recorded here
only so the same mistakes are not made again.

1. **Variable-stride "stats block at `0x38 + desc_width`" (former §1.4).** An earlier spec treated
   the description as a variable-width buffer whose width drove the record size, and placed a
   floating "stats block" (with inferred fields `template_ref_a`/`template_ref_b`/`price_gold`/
   `weight`/`stat_1..stat_4`, etc.) at `record_start + 0x38 + desc_buffer_width`. This is **REFUTED**.
   The record is a fixed 548-byte block; the description is a fixed-extent field inside it; the
   inferred "stats block" was simply the fixed bytes of the block read at offsets that *coincided*
   with `0x38 + desc_width` for one weapon family's particular description length. In particular, the
   apparent **"price that scales with enchant level"** was the **description text changing** between
   enchant variants (the CP949 bytes of the description being read as a binary price field), NOT a
   real numeric price field. All field offsets from that floating-stats model are invalid; the only
   surviving facts are name@0x000, uid@0x034, and description present from +0x038, now re-anchored as
   fixed offsets above. (Authority: items_scr_reconcile.raw.md; corroborated by the loader's
   single-fixed-read framing.)

2. **Three-sub-record "[A, B, C]" group model.** An earlier harness pass modelled each logical item
   as a group of three consecutive variable-length sub-records (an "item" sub-record A, a "filler"
   sub-record B, and a "stats" sub-record C), reporting ~32,402 groups. This is **REFUTED**. That
   harness detected record boundaries by scanning for CP949 lead bytes at offset +0x00; because the
   CP949 description (and occasional high bytes in the numeric region) also begin with CP949 lead
   bytes inside the single 548-byte block, the scanner found phantom sub-boundaries and split one real
   record into three. The "group" count and all "SubRec-A/B/C", "tail_C", and "stats in sub-record C"
   findings are invalid. The arithmetic reconciles to the true model: 90,937 records × 548 base bytes
   + (1×1,256 + 2×153 + 3×8) × 8 trailing bytes = exact file size with zero residual.
   (Authority: items_scr_reconcile.raw.md §2/§6; corroborated by the loader.)

---

## 2. citems.scr — cash-shop item database  (CONFIRMED)

The cash-shop (NX / cash-point) item database. A **fixed-stride record array**, straightforward to
iterate.

### 2.1 File-level structure

- **No file-level header.** The first record begins at byte 0. CONFIRMED.
- **Fixed stride: 1052 bytes (0x41C).** CONFIRMED — `file_size / 1052 = 512` exactly, no remainder.
- **Record count: 512.** CONFIRMED — derived as `file_size / 1052`; the sequential `slot_index`
  pattern and the per-record fields were independently confirmed across all 512 records.

### 2.2 Record layout — 1052 bytes per record

All offsets are relative to the start of the record.

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| 0x00 | 4 | u32 | `slot_index` | Sequential 1-based index (1, 2, 3, …). | CONFIRMED (512/512) |
| 0x04 | 48 | CP949[] | `item_name` | Fixed window 0x04..0x33 (48 bytes); CP949 text, NUL-terminated then zero-padded. (NOTE: the bytes at 0x04 are the start of this name string, not a separate id — see §2.5.) | CONFIRMED (512/512) |
| 0x30 | 4 | bytes | `pad_30` | Zero (tail padding of the name window: 0x30..0x33). | CONFIRMED (512/512) |
| 0x34 | 2 | bytes | `pad_34` | Zero. | CONFIRMED |
| 0x36 | 2 | u16 | `unknown_36` | Non-zero; value varies per record; purpose unknown. | CONFIRMED present; role UNVERIFIED |
| 0x38 | 4 | u32 | `cash_price_nx` | Cash-shop price in NX cash points (small values, e.g. ~950, ~3300). | CONFIRMED (value); role INFERRED |
| 0x3C | 4 | u32 | `slot_seq_2` | Sequential, increments per record; possibly a second slot or category index. | CONFIRMED (sequential); role UNVERIFIED |
| 0x40 | 8 | bytes | `pad_40` | Zero. | CONFIRMED |
| 0x48 | 4 | u32 | `item_uid` | Per-record unique id; increments across adjacent records (high-range values). | CONFIRMED |
| 0x4C | 4 | u32 | `flag_4C` | Value 1 in all observed records. | CONFIRMED |
| 0x50 | … | bytes | (zero region) | Largely zero through the middle of the record, up to the description block at 0x0E4. | CONFIRMED (zero-heavy) |
| 0x0E4 | 81 | CP949[] | `desc_para[0]` | Description paragraph 0. CP949, NUL-terminated within the 81-byte buffer, then zero-padded. | CONFIRMED (512/512) |
| 0x135 | 81 | CP949[] | `desc_para[1]` | Description paragraph 1. | CONFIRMED (512/512) |
| 0x186 | 81 | CP949[] | `desc_para[2]` | Description paragraph 2. | CONFIRMED (512/512) |
| 0x1D7 | 81 | CP949[] | `desc_para[3]` | Description paragraph 3. | CONFIRMED (512/512) |
| 0x228 | 81 | CP949[] | `desc_para[4]` | Description paragraph 4 (populated in fewer records). | CONFIRMED (512/512) |
| 0x279 | 81 | CP949[] | `desc_para[5]` | Description paragraph 5 (populated in fewer records). | CONFIRMED (512/512) |
| 0x2CA | 338 | bytes | (record remainder) | Remainder of the record after the description block (0x2CA..0x41B); content unmapped, likely duration / equip requirements / icon-graphic id. | UNVERIFIED |

### 2.3 citems.scr — record count and stride

- **Record count source:** `file_size / 1052` = 512. CONFIRMED.
- **Record stride:** 1052 bytes (0x41C). CONFIRMED — exact divisor.

### 2.4 citems.scr — description block (6 fixed paragraphs)

The description is NOT a single contiguous buffer. It is a block of **6 fixed-offset paragraphs,
each 81 bytes (0x51) wide**, CP949 text, NUL-terminated within each paragraph then zero-padded:

```
desc_para[i] start = 0x0E4 + i * 81        (i = 0..5)
desc_para_width    = 81 (0x51) bytes        (applies to all 6 paragraphs)
desc_para_count    = 6
```

Paragraph start offsets: 0x0E4, 0x135, 0x186, 0x1D7, 0x228, 0x279. The full description block spans
0x0E4..0x328. CONFIRMED across all 512 records with zero buffer overflows (no paragraph's text ever
exceeded its 81-byte buffer). Not all paragraphs are populated in every record (later paragraphs are
present in fewer records); an empty paragraph is all-zero.

### 2.5 citems.scr — corrections from earlier spec (do not reintroduce)

- **`item_name` is at +0x04, width 48 bytes (0x04..0x33)** — NOT at +0x08 with width 40 bytes.
- **The field formerly documented as `item_ref` (u32 at +0x04) DOES NOT EXIST.** Those 4 bytes are
  the first 4 bytes of the `item_name` CP949 string. It has been removed.
- **The description is NOT a single buffer near offset 0xDC.** It is the 6×81-byte paragraph block
  starting at 0x0E4 described in §2.4. (Authority: scr_desc_bounds.raw.md §1.)

### 2.6 citems.scr — Known Unknowns

- `unknown_36` (u16 at +0x36) value and purpose.
- `slot_seq_2` (+0x3C) role relative to item categorisation.
- Record remainder 0x2CA..0x41B (likely duration / equip requirements / icon-graphic id).

---

## 3. items.scr vs citems.scr — summary

| Aspect | items.scr | citems.scr |
|--------|-----------|-----------|
| Record structure | Fixed 548-byte block + optional 8-byte effect tail | Fixed stride 1052 B |
| Per-record stride | `0x224 + 8 * effect_count` (CONFIRMED) | 1052 B (CONFIRMED) |
| Total records | 90,937 (CONFIRMED by EOF-clean walk) | 512 (CONFIRMED) |
| Termination | EOF / short read (no count header) | `file_size / 1052` |
| File header | none | none |
| Name buffer | +0x000, 52 B fixed (CP949) | +0x004, 48 B fixed (CP949) |
| Per-record unique id | `item_uid` u32 at +0x034 | `item_uid` u32 at +0x048 |
| Description | CP949 from +0x038, bounded within the fixed block | 6 × 81-byte paragraphs from +0x0E4 |
| Price field | candidate numeric stat(s); roles UNVERIFIED | NX cash points at +0x038 |
| Schema | regular item master | cash-shop item master |
| Overall confidence | framing CONFIRMED; stat ROLES UNVERIFIED | CONFIRMED |

The two files are NOT the same schema. They share the concept of CP949 name + CP949 description +
numeric fields but organise them differently.

---

## 4. Engineer guidance

- A C# parser implementing either file MUST cite `// spec: Docs/RE/formats/items_scr.md` on every
  magic constant and offset.
- **items.scr** is now safe to *walk* and to read its confirmed leading fields: for each record, read
  the 548-byte block, read `effect_count` (u8 at +0x220), then read `effect_count × 8` trailing
  bytes; advance by `0x224 + 8 * effect_count`; stop at EOF. Read `item_name` (+0x000, 52 B),
  `item_uid` (+0x034, u32), and `item_desc` (CP949 from +0x038) as CONFIRMED. Treat every numeric
  stat field (+0x080, +0x084, +0x0A4, +0x0B8, +0x200, +0x21C) and the trailing effect-entry fields as
  UNVERIFIED — read their bytes but do not rely on their semantics until a cross-family debugger pass
  assigns roles.
- **citems.scr** is safe to implement: iterate 512 records at 1052-byte stride; read the confirmed
  leading fields and the 6 description paragraphs (`0x0E4 + i*81`, 81 bytes each, CP949
  NUL-terminated).
- `Assets.Parsers` stays rendering-free; any conversion or presentation belongs to `Assets.Mapping`.

---

## 5. Cross-references

| Format | File | Relationship |
|--------|------|--------------|
| `npc_spawns.md` | `data/map<NNN>/*.arr` | Spawn `mob_id` resolves to a mob/NPC template; item ids here are a sibling `.scr` family |
| `config_tables.md` | `data/script/*.scr` | Other `.scr` script/config tables |
| `pak.md` | `data.inf` / `data/data.vfs` | VFS container that holds `.scr` files |

- **Glossary (proposed names — orchestrator records in `names.yaml`):** `ItemRecord` (the 548+8N
  block), `item_name`, `item_uid`, `item_desc`, `item_type_tag` (u32 at +0x0B8), `template_ref`,
  `effect_count` (u8 at +0x220), `EffectEntry` (8-byte trailing record), `cash_price_nx`,
  `slot_index`, `slot_seq_2`, `citems_stride = 1052`, `citems_desc_para_width = 81`,
  `citems_desc_para_count = 6`.
- **Provenance:** see `Docs/RE/journal.md`
