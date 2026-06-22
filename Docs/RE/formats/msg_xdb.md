# Format: .xdb (message catalogue — `msg.xdb`, the client-wide caption string table)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code, NO
> literal caption strings. Promoted from dirty-room VFS-harness + static notes under EU Software
> Directive 2009/24/EC Art. 6, solely to achieve interoperability. No decompiler output and no
> binary addresses appear below. The committed caption text itself is **read from the VFS at
> runtime** — this spec lists only caption IDs and their roles, never the Korean strings.
> Consumed by `Assets.Parsers` (decode side) and the front-end UI layer (lookup side). Every
> offset an engineer cites must reference this file.

---

## Status

```
verification: sample-verified            # stride, record count, file size, +0x000 id, +0x004 CP949 text, 0xEE padding all byte-exact against the real VFS sample
ida_reverified: 2026-06-21
ida_anchor: 263bd994
evidence: [static-ida, vfs-sample]
conflicts: none
```

| Item | Value |
|------|-------|
| `sample_verified` | **true** — record stride, record count, and file size are byte-exact from VFS-harness observation; record layout cross-checked against the targeted front-end caption IDs. RE-CONFIRMED on build 263bd994 (2026-06-16): size 1,364,304 = 2,644 × 516 exactly; record 0 `caption_id = 1`, CP949 text at +0x004 with a `0x00` terminator inside the buffer and `0xEE` fill after. |
| Endianness | Little-endian |
| Magic / signature | **None** — there is no file header; the first record begins at byte 0 |
| Version field | None |
| Encoding | CP949 (code page 949) for the string payload |
| Loaded from | The boot path opens `data/script/msg.xdb` directly during the main-window startup sequence (NOT via the shared boot data-table corpus loader that handles the five small `.xdb` tables). The loader follows the generic fixed-stride disk-file read pattern (open-by-name → size → read-virtual → close). CONFIRMED (loader control flow). |

---

## Identification

- **Extension:** `.xdb` (this spec covers the message catalogue instance `msg.xdb`; other `.xdb`
  files in the client, e.g. `effectscale.xdb`, are unrelated flat tables with their own layouts).
- **Found in:** `.pak` / VFS archive; logical path `data/script/msg.xdb`.
- **Role:** flat, header-less array of fixed-size caption records. Each record pairs a numeric
  caption ID with a fixed-width CP949 text buffer. The whole front-end and in-game UI looks up
  display strings by caption ID against this single catalogue.
- **Magic / signature:** none. The format is identified by path and by its exact-multiple file
  size (record stride divides the file size with zero residual).
- **Endianness:** little-endian for the integer key.

---

## File layout

The file is a **header-less flat array** of fixed **516-byte records**. Record 0 begins at file
byte 0. There is no count prefix, no directory, and no footer.

- **Record stride:** **516 bytes** (0x204). CONFIRMED.
- **Record count source:** derived from file size — `record_count = file_size / 516`. CONFIRMED
  (file size is an exact integer multiple of the stride with zero remainder).
- **File size:** **1,364,304 bytes** in the sampled VFS. SAMPLE-VERIFIED (build 263bd994).
- **Observed record count:** exactly **2,644** records (`1,364,304 / 516 = 2,644`, zero remainder).
  SAMPLE-VERIFIED. Record 0 carries `caption_id = 1`; the first IDs ascend `1, 2, 3, 4, …`.

---

## Record layout (516 bytes, stride = 0x204)

| Sub-offset | Size | Type | Field | Notes | Confidence |
|---:|---:|------|-------|-------|------------|
| +0x000 | 4 | i32 LE | `caption_id` | Numeric caption identifier (the lookup key). | CONFIRMED |
| +0x004 | 512 | char[512] | `text` | CP949-encoded caption string, NUL-terminated (`0x00`) within the fixed 512-byte field. The bytes AFTER the terminator are filled with the client sentinel **`0xEE`**, NOT `0x00` (see padding note below) — a decoder must stop at the first `0x00` and must not assume the tail is zero-filled. An empty string is valid (a record may carry a zero-length caption). | CONFIRMED (layout, padding byte); CP949 CONFIRMED |

- The string field is a **fixed 512-byte buffer**, not a length-prefixed or variable-length string.
  Decode it as CP949 up to the first NUL byte (`0x00`).
- **Padding byte = `0xEE` (CONFIRMED).** The bytes following the NUL terminator inside the 512-byte
  buffer are filled with the client sentinel value `0xEE`, not `0x00`. This is a deliberate fill, not
  garbage. A decoder MUST terminate the string at the first `0x00`; it must NOT scan for `0xEE`, treat
  `0xEE` as content, or assume a zero-filled tail. (Practical effect: read CP949 up to the first
  `0x00`; everything after is `0xEE` padding and is discarded.)
- The four-byte key at +0x000 plus the 512-byte text buffer at +0x004 sum to the 516-byte stride
  exactly; there are no other fields and no inter-record padding.

---

## Lookup model

- Records are stored in **ascending `caption_id` order**; the runtime resolves a caption by
  **ordered-map lower-bound (binary search) on `caption_id`**. CONFIRMED.
- **The key comparison is UNSIGNED.** Although `caption_id` is stored as a 32-bit integer at +0x000,
  the ordered map orders and searches keys with an **unsigned** comparison. A re-implementation must
  use unsigned key ordering (e.g. a `uint`-keyed sorted map); using signed comparison would mis-order
  any caption ID with the high bit set. CONFIRMED.
- A reimplementation may load the whole table into a sorted dictionary keyed by `caption_id`; the
  on-disk ascending order is what makes a binary search valid, but an engineer is free to index it
  in any structure that preserves key→text resolution.
- Caption IDs are **sparse** — the ID space is not contiguous and the record index is NOT the
  caption ID. Always resolve by key, never by record position.

---

## Front-end caption-ID index (ID → role; strings read from the VFS at runtime)

The following front-end caption IDs are present in the catalogue and are consumed by the
login / server-list and character-select scenes. Each row gives the ID and its UI role only — the
actual CP949 text is read from `msg.xdb` at runtime and is intentionally NOT reproduced here.

### Login / server-list scene

| caption_id | Scene | Role | Confidence |
|---:|-------|------|------------|
| 4001–4022 | login notice panel | The 22 labels of the **static stacked notice/agreement text column** on the login notice panel. Built in a loop (`caption_id = 4001 + i`, `i = 0..21`); each label seat is `(X = 50, Y = 100 + 18·i, W = 383, H = 50)`. The labels overlap vertically (50 px tall, 18 px stride) to form a single tall left-aligned paragraph block. There is **no EULA/terms panel and no scroll/accept gate** anywhere in the login construct. | RESOLVED (CODE-CONFIRMED — `specs/frontend_scenes.md §1.4c / §11.2i`; `specs/login.md §1.5 / §7.1`) |
| 4023 | login / server-list | Body text of the "connecting to server" wait dialog. | RESOLVED |
| 4024 | login / server-list | Body text of the "could not connect to server" failure dialog. | RESOLVED |

> **Correction (CODE-CONFIRMED — IDB SHA 263bd994):** IDs 4001–4022 are the labels of a
> **static stacked notice/agreement text column on the login notice panel** — they are NOT EULA /
> Terms-of-Service body text, NOT server-name or channel-row labels, and NOT gated by any
> scroll/accept step. An element-by-element walk of the login scene builder (73 widgets, in build
> order) confirms no terms/agreement panel is constructed anywhere in the login build, and no
> substate ever shows or gates on one. Server and channel names are received dynamically from the
> server-list fetch (network) and are never sourced from this catalogue. Sources:
> `specs/frontend_scenes.md §1.4c / §11.2i` and `specs/login.md §1.5 / §7.1`.

### Character-select scene

| caption_id | Scene | Role | Confidence |
|---:|-------|------|------------|
| 2206 | char-select | Healthy-game-use advisory notice (regulatory welfare message), shown in the char-select area. | RESOLVED |
| 2209 | char-select | Slot-count caption (the per-slot occupancy label; the builder copies it once and writes the per-slot occupancy word at +0x66). | RESOLVED |
| 14001 | char-select | Generic input-field placeholder label ("please enter"-style prompt). | RESOLVED |
| 14002 | char-select | Body text of the delete-character confirmation dialog. | RESOLVED |
| 14003 | char-select / create-form | Per-class name caption for class Musa — set on the corresponding class-select button click. | RESOLVED |
| 14004 | char-select / create-form | Per-class name caption for class Salsu (Assassin). | RESOLVED |
| 14005 | char-select / create-form | Per-class name caption for class Dosa (Wizard). | RESOLVED |
| 14006 | char-select / create-form | Per-class name caption for class Monk. | RESOLVED |
| 14007 | char-select / create-form | Per-class name caption for the fifth class slot (additional class if present). | RESOLVED |
| 2190 | char-select / create-form | Character-name validation failure: name field is empty. | RESOLVED |
| 2075 | char-select / create-form | Character-name validation failure: name contains a banned word. | RESOLVED |
| 12012 | char-select / create-form | Character-name validation failure: name fails charset or length check. | RESOLVED |
| 46001 | char-select | Instruction label of the name-change sub-dialog (prompt to type the new name). | RESOLVED |
| 46002 | char-select | Field label ("Name") for the name-change input box. | RESOLVED |
| 48001 | char-select | Emergency-teleport confirmation prompt (force-move the character to the main town). | RESOLVED |
| 48003 | char-select | Sub-label for the emergency-teleport button (recommended-when-stuck note). | RESOLVED |
| 48004 | char-select | Emergency-teleport cooldown notice (usable once per 30 minutes). | RESOLVED |
| 48005 | char-select | Emergency-teleport fallback-advice line. | RESOLVED |
| 63030 | char-select | Tutorial-gate blocking notice (char-select entry blocked until tutorial is completed). | RESOLVED |

> **Class-name captions (14003–14007):** the class-button order left → right in the create form is
> Monk / Musa / Dosa / Salsu (enum 4 / 1 / 3 / 2); the captions 14003–14007 are assigned per
> button click, not in display order. Class-name text is read at runtime from the VFS — not
> reproduced here. (Source: `Docs/RE/_dirty/shared_assets_pipeline.md §5`.)

> **ID 48002** (unreferenced in the observed build-scene call sites) is not indexed here.

### PIN modal — NO catalogue entries (by design)

The PIN / second-password modal has **no `msg.xdb` caption IDs**. All of its labels (digit-input
title, reset, confirm, cancel, warning text) are **baked texture art** inside the PIN window
artwork (`data/ui/password.dds`), not catalogue strings. Implementers must source PIN labels from
that texture, not from a caption lookup. CONFIRMED (absence).

---

## Known unknowns

- The **full caption-ID range** of the catalogue beyond the targeted front-end set is not
  enumerated here. The ~2,644-record count is observed, but a complete ID census is out of scope
  for this front-end spec.
- Whether any caption record uses the **full 512 bytes** without a null terminator (i.e. an exactly
  512-byte non-terminated string) was not observed in the targeted set; treat the field as
  "CP949 up to the first null, else the whole field" to be safe.
- Any **secondary message catalogue** (a second `.xdb` with the same record shape for other locales
  or subsystems) was not surveyed; this spec covers `data/script/msg.xdb` only.

---

## Cross-references

- **Container:** `formats/pak.md` (the VFS that delivers `msg.xdb`).
- **Front-end scene flow** that consumes these captions: `specs/frontend_scenes.md` (login state
  machine, login notice panel static text column — no EULA/accept gate, see `§1.4c / §11.2i` —
  and char-select chrome).
- **PIN modal** (which has no catalogue entries — labels are baked art): see the PIN / second-
  password coverage in the front-end scene spec.
- **Encoding:** all caption text is CP949; register the code-page provider once and decode with
  code page 949 (project-wide convention).
- **Glossary:** see `Docs/RE/names.yaml`.
- **Provenance:** see `Docs/RE/journal.md` (add an entry for this spec).

> **Provenance — CAMPAIGN VFS-MASTERY-B (two-witness reconcile: loader + black-box over `msg.xdb`):**
> re-confirmed stride 516 (0x204), header-less, `caption_id` i32 @+0x000, CP949 text @+0x004 (512-byte
> buffer); added the **`0xEE` padding-byte** fact (post-terminator fill is the client sentinel `0xEE`,
> not `0x00`) and the **unsigned key-comparison** fact for the ordered map. Promoted as neutral prose;
> no addresses, no decompiler output, and no caption strings/sample bytes crossed the firewall.
>
> **Provenance — CAMPAIGN 10 Block D (re-verify on build 263bd994, 2026-06-16; two witnesses: static
> loader read + VFS sample):** ALL claims RE-CONFIRMED [sample-verified] with zero drift — file size
> 1,364,304 = 2,644 × 516 exactly, record 0 `caption_id = 1`, CP949 text terminated by `0x00` inside the
> buffer with `0xEE` fill after, ascending ids. New fact folded in: `msg.xdb` is loaded **directly from
> the main-window startup path** (not through the shared boot data-table corpus loader that handles the
> five small `.xdb` tables — see `xdb_tables.md`). No addresses or decompiler output crossed the firewall.
>
> **Provenance — 2026-06-21 (IDB SHA 263bd994, static-only re-walk; source `Docs/RE/_dirty/shared_assets_pipeline.md`):**
> front-end caption-ID index extended. Re-walk of the login and char-select `BuildScene` call sites confirmed
> `msg.xdb` load ordering (loaded once at state-1 entry, before the login window ctor and before
> `BuildScene`). New char-select caption IDs added: 2209 (slot-count), 14003–14007 (per-class create
> labels), 2190 / 2075 / 12012 (name-validation failure responses). All other claims re-confirmed with
> zero drift. No addresses or decompiler output crossed the firewall.
