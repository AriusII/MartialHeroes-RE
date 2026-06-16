# Format: game_ver  (`game.ver` — binary client version stamp)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers (`GameVerParser`). Every offset an engineer cites must reference this file.
>
> Promoted from dirty-room analyst notes under EU Software Directive 2009/24/EC Art. 6.
> No decompiler output and no binary virtual addresses appear anywhere in this file.
>
> verification: sample-verified — the structural layout and the two confirmed runtime fields are
>   corroborated by BOTH the IDB loader control flow and a real VFS sample of `data/cursor/game.ver`.
> ida_reverified: 2026-06-16
> ida_anchor: 263bd994
> evidence: [static-ida, vfs-sample]
> conflicts: none unresolved. The single-sample semantics of the five opaque fields
>   (index 0/1/2/4/6) remain capture/debugger-pending — only one effective on-disk sample exists and
>   no protocol trace identifies which fields the server validates; these are carried as open
>   unknowns, not resolved.
>
> status: layout SAMPLE-VERIFIED (variable-length u32LE list, MINIMUM 7 elements; the canonical
> shipping file is exactly 28 bytes = 7xu32LE — confirmed by the read loop + the >=7 reject check in
> the loader AND by a 28-byte real sample) / two fields have a confirmed runtime role —
> `version_source` at index 5 (offset 0x14) and the value at index 3 (offset 0x0C, consumed by a
> separate external reader) / the remaining fields' semantics are capture/debugger-pending
> (single-sample) — see per-field confidence.

`game.ver` is a small binary version-stamp file: a **variable-length list of 32-bit little-endian
integers** that the loader reads element-by-element until end-of-file, enforcing a **minimum of 7
elements**. The canonical shipping file is exactly 28 bytes (7xu32LE), but a longer file is tolerated
(more than 7 elements is accepted; fewer than 7 is rejected). Two of the integers have a confirmed
runtime role: the value at **index 5** (offset 0x14) is folded into the enter-game version token, and
the value at **index 3** (offset 0x0C) is read by a separate external reader. The remaining integers
are structurally present but their individual meanings rest on a single effective sample and are left
opaque rather than guessed.

## Identification

- **Extension:** `.ver`
- **Found in:** `.pak` archive / VFS; logical path: `data/cursor/game.ver`
- **Magic / signature:** None. No file-level header of any kind. Identification relies solely on
  the known VFS path and the size being a multiple of 4 with at least 7 u32 elements.
- **Version field:** None as a self-describing field; the file *is* a version stamp but carries no
  format-version tag of its own.
- **Endianness:** Little-endian throughout.
- **File size:** Variable-length; a whole number of 4-byte u32 elements, **minimum 7 elements
  (28 bytes)**. The canonical shipping file is exactly 28 bytes. A file with fewer than 7 elements is
  rejected; a file with more than 7 is tolerated (extra trailing elements are read and counted but not
  interpreted here). — confidence: CONFIRMED

## File layout

The file is a flat, **variable-length list of 32-bit unsigned integers**, little-endian, with no
header, no magic, and no terminator. The loader reads u32 elements until end-of-file and enforces a
**minimum count of 7**; longer files are tolerated. The canonical shipping file holds exactly 7
elements (`7 x 4 = 28` bytes). There is no stored element-count prefix — the count is the file size
divided by 4. The first seven elements (indices 0..6) are tabled below; any element beyond index 6 in
a longer file is read and counted but carries no interpreted meaning here.

### Field table (first 7 elements; canonical file = 28 bytes = 0x1C)

| Offset | Size | Type  | Field           | Notes                                                                 | Confidence |
|-------:|-----:|-------|-----------------|------------------------------------------------------------------------|------------|
| 0x00   | 4    | u32LE | field0          | Role unknown. Not read by the version-token path; not interpreted here. | semantic capture/debugger-pending (single-sample) |
| 0x04   | 4    | u32LE | field1          | Role unknown. Not read by the version-token path; not interpreted here. | semantic capture/debugger-pending (single-sample) |
| 0x08   | 4    | u32LE | field2          | Role unknown. Not read by the version-token path; not interpreted here. | semantic capture/debugger-pending (single-sample) |
| 0x0C   | 4    | u32LE | field3 (index 3) | Read by a **separate external (non-VFS, file-opened) reader** alongside index 5. The external reader seeks to **byte offset 12** (= index 3) and reads a u32 into a runtime field. Its exact runtime use is not pinned, but the field IS consumed — it is not dead. | SAMPLE-VERIFIED (consumed at offset 12); semantic capture/debugger-pending |
| 0x10   | 4    | u32LE | field4          | Role unknown. Not read by the version-token path; not interpreted here. | semantic capture/debugger-pending (single-sample) |
| 0x14   | 4    | u32LE | version_source (index 5) | Source value for the enter-game version token (see formula below). The single field the login version gate compares. Also read by the separate external reader, which seeks to **byte offset 20** (= index 5). | SAMPLE-VERIFIED |
| 0x18   | 4    | u32LE | field6          | Role unknown. Not read by the version-token path; not interpreted here. | semantic capture/debugger-pending (single-sample) |

> **Confirmed field — `version_source` (index 5, offset 0x14):** this is the field that feeds the
> enter-game version token and the login version gate; index 3 (offset 0x0C) is separately consumed by
> an external reader (see field table). `version_source` is the field with a confirmed *derived-token*
> role. The client derives the enter-game version token from it via a fixed formula
> (see below). Two witnesses agree on this field: the loader path that reads index 5 (the comparison
> routine fetches the field at list index 5; the external reader seeks to byte offset 20 = index 5),
> and a real VFS sample in which index 5 decodes to a concrete value and the derived token matches
> the previously observed enter-game token. Both witnesses agree on the offset (0x14) and on the
> derived token. **SAMPLE-VERIFIED.**

## Derived value — enter-game version token

The client computes a single integer token from `version_source` and presents it during the
enter-game handshake. The formula is:

```
version_token = 10 × version_source + 9
```

This token derivation is SAMPLE-VERIFIED: the loader/handshake path that folds index 5 into the
token and a black-box check of the resulting token against the real VFS sample agree
(`version_source` from the sample, run through the formula, reproduces the previously observed
enter-game token). The other six fields are not known to participate in any derived value.

## Field semantics discussion

Two fields have a confirmed role: `version_source` (index 5, offset 0x14) feeds the enter-game token
and the login version gate, and `field3` (index 3, offset 0x0C) is consumed by a separate external
reader. The other fields are structurally fixed 32-bit integers at the offsets tabled above, but
their individual meanings cannot be established from a single effective file instance with no protocol
trace identifying which fields the server validates. No semantic reading is assigned to them here.
**The structural layout (variable-length u32LE list, minimum 7 elements; canonical 28 bytes) is
SAMPLE-VERIFIED; index 5 is SAMPLE-VERIFIED; index 3 is SAMPLE-VERIFIED-as-consumed at byte offset 12
(semantic capture/debugger-pending); every other field semantic is capture/debugger-pending
(single-sample).**

## Census / sample notes

- The VFS exposes two `.ver` index entries, both naming `data/cursor/game.ver` at consecutive
  archive offsets, both exactly 28 bytes. **SAMPLE-VERIFIED.** This is consistent with the same
  logical 28-byte file appearing twice in the archive (a packing artefact or intentional
  redundancy), not two distinct version files with different contents. There is therefore only
  **one effective sample**, which is why the *semantics* of every field except `version_source`
  (index 5) and the consumed-ness of `field3` (index 3) stay single-sample / capture-pending.
- A defensive reader should resolve the path and use the first matching entry; the two entries carry
  identical content.

## Parser agreement

The existing `Assets.Parsers` decoder (`GameVerParser`) matches this layout: it reads little-endian
u32 elements with no header, requires **at least 7 elements** (the canonical file is 28 bytes / 7
elements; longer is tolerated), reads `version_source` at index 5 / offset 0x14, and produces
`version_token = 10 x version_source + 9`. If the parser currently hard-asserts an exact 28-byte size,
it should be relaxed to `size % 4 == 0 && element_count >= 7` to match the loader's variable-length,
minimum-7 contract. The value at index 3 (offset 0x0C) is consumed by a separate external reader and
must NOT be treated as a dead field.

> **Citation:** `GameVerParser` (and any other code touching `game.ver`) must cite this file:
> `// spec: Docs/RE/formats/game_ver.md` on every offset, the minimum-7-element size check, and the
> token formula.

## Known unknowns

- The semantic roles of `field0`, `field1`, `field2`, `field4`, and `field6` are
  capture/debugger-pending (single-sample) — these fields are confirmed **not** accessed by any
  identified consumer, but their server-side meaning is undetermined. `field3` (index 3) is
  SAMPLE-VERIFIED-as-consumed by the external reader (which seeks to byte offset 12), but its exact
  use is capture/debugger-pending. Confirming any of these needs a second `game.ver` with differing
  field values (a different client patch) or a server-side trace showing which fields the
  handshake/external reader validates.
- No magic / signature prefix exists; identification depends entirely on the known VFS path and the
  variable-length, minimum-7-element u32 list shape. **SAMPLE-VERIFIED** (the loader reads the first
  u32 as data with no magic check; the sample's first 4 bytes are an ordinary little-endian value,
  not a sentinel).
- The reason the same logical file appears as two consecutive archive entries (packing artefact vs.
  intentional redundancy) is unexplained; both entries are byte-identical 28-byte copies.
- The exact runtime use of index 3 by the separate external (non-VFS) reader, and whether the
  external reader and the login gate read the same on-disk copy, are capture/debugger-pending. The
  *consumption* of index 3 (seek to offset 12) is SAMPLE-VERIFIED; only its *meaning* is open.
- Whether the element count, field order, or the token formula varies across client patches is unknown
  (single effective sample only); only the minimum-7 contract is confirmed.

## Login version gate (single-field equality)

Separately from the enter-game token, the login/OK action runs a version gate that compares the
client's two copies of `game.ver`:

- The VFS copy `data/cursor/game.ver` and the external `<client-root>\game.ver` are both loaded.
- The loader reads the body as a loop of u32-LE values and **requires count >= 7** (a file with
  fewer than 7 values is rejected as invalid); a file with **more than 7** values is tolerated.
  So while the canonical sample is exactly 28 bytes (7 fields), a longer file is still accepted.
- The comparison is **single-field equality on list index 5** (byte offset 0x14 — the same slot as
  `version_source`). The other values are read and counted but NOT compared field-by-field.
- **Match ->** the gate passes and login proceeds. **Mismatch ->** a Win32 message box (body =
  message id **2204**) is shown and the client quits.
- **Separate external reader (index 3 + index 5):** beyond this login gate, a distinct external
  (`fopen`-style, non-VFS) reader of `game.ver` reads BOTH index 3 (offset 0x0C) AND index 5 (offset
  0x14). The external reader seeks to **byte offset 12** for index 3 and **byte offset 20** for index
  5. This is why index 3 is marked SAMPLE-VERIFIED-as-consumed in the field table — it is read by this
  external path even though the login gate compares only index 5.

The gate fires only on the login action. The single-field equality (both operands fetched at list
index 5) is CODE-CONFIRMED by the comparison routine; the **>= 7** element check guarding both the
VFS and external read paths is likewise CODE-CONFIRMED. Source: dirty-room IDA notes
`Docs/RE/_dirty/campaign9b/login.md` §3 (CAMPAIGN 9b) and the campaign-10 two-witness
re-verification.

## Cross-references

- Related formats: `pak.md` (archive container / VFS index that holds `game.ver`).
- Consumer: `Assets.Parsers` `GameVerParser`; the enter-game handshake that carries `version_token`
  is protocol-spec material under `Docs/RE/packets/` / `Docs/RE/opcodes.md`.
- Glossary: see `Docs/RE/names.yaml` (canonical names: `version_source`, `version_token`).
- Provenance: see `Docs/RE/journal.md`. Promoted under CAMPAIGN VFS-MASTERY and reconciled under
  CAMPAIGN VFS-MASTERY-B (two-witness gate: loader path + black-box token check). **Reconciliation:**
  layout corrected from "fixed 28 B / seven u32" to **variable-length u32LE list, minimum 7 elements**
  (28 B canonical); the enter-game token reads index 5 (offset 0x14) and the login gate compares index
  5; **index 3 (offset 0x0C) is now CONFIRMED-as-consumed** by a separate external (non-VFS) reader
  (previously marked unread). `version_source` confirmed; other field semantics held UNVERIFIED
  (single-sample). No addresses, decompiler output, or sample bytes crossed the firewall.
- **Campaign 10 re-verification (2026-06-16, IDB anchor 263bd994):** re-confirmed against both the
  IDB loader control flow and a real VFS sample. Promotions to **SAMPLE-VERIFIED**: the
  variable-length u32LE list / minimum-7 / canonical-28-byte layout (read loop + `>= 7` reject in
  both the VFS and external load paths, plus a 28-byte sample = 7 elements exactly); `version_source`
  at index 5 / offset 0x14 (loader fetches list index 5; the sample's index 5 reproduces the observed
  enter-game token through the published formula); `field3` consumption at byte offset 12 (the
  external reader seeks to offset 12 = index 3 and offset 20 = index 5); the duplicate 28-byte VFS
  entries; the no-magic identification. Held capture/debugger-pending: the *semantics* of fields
  0/1/2/4/6 and the *meaning* of field3 (single effective sample, no protocol trace). No addresses,
  decompiler output, or sample bytes crossed the firewall.
