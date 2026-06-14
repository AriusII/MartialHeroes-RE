# Format: .mi  (mob-info / target-info HUD panel data file)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file
> with `// spec: Docs/RE/formats/mi.md`.
>
> Promoted from dirty-room analyst notes under EU Software Directive 2009/24/EC Art. 6.
> No decompiler output and no binary virtual addresses appear anywhere in this file.

---

## Status

```
container_layout: SAMPLE-VERIFIED   # 4-byte count + N×28-byte records — exact factorization
record_field_semantics: PLAUSIBLE   # the 7 per-record u32 meanings are HYPOTHESES, parser-UNVERIFIED
loader: UNRESOLVED (static)         # no dedicated loader/consumer located statically — LIVE-DEBUGGER-PENDING
sample_count: 1                     # single VFS instance; no second sample to cross-check invariants
```

Read this as three independent confidence levels: the **container shape is solid**, the **field
meanings are working hypotheses**, and the **loader is unknown** until a live-debugger pass pins it.

---

## Identification

- **Extension:** `.mi`
- **Found in:** the VFS archive (`data.inf` + `data/data.vfs`); see `formats/pak.md` for VFS
  lookup. The single observed instance is `data/ui/mobinfo.mi` (592 bytes).
- **Role:** binary data file for the **mob-info / target-info HUD panel** — the popup shown for a
  targeted monster. Each record is believed to describe one widget / sub-element of that panel.
- **Magic / signature:** none — the file has no magic bytes. It is identified by its VFS path.
- **Version field:** none.
- **Endianness:** little-endian throughout (32-bit x86 client). All fields read cleanly as 32-bit
  values. Endianness is inferred, not debugger-confirmed.
- **Census:** exactly one `.mi` file exists in the 43,347-entry VFS. The extension is effectively
  single-purpose.

---

## Container structure — SAMPLE-VERIFIED

The file is a 4-byte header (a single record count) followed by a flat array of fixed-size
28-byte records. The container shape is sample-verified by exact factorization against the full
592-byte instance:

```
4 (header recordCount) + recordCount × 28 = file size
4 + 21 × 28 = 4 + 588 = 592 bytes   (exact, zero residual)
```

The header count and the record stride are mutually self-consistent and reconcile the observed
file size exactly with no leftover bytes, so the header field is confidently the record count and
the stride is confidently 28 bytes (= 7 × u32).

---

## Header layout

| Offset | Size | Type | Field        | Notes                                          | Confidence       |
|-------:|-----:|------|--------------|------------------------------------------------|------------------|
| 0x00   | 4    | u32  | recordCount  | number of widget records that follow (= 21 in the sample) | SAMPLE-VERIFIED |

- **Record count source:** the header `recordCount` field at 0x00.
- Body starts at 0x04.

---

## Record layout — 28 bytes per record (7 × u32) — fields PLAUSIBLE, parser-UNVERIFIED

The record is exactly seven consecutive 32-bit little-endian fields. The **stride (28 bytes) and
the field boundaries (7 × u32) are SAMPLE-VERIFIED**. The **field names and meanings below are
working hypotheses (PLAUSIBLE) derived from a full re-parse of all 21 sample records — they are
parser-UNVERIFIED**: no dedicated loader was located to confirm read order or semantics. Do not
implement business logic against these meanings; treat each as an opaque u32 slot until a loader
confirms them.

| Field | Offset | Size | Type | Hypothesised meaning (PLAUSIBLE)                          | Confidence |
|------:|-------:|-----:|------|----------------------------------------------------------|------------|
| 0     | +0x00  | 4    | u32  | sequential per-record ordinal (one per record, no gaps)  | structure SAMPLE-VERIFIED / meaning PLAUSIBLE |
| 1     | +0x04  | 4    | u32  | caption / text id (primary of a ±1 couple), or none-sentinel | PLAUSIBLE |
| 2     | +0x08  | 4    | u32  | caption / text id (sibling of field 1), or none-sentinel  | PLAUSIBLE |
| 3     | +0x0C  | 4    | u32  | small kind / link id; co-varies with field 6              | PLAUSIBLE / kind-vs-link UNRESOLVED |
| 4     | +0x10  | 4    | u32  | decimal-packed icon / sprite id (primary of a pair); confirmed NOT a pointer | PLAUSIBLE |
| 5     | +0x14  | 4    | u32  | decimal-packed icon / sprite id (sibling of field 4); confirmed NOT a pointer, or none-sentinel | PLAUSIBLE |
| 6     | +0x18  | 4    | u32  | small kind / link id; co-varies with field 3, or none-sentinel | PLAUSIBLE / kind-vs-link UNRESOLVED |

- **Record stride:** 28 bytes (SAMPLE-VERIFIED).
- **None sentinel:** the value `0xFFFFFFFF` is read as a "none / null / absent" marker. It is
  observed in fields 1, 2, 5, and 6, and never in the ordinal field 0. That it is a sentinel is
  HIGH confidence; exactly which fields legitimately carry it is UNVERIFIED.

### Structural signals (the strongest observables — still semantically UNVERIFIED)

These are layout observations from re-parsing all 21 records; they are the evidence behind the
hypothesised meanings above, not confirmation of them.

- **Field 0 is unambiguously a sequential per-record ordinal** — strictly increasing, one per
  record, no gaps. This is the cleanest signal in the file.
- **Two id-couples per record:** (field 1, field 2) and (field 4, field 5).
  - The first couple is a clean **±1 adjacent pair** in a single value band (a text-id family
    signature) — when both are present, one is the other minus 1. One record carries the sentinel
    on a member of this couple instead.
  - The second couple's members are **decimal-structured composite ids**, falling in two value
    bands. The sibling delta is **+1 in most records but +3 in the lower-band records**, so the
    pair is *not* a uniform two-state ±1 pair. This argues for a packed (base, range) encoding for
    an icon / sprite group rather than two adjacent atlas cells. The "base · 10000 + small suffix"
    shape reads as a packed icon/sprite id.
- **Fields 3 and 6 co-vary** (field 6 commonly tracks field 3 by a small constant offset, or is the
  none-sentinel) — they look like a small **kind / link couple**, not geometry.
- **Fields 4 and 5 were confirmed NOT to be pointers** — their values do not land on valid code or
  data addresses and are best read as decimal-structured composite ids, i.e. data read from the
  file, not runtime handles.
- **No field reads as a plausible pixel x/y/w/h or IEEE-754 float.** If the panel's geometry lives
  in this file it is encoded indirectly (e.g. field 3 indexing a layout table elsewhere); it may
  also not be in `.mi` at all (the separately-located mob-info panel *renderer* uses hard-coded
  screen coordinates and hard-coded caption ids — see "Loader" below).

---

## Enumerations / flags

- `0xFFFFFFFF` — none / null / absent sentinel (see record layout). Sentinel role HIGH; per-field
  legitimacy UNVERIFIED.
- Field 3 (+0x0C) and field 6 (+0x18) appear to be small bounded id enums (kind / style / link),
  but their value sets and meanings are UNVERIFIED.

---

## Loader — UNRESOLVED (static), LIVE-DEBUGGER-PENDING

No dedicated `.mi` loader / consumer could be located in the static pass. This is a deliberate,
honest "unknown", not an omission. The reasons it resisted static discovery:

- **No path literal exists.** There is no `mobinfo`, `mobinfo.mi`, `%s.mi`, `data/ui/...mi`, or any
  literal `.mi` path in the client's string table. The only superficial `.mi` substring hit is an
  unrelated MSVC C-runtime section-name fragment (a false positive). Consequently the file's path
  is supplied as non-literal data (built at runtime, or held in a CP949 table not flagged as a
  string), so the loader cannot be pinned by a path string cross-reference.
- **The file is opened through the generic by-name VFS reader.** The VFS layer exposes a by-name
  open path feeding a generic file-object reader and a load-whole-file helper. Its callers are
  named per-asset loaders (terrain cell list, texture-name list, sky, sound tables, skin manager,
  bind-pose pool, region/area tables, and others) — **none of them is a mob-info / `.mi` loader.**
  The `.mi` file is opened through the generic reader with a non-literal path, which is exactly why
  no named loader surfaces.
- **The 28-byte stride signature is unreliable here.** A binary-wide sweep for "count then 28-byte
  loop" returns many sites, but the strongest UI candidates iterate **arrays of MSVC `std::string`
  objects**, which happen to be ~28 bytes each in this build. The stride coincidence drowns out the
  genuine `.mi` record loop, so the structural-signature shortcut does not isolate it.
- **The mob-info panel *renderer* was located and is NOT the `.mi` consumer.** There is a clear
  mob-info / target-info HUD panel subsystem (a renderer plus an input/event handler with mouse
  hit-testing on screen rectangles). It builds its rows from **caption ids hard-coded in code** and
  uses **hard-coded screen coordinates**. Critically, the data values seen inside `mobinfo.mi`
  (e.g. caption-band ids and the decimal-packed icon ids) do **not** appear anywhere as compiled-in
  immediates — proving those values are read from the file at runtime, not baked into the renderer.
  So `mobinfo.mi` feeds a *different*, data-driven consumer that this static pass could not isolate
  without the runtime path.

Net: the container is sample-verified and the field structure is well characterised, but the
**dedicated loader / consumer is statically unresolved** and resolving it requires the live
debugger.

---

## Live-debugger probe plan (how the loader gets pinned — neutral prose)

This is the prescribed next step. The maintainer launches the real client live (pilot the existing
session — never auto-start the debugger), then:

1. **Catch the open by path content.** Breakpoint the generic by-name VFS open router and the
   load-whole-file helper it calls. On each hit, read the path argument (a CP949 string) and
   continue until the path ends in `mobinfo.mi`. The **return frame at that hit identifies the
   actual `.mi` loader** — that function is the one to analyse for the real read order and field
   bindings. In-game trigger: target a monster / open the mob-info panel so the panel's data file
   is requested.

2. **Read the parsed buffer.** Once the loader frame is known, breakpoint just after the file
   buffer is loaded and read the 592-byte buffer at the buffer pointer; confirm the header u32 is
   the record count (21) and walk the 28-byte stride to re-verify the factorization at runtime.

3. **Single-step the consume loop to bind each field.** Step the record-consume loop and observe
   which struct slot each of the 7 u32 fields is stored into and what each is subsequently passed
   to:
   - a caption / text lookup would confirm fields 1 and 2 are caption ids;
   - an icon / sprite resource fetch would confirm fields 4 and 5 are packed icon ids;
   - a UI rect / placement setter, if any field flows into one, would finally tie a field to
     geometry — none looks like a coordinate statically, so a *negative* result here is itself
     informative (geometry lives elsewhere);
   - a kind/style switch or a sibling-link store would clarify fields 3 and 6.

Outcome: this probe is expected to **upgrade fields 0–6 from PLAUSIBLE to CONFIRMED**, resolve the
kind-vs-link ambiguity on fields 3 / 6, and confirm the icon-id packing on fields 4 / 5. Until it
runs, the 7 record fields stay PLAUSIBLE / UNVERIFIED.

---

## Known unknowns

- **Loader not located (static).** No dedicated parser/consumer was found; the path is non-literal
  and the file is opened through the generic by-name VFS reader. All per-field meanings are
  sample-inferred, not parser-verified. LIVE-DEBUGGER-PENDING (see probe plan).
- **No coordinates identified.** No field reads as a plausible pixel x/y/w/h or float. Panel
  geometry, if encoded here, is indirect (an index into a layout table elsewhere); it may not live
  in `.mi` at all (the located renderer uses hard-coded coordinates).
- **Fields 1 / 2 (caption ids)** are assumed text/string ids by value-band analogy to the client's
  message-id range; not confirmed against the string/message table.
- **Fields 4 / 5 (icon ids)** are confirmed NOT pointers and read as decimal-packed composite ids,
  but their resolution target (which atlas / sprite table they index) is unknown.
- **Fields 3 / 6** are a small co-varying couple of unknown role (kind vs. style vs. link).
- **Endianness** is inferred (consistent with the 32-bit client and clean LE reads), not
  debugger-confirmed.
- **Single sample.** Only one `.mi` file exists, so the stride/header invariants cannot be
  cross-checked against a second instance.

---

## Cross-references

- Container/VFS lookup: `Docs/RE/formats/pak.md`.
- Sibling UI/config descriptors: `Docs/RE/formats/ui_manifests.md`,
  `Docs/RE/formats/config_tables.md`.
- Possible caption/text-id band relationship (UNVERIFIED): `Docs/RE/formats/msg_xdb.md`.
- Glossary: see `Docs/RE/names.yaml` (proposed names flagged for the orchestrator, all provisional
  pending loader confirmation): `MiPanelDescriptor` (the file), `MiPanelHeader.RecordCount`
  (header u32), `MiWidgetRecord` (the 28-byte record), and the provisional record fields
  `EntryId` (0), `CaptionId` / `CaptionIdAlt` (1/2), `KindOrLink` (3), `IconId` / `IconIdSibling`
  (4/5), `LinkOrNext` (6).
- Provenance: see `Docs/RE/journal.md`.
