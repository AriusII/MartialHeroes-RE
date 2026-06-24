# Format: .csv  (`data/script/items.csv` — flat comma-delimited item table)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code,
> NO binary addresses. Consumed by `Assets.Parsers`. Every loader an engineer writes for this
> table must cite `// spec: Docs/RE/formats/items_csv.md`.
>
> Promoted from dirty-room harness observation of the maintainer's legally-owned client VFS
> under EU Software Directive 2009/24/EC Art. 6. No decompiler output appears here.

---

## Verification banner

```
verification:   sample-verified   # delimiter/encoding/line-ending/headerlessness/hazards corroborated
                                   # against the FULL real VFS sample (all rows scanned, not just
                                   # line 1): exact row count, exact column count, and both hazard
                                   # magnitudes are now COUNTED over the whole file; the runtime
                                   # non-loading is confirmed from the boot data-loader's callee set
                                   # (loader-control-flow only).
                                   # re-verified against doida.exe IDB SHA
                                   # 263bd994c927c20a38624cf0ca452eaef365057fa9db1543d8f668c14a6fd8ee
                                   # (2026-06-24, static-IDA): all load-bearing facts RE-CONFIRMED —
                                   # zero .csv string references in the IDB, binary items loader
                                   # callee set is offset-driven with no comma scan, record-0
                                   # cross-check (scr item_uid @+0x034 = 201,155,514 == csv col1)
                                   # upgrades the join-key claim from INFERRED to SAMPLE-VERIFIED.
                                   # item_id range (col1) confirmed as purely 9-digit:
                                   # 201011001..299010149. No corrections required.
ida_reverified: 2026-06-24
ida_anchor:     263bd994c927c20a38624cf0ca452eaef365057fa9db1543d8f668c14a6fd8ee
evidence:       [static-ida, vfs-sample-full]
conflicts:      none open          # the prior "runtime role UNVERIFIED" is now resolved:
                                   #   items.csv is CONFIRMED not loaded by the shipping client
                                   #   (authoring/dev export only). No contradictions found on the
                                   #   full-file re-pass — only UNVERIFIED -> SAMPLE-VERIFIED upgrades.
                                   # The join-key correspondence (csv col1 == scr item_uid @+0x034)
                                   #   is upgraded from INFERRED to SAMPLE-VERIFIED (record-0 byte check).
```

---

## Status

```
items.csv:  CONFIRMED  # delimiter/encoding/line-ending/headerlessness SAMPLE-VERIFIED;
                       # runtime non-loading CONFIRMED (authoring/dev export only, not read by the client);
                       # leading columns (0..6) role-inferred from observed value patterns;
                       # canonical column count = 139 SAMPLE-VERIFIED (full-file count, see §1);
                       # row count = 89,712 SAMPLE-VERIFIED (exact LF count);
                       # item_id (col1) range 201011001..299010149, purely 9-digit, SAMPLE-VERIFIED;
                       # join-key correspondence (col1 == items.scr item_uid @+0x034) SAMPLE-VERIFIED
                       #   (record-0 byte+code two-witness; IDB SHA 263bd994, 2026-06-24);
                       # the TWO parser hazards below are SAMPLE-VERIFIED, MAGNITUDE-COUNTED, and load-bearing.
```

This is the **only `.csv` file in the VFS** and is **not** referenced by any prior spec
(`items_scr.md`, `text_tables.md`, `scr.md`). It was discovered during the VFS-DEEP-II residual
text sweep. It is a flat, comma-separated, line-oriented text table — distinct from the binary
`data/script/items.scr` item database (see §6, Relationship).

> **Runtime role (RESOLVED — authoring/dev export only).** The shipping client does **NOT** load
> `data/script/items.csv` at runtime. The IDB string table holds **zero** references to any `.csv`
> path, and the authoritative boot data-loader (which fans out to 60+ `.scr`/`.do`/`.xdb` table
> loaders) has **no CSV reader** among its callees. `items.csv` is therefore a developer/authoring
> export of the binary `items.scr` master database — a tooling artifact, not runtime game data. A
> faithful re-implementation MUST read `items.scr` for runtime item data and treat `items.csv` as a
> human-editable side export only. (Witness: boot-loader callee set, loader-control-flow only.)

---

## Identification

- **Extension:** `.csv`
- **Found in:** the engine VFS (`data.inf` + `data/data.vfs`); logical path `data/script/items.csv`.
  Identified by path, not by content magic. See `formats/pak.md` for the VFS container.
- **Magic / signature:** none — byte 0 is the first character of the first data field (a CP949 lead
  byte). SAMPLE-VERIFIED.
- **Version field:** none.
- **Header row:** **NONE.** The file begins directly with data (the first item-name field), not a
  column-name row. SAMPLE-VERIFIED.
- **Delimiter:** comma (`,`, byte `0x2C`). SAMPLE-VERIFIED (separates fields throughout).
- **Line ending:** **LF only** (`0x0A`) — note this differs from the CRLF used by the other VFS
  text tables. SAMPLE-VERIFIED (LF, no preceding `0x0D`, at the end of line 1).
- **Encoding:** CP949 (EUC-KR superset). The name and description fields hold Korean text; numeric
  fields are the ASCII subset of CP949. SAMPLE-VERIFIED (first fields decode to a valid Korean item
  name).
- **Endianness:** not applicable (text format).
- **Size class:** the largest text table in the VFS (33,069,300 bytes, ~33 MB). The file holds
  **89,712 LF-terminated rows** (SAMPLE-VERIFIED — exact LF count over the full file), averaging
  ~369 bytes per row. A loader should still derive the count by counting LF terminators rather than
  hard-coding it.

---

## 1. Column layout (as recovered)

Each row is one item record. The leading columns below were inferred from value patterns across the
observed rows; columns beyond the early stat fields are a wide numeric array whose individual roles
are UNVERIFIED. **Column indices below assume the embedded-comma hazard (§2) has already been
correctly resolved** — a naive split shifts every index after column 0.

| col# | type   | role (inferred)                                              | confidence |
|------|--------|-------------------------------------------------------------|------------|
| 0    | string | `item_name` — CP949; **MAY CONTAIN EMBEDDED COMMAS** (hazard) | HIGH (type, SAMPLE-VERIFIED); HAZARD |
| 1    | u32    | `item_id` — 9-digit numeric id; range 201011001..299010149 (SAMPLE-VERIFIED, full-file) | HIGH (SAMPLE-VERIFIED) |
| 2    | string | `item_description` — CP949; **MAY CONTAIN EMBEDDED COMMAS** (hazard) | HIGH (type, SAMPLE-VERIFIED); HAZARD |
| 3    | u32    | small int (0 in all observed rows)                          | LOW        |
| 4    | u32    | `base_item_id` / archetype id (9-digit)                     | HIGH       |
| 5    | u32    | secondary type id (9-digit)                                 | MEDIUM     |
| 6    | u32    | small flag (1 observed)                                     | LOW        |
| 7..138 | u32 **or f32** | wide stat array — mostly 0 with sparse non-zero values; **several columns are floats** (hazard, §2; primary float at col ~75) | LOW–MEDIUM |

- **Record count source:** number of LF-terminated lines (no count prefix, no header).
  **SAMPLE-VERIFIED = 89,712** (exact LF count); a loader should still derive it at load by counting
  LF terminators.
- **Record structure:** `{name},{id},{description},{numeric columns…}`. The first two text fields
  (cols 0 and 2) carry the embedded-comma hazard; everything from col 3 onward is numeric. The full
  ordered shape `name → id → description → small-int → base_item_id → secondary-type-id → flag →
  numeric-tail` was SAMPLE-VERIFIED against the real VFS sample.
- **Canonical column count = 139 (138 commas) — SAMPLE-VERIFIED over the full file.** Counting commas
  per row across all 89,712 rows: **89,109 rows are exactly 139 fields**; the remaining 603 rows show
  140–143 naive fields purely because their name/description carries a literal comma (Hazard A, §2 —
  after the hazard is resolved those rows are also 139 columns). The canonical schema is therefore
  **139 columns** (cols 0..138). The early columns spot-checked at several row indices (0, 1, 1000,
  30000, 60000) all hold the stable 139-field shape.

---

## 2. Parser hazards (CONFIRMED — both are load-bearing)

This file is **not** standard CSV. Do **not** feed it to a generic CSV library without a custom
reader. Two independent hazards corrupt column alignment if ignored.

### HAZARD A — embedded commas in the CP949 string fields (CRITICAL)

The item-name field (col 0) and the description field (col 2) are **unquoted** CP949 strings that
may contain literal comma characters (Korean punctuation, or a comma as part of a name suffix). The
fields are **not** wrapped in quotes and **not** escaped. A naive `string.Split(',')` therefore
mis-counts columns for any row whose name or description contains a comma, silently shifting every
downstream column.

**Magnitude (SAMPLE-VERIFIED, full-file count).** Of the 89,712 rows, **603 carry embedded commas**
and so split into more than 139 naive fields:

| naive field count | rows |
|-------------------|------|
| 139 (clean)       | 89,109 |
| 140               | 449  |
| 141               | 126  |
| 142               | 25   |
| 143               | 3    |

A purely positional `Split(',')` corrupts those 603 rows; the count-/structure-based rule below
recovers all of them.

**Correct field-splitting rule (count-/structure-based, not quote-based):**

The record has a **fixed, known shape**: a text field, then a numeric id, then a text field, then an
all-numeric tail. The two text fields are the only places commas may appear inside a field, and the
boundary out of each text field is the transition to a purely numeric token. Split as follows:

1. **Column 0 (name)** runs from the start of the line up to the comma that immediately precedes the
   first **purely-numeric** token (the `item_id`). Scan forward token-by-token; the name ends at the
   last comma before that numeric id. (Equivalently: the name is everything before the first numeric
   field, with any internal commas kept as part of the name.)
2. **Column 1 (id)** is that first purely-numeric token.
3. **Column 2 (description)** runs from the comma after the id up to the comma that immediately
   precedes the next purely-numeric token (the start of the numeric tail, col 3+).
4. **Columns 3..N** are the remaining tokens, split on commas normally — they are all numeric
   (subject to Hazard B) and contain no embedded commas.

In other words: the **numeric tail is the anchor**. Find the first numeric token to close the name,
find the start of the contiguous numeric run after the description to close the description, and only
split the numeric tail on raw commas. A purely positional `Split(',')` is incorrect.

> Implementation caution: when testing "is this token purely numeric", treat a token containing a
> period as numeric too (it is the float field, Hazard B) — otherwise a `0.26` token would be
> mistaken for the start/continuation of a text field and re-open the description boundary.

### HAZARD B — several numeric columns are floats (HIGH, pervasive)

Several numeric columns use **floating-point notation with a period decimal separator** (observed
values in the `0.26`–`0.3` range). This is **not rare**: **69,399 of 89,712 rows (~77%)** contain at
least one float token. A parser that assumes every column after the description is an **integer**
will fail two ways:

1. **Phantom column split** — a hand-rolled tokenizer that treats the period as a separator (or
   rejects non-integer tokens) turns `0.26` into two tokens (`0` and `26`, or `0` and `.26`),
   inserting a phantom column and **shifting every subsequent stat column by one** for that row.
2. **Silent value loss** — an integer-only parse of `0.26` yields `0` (or throws), dropping the
   fractional stat.

**Correct rule:** parse the numeric tail as **floats** (invariant/US culture — period is the decimal
separator, never a comma), or detect-and-branch per token. **Use the invariant culture explicitly**
so a machine with a comma-decimal locale does not mis-read `0.26`. The decimal point is part of the
value, never a delimiter.

**Float column positions (SAMPLE-VERIFIED, clean-row scan).** Over the 139-field clean rows, floats
cluster at a small set of fixed 0-based indices in the numeric tail:

| col index (0-based) | hits (over a 20,000-row scan) | role |
|---------------------|-------------------------------|------|
| **75** (primary)    | 13,236 | dominant float column — the `0.26`/`0.27`/`0.28` value seen incrementing across the first item-upgrade ladder |
| 78                  | 6,764  | secondary float |
| 76                  | 1,776  | secondary float |
| 81                  | 960    | secondary float |
| 82                  | 816    | secondary float |

These indices are positional in **clean** (139-field) rows; for Hazard-A rows the index shifts, but
the numeric-anchor recipe in §3 handles both without needing a fixed index. (Note: a period also
appears inside col 2 — the Korean description text — in a few hundred rows; that is text punctuation,
not a float, and the description-boundary rule in §2 excludes it.)

### Secondary note — LF-only line endings (MEDIUM)

Lines are terminated by LF only, unlike the CRLF tables elsewhere in the VFS. A standard .NET
`StreamReader.ReadLine()` handles this transparently; only a **hand-rolled byte scanner that
searches for `\r\n`** will fail to split rows. Split on `\n` and trim any stray `\r` defensively.

---

## 3. Implementable parsing recipe (hazard-safe)

For each LF-delimited line:

1. **Split rows on `\n`** (handle LF-only; trim a trailing `\r` if present). Skip empty trailing
   lines.
2. **Find the `item_id`:** scan comma-separated tokens left-to-right; the first token that is purely
   an integer is `item_id` (col 1). Everything before its preceding comma is `item_name` (col 0),
   commas included.
3. **Find the numeric tail:** after the id, the next token begins the description (col 2). Continue
   scanning until the first token that is numeric (integer **or** float-with-period) — that token
   starts the numeric tail (col 3). Everything between the id and that token (commas included) is the
   description.
4. **Parse the tail:** split the remaining text on raw commas; parse each token as a number, using
   **invariant culture** so a period is read as a decimal separator. Treat tokens as float where a
   period is present; integer otherwise.
5. **Decode strings as CP949** (`Encoding.GetEncoding(949)` after registering
   `CodePagesEncodingProvider`).

This recipe is robust to both embedded commas (the numeric anchors bound the text fields) and the
float column (the period is kept inside its value and parsed under invariant culture).

---

## 4. Enumerations / flags

- None confirmed. Col 3 (small int, all-zero observed) and col 6 (small flag, `1` observed) are
  likely boolean/flag fields but their meaning is UNVERIFIED.

---

## 5. Known unknowns

- **Per-column roles of the numeric tail** — the canonical column count is **139** (SAMPLE-VERIFIED,
  §1) and only cols 0–6 are role-inferred. The wide numeric tail (cols 7..138) is an item-stat array
  (attack/defense/required-level/etc., by analogy to the binary `items.scr` stats block) but the
  meaning of each individual column is UNVERIFIED. IMPACT: MEDIUM.
- **Semantics of the float columns** — their fixed indices are now pinned (col ~75 primary, plus
  76/78/81/82; §2 Hazard B); what stat each float encodes (a multiplier/rate in the `0.26`–`0.3`
  range) is UNVERIFIED. IMPACT: LOW.
- **Col 3 / col 6 flag semantics** — UNVERIFIED.
- **Runtime role — RESOLVED (no longer a known unknown):** `items.csv` is CONFIRMED **not** loaded
  by the shipping client (zero `.csv` string references; absent from the boot data-loader callee
  set). It is an authoring/dev export. See §6.

---

## 6. Relationship to `items.scr`

`data/script/items.scr` is the **binary** item master database (variable-length records, embedded
null-padded CP949 name/description + a numeric stats block — see `formats/items_scr.md`). This CSV
appears to be a **flat text parallel** of the same item data, formatted for human editing/export:

- **Join key:** the `item_id` in CSV col 1 corresponds to `item_uid` at on-disk offset `+0x034` of
  the `items.scr` binary record (see `formats/items_scr.md §1.4`). Record-0 cross-check: the `items.scr`
  leading record carries `item_uid` = 201,155,514 at `+0x034`; the corresponding CSV row col1 holds
  the same value. The two are parallel views of one dataset rather than independent sources.
  (Cross-key correspondence is **SAMPLE-VERIFIED** on record 0 via a byte+code two-witness check;
  field names col0/col1/col2 also match scr name/uid/desc at +0x000/+0x034/+0x038 on that same record.)
- **Record-count cross-check:** the CSV holds **89,712 rows**; `items.scr` holds roughly **90,937
  records**. The counts are close but **not equal**, so the two are *parallel* views, not a
  byte-identical export — the CSV is most likely a subset or an older snapshot. (Counts compared, not
  rows byte-joined — INFERRED.)
- Which one the shipping client loads at runtime is now **RESOLVED**: the client loads **`items.scr`**
  (it is among the boot data-loader's callees) and does **not** load `items.csv` at all (zero `.csv`
  string references; no CSV reader in the loader callee set; the same items.scr load also appears on
  an alternate/orphan boot path, never a CSV). `items.csv` is therefore an authoring/developer export
  of the binary master, not a runtime source. A faithful loader MUST read `items.scr` for runtime
  data; treat `items.csv` strictly as a human-editable side export. If a tool reconciles the two for
  export/diff purposes, key on `item_id`.

> **Runtime build chain (items.scr, for orientation — clean):** at boot the data-loader opens
> `items.scr` from the VFS and reads it as a sequence of **fixed-size binary header records** (item
> name and uid in the header, an effect/effect-count field near the record tail). For each record it
> reads a variable block of effect entries (widened from the on-disk stride into the runtime stride),
> allocates the runtime item object, populates it, and dispatches it to the actor/animation catalogue
> by a discriminator byte at the record tail (an animation-class selector). This is a pure binary,
> offset-driven reader with no comma scanning, no text tokenizing and no float-string parsing — i.e.
> categorically not a CSV parser, corroborating that `items.csv` is the dev export and `items.scr` is
> the runtime master. Full binary layout lives in `formats/items_scr.md`.

**Proposed canonical name:** `items_csv_table` (flag for `names.yaml`, orchestrator-owned).

---

## 7. Cross-references

- Binary item database: `formats/items_scr.md` (`items.scr`, `citems.scr`).
- `.scr` family overview / binary-only confirmation: `formats/scr.md`.
- Other VFS text tables: `formats/text_tables.md`.
- VFS container/lookup: `formats/pak.md`.
- CP949 handling convention: register `CodePagesEncodingProvider`, then `Encoding.GetEncoding(949)`.
- Glossary: see `Docs/RE/names.yaml` (proposed name above; orchestrator-owned).
- Provenance: see `Docs/RE/journal.md` (add an entry for this spec).

> **Engineering note:** `items.csv` is an authoring/dev export and is **not loaded by the shipping
> client** (CONFIRMED) — runtime item data comes from `items.scr` (see `formats/items_scr.md`). If a
> developer tool nonetheless parses this CSV, it must cite `// spec: Docs/RE/formats/items_csv.md`
> and use the §3 hazard-safe recipe — a generic `Split(',')` or integer-only numeric parse will
> silently corrupt the table.
