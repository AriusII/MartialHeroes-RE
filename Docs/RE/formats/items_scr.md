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
                # re-verified against doida.exe IDB SHA 263bd994, CYCLE 7 (2026-06-20)
                # re-verified against doida.exe IDB SHA 263bd994c927c20a38624cf0ca452eaef365057fa9db1543d8f668c14a6fd8ee,
                #   CYCLE 11 (2026-06-24): all load-bearing facts RE-CONFIRMED (two-witness: full-file
                #   byte walk + loader/dispatch control flow). No corrections. Three consumer-touched
                #   citems fields (+0x37, +0x40/+0x48 icon-id A/B, +0x418) promoted to CONSUMER-INFERRED
                #   in §2.2.
ida_reverified: 2026-06-24
ida_anchor:     263bd994c927c20a38624cf0ca452eaef365057fa9db1543d8f668c14a6fd8ee
evidence:       [static-ida, vfs-sample]
corrected:      CORRECTED CYCLE 1 (ida_anchor 263bd994, 2026-06-19): items.scr +0x80 = data/char/skin/g%d.skn
                mesh selector, +0x84 = bind-pose pool id; citems.scr description paragraphs = 10 (capacity,
                '#'-sentinel terminated), resolving the 6-vs-10 conflict.
                CORRECTED CYCLE 7 (ida_anchor 263bd994, 2026-06-20): items.scr record discriminator is
                on-disk +0xBA (the loader reads the 548-byte block DIRECTLY into the staging buffer —
                read base = record start — with NO 0x18 shift); the four dispatch flags are on-disk
                +0xCD..+0xD0. The earlier +0xD2 / "+0x18-shift / 0x18-ahead working buffer" model is
                REFUTED by the binary. items.scr carries NO paragraph block (the paragraph model is
                citems-only).
                RE-CONFIRMED CYCLE 11 (ida_anchor 263bd994, 2026-06-24): all structural facts
                re-confirmed with zero contradictions. Three additional citems consumer-touched fields
                (+0x37 type byte, +0x40/+0x48 icon-id A/B selection, +0x418 branch flag) promoted from
                zero-region to CONSUMER-INFERRED in §2.2.
conflicts:      - items.scr discriminator offset: RESOLVED — on-disk +0xBA (186). The loader reads the
                    548-byte (0x224) block directly into the staging buffer (read base = record start),
                    so the discriminator local at staging-buffer +0xBA is the on-disk +0xBA — there is
                    NO 0x18 shift. The earlier +0xD2 / "+0x18-shifted working buffer" reading is REFUTED.
                - citems.scr +0x00 field: RESOLVED — it is `item_id`, NOT `slot_index`
                    (values are non-monotonic; billing ids > 100000 appear mid-file).
                - citems.scr description paragraph count: RESOLVED in favor of 10 (capacity;
                    '#'-sentinel terminated; the loader's verbatim record copy carries no count
                    constant, while the consumer paragraph accessor bounds the index < 10). The
                    prior 6 was merely the count of typically-populated paragraphs in samples.
                    See §2.4.
```

---

## Status

```
items.scr:   framing SAMPLE-VERIFIED  # fixed 548-byte (0x224) record + optional 8-byte effect tail;
                                       # stride = 0x224 + 8*effect_count; 90,937 records; EOF-terminated.
                                       # +0x80 = .skn mesh selector (data/char/skin/g<key>.skn);
                                       # +0x84 = bind-pose/skeleton pool id; both resolved at
                                       # actor-spawn via the shared actor-visual catalogue (§1.4.2).
                                       # record discriminator = on-disk +0xBA (!=14); dispatch flags
                                       # on-disk +0xCD..+0xD0. NO paragraph block (citems-only model).
                                       # Remaining stat-field ROLES remain UNVERIFIED / DBG-pending.
citems.scr:  SAMPLE-VERIFIED          # fixed stride 1052 B × 512 records; item_id@+0x00, name@+0x04;
                                       # desc paragraphs @+0xE4 (81 B each) — paragraph COUNT = 10
                                       # (capacity, '#'-sentinel early-terminated; CONFIRMED, §2.4).
```

The `items.scr` framing (record stride, termination, and the leading name/uid/effect-count fields)
is **SAMPLE-VERIFIED** on two independent grounds: a full-file black-box walk of all 90,937 records
(EOF-clean, zero residual; file size 49,846,164 bytes reconciles exactly) and the runtime loader's
own cursor arithmetic (a single fixed-size read of the leading block followed by a count-driven tail
read). Two reference fields inside the fixed block — `+0x80` and `+0x84` — are **loader-resolved** as
model-reference and animation-reference keys (the loader resolves them against asset-lookup paths).
The per-record **discriminator** the loader branches on is at **on-disk offset +0xBA** (tested
`!= 14`), correcting an earlier mis-located `+0xB8` item-type branch. The loader reads the 548-byte
(0x224) block **directly** into its staging buffer (read base = record start), so the discriminator
local at staging-buffer +0xBA IS the on-disk +0xBA — there is **no 0x18 working-buffer shift** (the
prior +0xD2 reading is refuted; see §1.4.1 and §1.7). The four dispatch flag bytes the loader
consults alongside it sit at on-disk **+0xCD..+0xD0**. The remaining numeric stat ROLES inside the
fixed block are still
UNVERIFIED / DBG-pending; they are best assigned by a later cross-family debugger pass that dumps
complete fixed records across weapon / armour / consumable / quest families. The `citems.scr` schema
is fully fixed-stride and boundary-confirmed across all 512 records; its leading `+0x00` field is the
item id (see §2) and its description-paragraph **count** is now CONFIRMED at **10** (the structural
capacity; runtime-terminated early by a `'#'`-sentinel paragraph), resolving the former 6-vs-10 OPEN
conflict (§2.4).

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

> **No paragraph block — the paragraph model is citems-only.** `items.scr` does **not** carry a
> citems-style description-paragraph block (no 338-byte tail, no `10 × 81`-byte paragraphs, no
> `'#'`-sentinel). Its body is exactly the fixed 548-byte block plus the optional 8-byte-per-entry
> trailing effect array (`effect_count × 8` at +0x220). The record installer copies the 548-byte
> block verbatim and stores a single trailing-effect pointer immediately after it — there is no
> inline paragraph region. The 10-paragraph / `'#'`-sentinel model belongs to `citems.scr` alone
> (§2.4); the two are unrelated schemas that merely share the `.scr` extension.

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

> **Loader buffer base.** The runtime loader reads the 548-byte (0x224) block **directly** into its
> staging buffer with a single fixed-size read — the **read base IS the record start / staging-buffer
> start**. There is **no 0x18 shift**: every staging-buffer offset the loader uses for its
> branch/flag fields is identical to the on-disk offset within the 548-byte record block. So the
> discriminator local at staging-buffer +0xBA is the on-disk **+0xBA**, and the dispatch flags at
> staging-buffer +0xCD..+0xD0 are on-disk **+0xCD..+0xD0**. (An earlier revision claimed a 0x18-ahead
> working buffer normalized these to +0xD2 / +0xE5..+0xE8; that +0x18-shift model is REFUTED by the
> binary — see §1.4.1 and §1.7.) The on-disk offsets in the table below are absolute within the
> 548-byte record block.

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| 0x000 | 52 | CP949[] | `item_name` | Fixed window 0x000..0x033 (52 bytes); CP949 text, NUL-terminated then zero-padded. Enchant variants of one base item carry a `+N` suffix inside this buffer. | CONFIRMED (90,937/90,937) |
| 0x034 | 4 | u32 | `item_uid` | Per-record id; increments by 1 within a family (e.g. across enchant levels). Observed high-byte families: 0x0B,0x0C,0x0D,0x0E,0x0F,0x10,0x11. The loader uses this value as the per-record lookup-tree key. Sample id 201,171,898 (0x0B family) confirms the 9-digit UID range. | SAMPLE-VERIFIED (90,937/90,937) |
| 0x038 | variable extent within block | CP949[] | `item_desc` | CP949 description, NUL-terminated, beginning at +0x038 and bounded by the fixed block. Its exact end offset within the block is UNVERIFIED. | CONFIRMED present; exact extent UNVERIFIED |
| 0x080 | 4 | u32 | `model_ref_key` | Mesh-selector key. Stored verbatim into the shared actor-visual catalogue at load and resolved at actor-spawn to a `.skn` mesh path **`data/char/skin/g<model_ref_key>.skn`** (printf-formatted numeric selector). Non-zero for families that carry a visual model; identical across enchant variants of one base item. See §1.4.2. | resolved (asset key; HIGH) |
| 0x084 | 4 | u32 | `anim_ref_key` | Bind-pose/skeleton-pool selector. Stored verbatim alongside `model_ref_key` and resolved at actor-spawn as an **id into the pre-loaded bind-pose/skeleton pool** (the `data/char/bind/g{id}.bnd` skeletons + `data/char/mot/g{id}.mot` motions seeded at boot from `bindlist.txt`) — NOT a direct printf. Identical across enchant variants; varies by item template/category. See §1.4.2. | resolved (pool id; HIGH that it is a pool id, MEDIUM that the item-side g{id}.bnd/.mot file is the exact 1:1 member) |
| 0x0A4 | 4 | bytes | (opaque) | Read into the staged record but no consumer semantics are settled. Reads as a plausible small float for some weapon families, but its role is unconfirmed. Keep OPAQUE — needs live-debugger confirmation; do not assign a stat meaning. | DBG-pending |
| 0x0BA | 1 | u8 | `record_discriminator` | The value the loader branches on to select per-record handling — see §1.4.1. Tested `!= 14`. On-disk offset is **+0xBA** (the loader reads the 548-byte block directly into the staging buffer, read base = record start, so staging-buffer +0xBA = on-disk +0xBA — NO 0x18 shift). (Supersedes the earlier `+0x0B8 item_type_tag` claim AND the later +0xD2 / +0x18-shift reading, both REFUTED; see §1.7.) | loader-resolved (offset CONFIRMED; value enumeration DBG-pending) |
| 0x0CD | 4 | u8[] | dispatch flags | Four per-record dispatch flag bytes the loader consults alongside the discriminator: `+0xCD`, `+0xCE`, `+0xCF`, `+0xD0` (on-disk; read base = record start, no shift). The loader maps each byte's `== 1` to comparison codes 1 / 26 / 11 / 16 respectively. Role of each flag is DBG-pending. | loader-resolved (offsets CONFIRMED; semantics DBG-pending) |
| 0x200 | 4 | bytes | (opaque) | Read into the staged record but no consumer semantics are settled. Keep OPAQUE — needs live-debugger confirmation; do not assign a meaning. | DBG-pending |
| 0x21C | 4 | bytes | (opaque) | Read into the staged record but no consumer semantics are settled (non-zero in only a small subset of records). Keep OPAQUE — needs live-debugger confirmation; do not assign a meaning. | DBG-pending |
| 0x220 | 1 | u8 | `effect_count` | Count of trailing 8-byte effect/upgrade entries; drives the per-record stride (see §1.2). | CONFIRMED (90,937/90,937) |
| 0x221 | 3 | bytes | reserved / padding | Not individually sampled; assumed zero. | UNVERIFIED |

Region +0x038..+0x07F (between the description start and the first reference field) and the remaining
bytes of the block that are not tabled above are not yet mapped. See Known Unknowns.

#### 1.4.1 Record discriminator — on-disk +0xBA, tested `!= 14`

The runtime loader reads the 548-byte (0x224) block with a **single fixed-size read directly into its
staging buffer** — the **read base is the record start**, so every staging-buffer offset equals the
on-disk offset. The field the loader uses to select per-record handling is a **discriminator at
on-disk offset +0xBA (186)** of the record block (the u8 the loader passes to the actor-anim
catalogue dispatch). Engineers reading the file from disk use **+0xBA** directly — there is **no
0x18 shift**. The loader's primary branch tests this discriminator for **`!= 14`**: one handling path
is taken when the value is 14, the other when it is not.

Alongside the discriminator the loader consults **four dispatch flag bytes at on-disk +0xCD, +0xCE,
+0xCF, +0xD0**; for each, an `== 1` maps to comparison codes **1, 26, 11, 16** respectively. The
per-flag semantics are DBG-pending.

This **corrects** the prior model that placed an `item_type_tag` branch at `+0xB8`: that offset and
that branch are REFUTED (see §1.7). It also **supersedes** the later +0xD2 reading: an earlier
revision of this spec claimed the loader staged the record into a working buffer 0x18 bytes ahead of
the record start, normalizing the discriminator to "on-disk +0xD2" and the flags to +0xE5..+0xE8.
That **+0x18-shift model is REFUTED** — the binary (arbitrated against the doida.exe IDB this cycle)
reads the block directly into the staging buffer with no shift, anchored by the CONFIRMED
`effect_count` field at on-disk +0x220 landing at the same staging buffer; measuring the discriminator
and flags against that same buffer base yields **+0xBA** and **+0xCD..+0xD0** with no shift. The full
enumeration of discriminator values and what each selected handling path does is **DBG-pending** —
only the offset and the `!= 14` split are established; do not invent the remaining category meanings.

#### 1.4.2 Asset-key resolution — how +0x80 / +0x84 become real VFS paths

`items.scr` does **not** retain a parsed-items array that is later path-resolved. While the loader
walks the file, **each record's `model_ref_key` (+0x80) and `anim_ref_key` (+0x84) are stored
verbatim** — as the two 32-bit words of a small value node — **into a single process-global
actor-visual catalogue** (a sorted map). This is the *same* catalogue that `data/char/skin.txt`
feeds, so an item's visual model rides the identical mechanism as a character skin. The node is keyed
by a composite id the loader derives from the record's uid, discriminator, dispatch code, and an
actor-visual class/base field on the catalogue. Path resolution is **deferred to actor-spawn time**:
when an actor part attaches, the catalogue is queried by the same composite key, the stored
`{model_ref_key, anim_ref_key}` pair is recovered, and only then do the two keys diverge into asset
I/O. (The composite arithmetic only forms the lookup key — the +0x80/+0x84 values themselves are
stored and recovered unchanged.) CONFIRMED that the values ride this shared catalogue (loader
control-flow + the matching skin.txt insertion are both explicit).

- **`model_ref_key` (+0x80) → a `.skn` MESH path.** At spawn the skin cache, on a miss, formats the
  numeric key into **`data/char/skin/g%d.skn`** and opens/parses the `.skn`. The loaded skin then runs
  the existing `.skn` → `skin.txt` → texture chain downstream (out of scope here; see the character
  skin chain). So +0x80 is a numeric `.skn` mesh selector; the on-disk path is
  **`data/char/skin/g<model_ref_key>.skn`**. Confidence **HIGH** (the format string and the `.skn`
  open are both explicit).

- **`anim_ref_key` (+0x84) → a bind-pose/skeleton POOL id (NOT a direct printf).** At spawn the key is
  looked up **by id** in the global bind-pose/skeleton pool — the pool that is pre-populated *by name*
  earlier in boot from `bindlist.txt` (the `data/char/bind/g{id}.bnd` skeletons and their
  `data/char/mot/g{id}.mot` motions, the established character skeleton/idle chain). The resolved pose
  handle is stored on the skin and ref-counted. So +0x84 selects **which bind skeleton + motion set
  the item's mesh binds to**, indexing the pre-loaded pose pool rather than naming a file directly.
  Confidence: **HIGH** that it is an id into the pose pool (the by-id lookup is explicit); **MEDIUM**
  that the exact item-side `g{id}.bnd` / `g{id}.mot` file is the literal 1:1 pool member.

> **OPEN-RISK — `anim_ref_key` (+0x84) exact bind file.** The precise `g{id}.bnd` / `g{id}.mot` file
> that an item's +0x84 id selects from the pre-loaded pool is **not byte-pinned**. The wiring (a by-id
> lookup into the pool seeded from `bindlist.txt` / `g{id}.bnd`) is established statically; the exact
> 1:1 id→file mapping on the *item* side would need either tracing the pool-seeding key assignment or
> a debugger pass at actor spawn. No guess is recorded. The attach site uses only the pool indirection
> — no item-side `g%d.bnd` / `g%d.mot` printf was observed.

This **refines** §1.7 item 2 (which already retired the "free numeric stats at +0x080/+0x084"
reading): both fields are asset-resolution keys, now with their concrete resolution shapes
(`.skn` mesh path for +0x80; bind-pose pool id for +0x84). The loader reads the leading 548-byte
block verbatim into the staging buffer (read base = record start), so the on-disk discriminator in
§1.4.1 (+0xBA) is unaffected by this lane.

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
- The full enumeration of `record_discriminator` (on-disk +0xBA) values beyond the established
  `!= 14` split, and what each selected handling path does — DBG-pending.
- The per-flag semantics of the four dispatch flag bytes at on-disk +0xCD..+0xD0 (`== 1` maps to
  codes 1 / 26 / 11 / 16) — DBG-pending.
- Consumer semantics of the opaque fields at +0x0A4, +0x200, and +0x21C — DBG-pending (read their
  bytes, assign no meaning until a live-debugger pass settles them).
- Semantic layout of the trailing 8-byte effect entries (§1.5); which field, if any, carries
  per-enchant stat deltas.
- Reserved bytes at +0x221..+0x223.

### 1.7 SUPERSEDED / REFUTED — do not reintroduce

Earlier models of `items.scr` were proven wrong and must NOT be revived. They are recorded here only
so the same mistakes are not made again.

1. **`item_type_tag` discriminator at `+0x0B8`, AND the later `+0xD2` / `+0x18-shift` reading.** Two
   successive readings here are now REFUTED:
   - An earlier spec placed the record's category / dispatch field at `+0x0B8` and treated it as a
     packed item-type tag. **REFUTED** — no branch reads `+0x0B8`.
   - A subsequent revision claimed the loader staged the record into a working buffer **0x18 bytes
     ahead** of the record start, normalizing the discriminator to **on-disk +0xD2** and the four
     dispatch flags to +0xE5..+0xE8. **That +0x18-shift model is itself REFUTED.** The binary
     (arbitrated against the doida.exe IDB, SHA 263bd994, CYCLE 7) reads the 548-byte block
     **directly** into the staging buffer — the read base IS the record start, with **no shift**.
   The field the loader actually branches on is the **discriminator at on-disk +0xBA**, tested
   **`!= 14`**, with the four dispatch flags at on-disk **+0xCD..+0xD0** (see §1.4.1). The
   staging-buffer base is iron-clad because the CONFIRMED `effect_count` field at on-disk +0x220
   lands at the same staging buffer; measured against that base the discriminator is +0xBA and the
   flags +0xCD..+0xD0 with no shift. Do not reintroduce `+0x0B8`, `+0xD2`, the `+0x18-shift /
   0x18-ahead working buffer` notation, or the +0xE5..+0xE8 flag offsets. (Authority: two-witness —
   loader branch + black-box; CYCLE 7 IDB arbitration against the +0x220 effect_count anchor.)

2. **Free numeric stats at `+0x080` / `+0x084`.** An earlier spec read these as inferred
   `template_ref` / `template_ref_b` numeric stats of UNVERIFIED role. They are now **resolved asset
   keys**, not free numeric stats: `+0x080` is the **mesh selector** that resolves to
   `data/char/skin/g<key>.skn`, and `+0x084` is the **bind-pose/skeleton-pool id**. Both are stored
   verbatim into the shared actor-visual catalogue at load and resolved at actor-spawn — see §1.4.2 for
   the full resolution chain.

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
| 0x36 | 1 | u8 | `unknown_36` | Non-zero; value varies per record; purpose unknown. | CONFIRMED present; role UNVERIFIED |
| 0x37 | 1 | u8 | `item_category` | Type/category byte; the cash-shop info-panel consumer special-cases value `== 10` (distinct display path). Role of other values DBG-pending. | CONSUMER-INFERRED (branch offset confirmed; semantics partial) |
| 0x38 | 4 | u32 | `cash_price_nx` | Cash-shop price in NX cash points (small values; sample record 0 = 950). | SAMPLE-VERIFIED (value); role INFERRED |
| 0x3C | 4 | u32 | `slot_seq_2` | Increments per record; possibly a second slot or category index. | CONFIRMED present; role UNVERIFIED |
| 0x40 | 4 | u32 | `icon_id_a` | Icon/texture id A. The cash-shop display consumer reads this field; if nonzero, uses it as the icon identifier. When zero, falls back to `icon_id_b` at +0x48. | CONSUMER-INFERRED (selection rule confirmed; exact texture chain UNVERIFIED) |
| 0x44 | 4 | bytes | (pad / unknown) | Zero in observed records. | CONFIRMED (zero); role UNVERIFIED |
| 0x48 | 4 | u32 | `icon_id_b` | Fallback icon/texture id used by the consumer when `icon_id_a` (+0x40) is zero. High-range values observed (sample record 0 = 17,883,710). | CONSUMER-INFERRED (fallback role); role INFERRED |
| 0x4C | 4 | u32 | `flag_4C` | Value 1 in all observed records. | SAMPLE-VERIFIED |
| 0x50 | … | bytes | (zero region) | Largely zero through the middle of the record, up to the description block at 0x0E4. | CONFIRMED (zero-heavy) |
| 0x0E4 | 81 | CP949[] | `desc_para[0]` | Description paragraph 0. CP949, NUL-terminated within the 81-byte buffer, then zero-padded. | SAMPLE-VERIFIED (512/512) |
| 0x135 | 81 | CP949[] | `desc_para[1]` | Description paragraph 1. | SAMPLE-VERIFIED (512/512) |
| 0x186 | 81 | CP949[] | `desc_para[2]` | Description paragraph 2. | CONFIRMED (512/512) |
| 0x1D7 | 81 | CP949[] | `desc_para[3]` | Description paragraph 3. | CONFIRMED (512/512) |
| 0x228 | 81 | CP949[] | `desc_para[4]` | Description paragraph 4 (populated in fewer records). | CONFIRMED (512/512) |
| 0x279 | 81 | CP949[] | `desc_para[5]` | Description paragraph 5 (populated in fewer records). | CONFIRMED (512/512) |
| 0x2CA | 81 | CP949[] | `desc_para[6]` | Description paragraph 6 (structural capacity; rarely populated in samples — see §2.4). | CONFIRMED (capacity) |
| 0x31B | 81 | CP949[] | `desc_para[7]` | Description paragraph 7 (structural capacity; rarely populated). | CONFIRMED (capacity) |
| 0x36C | 81 | CP949[] | `desc_para[8]` | Description paragraph 8 (structural capacity; rarely populated). | CONFIRMED (capacity) |
| 0x3BD | 81 | CP949[] | `desc_para[9]` | Description paragraph 9 (structural capacity; rarely populated). Block ends at 0x40E (`+0xE4 + 10·81 = 0x40E`). | CONFIRMED (capacity) |
| 0x40E | 10 | bytes | (record tail — unmapped) | Bytes 0x40E..0x417 (10 bytes), after the 10-paragraph description block; content unmapped, likely duration / equip requirements / graphic id. | UNVERIFIED |
| 0x418 | 1 | u8 | `tail_flag_418` | The cash-shop info-panel consumer reads this byte and gates a display branch on its value. Exact semantics DBG-pending. | CONSUMER-INFERRED (branch confirmed; semantics UNVERIFIED) |
| 0x419 | 3 | bytes | (record tail — unmapped) | Bytes 0x419..0x41B; content unmapped. | UNVERIFIED |

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

### 2.4 citems.scr — description block (fixed 81-byte paragraphs; COUNT = 10, CONFIRMED)

The description is NOT a single contiguous buffer. It is a block of **ten fixed-offset paragraphs,
each 81 bytes (0x51) wide**, CP949 text, NUL-terminated within each paragraph then zero-padded,
starting at **+0x0E4**:

```
desc_para[i] start = 0x0E4 + i * 81     (i = 0 .. 9)
desc_para_width    = 81 (0x51) bytes    (applies to every paragraph)
desc_para_count    = 10                 (structural capacity; CONFIRMED)
```

Paragraph start offsets (all ten): 0x0E4, 0x135, 0x186, 0x1D7, 0x228, 0x279, 0x2CA, 0x31B, 0x36C,
0x3BD. The block ends at **0x40E** (`0x0E4 + 10·81 = 228 + 810 = 1038 = 0x40E`), leaving a 14-byte
non-paragraph tail at 0x40E..0x41B inside the 1052-byte (0x41C) record. The first two paragraphs
(`+0x0E4`, `+0x135`) are SAMPLE-VERIFIED to hold CP949 text; the 81-byte stride between them is
confirmed. An empty paragraph is all-zero.

> **RESOLVED — paragraph count is 10 (structural capacity), CONFIRMED.** The former 6-vs-10 conflict
> is settled in favour of **10**:
>
> - The runtime paragraph accessor **hard-bounds the index at `< 10`** and returns
>   `record_base + 81·index + 228` (paragraph base +0xE4, width 81 / 0x51 bytes); the cash-shop
>   description-builder loops `i < 10`. The structural CAPACITY is unambiguously **10**, not 6.
> - A `'#'`-first-byte sentinel paragraph (an 81-byte paragraph whose first byte is `'#'`)
>   **early-terminates** the consumer at runtime. This is why VFS samples populate only the first few
>   paragraphs — 6 was merely the count of *typically-populated* paragraphs, not the capacity.
> - **No overflow.** The 10-paragraph block ends at `0x0E4 + 10·81 = 0x40E` = byte 1038, INSIDE the
>   1052-byte (0x41C) record (14-byte tail at 0x40E..0x41B). The earlier "10×81 overflows by 2 bytes
>   (ends at 0x41E)" worry was a hex-arithmetic slip — `0xE4 + 810` is `0x40E`, not `0x41E`.
> - The conflict was inconclusive earlier because the count is NOT in the loader: the loader copies
>   the whole 1052-byte record **verbatim** (no per-paragraph loop), so it exposes no count constant.
>   The count lives in the **consumer** accessor (the `< 10` index bound), so the loader-only witness
>   was correctly inconclusive — the consumer witness settles it. CONFIRMED (code-confirmed, static;
>   compile-time constant in the binary). (Resolves: Campaign 10 D8 §3.2 CONFLICT B / §7 action item 1.)
>
> Engineering note: the number of *populated* paragraphs per record is data-dependent and
> runtime-terminated by the `'#'` sentinel; read up to 10 paragraphs but stop at the first `'#'`-sentinel
> paragraph (a paragraph whose first byte is `'#'`). Do not hard-code a count of 6.

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
  starting at 0x0E4 described in §2.4 — **ten paragraphs (capacity)**, runtime-terminated early by a
  `'#'` sentinel. The former 6-vs-10 count conflict is RESOLVED in favour of 10 (see §2.4); do not
  re-document a 6-paragraph block.
- **`unknown_36` was formerly a u16 spanning +0x36..+0x37.** CYCLE 11 consumer analysis split it into
  `unknown_36` (u8 at +0x36, role still unknown) and `item_category` (u8 at +0x37, consumer branches
  `== 10`). The two-byte region is identical on disk; only the labelling is updated.
- **`pad_40` (8-byte zero region) is now split.** CYCLE 11 consumer analysis revealed that +0x40 (4 B)
  is read by the cash-shop display consumer as `icon_id_a` (icon selector A; nonzero → use it, else
  fall back to +0x48). +0x44 (4 B) remains an unverified zero region. Do not treat +0x40 as padding.
- **`item_uid` at +0x48 is renamed `icon_id_b`.** The field holds high-range ids used as the
  fallback icon when `icon_id_a` is zero; its uid semantics are UNVERIFIED. Do not assume a monotonic
  uid role.

### 2.6 citems.scr — Known Unknowns

- `unknown_36` (u8 at +0x36) value and purpose.
- `item_category` (u8 at +0x37): the consumer special-cases value `== 10`; meaning of other values is DBG-pending.
- `slot_seq_2` (+0x3C) role relative to item categorisation.
- `icon_id_a` (+0x40) and `icon_id_b` (+0x48): confirmed as the consumer's icon-selection pair
  (if +0x40 nonzero use it, else use +0x48); the exact texture-chain they feed is UNVERIFIED.
- `tail_flag_418` (u8 at +0x418): consumer gates a display branch on this byte; semantics DBG-pending.
- Unmapped tail bytes 0x40E..0x417 and 0x419..0x41B.

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
| Per-record unique id | `item_uid` u32 at +0x034 | none mapped (high-range `icon_id_b` at +0x048 was formerly labelled `item_uid`; its uid role is UNVERIFIED) |
| Description | CP949 from +0x038, bounded within the fixed block | 10 × 81-byte paragraphs from +0x0E4 (capacity; `'#'`-sentinel early-terminated; CONFIRMED, §2.4) |
| Asset refs / discriminator | `model_ref_key` +0x080 → `data/char/skin/g<key>.skn`, `anim_ref_key` +0x084 → bind-pose pool id (both via the shared actor-visual catalogue, §1.4.2); discriminator on-disk +0x0BA `!= 14`; dispatch flags +0x0CD..+0x0D0 | — |
| Price field | none confirmed (asset-ref keys at +0x080/+0x084; +0x0A4 opaque) | NX cash points at +0x038 |
| Schema | regular item master | cash-shop item master |
| Overall confidence | framing SAMPLE-VERIFIED; +0x80 (.skn path) HIGH / +0x84 (bind-pose pool id) HIGH–MEDIUM; discriminator +0xBA settled (no 0x18 shift); other stat ROLES UNVERIFIED/DBG-pending | SAMPLE-VERIFIED; desc paragraph COUNT = 10 CONFIRMED (§2.4) |

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
  `model_ref_key` (+0x080) and `anim_ref_key` (+0x084) as resolved asset-reference keys — +0x080 is the
  `.skn` mesh selector resolving to `data/char/skin/g<key>.skn` (then the `.skn` → skin.txt → tex
  chain), and +0x084 is a bind-pose/skeleton-pool id selecting a `g{id}.bnd` skeleton + `g{id}.mot`
  motions from the pre-loaded pool; both are recovered at actor-spawn from the shared actor-visual
  catalogue (§1.4.2). The exact item-side `g{id}.bnd`/`.mot` file for +0x084 is OPEN-RISK (not
  byte-pinned). The per-record **discriminator** is at **on-disk +0x0BA** with the loader's branch
  testing `!= 14` (the loader reads the 548-byte block directly into the staging buffer, read base =
  record start, so staging-buffer +0xBA = on-disk +0xBA — there is NO 0x18 shift; the four dispatch
  flags sit at on-disk +0x0CD..+0x0D0) — read it, but the full meaning of each discriminator value is
  DBG-pending. `items.scr` carries **no description-paragraph block** (the `10 × 81`-byte /
  `'#'`-sentinel model is citems-only). Treat the fields at **+0x0A4, +0x200, +0x21C** as **OPAQUE /
  DBG-pending** — read their bytes but assign no semantics until a live-debugger pass settles them.
  Do NOT read a field at +0x0B8 or +0x0D2 (both REFUTED).
- **citems.scr** is safe to implement: iterate 512 records at 1052-byte stride; read `item_id`
  (u32 at +0x00 — **not** a slot index), `item_name` (+0x04, 48 B CP949), `item_category` (u8 at
  +0x37; consumer special-cases `== 10`), `cash_price_nx` (+0x38, u32), `icon_id_a` (+0x40, u32 —
  nonzero means use this as icon id) / `icon_id_b` (+0x48, u32 — fallback when `icon_id_a == 0`),
  and the description paragraphs (`0x0E4 + i*81`, 81 bytes each, CP949 NUL-terminated). **The
  description block holds 10 paragraphs (capacity)**; iterate `i` from 0 to 9 and **stop at the
  first `'#'`-sentinel paragraph** (first byte `'#'`), which is the runtime early terminator (§2.4).
  Do not hard-code a count of 6 (it would silently drop later text). The tail at 0x40E..0x41B (14
  bytes) follows; `tail_flag_418` (u8 at +0x418) is consumer-gated — read it but assign no semantics
  until debugger-confirmed. If filtering billing/premium items, the threshold is
  `item_id >= 100000` (see §2.2 billing-filter note).
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
  `anim_ref_key` (u32 at +0x084), `record_discriminator` (u8 at on-disk +0x0BA, tested `!= 14`),
  `effect_count` (u8 at +0x220), `EffectEntry` (8-byte trailing record); for citems.scr —
  `item_id` (u32 at +0x000, **not** `slot_index`), `item_category` (u8 at +0x037),
  `cash_price_nx` (u32 at +0x038), `icon_id_a` (u32 at +0x040), `icon_id_b` (u32 at +0x048),
  `tail_flag_418` (u8 at +0x418), `slot_seq_2`,
  `citems_stride = 1052`, `citems_desc_para_width = 81`, `citems_desc_para_base = 0x0E4`,
  `citems_desc_para_count = 10` (capacity; `'#'`-sentinel early-terminated).
  Candidate loader names from the dirty-room (orchestrator-owned): `ItemsScr_LoadFile`,
  `CitemsScr_LoadFile`.
- **Provenance:** see `Docs/RE/journal.md` — promotion entries: CAMPAIGN VFS-MASTERY (two-witness:
  loader + black-box) settled `+0x80/+0x84` as model/anim ref keys and REFUTED the `+0x0B8
  item_type_tag` branch and left `+0x0A4 / +0x200 / +0x21C` opaque (DBG-pending). Campaign 10 D8
  (two-witness, IDB SHA 263bd994 + VFS sample) placed the record discriminator at on-disk +0x0D2 via a
  "+0x18-shifted working buffer" model, surfaced four dispatch flags at +0x0E5..+0x0E8, corrected the
  citems.scr `+0x00` field from `slot_index` to `item_id`, and raised the citems.scr
  description-paragraph **count** (6 vs 10) as an OPEN conflict pending a sample probe. **CORRECTED
  CYCLE 7** (static-IDA, IDB SHA 263bd994, 2026-06-20) REFUTES that +0x18-shift model: the loader
  reads the 548-byte block **directly** into the staging buffer (read base = record start, anchored by
  the CONFIRMED `effect_count` at on-disk +0x220), so the **discriminator is on-disk +0x0BA** (`!= 14`)
  and the **four dispatch flags are on-disk +0x0CD..+0x0D0** — with NO shift. CYCLE 7 also confirms
  `items.scr` carries **no paragraph block** (the `10 × 81` / `'#'`-sentinel paragraph model is
  citems-only). **CORRECTED CYCLE 1** (static-IDA, IDB SHA 263bd994, 2026-06-19)
  resolved both: (1) `items.scr` +0x080 / +0x084 are asset-resolution keys recovered at actor-spawn
  from the shared actor-visual catalogue — +0x080 → a `.skn` mesh path `data/char/skin/g<key>.skn`
  (printf selector, HIGH), +0x084 → a bind-pose/skeleton-pool id resolved against the pre-loaded
  `g{id}.bnd`/`g{id}.mot` pool (HIGH it is a pool id; the exact item-side file is OPEN-RISK / not
  byte-pinned) — see §1.4.2; (2) the citems.scr description-paragraph **count is 10** (structural
  capacity), CODE-CONFIRMED by the consumer paragraph accessor's `< 10` index bound and the
  `'#'`-sentinel early-termination — the loader's verbatim record copy carries no count constant, so
  the loader-only witness was correctly inconclusive — resolving the 6-vs-10 conflict (§2.4).
