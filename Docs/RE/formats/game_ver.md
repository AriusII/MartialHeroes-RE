# Format: game_ver  (`game.ver` — binary client version stamp)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers (`GameVerParser`). Every offset an engineer cites must reference this file.
>
> Promoted from dirty-room analyst notes under EU Software Directive 2009/24/EC Art. 6.
> No decompiler output and no binary virtual addresses appear anywhere in this file.
>
> status: sample-verified layout / one field semantically confirmed — see per-field confidence.

`game.ver` is a tiny fixed-size binary blob that stamps the client build with a set of integers.
Only one of those integers has a confirmed runtime role: it is the source value the client folds
into the enter-game version token. The remaining six integers are structurally fixed but their
individual meanings are unconfirmed.

## Identification

- **Extension:** `.ver`
- **Found in:** `.pak` archive / VFS; logical path: `data/cursor/game.ver`
- **Magic / signature:** None. No file-level header of any kind. Identification relies solely on
  the known VFS path and the fixed 28-byte size.
- **Version field:** None as a self-describing field; the file *is* a version stamp but carries no
  format-version tag of its own.
- **Endianness:** Little-endian throughout.
- **File size:** Exactly 28 bytes (fixed). — confidence: HIGH (sample-verified)

## File layout

The file is a flat array of **seven 32-bit unsigned integers**, little-endian, with no header,
no magic, and no terminator. Total size is `7 × 4 = 28` bytes. There is no stored record count and
no repeating record — the file is a single fixed structure, not an array of records.

### Field table (28 bytes = 0x1C)

| Offset | Size | Type  | Field           | Notes                                                                 | Confidence |
|-------:|-----:|-------|-----------------|------------------------------------------------------------------------|------------|
| 0x00   | 4    | u32LE | field0          | Small integer; candidate format/protocol-version tag. Role unconfirmed | LOW / UNVERIFIED |
| 0x04   | 4    | u32LE | field1          | Small integer; candidate minor-version or build-date component. Role unconfirmed | LOW / UNVERIFIED |
| 0x08   | 4    | u32LE | field2          | Small integer; candidate build increment / week index. Role unconfirmed | LOW / UNVERIFIED |
| 0x0C   | 4    | u32LE | field3          | Mid-range integer; candidate packed build number or packed date. Role unconfirmed | LOW / UNVERIFIED |
| 0x10   | 4    | u32LE | field4          | Small integer; candidate patch / sub-build level. Role unconfirmed     | LOW / UNVERIFIED |
| 0x14   | 4    | u32LE | version_source  | Source value for the enter-game version token (see formula below)      | HIGH / CONFIRMED |
| 0x18   | 4    | u32LE | field6          | Small integer at the tail; candidate reserved / checksum-seed. Role unconfirmed | LOW / UNVERIFIED |

> **Confirmed field — `version_source` (offset 0x14):** this is the only field with a confirmed
> functional role. The client derives the enter-game version token from it via a fixed formula
> (see below). This was established by cross-checking the existing `GameVerParser` and its test
> suite against the observed file, which agree on both the field offset and the resulting token.

## Derived value — enter-game version token

The client computes a single integer token from `version_source` and presents it during the
enter-game handshake. The formula is:

```
version_token = 10 × version_source + 9
```

This token derivation is CONFIRMED (it matches `GameVerParser` and its tests). The other six
fields are not known to participate in any derived value.

## Field semantics discussion

Only `version_source` (offset 0x14) has a confirmed role. The other six fields all hold small,
plausible version/build-stamp components, but with a single effective file instance and no protocol
trace identifying which fields the server validates, their individual meanings cannot be confirmed
from the bytes alone. Plausible-but-unverified readings include a format/protocol-version tag, a
packed build date, a build/patch counter, and a reserved/checksum tail. None of these is graded
above LOW until a second `game.ver` with different field values (e.g. from another client patch) or
a handshake trace is available. **The structural layout (seven u32LE, 28 bytes) is fully confirmed;
all field semantics except `version_source` are UNVERIFIED.**

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

- The semantic roles of `field0`–`field4` and `field6` are UNVERIFIED. Confirming them needs a
  second `game.ver` with differing field values (a different client patch) or a server-side trace
  showing which fields the enter-game handshake validates.
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
- Provenance: see `Docs/RE/journal.md` (add an entry for this spec).
