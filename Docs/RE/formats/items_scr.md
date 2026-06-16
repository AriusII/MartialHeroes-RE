# Format: .scr  (item master databases — `items.scr` regular items, `citems.scr` cash-shop items)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file
> with `// spec: Docs/RE/formats/items_scr.md`.

---

## Verification banner

```
verification:   sample-verified   # framing, leading fields, stride and counts corroborated against
                                   # real VFS sample bytes AND the runtime loader's control flow
                                   # (two-witness). Field ROLES inside the fixed block and the full
                                   # discriminator enumeration remain debugger-pending.
ida_reverified: 2026-06-16
ida_anchor:     263bd994
evidence:       [static-ida, vfs-sample]
conflicts:      - items.scr discriminator offset: RESOLVED — on-disk +0xD2 (210); the prior
                    "+0xBA" was the loader's internal 0x18-shifted working-buffer notation
                    (+0xBA + 0x18 = +0xD2). Normalized to on-disk +0xD2 below.
                - citems.scr +0x00 field: RESOLVED — it is `item_id`, NOT `slot_index`
                    (values are non-monotonic; billing ids > 100000 appear mid-file).
                - citems.scr description paragraph count: OPEN — 6 vs 10 UNRESOLVED
                    (sample-probe pending; see §2.4). Carried as an explicit open conflict.
```

---

## Status

```
items.scr:   framing SAMPLE-VERIFIED  # fixed 548-byte (0x224) record + optional 8-byte effect tail;
                                       # stride = 0x224 + 8*effect_count; 90,937 records; EOF-terminated.
                                       # +0x80/+0x84 = model/anim ref keys (loader-resolved);
                                       # record discriminator = on-disk +0xD2 (!=14).
                                       # Remaining stat-field ROLES remain UNVERIFIED / DBG-pending.
citems.scr:  SAMPLE-VERIFIED          # fixed stride 1052 B × 512 records; item_id@+0x00, name@+0x04;
                                       # desc paragraphs @+0xE4 (81 B each) — paragraph COUNT 6-vs-10 OPEN.
```

The `items.scr` framing (record stride, termination, and the leading name/uid/effect-count fields)
is **SAMPLE-VERIFIED** on two independent grounds: a full-file black-box walk of all 90,937 records
(EOF-clean, zero residual; file size 49,846,164 bytes reconciles exactly) and the runtime loader's
own cursor arithmetic (a single fixed-size read of the leading block followed by a count-driven tail
read). Two reference fields inside the fixed block — `+0x80` and `+0x84` — are **loader-resolved** as
model-reference and animation-reference keys (the loader resolves them against asset-lookup paths).
The per-record **discriminator** the loader branches on is at **on-disk offset +0xD2** (tested
`!= 14`), correcting an earlier mis-located `+0xB8` item-type branch; the loader expresses this byte
internally as `+0xBA` relative to a working buffer whose base sits 0x18 ahead of the record start
(`+0xBA + 0x18 = +0xD2`). The remaining numeric stat ROLES inside the fixed block are still
UNVERIFIED / DBG-pending; they are best assigned by a later cross-family debugger pass that dumps
complete fixed records across weapon / armour / consumable / quest families. The `citems.scr` schema
is fully fixed-stride and boundary-confirmed across all 512 records; its leading `+0x00` field is the
item id (see §2) and its description-paragraph **count** (6 vs 10) is an explicit OPEN conflict
(§2.4).

Both files were re-verified by a two-witness gate: black-box byte observation of the user-supplied
reference VFS sample AND the runtime loader's control flow / cursor arithmetic. These are two
distinct schemas that happen to share the same extension; they are NOT interchangeable, and a parser
must dispatch on the logical filename.

---

## Identification

- **Extension:** `.scr`
- **Found in:** `.pak` VFS archive — `data/script/items.scr` and `data/script/citems.scr`
- **Magic / signature:** none — neither file has a magic signature, version field, or file-level header. The first record begins at byte 0.
- **Version field:** none
- **Endianness:** little-endian throughout (x86 client)
- **Text encoding:** CP949 (EUC-KR), NUL-terminated and zero-padded inside fixed buffers
- **Record count:**
  - `items.scr` — not stored; obtained only by walking the variable-stride record sequence to EOF
    (90,937 records observed). The walk reconciles exactly to the on-disk file size (89,520 records ×
    548 B + 1,256 × 556 B + 153 × 564 B + 8 × 572 B = 49,846,164 B, zero residual). See §1.3.
  - `citems.scr` — fixed 512 (derived from `file_size / stride` = 538,624 / 1052). See §2.3.

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

> **Loader buffer base.** When the runtime loader stages a record into memory it does so against a
> working buffer whose base sits **0x18 bytes** ahead of the record start, and its branch/flag
> offsets are expressed relative to that **0x18 buffer base**. To convert any loader-relative offset
> to the on-disk position used in the table below, **add 0x18** (e.g. loader `+0xBA` → on-disk
> `+0xD2`; loader `+0xCD..+0xD0` → on-disk `+0xE5..+0xE8`). The on-disk offsets in the table below
> are absolute within the 548-byte record block; the discriminator note in §1.4.1 maps the loader's
> branch offset onto the on-disk position so an engineer needs only this file.

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| 0x000 | 52 | CP949[] | `item_name` | Fixed window 0x000..0x033 (52 bytes); CP949 text, NUL-terminated then zero-padded. Enchant variants of one base item carry a `+N` suffix inside this buffer. | CONFIRMED (90,937/90,937) |
| 0x034 | 4 | u32 | `item_uid` | Per-record id; increments by 1 within a family (e.g. across enchant levels). Observed high-byte families: 0x0B,0x0C,0x0D,0x0E,0x0F,0x10,0x11. The loader uses this value as the per-record lookup-tree key. Sample id 201,171,898 (0x0B family) confirms the 9-digit UID range. | SAMPLE-VERIFIED (90,937/90,937) |
| 0x038 | variable extent within block | CP949[] | `item_desc` | CP949 description, NUL-terminated, beginning at +0x038 and bounded by the fixed block. Its exact end offset within the block is UNVERIFIED. | CONFIRMED present; exact extent UNVERIFIED |
| 0x080 | 4 | u32 | `model_ref_key` | Model-reference key. The loader resolves this value against the model/asset-lookup path (a reference into the visual-asset set, not a free numeric stat). Non-zero for families that carry a visual model; identical across enchant variants of one base item. | loader-resolved |
| 0x084 | 4 | u32 | `anim_ref_key` | Animation-reference key. The loader resolves this value against the animation/asset-lookup path. Identical across enchant variants of one base item; varies by item template/category. | loader-resolved |
| 0x0A4 | 4 | bytes | (opaque) | Read into the staged record but no consumer semantics are settled. Reads as a plausible small float for some weapon families, but its role is unconfirmed. Keep OPAQUE — needs live-debugger confirmation; do not assign a stat meaning. | DBG-pending |
| 0x0D2 | 1 | u8 | `record_discriminator` | The value the loader branches on to select per-record handling — see §1.4.1. Tested `!= 14`. On-disk offset is **+0xD2**; the loader expresses it internally as `+0xBA` against its 0x18-ahead working buffer (`+0xBA + 0x18 = +0xD2`). (Supersedes the earlier `+0x0B8 item_type_tag` claim, which is REFUTED; see §1.7.) | loader-resolved (offset CONFIRMED; value enumeration DBG-pending) |
| 0x0E5 | 4 | u8[] | dispatch flags | Four per-record dispatch flag bytes the loader consults alongside the discriminator: `+0xE5`, `+0xE6`, `+0xE7`, `+0xE8`. Observed loader-pushed comparison codes 1 / 26 / 11 / 16 respectively (loader-relative `+0xCD..+0xD0`). Role of each flag is DBG-pending. | loader-resolved (offsets CONFIRMED; semantics DBG-pending) |
| 0x200 | 4 | bytes | (opaque) | Read into the staged record but no consumer semantics are settled. Keep OPAQUE — needs live-debugger confirmation; do not assign a meaning. | DBG-pending |
| 0x21C | 4 | bytes | (opaque) | Read into the staged record but no consumer semantics are settled (non-zero in only a small subset of records). Keep OPAQUE — needs live-debugger confirmation; do not assign a meaning. | DBG-pending |
| 0x220 | 1 | u8 | `effect_count` | Count of trailing 8-byte effect/upgrade entries; drives the per-record stride (see §1.2). | CONFIRMED (90,937/90,937) |
| 0x221 | 3 | bytes | reserved / padding | Not individually sampled; assumed zero. | UNVERIFIED |

Region +0x038..+0x07F (between the description start and the first reference field) and the remaining
bytes of the block that are not tabled above are not yet mapped. See Known Unknowns.

#### 1.4.1 Record discriminator — on-disk +0xD2, tested `!= 14`

The runtime loader stages each record into a working buffer whose **base is 0x18 bytes ahead of the
record start** and expresses its branch offsets relative to that **0x18 buffer base**. The field the
loader uses to select per-record handling is a **discriminator at on-disk offset +0xD2 (210)** of the
record block. The loader's own disassembly comment names this byte `+0xBA`, but that is the
**loader-relative** offset against its 0x18-ahead working buffer; the **on-disk** offset is
unambiguously **+0xD2** (`+0xBA + 0x18 = +0xD2`, confirmed by stack-frame analysis of the dispatch
routine). Engineers reading the file from disk must use **+0xD2**. The loader's primary branch tests
this discriminator for **`!= 14`**: one handling path is taken when the value is 14, the other when it
is not.

Alongside the discriminator the loader consults **four dispatch flag bytes at on-disk +0xE5, +0xE6,
+0xE7, +0xE8** (loader-relative `+0xCD..+0xD0`); the loader compares them against codes **1, 26, 11,
16** respectively. The per-flag semantics are DBG-pending.

This **corrects** the prior model that placed an `item_type_tag` branch at `+0xB8`: that offset and
that branch are REFUTED (see §1.7). It also **normalizes** the discriminator's reference frame: an
earlier revision of this spec stated "on-disk offset +0xBA", but +0xBA is the loader's 0x18-shifted
internal notation — the on-disk offset is **+0xD2**. The full enumeration of discriminator values and
what each selected handling path does is **DBG-pending** — only the offset and the `!= 14` split are
established; do not invent the remaining category meanings.

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
- The full enumeration of `record_discriminator` (on-disk +0xD2) values beyond the established
  `!= 14` split, and what each selected handling path does — DBG-pending.
- The per-flag semantics of the four dispatch flag bytes at on-disk +0xE5..+0xE8 (compared against
  codes 1 / 26 / 11 / 16) — DBG-pending.
- Consumer semantics of the opaque fields at +0x0A4, +0x200, and +0x21C — DBG-pending (read their
  bytes, assign no meaning until a live-debugger pass settles them).
- Semantic layout of the trailing 8-byte effect entries (§1.5); which field, if any, carries
  per-enchant stat deltas.
- Reserved bytes at +0x221..+0x223.

### 1.7 SUPERSEDED / REFUTED — do not reintroduce

Earlier models of `items.scr` were proven wrong and must NOT be revived. They are recorded here only
so the same mistakes are not made again.

1. **`item_type_tag` discriminator at `+0x0B8`.** An earlier spec placed the record's category /
   dispatch field at `+0x0B8` and treated it as a packed item-type tag. This is **REFUTED**. The
   field the loader actually branches on is the **discriminator at on-disk +0xD2**, tested **`!= 14`**
   (the loader names it `+0xBA` relative to its 0x18-ahead working buffer; `+0xBA + 0x18 = +0xD2` —
   see §1.4.1). No branch reads `+0x0B8`. Do not reintroduce a `+0x0B8 item_type_tag`. (Authority:
   two-witness gate — loader branch + black-box; Campaign 10 D8 stack-frame analysis normalized the
   on-disk offset to +0xD2.)

2. **Free numeric stats at `+0x080` / `+0x084`.** An earlier spec read these as inferred
   `template_ref` / `template_ref_b` numeric stats of UNVERIFIED role. They are now **loader-resolved**
   as the **model-reference** (`+0x080`) and **animation-reference** (`+0x084`) keys (§1.4) — they are
   asset-lookup keys the loader resolves, not free numeric stats.

3. **Variable-stride "stats block at `0x38 + desc_width`" (former §1.4).** An earlier spec treated
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

4. **Three-sub-record "[A, B, C]" group model.** An earlier harness pass modelled each logical item
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

## 2. citems.scr — cash-shop item database  (SAMPLE-VERIFIED)

The cash-shop (NX / cash-point) item database. A **fixed-stride record array**, straightforward to
iterate.

### 2.1 File-level structure

- **No file-level header.** The first record begins at byte 0. SAMPLE-VERIFIED (first bytes are a
  record's `item_id`, not a header).
- **Fixed stride: 1052 bytes (0x41C).** SAMPLE-VERIFIED — `538,624 / 1052 = 512` exactly, no
  remainder (and the loader reads exactly 0x41C bytes per record).
- **Record count: 512.** SAMPLE-VERIFIED — derived as `file_size / 1052`; the per-record fields were
  independently confirmed across all 512 records.
- **`+0x00` is `item_id`, NOT a sequential slot index.** The leading u32 is the **item id** used by
  the loader as a (dense-array) lookup key. Its values are **not** monotonic by record position —
  e.g. the billing/premium items carry ids `>= 100000` and appear mid-file (ids 100018, 100019 at
  records 374–375) while the final record holds id 375. An earlier revision called this field
  `slot_index`; that was WRONG (see §2.5). SAMPLE-VERIFIED.

### 2.2 Record layout — 1052 bytes per record

All offsets are relative to the start of the record.

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| 0x00 | 4 | u32 | `item_id` | Item id (the loader uses it as a dense-array lookup key). **NOT a sequential slot index** — values are non-monotonic by record position (billing items have ids >= 100000 and appear mid-file). See §2.1 and §2.5. | SAMPLE-VERIFIED (512/512) |
| 0x04 | 48 | CP949[] | `item_name` | Fixed window 0x04..0x33 (48 bytes); CP949 text, NUL-terminated then zero-padded. (NOTE: the bytes at 0x04 are the start of this name string, not a separate id — see §2.5.) | SAMPLE-VERIFIED (512/512) |
| 0x30 | 4 | bytes | `pad_30` | Zero (tail padding of the name window: 0x30..0x33). | CONFIRMED (512/512) |
| 0x34 | 2 | bytes | `pad_34` | Zero. | CONFIRMED |
| 0x36 | 2 | u16 | `unknown_36` | Non-zero; value varies per record; purpose unknown. | CONFIRMED present; role UNVERIFIED |
| 0x38 | 4 | u32 | `cash_price_nx` | Cash-shop price in NX cash points (small values; sample record 0 = 950). | SAMPLE-VERIFIED (value); role INFERRED |
| 0x3C | 4 | u32 | `slot_seq_2` | Increments per record; possibly a second slot or category index. | CONFIRMED present; role UNVERIFIED |
| 0x40 | 8 | bytes | `pad_40` | Zero. | CONFIRMED |
| 0x48 | 4 | u32 | `item_uid` | Per-record unique id (high-range values; sample record 0 = 17,883,710). Not a simple per-record increment. | SAMPLE-VERIFIED present; role INFERRED |
| 0x4C | 4 | u32 | `flag_4C` | Value 1 in all observed records. | SAMPLE-VERIFIED |
| 0x50 | … | bytes | (zero region) | Largely zero through the middle of the record, up to the description block at 0x0E4. | CONFIRMED (zero-heavy) |
| 0x0E4 | 81 | CP949[] | `desc_para[0]` | Description paragraph 0. CP949, NUL-terminated within the 81-byte buffer, then zero-padded. | SAMPLE-VERIFIED (512/512) |
| 0x135 | 81 | CP949[] | `desc_para[1]` | Description paragraph 1. | SAMPLE-VERIFIED (512/512) |
| 0x186 | 81 | CP949[] | `desc_para[2]` | Description paragraph 2. | CONFIRMED (512/512) |
| 0x1D7 | 81 | CP949[] | `desc_para[3]` | Description paragraph 3. | CONFIRMED (512/512) |
| 0x228 | 81 | CP949[] | `desc_para[4]` | Description paragraph 4 (populated in fewer records). | CONFIRMED (512/512) |
| 0x279 | 81 | CP949[] | `desc_para[5]` | Description paragraph 5 (populated in fewer records). | CONFIRMED (512/512) |
| 0x2CA | 338 | bytes | (record remainder) | Remainder of the record after the **6-paragraph** description block (0x2CA..0x41B); content unmapped, likely duration / equip requirements / icon-graphic id. **If the description is actually 10 paragraphs (see §2.4 OPEN conflict), this region is partly paragraphs 6–9 instead.** | UNVERIFIED |

> **Billing filter (loader behaviour).** The loader filters cash-shop records by `item_id` against a
> threshold: when billing is **inactive** it admits only `item_id < 100000` (regular cash items);
> when billing is **active** it admits only `item_id >= 100000` (premium/billing items). This is why
> the `>= 100000` ids appear as a distinct block within the 512 records (records 374–375 carry ids
> 100018, 100019). The threshold is `100000`; the active/inactive flag is a runtime state.
> SAMPLE-VERIFIED (the high-id records exist where the loader expects them); loader-control-flow.

### 2.3 citems.scr — record count and stride

- **Record count source:** `file_size / 1052` = 512. SAMPLE-VERIFIED (538,624 / 1052 = 512 exact).
- **Record stride:** 1052 bytes (0x41C). SAMPLE-VERIFIED — exact divisor; loader reads 0x41C per
  record.

### 2.4 citems.scr — description block (fixed 81-byte paragraphs; COUNT 6-vs-10 OPEN)

The description is NOT a single contiguous buffer. It is a block of **fixed-offset paragraphs, each
81 bytes (0x51) wide**, CP949 text, NUL-terminated within each paragraph then zero-padded, starting
at **+0x0E4**:

```
desc_para[i] start = 0x0E4 + i * 81
desc_para_width    = 81 (0x51) bytes        (applies to every paragraph)
```

Paragraph start offsets for the first six: 0x0E4, 0x135, 0x186, 0x1D7, 0x228, 0x279. The first two
paragraphs (`+0x0E4`, `+0x135`) are SAMPLE-VERIFIED to hold CP949 text; the 81-byte stride between
them is confirmed. An empty paragraph is all-zero, and later paragraphs are populated in fewer
records.

> **OPEN CONFLICT — paragraph count (6 vs 10), UNRESOLVED.** Two prior readings disagree on how many
> 81-byte paragraphs the description block contains, and this re-verification could **not** settle it
> from the available sample (only the first two paragraphs were byte-observed):
>
> - **6 paragraphs** (this spec's prior reading): block = 0x0E4..0x328 (486 bytes), leaving a
>   338-byte record remainder at 0x2CA..0x41B. This matches the prior-campaign observation of a
>   non-paragraph remainder there.
> - **10 paragraphs** (the sibling `config_tables.md` §2.11 reading): block = 0x0E4 + 10×81 =
>   0x0E4..0x41E. Note 0x41E is **2 bytes past** the 1052-byte (0x41C) record stride, so a strict
>   10-paragraph block does not fit cleanly — but a 9-paragraph-plus reading (or the remainder being
>   partly paragraphs 6–9) is not excluded by the first-two-paragraph sample alone.
>
> Neither witness is decisive: the loader copies the whole 1052-byte record verbatim (one `memcpy`),
> so the disassembly exposes **no paragraph-count constant**; and the VFS sample observed only
> paragraphs 0–1. **Do not pick a count.** RESOLUTION REQUIRES a sample probe: read the bytes at
> +0x2CA, +0x31B, +0x36C, +0x3BD (paragraphs 6–9 under the 10-para model) across many records and
> check whether they ever hold CP949 text (→ count > 6) or are always zero / non-text (→ count = 6).
> Until that probe runs, treat the description as **>= 6 fixed 81-byte paragraphs from +0x0E4**, and
> treat the 0x2CA..0x41B remainder as POSSIBLY further paragraphs. (Conflict raised: Campaign 10 D8,
> §3.2 CONFLICT B / §7 action item 1.)

### 2.5 citems.scr — corrections from earlier spec (do not reintroduce)

- **`+0x00` is `item_id`, NOT `slot_index`.** An earlier revision labelled the leading u32 a
  sequential 1-based `slot_index`. That is WRONG: the loader uses it as an item-id lookup key, and the
  values are non-monotonic by record position (billing items carry ids `>= 100000` and appear
  mid-file — ids 100018, 100019 at records 374–375 — while the final record holds id 375). The field
  is `item_id`. Do not reintroduce `slot_index`. (Authority: Campaign 10 D8 two-witness — loader
  annotation "item_id, not a sequential slot" + non-monotonic VFS sample values.)
- **`item_name` is at +0x04, width 48 bytes (0x04..0x33)** — NOT at +0x08 with width 40 bytes.
- **The field formerly documented as `item_ref` (u32 at +0x04) DOES NOT EXIST.** Those 4 bytes are
  the first 4 bytes of the `item_name` CP949 string. It has been removed.
- **The description is NOT a single buffer near offset 0xDC.** It is the fixed 81-byte paragraph block
  starting at 0x0E4 described in §2.4. (Authority: scr_desc_bounds.raw.md §1.) Note the paragraph
  **count** (6 vs 10) is an OPEN conflict — see §2.4.

### 2.6 citems.scr — Known Unknowns

- **Description paragraph COUNT — 6 vs 10, OPEN.** See §2.4; resolution requires a multi-record
  sample probe of the bytes at +0x2CA / +0x31B / +0x36C / +0x3BD. PRIORITY: HIGH.
- `unknown_36` (u16 at +0x36) value and purpose.
- `slot_seq_2` (+0x3C) role relative to item categorisation.
- `item_uid` (+0x48) generation rule (high-range, not a simple increment).
- Record remainder 0x2CA..0x41B (likely duration / equip requirements / icon-graphic id — or, under
  the 10-paragraph reading, partly further description paragraphs; see §2.4).

---

## 3. items.scr vs citems.scr — summary

| Aspect | items.scr | citems.scr |
|--------|-----------|-----------|
| Record structure | Fixed 548-byte block + optional 8-byte effect tail | Fixed stride 1052 B |
| Per-record stride | `0x224 + 8 * effect_count` (SAMPLE-VERIFIED) | 1052 B (SAMPLE-VERIFIED) |
| Total records | 90,937 (SAMPLE-VERIFIED by EOF-clean walk) | 512 (SAMPLE-VERIFIED) |
| Termination | EOF / short read (no count header) | `file_size / 1052` |
| File header | none | none |
| Leading id field | — (name at +0x000) | `item_id` u32 at +0x000 (NOT a slot index) |
| Name buffer | +0x000, 52 B fixed (CP949) | +0x004, 48 B fixed (CP949) |
| Per-record unique id | `item_uid` u32 at +0x034 | `item_uid` u32 at +0x048 |
| Description | CP949 from +0x038, bounded within the fixed block | >= 6 × 81-byte paragraphs from +0x0E4 (COUNT 6-vs-10 OPEN, §2.4) |
| Asset refs / discriminator | `model_ref_key` +0x080, `anim_ref_key` +0x084 (loader-resolved); discriminator on-disk +0x0D2 `!= 14`; dispatch flags +0x0E5..+0x0E8 | — |
| Price field | none confirmed (asset-ref keys at +0x080/+0x084; +0x0A4 opaque) | NX cash points at +0x038 |
| Schema | regular item master | cash-shop item master |
| Overall confidence | framing SAMPLE-VERIFIED; +0x80/+0x84 + discriminator +0xD2 settled; other stat ROLES UNVERIFIED/DBG-pending | SAMPLE-VERIFIED; desc paragraph COUNT OPEN (§2.4) |

The two files are NOT the same schema. They share the concept of CP949 name + CP949 description +
numeric fields but organise them differently.

---

## 4. Engineer guidance

- A C# parser implementing either file MUST cite `// spec: Docs/RE/formats/items_scr.md` on every
  magic constant and offset.
- **items.scr** is now safe to *walk* and to read its confirmed leading fields: for each record, read
  the 548-byte block, read `effect_count` (u8 at +0x220), then read `effect_count × 8` trailing
  bytes; advance by `0x224 + 8 * effect_count`; stop at EOF. Read `item_name` (+0x000, 52 B),
  `item_uid` (+0x034, u32), and `item_desc` (CP949 from +0x038) as SAMPLE-VERIFIED. Read
  `model_ref_key` (+0x080) and `anim_ref_key` (+0x084) as the loader-resolved asset-reference keys.
  The per-record **discriminator** is at **on-disk +0x0D2** with the loader's branch testing `!= 14`
  (the loader names this byte `+0xBA` against a working buffer 0x18 ahead of the record start;
  `+0xBA + 0x18 = +0xD2`; the four dispatch flags sit at on-disk +0x0E5..+0x0E8) — read it, but the
  full meaning of each discriminator value is DBG-pending. Treat the fields at **+0x0A4, +0x200,
  +0x21C** as **OPAQUE / DBG-pending** — read their bytes but assign no semantics until a
  live-debugger pass settles them. Do NOT read a field at +0x0B8 (REFUTED).
- **citems.scr** is safe to implement: iterate 512 records at 1052-byte stride; read `item_id`
  (u32 at +0x00 — **not** a slot index), `item_name` (+0x04, 48 B CP949), `cash_price_nx` (+0x38),
  `item_uid` (+0x48), and the description paragraphs (`0x0E4 + i*81`, 81 bytes each, CP949
  NUL-terminated). **Read at least 6 paragraphs**, and treat the 0x2CA..0x41B remainder cautiously
  until the paragraph **count** (6 vs 10) is resolved — see the §2.4 OPEN conflict; do not hard-code a
  count of exactly 6 in a way that would silently drop later text. If filtering billing/premium items,
  the threshold is `item_id >= 100000` (see §2.2 billing-filter note).
- `Assets.Parsers` stays rendering-free; any conversion or presentation belongs to `Assets.Mapping`.

---

## 5. Cross-references

| Format | File | Relationship |
|--------|------|--------------|
| `npc_spawns.md` | `data/map<NNN>/*.arr` | Spawn `mob_id` resolves to a mob/NPC template; item ids here are a sibling `.scr` family |
| `config_tables.md` | `data/script/*.scr` | Other `.scr` script/config tables |
| `pak.md` | `data.inf` / `data/data.vfs` | VFS container that holds `.scr` files |

- **Glossary (proposed names — orchestrator records in `names.yaml`):** `ItemRecord` (the 548+8N
  block), `item_name`, `item_uid`, `item_desc`, `model_ref_key` (u32 at +0x080),
  `anim_ref_key` (u32 at +0x084), `record_discriminator` (u8 at on-disk +0x0D2, tested `!= 14`),
  `effect_count` (u8 at +0x220), `EffectEntry` (8-byte trailing record); for citems.scr —
  `item_id` (u32 at +0x000, **not** `slot_index`), `cash_price_nx`, `slot_seq_2`,
  `citems_stride = 1052`, `citems_desc_para_width = 81`, `citems_desc_para_count` (= 6 or 10, OPEN).
  Candidate loader names from the dirty-room (orchestrator-owned): `ItemsScr_LoadFile`,
  `CitemsScr_LoadFile`.
- **Provenance:** see `Docs/RE/journal.md` — promotion entries: CAMPAIGN VFS-MASTERY (two-witness:
  loader + black-box) settled `+0x80/+0x84` as model/anim ref keys and REFUTED the `+0x0B8
  item_type_tag` branch and left `+0x0A4 / +0x200 / +0x21C` opaque (DBG-pending). Campaign 10 D8
  (two-witness, IDB SHA 263bd994 + VFS sample) normalized the record discriminator to **on-disk
  +0x0D2** (`!= 14`; loader-internal `+0xBA` against the 0x18-ahead working buffer), surfaced the four
  dispatch flags at on-disk +0x0E5..+0x0E8, corrected the citems.scr `+0x00` field from `slot_index`
  to `item_id`, and raised the citems.scr description-paragraph **count** (6 vs 10) as an OPEN
  conflict pending a sample probe.
