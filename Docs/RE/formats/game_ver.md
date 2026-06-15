# Format: game_ver  (`game.ver` — binary client version stamp)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers (`GameVerParser`). Every offset an engineer cites must reference this file.
>
> Promoted from dirty-room analyst notes under EU Software Directive 2009/24/EC Art. 6.
> No decompiler output and no binary virtual addresses appear anywhere in this file.
>
> status: layout confirmed (28 bytes, seven u32LE) / exactly one field semantically confirmed
> (`version_source` at offset 0x14) / the remaining six fields are UNVERIFIED (single-sample) —
> see per-field confidence.

`game.ver` is a tiny fixed-size binary blob that stamps the client build with a set of integers.
Only one of those integers has a confirmed runtime role: it is the source value the client folds
into the enter-game version token. The remaining six integers are structurally fixed (their offset,
size, and order are confirmed), but their individual meanings are not — they rest on a single
effective sample and are deliberately left opaque rather than guessed.

## Identification

- **Extension:** `.ver`
- **Found in:** `.pak` archive / VFS; logical path: `data/cursor/game.ver`
- **Magic / signature:** None. No file-level header of any kind. Identification relies solely on
  the known VFS path and the fixed 28-byte size.
- **Version field:** None as a self-describing field; the file *is* a version stamp but carries no
  format-version tag of its own.
- **Endianness:** Little-endian throughout.
- **File size:** Exactly 28 bytes (fixed). — confidence: CONFIRMED

## File layout

The file is a flat array of **seven 32-bit unsigned integers**, little-endian, with no header,
no magic, and no terminator. Total size is `7 × 4 = 28` bytes. There is no stored record count and
no repeating record — the file is a single fixed structure, not an array of records.

### Field table (28 bytes = 0x1C)

| Offset | Size | Type  | Field           | Notes                                                                 | Confidence |
|-------:|-----:|-------|-----------------|------------------------------------------------------------------------|------------|
| 0x00   | 4    | u32LE | field0          | Role unknown. Not read by the version-token path; not interpreted here. | UNVERIFIED (single-sample) |
| 0x04   | 4    | u32LE | field1          | Role unknown. Not read by the version-token path; not interpreted here. | UNVERIFIED (single-sample) |
| 0x08   | 4    | u32LE | field2          | Role unknown. Not read by the version-token path; not interpreted here. | UNVERIFIED (single-sample) |
| 0x0C   | 4    | u32LE | field3          | Role unknown. Not read by the version-token path; not interpreted here. | UNVERIFIED (single-sample) |
| 0x10   | 4    | u32LE | field4          | Role unknown. Not read by the version-token path; not interpreted here. | UNVERIFIED (single-sample) |
| 0x14   | 4    | u32LE | version_source  | Source value for the enter-game version token (see formula below).     | CONFIRMED |
| 0x18   | 4    | u32LE | field6          | Role unknown. Not read by the version-token path; not interpreted here. | UNVERIFIED (single-sample) |

> **Confirmed field — `version_source` (offset 0x14):** this is the only field with a confirmed
> functional role. The client derives the enter-game version token from it via a fixed formula
> (see below). Two witnesses agree on this field: the loader path that folds the value into the
> token, and a black-box check of the resulting token against the observed file. Both agree on the
> offset (0x14) and on the derived token.

## Derived value — enter-game version token

The client computes a single integer token from `version_source` and presents it during the
enter-game handshake. The formula is:

```
version_token = 10 × version_source + 9
```

This token derivation is CONFIRMED (loader and black-box witnesses agree). The other six fields are
not known to participate in any derived value.

## Field semantics discussion

Only `version_source` (offset 0x14) has a confirmed role. The other six fields are structurally
fixed 32-bit integers at the offsets tabled above, but their individual meanings cannot be
established from a single effective file instance with no protocol trace identifying which fields
the server validates. No semantic reading is assigned to them here — assigning one would be a guess
on one sample. **The structural layout (seven u32LE, 28 bytes) is CONFIRMED; every field semantic
except `version_source` is UNVERIFIED (single-sample).**

## Census / sample notes

- The VFS exposes two `.ver` index entries, both naming `data/cursor/game.ver` at consecutive
  archive offsets exactly 28 bytes apart. This is consistent with the same logical 28-byte file
  appearing twice in the archive (a packing artefact or intentional redundancy), not two distinct
  version files with different contents. There is therefore only **one effective sample**, which is
  why every field except `version_source` stays single-sample-unverified.
- A defensive reader should resolve the path and use the first matching entry; the two entries carry
  identical content.

## Parser agreement

The existing `Assets.Parsers` decoder (`GameVerParser`) already matches this layout: it validates the
28-byte size, reads seven little-endian u32 fields with no header, reads `version_source` at offset
0x14, and produces `version_token = 10 × version_source + 9`. The parser's comments already mark the
non-`version_source` fields as unverified, so there is no conflict — both this spec and the parser
agree those six fields are unknown.

> **Citation:** `GameVerParser` (and any other code touching `game.ver`) must cite this file:
> `// spec: Docs/RE/formats/game_ver.md` on every offset, the 28-byte size check, and the token formula.

## Known unknowns

- The semantic roles of `field0`–`field4` and `field6` are UNVERIFIED (single-sample). Confirming
  them needs a second `game.ver` with differing field values (a different client patch) or a
  server-side trace showing which fields the enter-game handshake validates.
- No magic / signature prefix exists; identification depends entirely on the known VFS path and the
  fixed 28-byte size.
- The reason the same logical file appears as two consecutive archive entries (packing artefact vs.
  intentional redundancy) is unexplained; both entries are byte-identical.
- Whether the seven-field layout or the token formula varies across client patches is unknown
  (single effective sample only).

## Cross-references

- Related formats: `pak.md` (archive container / VFS index that holds `game.ver`).
- Consumer: `Assets.Parsers` `GameVerParser`; the enter-game handshake that carries `version_token`
  is protocol-spec material under `Docs/RE/packets/` / `Docs/RE/opcodes.md`.
- Glossary: see `Docs/RE/names.yaml` (canonical names: `version_source`, `version_token`).
- Provenance: see `Docs/RE/journal.md`. Promoted under CAMPAIGN VFS-MASTERY (two-witness gate:
  loader path + black-box token check) — layout 28 B / seven u32LE confirmed; `version_source`
  at offset 0x14 confirmed; the other six fields held UNVERIFIED (single-sample).
