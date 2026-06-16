# Format: bindlist.txt  (skeleton registry — authoritative list of registered `.bnd` skeletons)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code,
> NO binary addresses. Consumed by `Assets.Parsers`. Every offset/rule an engineer cites must
> reference this file.

<!--
verification: sample-verified
ida_reverified: 2026-06-16
ida_anchor: 263bd994
evidence: [static-ida, vfs-sample]
conflicts: none
-->

> **Verification banner.** `sample-verified` · `ida_reverified: 2026-06-16` · `ida_anchor: 263bd994` ·
> `evidence: [static-ida, vfs-sample]` · `conflicts: none`. Re-verified two-witness against build
> `263bd994` (the bindlist load-and-register loop and the per-skeleton register-by-`actor_id` path) AND
> the real `data/char/bindlist.txt` VFS sample. The 349-entry count, the 348-CRLF-plus-final-line-
> without-CRLF structure, the bare-stem single-column shape, the ASCII-only encoding, and registration
> by parsed `actor_id` all re-confirmed exactly. **One cosmetic refinement (DRIFT D-1):** the list is
> **lexicographically (byte-string) sorted ascending**, not numerically sorted — see §Naming convention.
>
> **Why this matters:** this file is the authoritative skeleton registry. It CONFIRMS the
> correction recorded in `CLAUDE.md`: a skeleton is registered via this explicit catalogue (and via
> the `.skn` `id_b` / `actormotion.txt` `skin_class` join), **not** by a computed `g{N}.bnd` numeric
> rule. There is no client-enforced numeric-format rule that derives a skeleton path from an integer
> range. A skeleton that is not listed here is not registered even if a `g{N}.bnd` file physically
> exists in the VFS.
>
> **Scope correction (CONFIRMED):** the registry is NOT limited to the four player rigs
> `g1.bnd`–`g4.bnd`. Any prior "only g1..g4 exist / are registered" framing is wrong. The registry
> holds **all 349 skeletons**, each keyed by the `actor_id` parsed from its `.bnd` header. The four
> player rigs (`g1`–`g4`: Musa / Salsu / Dosa / Monk) are merely four of those 349 entries — the
> playable-class subset, not the whole registry.

## Identification

- **Logical path:** `data/char/bindlist.txt`
- **Found in:** the VFS / loose client tree (see `formats/pak.md`)
- **Role:** startup skeleton registry — the client reads this file once and registers (or preloads)
  each named `.bnd` relative to `data/char/bind/`. The file stores **bare stems only** (`g<N>.bnd`,
  no directory prefix); the loader **prepends the literal `data/char/bind/`** to each line before
  opening it. Each `.bnd` it loads is then registered under the `actor_id` parsed from that file's
  header (see `formats/mesh.md` §`.bnd` Header → `actor_id`), so the registry is keyed by `actor_id`,
  not by the order or numeric stem of the line. | Re-verified [sample-verified] on build `263bd994`:
  the load loop is an EOF-bounded line reader that prepends `data/char/bind/` then registers, and the
  real `bindlist.txt` sample contains no slash in any line (bare stems, the loader supplies the dir).
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
| Total entries | **349** | [sample-verified] — exact match on build `263bd994` (real `bindlist.txt`: 349 distinct entries) |
| Of which terminated by CRLF | 348 | [sample-verified] — sample `crlf_count = 348` |
| Final entry without trailing CRLF | 1 | [sample-verified] — sample `ends_with_crlf = False` |

The 349-entry count is a **1:1 match** with the count of `data/char/bind/*.bnd` files in the VFS
(also 349). This confirms the list is exhaustive: every registered skeleton appears here, and no
extra entries are present.

### Registration is by parsed `.bnd` `actor_id` — all 349 (CONFIRMED)

The registry is populated by loading each listed `.bnd` and registering it under the `actor_id`
read from that file's header. **All 349 listed skeletons are registered this way** — the registry
is the full set of `actor_id`s parsed from the 349 `.bnd` files, NOT a four-entry table of player
rigs.

| Claim | Status |
|-------|--------|
| The registry holds 349 entries, one per listed `.bnd` | [sample-verified] — 349 distinct stems on build `263bd994` |
| Each entry is keyed by the `actor_id` parsed from the `.bnd` header (not by line order or stem) | [sample-verified] — registration-by-`actor_id` is the loader's literal step; the listed `g1.bnd` parses to `actor_id = 1` |
| The 349 `actor_id` keys are exactly the 349 `.bnd` `actor_id` values, matching the 349 distinct `.skn` `id_b` values (bijection) | CONFIRMED — see `formats/mesh.md` §id_b ↔ skeleton bijection |
| The registry is limited to `g1`–`g4` (the four player rigs) | REFUTED — `g1`–`g4` are 4 of the 349 entries (the playable-class subset), not the whole registry; the load loop registers every listed line identically with no special-casing |

> The four player rigs `g1.bnd`–`g4.bnd` (Musa / Salsu / Dosa / Monk) are the only rigs that carry
> the base playable classes, which is why they receive special handling downstream — but they are
> still just four ordinary entries within a 349-entry registry. Every non-player skeleton (NPC, mob,
> mount, etc.) is registered exactly the same way, keyed by its own parsed `actor_id`.

## Naming convention (descriptive, NOT a derivation rule)

Each entry follows the form `g<N>.bnd`, where `<N>` is an integer.

**Ordering (corrected — DRIFT D-1).** The lines are **lexicographically (byte-string) sorted
ascending**, NOT numerically sorted. They are **numerically sparse / non-contiguous** — there are
deliberate gaps in the numeric range (some integers in the observed span have no entry; the distinct
values span min `1` to max `8892`). The file is an enumeration of the skeletons that actually exist,
not a dense numeric sequence.

> **Why this is a refinement, not a conflict.** A prior revision said the integers were "sorted but
> NON-contiguous", which read as *numeric* sort. The real file is string-sorted: the few apparent
> numeric descents (the stems `g2.bnd` / `g3.bnd` / `g4.bnd` appearing after their `g1xxx` / `g2xxx` /
> `g3xxx` lexical neighbours) are exactly where byte-string ordering places a short stem after the
> longer stems that share its leading characters. The precise statement is **string-sorted ascending,
> numerically sparse/non-contiguous**. This is cosmetic: the registry is keyed by parsed `actor_id`,
> so **line order is never load-bearing** (the loader reads file order and does not sort). | Confidence:
> [sample-verified] — the byte-string sort test passes on the real `bindlist.txt` (build `263bd994`).

**Important — there is NO `g{N}.bnd` numeric-format rule.** The client does not compute a skeleton
path from an arbitrary integer and assume the file exists. It reads this explicit list. The
`g<N>.bnd` shape is a descriptive observation about how the listed filenames happen to be named — it
is not a contract that any integer `N` maps to a valid skeleton. Engineers must treat membership in
this list (or the join via `id_b` / `skin_class`) as the test of whether a skeleton is registered.

## Cross-file join

`bindlist.txt` is a registry (a set of valid skeleton names, each resolving to a parsed `actor_id`),
not a keyed lookup table on its own columns. The character bind/idle chain joins as follows:

- `actormotion.txt` `skin_class` (the per-actor skeleton class) → the registered skeleton whose
  parsed `actor_id` equals `skin_class` (resolve by `actor_id` lookup, not by formatting a path).
- the `.skn` field `id_b` (an `actor_id`, **sparse over the range 1..8892**, `0` = no skeleton) →
  the registered skeleton whose parsed `actor_id` equals `id_b`. Resolve by `actor_id` lookup; do
  **not** treat `id_b` as a dense 0..348 index or assume a `g{id_b}.bnd` filename exists for an
  arbitrary value (see `formats/mesh.md` §`.skn` Header → `id_b`).
- `bindlist.txt` is the authoritative set of the 349 registered skeletons; it validates which
  `id_b` / `skin_class` values resolve to a real registered skeleton (a value resolves iff some
  listed `.bnd` parses to that `actor_id`). The on-disk filenames happen to follow `g<N>.bnd`, but
  membership/registration is by parsed `actor_id`, never by a computed numeric path.

`bindlist.txt` itself is not joined ON a text column — it is loaded at startup (each bare stem
prefixed with `data/char/bind/` by the loader), and each `.bnd` it names is registered under its
parsed `actor_id`, producing the 349-entry keyed registry the joins above resolve against.

> **`id_b` is the skeleton pointer, not the `skin.txt` class tag.** The `id_b` that resolves against
> this registry is the `.skn` `id_b` skeleton pointer — an `actor_id` sparse over 1..8892, resolved by
> `actor_id` lookup, not a dense 0..348 index (see `formats/mesh.md` §`.skn` Header). It must not be
> confused with the separate 6-value `skin.txt` `col2` outfit/class tag, which never
> selects a skeleton. Both were historically labelled "IdB"; they are two distinct fields. The
> canonical glossary split of the two meanings is owned by Tier-1 in `Docs/RE/names.yaml`.

## Known unknowns

- Whether the client **preloads** every listed `.bnd` at startup or merely registers the names for
  lazy load on first reference (the registration vs. preload distinction is not pinned down here).
- The semantic meaning of the integer `<N>` beyond "skeleton id" (whether the value encodes a
  body type / gender / class grouping is not established from this file alone).

## Cross-references

- **Container:** `Docs/RE/formats/pak.md`
- **Skeleton key / `id_b` resolution:** `Docs/RE/formats/mesh.md` (the `.bnd` `actor_id` field this
  registry is keyed on, and the `.skn` `id_b` → skeleton bijection it validates).
- **Bind/idle and skin chains:** `Docs/RE/formats/actormotion.md` (the `skin_class` / motion join),
  `Docs/RE/specs/skinning.md` (the `.skn` `id_a`/`id_b` → bind/skin chain).
- **Skeleton/motion file formats:** `Docs/RE/formats/animation.md` (`.bnd` / `.mot`).
- **Glossary:** see `Docs/RE/names.yaml` (proposed: `skeleton-registry` / `bind-list`; the
  `id_b` skeleton-pointer vs `skin.txt col2` class-tag split is owned by Tier-1).
- **Provenance:** see `Docs/RE/journal.md`. The 349-by-`actor_id` registration correction was
  promoted under CAMPAIGN VFS-MASTERY (two-witness: loader + black-box). Re-verified under CAMPAIGN 10
  Block D against build `263bd994` + the real `bindlist.txt` sample on 2026-06-16 — count/structure/
  encoding/registration all re-confirmed; the only delta is the cosmetic D-1 ordering refinement
  (lexicographic, not numeric sort).
