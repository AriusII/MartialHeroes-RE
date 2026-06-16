# Format: .mi  (mob-info data file — PRESENT in the VFS at `data/ui/mobinfo.mi`, but NO client loader)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file
> with `// spec: Docs/RE/formats/mi.md`.
>
> Promoted from dirty-room analyst notes under EU Software Directive 2009/24/EC Art. 6.
> No decompiler output and no binary virtual addresses appear anywhere in this file.

---

## Status

```
verification: sample-verified            # mobinfo.mi IS in the VFS at data/ui/mobinfo.mi; 592 B = 4 + 21 x 28 confirmed by stride arithmetic and the count-header byte
ida_reverified: 2026-06-16
ida_anchor: 263bd994
evidence: [static-ida, vfs-sample]
conflicts: D10-C2 RESOLVED — prior "ABSENT from VFS" verdict REVERTED; file is present. "No client loader" verdict UNCHANGED (no path literal in the executable).
file_presence: PRESENT at data/ui/mobinfo.mi  # 592 bytes; the prior "ABSENT" verdict is WITHDRAWN/REVERTED
loader: RESOLVED — NO CLIENT LOADER          # the shipped client has no .mi path literal; it never opens/parses this file by name
container_layout: SAMPLE-VERIFIED            # 4-byte u32 count (=21) + 21 x 28-byte records; 7 u32 fields per record
record_field_semantics: OUT-OF-CLIENT-SCOPE  # no client consumer => per-field meanings are not recoverable from the client (single-sample provisional reading only)
```

> **CAMPAIGN 10 Block D re-verify (build 263bd994, 2026-06-16; two-witness: VFS sample + static IDA).
> The earlier "ABSENT FROM VFS" verdict is REVERTED.** A black-box pass over the 43,347-entry VFS
> located the file at **`data/ui/mobinfo.mi`** (not under `data/script/` — the prior census missed it
> by searching the wrong path/pattern), **592 bytes**. The two witnesses now read as follows:
>
> 1. **PRESENT in the VFS (witness 1 — VFS sample).** `data/ui/mobinfo.mi`, 592 bytes. The container
>    shape that an earlier draft had *withdrawn* — `4-byte count + N × 28-byte records` — is
>    **re-instated** as [sample-verified]: the leading `u32` count is **21** and `(592 − 4) / 28 = 21`
>    exactly (zero remainder). Each record is 7 × `u32 LE`.
> 2. **No client loader (witness 2 — static IDA).** The shipped client contains **no `.mi` path
>    literal** (`mobinfo`, `mobinfo.mi`, `ui/mob…`, `%s.mi`) — re-confirmed on build 263bd994 (the path
>    string is absent from the executable). The only superficial `.mi` substring elsewhere is an
>    unrelated runtime section-name fragment (a false positive). The mob-info / target-info HUD panel
>    the client renders is driven by other data and hard-coded layout, **not** by this file.
>
> Net: the file **ships in the VFS and its container shape is sample-verified**, but the **shipped
> client has no code path that opens it by name** — it loads/uses no runtime path for `.mi`. It is, in
> effect, a present-but-unloaded VFS artefact (consistent with a tool / editor data file packed into
> the archive). Because there is **no client consumer**, the per-record field *meanings* cannot be
> confirmed from the client side and remain **OUT-OF-CLIENT-SCOPE** (a single-sample provisional
> reading is given below for archival/interoperability completeness only).

---

## Identification

- **Extension:** `.mi`
- **Found in:** the VFS (`data.inf` + `data/data.vfs`; see `formats/pak.md` for VFS lookup), at the
  logical path **`data/ui/mobinfo.mi`** — SAMPLE-VERIFIED (build 263bd994). Exactly **one** `.mi` file
  is present in the shipped data set.
- **File size:** **592 bytes** (SAMPLE-VERIFIED).
- **Role:** mob-info / target-info data, by name. The file **ships in the VFS** but is **not opened by
  the shipped client** (no path literal). Its precise intended use lives with the original content
  tooling; the runtime mob-info / target-info HUD is rendered from other, in-code data.
- **Magic / signature:** none — the file begins directly with a `u32` record-count header (no magic
  bytes, no version field).
- **Version field:** none observed.
- **Endianness:** little-endian (the count header and all record fields are `u32 LE`).
- **Census:** **one** `.mi` file in the 43,347-entry VFS (`data/ui/mobinfo.mi`).

---

## Loader — RESOLVED: NO CLIENT LOADER (file present, but never opened by name)

The shipped client has **no `.mi` loader** and **no `.mi` path literal**. This was re-confirmed on
build 263bd994: there is no `mobinfo`, `mobinfo.mi`, `ui/mob…`, or `%s.mi` string in the executable,
and no code path that opens, parses, or consumes a `.mi` file by name. The mob-info / target-info HUD
panel the client renders is driven by other data and by hard-coded layout (hard-coded captions and
screen positions), not by this file.

The correction this pass is **only** to the file's *presence*: the file **is** packed in the VFS at
`data/ui/mobinfo.mi`, but the client has **no code to open it**. The two facts are not in tension — a
VFS can carry an asset the shipped client never references. Net: the loader question is settled as
**"file present in the VFS, but no client loader / no path literal."** The associated parser
(`Assets.Parsers`) need implement nothing for a faithful 1:1 client port — the original consumes no
`.mi` at runtime. The container layout below is documented for archival / interoperability
completeness (it is a real, decodable VFS artefact), **not** because the shipped client reads it.

---

## Container layout — SAMPLE-VERIFIED (count header + fixed-stride records)

The file is a **count-prefixed flat array of fixed 28-byte records**.

- **Header:** a single **`u32 LE` record count** at file offset +0. Observed value **21**. SAMPLE-VERIFIED.
- **Records:** **21 records × 28 bytes** immediately following the header. SAMPLE-VERIFIED via stride
  arithmetic: `(592 − 4) / 28 = 21` exactly (zero remainder).
- **Record stride:** **28 bytes** = **7 × `u32 LE`**. SAMPLE-VERIFIED (field width); the per-field
  *roles* are single-sample provisional (see below).

| Offset | Size | Type   | Field         | Notes |
|-------:|-----:|--------|---------------|-------|
| +0     | 4    | u32 LE | `record_count`| Number of records that follow (observed **21**). The records begin at +4. |

### Per-record layout (28 bytes, stride = 0x1C) — SINGLE-SAMPLE / OUT-OF-CLIENT-SCOPE roles

Each record is **7 consecutive `u32 LE`** fields. The field *widths and count* are SAMPLE-VERIFIED;
the *roles* below are a **single-file provisional reading** only — there is **no client consumer**, so
the meanings cannot be confirmed from the client side.

| Offset within record | Size | Type   | Proposed role (provisional)                                  | Confidence |
|---------------------:|-----:|--------|--------------------------------------------------------------|------------|
| +0  | 4 | u32 LE | entry index or resource id                                       | SINGLE-SAMPLE |
| +4  | 4 | u32 LE | resource / text id A (e.g. a string-table reference)             | SINGLE-SAMPLE |
| +8  | 4 | u32 LE | nullable field — `0xFFFFFFFF` observed as an absent/optional sentinel; otherwise a secondary resource id | SINGLE-SAMPLE |
| +12 | 4 | u32 LE | sub-id or category code                                          | SINGLE-SAMPLE |
| +16 | 4 | u32 LE | **paired field A** — large value; in every observed record this and the next field are **consecutive integers** (field[5] = field[4] + 1) | SINGLE-SAMPLE |
| +20 | 4 | u32 LE | **paired field B** — `= field[4] + 1` across all observed records (a consecutive pair with +16) | SINGLE-SAMPLE |
| +24 | 4 | u32 LE | secondary sub-id or link code                                    | SINGLE-SAMPLE |

- **Field[2] (+8)** frequently carries `0xFFFFFFFF`, consistent with an absent/optional reference
  (a null sentinel). SINGLE-SAMPLE.
- **Field[4] / field[5] (+16 / +20)** are a **consecutive pair** (the second equals the first plus 1)
  across the observed records — the signature of a `[start, end)` index pair into a separate resource
  array (e.g. a contiguous run of resource/text ids), though this cannot be confirmed without a
  consumer. SINGLE-SAMPLE.

> All record-field roles are **SINGLE-SAMPLE and UNVERIFIED** (one file, no second instance, no client
> consumer). They are recorded for interoperability/archival completeness only. A port must **not**
> branch on these provisional roles. The **container shape** (count header + 21 × 28-byte records,
> 7 `u32` per record) is the sample-verified, load-bearing fact; the field *meanings* are not.

---

## Implications for a faithful port

- **Do not implement a runtime `.mi` consumer.** The original client opens no `.mi` file by name
  (no path literal), so a 1:1 port must not either. The mob-info / target-info panel is rendered from
  its other, in-code data path.
- **The file is present in the VFS** (`data/ui/mobinfo.mi`, 592 B). A VFS-level tool may decode its
  container (count + 28-byte records) for inspection, but the shipped game does not read it — keep it
  out of the runtime path.

---

## Known unknowns

- **Per-record field semantics.** With **no client consumer**, the meaning of each of the 7 `u32`
  fields is **OUT-OF-CLIENT-SCOPE / single-sample provisional** (the pointer/index-pair reading at
  +16/+20 and the `0xFFFFFFFF` sentinel at +8 are inferences from one file). Likely un-recoverable
  from the client; would need the original content tool to confirm.
- **Whether the 21-record count is stable** across any other VFS revision (only one instance exists in
  this build). The container shape itself is sample-verified for this build.

---

## Cross-references

- Container/VFS lookup: `Docs/RE/formats/pak.md`.
- Companion spawn-data spec (also notes `mobinfo.mi` has no client loader and `mob.arr` is a
  present-but-dead tool format): `Docs/RE/formats/npc_spawns.md`.
- The client's actual mob-data / mob-info path is data-driven from other sources (e.g.
  `Docs/RE/formats/config_tables.md` for the mob template tables); `.mi` is not part of the runtime
  path.
- Glossary: see `Docs/RE/names.yaml`.
- Provenance: see `Docs/RE/journal.md`.

> **Provenance — CAMPAIGN 10 Block D (build 263bd994, 2026-06-16; two-witness: VFS sample + static
> IDA).** REVERTED the prior "ABSENT FROM VFS / container WITHDRAWN" verdict: the file **is** present
> at `data/ui/mobinfo.mi` (592 B), and the container shape **`4-byte u32 count (=21) + 21 × 28-byte
> records`, 7 `u32`/record** is re-instated as [sample-verified] (CONFLICT D10-C2 resolved in favour of
> the sample). The **"no client loader / no path literal"** verdict is UNCHANGED — re-confirmed on
> build 263bd994 (no `.mi` path string in the executable). Per-record field meanings remain
> OUT-OF-CLIENT-SCOPE (single-sample provisional). No addresses, no decompiler output, and no sample
> payload bytes crossed the firewall.
