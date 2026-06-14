# Format: bindlist.txt  (skeleton registry — authoritative list of registered `.bnd` skeletons)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code,
> NO binary addresses. Consumed by `Assets.Parsers`. Every offset/rule an engineer cites must
> reference this file.
>
> **Why this matters:** this file is the authoritative skeleton registry. It CONFIRMS the
> correction recorded in `CLAUDE.md`: a skeleton is registered via this explicit catalogue (and via
> the `.skn` `IdB` / `actormotion.txt` `skin_class` join), **not** by a computed `g{N}.bnd` numeric
> rule. There is no client-enforced numeric-format rule that derives a skeleton path from an integer
> range. A skeleton that is not listed here is not registered even if a `g{N}.bnd` file physically
> exists in the VFS.

## Identification

- **Logical path:** `data/char/bindlist.txt`
- **Found in:** the VFS / loose client tree (see `formats/pak.md`)
- **Role:** startup skeleton registry — the client reads this file once and registers (or preloads)
  each named `.bnd` relative to `data/char/bind/`.
- **Container family:** plain delimited **text** (not a binary record file).
- **Encoding:** ASCII only — every byte observed is within the printable range `0x20`–`0x7E` plus
  the line terminators. No CP949 / Korean codepoints appear in this file (unlike most game text
  tables). Confidence: HIGH (sample-verified across head and tail).

## File structure

- **Single column, no header, no delimiter.** Each line is exactly one bare `.bnd` filename — stem
  plus extension, with no directory prefix, no leading/trailing whitespace, no comment lines, and no
  blank lines. The entire line IS the value.
- **Line terminator:** CRLF (`\r\n`).
- **Final line:** terminated WITHOUT a trailing CRLF — the file ends immediately after the last
  filename. A parser must treat the final line as a valid entry even though it has no terminator
  (split on CRLF and keep a non-empty trailing segment).

### Column

| Col | Meaning | Type | Example form | Confidence |
|-----|---------|------|--------------|------------|
| 0 | `.bnd` filename (explicit) | string | `g<N>.bnd` | HIGH — every row |

### Entry count

| Property | Value | Confidence |
|----------|-------|------------|
| Total entries | **349** | HIGH |
| Of which terminated by CRLF | 348 | HIGH |
| Final entry without trailing CRLF | 1 | HIGH |

The 349-entry count is a **1:1 match** with the count of `data/char/bind/*.bnd` files in the VFS
(also 349). This confirms the list is exhaustive: every registered skeleton appears here, and no
extra entries are present.

## Naming convention (descriptive, NOT a derivation rule)

Each entry follows the form `g<N>.bnd`, where `<N>` is an integer. The integers are **sorted but
NON-contiguous** — there are deliberate gaps in the numeric range (some integers in the observed
span have no entry). The file is therefore an enumeration of the skeletons that actually exist, not
a dense numeric sequence.

**Important — there is NO `g{N}.bnd` numeric-format rule.** The client does not compute a skeleton
path from an arbitrary integer and assume the file exists. It reads this explicit list. The
`g<N>.bnd` shape is a descriptive observation about how the listed filenames happen to be named — it
is not a contract that any integer `N` maps to a valid skeleton. Engineers must treat membership in
this list (or the join via `IdB` / `skin_class`) as the test of whether a skeleton is registered.

## Cross-file join

`bindlist.txt` is a registry (a set of valid skeleton names), not a keyed lookup table. The
character bind/idle chain joins as follows:

- `actormotion.txt` `skin_class` (the per-actor skeleton class) → `data/char/bind/g<skin_class>.bnd`
- the `.skn` field `IdB` → `data/char/bind/g<IdB>.bnd`
- `bindlist.txt` is the authoritative set of `g<N>.bnd` values that are valid registered skeletons;
  it validates which `IdB` / `skin_class` values resolve to a real skeleton.

`bindlist.txt` itself is not joined ON a column — it is loaded at startup as the registered set.

## Known unknowns

- Whether the client **preloads** every listed `.bnd` at startup or merely registers the names for
  lazy load on first reference (the registration vs. preload distinction is not pinned down here).
- The semantic meaning of the integer `<N>` beyond "skeleton id" (whether the value encodes a
  body type / gender / class grouping is not established from this file alone).

## Cross-references

- **Container:** `Docs/RE/formats/pak.md`
- **Bind/idle and skin chains:** `Docs/RE/formats/actormotion.md` (the `skin_class` / motion join),
  `Docs/RE/specs/skinning.md` (the `.skn` `IdA`/`IdB` → bind/skin chain).
- **Skeleton/motion file formats:** `Docs/RE/formats/animation.md` (`.bnd` / `.mot`).
- **Glossary:** see `Docs/RE/names.yaml` (proposed: `skeleton-registry` / `bind-list`).
- **Provenance:** see `Docs/RE/journal.md`.
