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

| Item | Value |
|------|-------|
| `sample_verified` | **true** — record stride, record count, and file size are byte-exact from VFS-harness observation; record layout cross-checked against the targeted front-end caption IDs |
| Endianness | Little-endian |
| Magic / signature | **None** — there is no file header; the first record begins at byte 0 |
| Version field | None |
| Encoding | CP949 (code page 949) for the string payload |

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
- **Observed record count:** approximately **2,644** records in the sampled VFS. SAMPLE-VERIFIED.

---

## Record layout (516 bytes, stride = 0x204)

| Sub-offset | Size | Type | Field | Notes | Confidence |
|---:|---:|------|-------|-------|------------|
| +0x000 | 4 | i32 LE | `caption_id` | Numeric caption identifier (the lookup key). | CONFIRMED |
| +0x004 | 512 | char[512] | `text` | CP949-encoded caption string, null-terminated within the fixed 512-byte field; trailing bytes after the terminator are unused (padding). An empty string is valid (a record may carry a zero-length caption). | CONFIRMED (layout); CP949 CONFIRMED |

- The string field is a **fixed 512-byte buffer**, not a length-prefixed or variable-length string.
  Decode it as CP949 up to the first null byte (or the full 512 bytes if no null is present).
- The four-byte key at +0x000 plus the 512-byte text buffer at +0x004 sum to the 516-byte stride
  exactly; there are no other fields and no inter-record padding.

---

## Lookup model

- Records are stored in **ascending `caption_id` order**; the runtime resolves a caption by
  **ordered-map lower-bound (binary search) on `caption_id`**. CONFIRMED.
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
| 4001–4021 | login / EULA gate | The 21 sequential lines of the Terms-of-Service / EULA passage shown in the scrollable agreement panel that gates login. Displayed in order as a continuous block. | RESOLVED (VFS-harness) |
| 4022 | login / EULA gate | Trailing blank line of the EULA block (empty string). | RESOLVED |
| 4023 | login / server-list | Body text of the "connecting to server" wait dialog. | RESOLVED |
| 4024 | login / server-list | Body text of the "could not connect to server" failure dialog. | RESOLVED |

> **Semantic note (important reconciliation):** IDs 4001–4022 are the **EULA / Terms-of-Service
> text**, NOT server-name or channel-row labels. Server and channel names are received dynamically
> from the server-list fetch (network), and are never sourced from this catalogue. Only 4023–4024
> are dynamic dialog bodies.

### Character-select scene

| caption_id | Scene | Role | Confidence |
|---:|-------|------|------------|
| 2206 | char-select | Healthy-game-use advisory notice (regulatory welfare message), shown in the char-select area. | RESOLVED |
| 14001 | char-select | Generic input-field placeholder label ("please enter"-style prompt). | RESOLVED |
| 14002 | char-select | Body text of the delete-character confirmation dialog. | RESOLVED |
| 46001 | char-select | Instruction label of the name-change sub-dialog (prompt to type the new name). | RESOLVED |
| 46002 | char-select | Field label ("Name") for the name-change input box. | RESOLVED |
| 48001 | char-select | Emergency-teleport confirmation prompt (force-move the character to the main town). | RESOLVED |
| 48003 | char-select | Sub-label for the emergency-teleport button (recommended-when-stuck note). | RESOLVED |
| 48004 | char-select | Emergency-teleport cooldown notice (usable once per 30 minutes). | RESOLVED |
| 48005 | char-select | Emergency-teleport fallback-advice line. | RESOLVED |
| 63030 | char-select | Tutorial-gate blocking notice (char-select entry blocked until tutorial is completed). | RESOLVED |

> **Out of scope here:** caption IDs 14003–14007 (the character-creation form labels) and ID 48002
> (unreferenced) belong to separate creation-form lanes and are not indexed in this front-end set.

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
  machine, EULA gate, char-select chrome — parallel lane).
- **PIN modal** (which has no catalogue entries — labels are baked art): see the PIN / second-
  password coverage in the front-end scene spec.
- **Encoding:** all caption text is CP949; register the code-page provider once and decode with
  code page 949 (project-wide convention).
- **Glossary:** see `Docs/RE/names.yaml`.
- **Provenance:** see `Docs/RE/journal.md` (add an entry for this spec).
