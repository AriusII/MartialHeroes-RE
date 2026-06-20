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
re-verified against doida.exe IDB SHA 263bd994, CYCLE 7 (2026-06-20)   # no-loader verdict HARDENED to CONFIRMED not read (4-way exhaustive static search)
ida_anchor: 263bd994
evidence: [static-ida, vfs-sample]
conflicts: D10-C2 RESOLVED — prior "ABSENT from VFS" verdict REVERTED; file is present. "No client loader" verdict UNCHANGED and HARDENED (CYCLE 7: confirmed not read in build 263bd994).
file_presence: PRESENT at data/ui/mobinfo.mi  # 592 bytes; the prior "ABSENT" verdict is WITHDRAWN/REVERTED
loader: CONFIRMED NOT READ (build 263bd994)   # CYCLE 7 upgrade: no .mi path literal, not in the boot data-table corpus pointer table, not compiled in as a static array — proven by 4-way exhaustive static search (string index, case-insensitive regex, raw ASCII byte scan, UTF-16LE wide scan)
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

## Loader — CONFIRMED NOT READ in build 263bd994 (file present, but never opened by name)

The shipped client has **no `.mi` loader** and **no `.mi` path literal**. CYCLE 7 (2026-06-20)
**hardens** the earlier "appears not read / no code to open it" verdict to a hard
**"CONFIRMED not read in build 263bd994"**, established by an **exhaustive four-way static search** that
returned **zero hits** every way:

1. **String-index search** for the name and its path variants (`mobinfo`, `mobinfo.mi`, `ui/mobinfo`,
   `/mobinfo`, `%s.mi`) → 0 hits.
2. **Case-insensitive regex** over the whole string store for `mobinfo` → 0 hits.
3. **Raw ASCII byte scan** (segment-wide) for the literal bytes of `mobinfo` → 0 hits.
4. **UTF-16LE wide-character scan** for a wide `mobinfo` → 0 hits.

Beyond the absence of the name string, two further static checks confirm the file is genuinely unused:

- **Not in the boot data-table corpus pointer table.** The small data tables and class files the
  client loads at boot are reached through a shared boot-corpus loader that walks a filename-pointer
  array (the `.do` class scripts, `mobs.scr`, the handful of `.xdb` tables, etc.). `mobinfo.mi` is
  **not** an entry in that array — and could not be, since its name string does not exist in the image.
- **Not compiled into the binary as a static array.** Byte scans of the data segments for the
  on-disk record signature (the `count = 21` header followed by the first record, and the
  28-byte-stride record pattern) returned 0 hits, so the table is **not** embedded in the executable
  either. It lives only as a VFS file that nothing opens.

The only superficial `.mi` substring anywhere in the image is an unrelated CRT runtime data fragment
(a false positive), not a filename. The mob-info / target-info HUD panel the client renders is driven
by other data and by hard-coded layout (hard-coded captions and screen positions), not by this file.

The correction earlier passes made was **only** to the file's *presence*: the file **is** packed in the
VFS at `data/ui/mobinfo.mi`, but the client has **no code to open it**. **"Present" ≠ "read"** — a VFS
can carry an asset the shipped client never references (consistent with a tool / editor data file
packed into the archive). Net: the loader question is settled as
**"file present in the VFS, but CONFIRMED not read by the shipped client — no path literal, not in the
boot corpus table, not compiled in."** The associated parser (`Assets.Parsers`) need implement nothing
for a faithful 1:1 client port — the original consumes no `.mi` at runtime. The container layout below
is documented for archival / interoperability completeness (it is a real, decodable VFS artefact),
**not** because the shipped client reads it.

### Where the client's mob data actually comes from (cross-ref)

The mob data the client **does** read does not come from `mobinfo.mi`:

- **`data/script/mobs.scr`** — the mob script/template table, loaded at boot through the boot-corpus
  loader. This (not `mobinfo.mi`) is the runtime mob-template source.
- **`msg.xdb`** — mob name / portrait **strings** (the displayed names and portrait references the
  target-info panel shows) resolve through the message-string table, not through `.mi`.

See **Cross-references** below.

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
| +24 | 4 | u32 LE | **field6 (7th / last field)** — small optional id/index (`0xFFFFFFFF = -1 = none`; small values `99`/`103` observed). Role MOOT — no consumer read-site. | SINGLE-SAMPLE / HYPOTHESIS |

- **Field[2] (+8)** frequently carries `0xFFFFFFFF`, consistent with an absent/optional reference
  (a null sentinel). SINGLE-SAMPLE.
- **Field[4] / field[5] (+16 / +20)** are a **consecutive pair** (the second equals the first plus 1)
  across the observed records — the signature of a `[start, end)` index pair into a separate resource
  array (e.g. a contiguous run of resource/text ids), though this cannot be confirmed without a
  consumer. SINGLE-SAMPLE.
- **Field6 (+0x18 / +24, the 7th and last field) — role MOOT.** Because the file has **no consumer
  read-site** (loader CONFIRMED not read, above), the meaning of this field cannot be pinned from
  client behaviour, so its role is **moot, not merely unresolved**. On the on-disk shape only it is a
  `u32`/`i32 LE` where **`0xFFFFFFFF = -1`** is the "none / not present" sentinel and the small
  populated values (`99`, `103`) read as an **optional small id/index** (HYPOTHESIS — an icon/sprite
  index, a secondary category key, or similar; the integer-with-`-1`-sentinel shape rules out a float
  probability/scale and the small magnitude rules out the large adjacent portrait-resource IDs that
  +16/+20 carry). Any earlier **"portrait_res_3"** labeling for this field is **withdrawn**: the small
  `99`/`103` magnitudes are three orders of magnitude below the ~5.0e6-range portrait IDs and do not fit
  that encoding. SINGLE-SAMPLE / HYPOTHESIS, unconfirmable without a reader.

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
  +16/+20 and the `0xFFFFFFFF` sentinel at +8 are inferences from one file). This includes
  **field6 (+0x18 / +24), whose role is MOOT** — there is no read-site to settle "small index" vs
  "small id" vs "small category". Likely un-recoverable from the client; would need the original
  content tool to confirm.
- **Whether the 21-record count is stable** across any other VFS revision (only one instance exists in
  this build). The container shape itself is sample-verified for this build.

---

## Cross-references

- Container/VFS lookup: `Docs/RE/formats/pak.md`.
- Companion spawn-data spec (also notes `mobinfo.mi` has no client loader and `mob.arr` is a
  present-but-dead tool format): `Docs/RE/formats/npc_spawns.md`.
- **The client's actual mob data comes from elsewhere, NOT from `mobinfo.mi`:**
  - **`data/script/mobs.scr`** — the mob script/template table, loaded at boot (the runtime mob source).
  - **`msg.xdb`** — mob name / portrait strings shown by the target-info HUD.
  See also `Docs/RE/formats/config_tables.md` / `Docs/RE/formats/misc_data.md` for the mob template
  tables; `.mi` is not part of the runtime path.
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
>
> **Provenance — CYCLE 7 (build 263bd994, 2026-06-20; static IDA).** HARDENED the no-loader verdict
> from "appears not read" to **"CONFIRMED not read in build 263bd994"** via an exhaustive **four-way
> static search** (string index, case-insensitive regex, raw ASCII byte scan for `mobinfo`, UTF-16LE
> wide scan) — all 0 hits — plus two corroborating checks: the file is **not in the boot data-table
> corpus filename-pointer table**, and it is **not compiled into the binary as a static array**
> (data-segment scans for the record signature returned 0 hits). Recorded the cross-ref that the mob
> data the client DOES read comes from **`mobs.scr`** (boot-loaded) + **`msg.xdb`** (name/portrait
> strings), not `mobinfo.mi`. Documented **field6 (+0x18 / +24)** as **MOOT** (no consumer read-site):
> on-disk shape only — `u32`/`i32 LE`, `0xFFFFFFFF = -1 = none`, small values an optional small
> id/index (HYPOTHESIS); any "portrait_res_3" labeling is WITHDRAWN. "Present" ≠ "read". No addresses,
> no decompiler output, and no sample payload bytes crossed the firewall.
