# Format: .csv  (`data/script/items.csv` — flat comma-delimited item table)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code,
> NO binary addresses. Consumed by `Assets.Parsers`. Every loader an engineer writes for this
> table must cite `// spec: Docs/RE/formats/items_csv.md`.
>
> Promoted from dirty-room harness observation of the maintainer's legally-owned client VFS
> under EU Software Directive 2009/24/EC Art. 6. No IDA used; no decompiler output appears here.

---

## Status

```
items.csv:  PARTIAL   # delimiter/encoding/line-ending/headerlessness CONFIRMED;
                      # leading columns (0..6) role-inferred from observed value patterns;
                      # full column count and per-stat-column roles UNVERIFIED;
                      # the TWO parser hazards below are CONFIRMED and load-bearing.
```

This is the **only `.csv` file in the VFS** and is **not** referenced by any prior spec
(`items_scr.md`, `text_tables.md`, `scr.md`). It was discovered during the VFS-DEEP-II residual
text sweep. It is a flat, comma-separated, line-oriented text table — distinct from the binary
`data/script/items.scr` item database (see §6, Relationship).

---

## Identification

- **Extension:** `.csv`
- **Found in:** the engine VFS (`data.inf` + `data/data.vfs`); logical path `data/script/items.csv`.
  Identified by path, not by content magic. See `formats/pak.md` for the VFS container.
- **Magic / signature:** none — byte 0 is the first character of the first data field. CONFIRMED.
- **Version field:** none.
- **Header row:** **NONE.** The file begins directly with data; there is no column-name row.
  CONFIRMED.
- **Delimiter:** comma (`,`). CONFIRMED.
- **Line ending:** **LF only** (`0x0A`) — note this differs from the CRLF used by the other VFS
  text tables. CONFIRMED.
- **Encoding:** CP949 (EUC-KR superset). The name and description fields hold Korean text; numeric
  fields are the ASCII subset of CP949. CONFIRMED.
- **Endianness:** not applicable (text format).
- **Size class:** the largest text table in the VFS (~33 MB). At an estimated ~1 KB per row this is
  on the order of tens of thousands of rows; the exact row count is UNVERIFIED and should be derived
  by counting LF line terminators at load time, not assumed.

---

## 1. Column layout (as recovered)

Each row is one item record. The leading columns below were inferred from value patterns across the
observed rows; columns beyond the early stat fields are a wide numeric array whose individual roles
are UNVERIFIED. **Column indices below assume the embedded-comma hazard (§2) has already been
correctly resolved** — a naive split shifts every index after column 0.

| col# | type   | role (inferred)                                              | confidence |
|------|--------|-------------------------------------------------------------|------------|
| 0    | string | `item_name` — CP949; **MAY CONTAIN EMBEDDED COMMAS** (hazard) | HIGH (type); HAZARD |
| 1    | u32    | `item_id` — 9–12 digit numeric id                            | HIGH       |
| 2    | string | `item_description` — CP949; **MAY CONTAIN EMBEDDED COMMAS** (hazard) | HIGH (type); HAZARD |
| 3    | u32    | small int (0 in all observed rows)                          | LOW        |
| 4    | u32    | `base_item_id` / archetype id (9-digit)                     | HIGH       |
| 5    | u32    | secondary type id (9-digit)                                 | MEDIUM     |
| 6    | u32    | small flag (1 observed)                                     | LOW        |
| 7..N | u32 **or f32** | wide stat array — mostly 0 with sparse non-zero values; **at least one column is a float** (hazard, §2) | LOW–MEDIUM |

- **Record count source:** number of LF-terminated lines (no count prefix, no header). UNVERIFIED
  exact value; derive at load.
- **Record structure:** `{name},{id},{description},{numeric columns…}`. The first two text fields
  (cols 0 and 2) carry the embedded-comma hazard; everything from col 3 onward is numeric.
- **Full column count UNVERIFIED** — rows span well over 100 comma-separated fields. A complete
  column census requires a dedicated pass that counts commas per row **after** correcting for the
  embedded commas in cols 0 and 2.

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

### HAZARD B — at least one numeric column is a float (HIGH)

At least one numeric column uses **floating-point notation with a period decimal separator**
(observed values in the `0.26`–`0.3` range). A parser that assumes every column after the
description is an **integer** will fail two ways:

1. **Phantom column split** — a hand-rolled tokenizer that treats the period as a separator (or
   rejects non-integer tokens) turns `0.26` into two tokens (`0` and `26`, or `0` and `.26`),
   inserting a phantom column and **shifting every subsequent stat column by one** for that row.
2. **Silent value loss** — an integer-only parse of `0.26` yields `0` (or throws), dropping the
   fractional stat.

**Correct rule:** parse the numeric tail as **floats** (invariant/US culture — period is the decimal
separator, never a comma), or detect-and-branch per token. **Use the invariant culture explicitly**
so a machine with a comma-decimal locale does not mis-read `0.26`. The decimal point is part of the
value, never a delimiter.

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

- **Full column count** — rows exceed ~100 fields; only cols 0–6 are role-inferred. The wide numeric
  tail (col 7+) is an item-stat array (attack/defense/required-level/etc., by analogy to the binary
  `items.scr` stats block) but per-column roles are UNVERIFIED. IMPACT: MEDIUM.
- **Exact float-column position(s)** — at least one float column exists; which fixed index (or
  indices) it occupies is UNVERIFIED because the embedded-comma hazard must be resolved first to
  establish a stable index. IMPACT: MEDIUM. The recipe in §3 handles it positionally without needing
  the fixed index.
- **Row count** — file is ~33 MB; exact record count UNVERIFIED (derive by counting LF lines).
- **Runtime role** — whether the shipping client loads `items.csv` at all, or whether it is a
  developer/authoring export superseded at runtime by binary `items.scr`, is UNVERIFIED (see §6).
- **Col 3 / col 6 flag semantics** — UNVERIFIED.
- No IDA cross-check was performed (black-box lane).

---

## 6. Relationship to `items.scr`

`data/script/items.scr` is the **binary** item master database (variable-length records, embedded
null-padded CP949 name/description + a numeric stats block — see `formats/items_scr.md`). This CSV
appears to be a **flat text parallel** of the same item data, formatted for human editing/export:

- The `item_id` in CSV col 1 is expected to correspond to the item id in `items.scr` records,
  making the two parallel views of one dataset rather than independent sources. (Cross-key
  correspondence is **inferred, not byte-verified** — UNVERIFIED.)
- Which one the shipping client loads at runtime is UNVERIFIED. The binary form is smaller and faster
  to parse, so `items.scr` is the **probable runtime loader** and `items.csv` is plausibly an
  authoring/developer export. A faithful loader should prefer `items.scr` unless a CSV-only field is
  needed; if both are read, reconcile on `item_id`.

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

> **Engineering note:** a C# loader for this table must cite
> `// spec: Docs/RE/formats/items_csv.md` and must use the §3 hazard-safe recipe — a generic
> `Split(',')` or integer-only numeric parse will silently corrupt the table.
