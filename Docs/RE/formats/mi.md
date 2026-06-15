# Format: .mi  (mob-info data file — tool/editor format, NO client loader)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file
> with `// spec: Docs/RE/formats/mi.md`.
>
> Promoted from dirty-room analyst notes under EU Software Directive 2009/24/EC Art. 6.
> No decompiler output and no binary virtual addresses appear anywhere in this file.

---

## Status

```
container_layout: SAMPLE-VERIFIED         # 4-byte count + N×28-byte records — exact factorization
loader: RESOLVED — NO CLIENT LOADER       # CAMPAIGN VFS-MASTERY: the shipped client never parses .mi
record_field_semantics: OUT-OF-SCOPE      # no client consumer => meanings un-recoverable from the client (DBG-pending, likely permanent)
sample_count: 1                           # single VFS instance; no second sample to cross-check invariants
```

> **CAMPAIGN VFS-MASTERY verdict (two-witness: loader read-set + black-box runtime).** The
> reconciliation pass settled the long-open loader question: **the shipped client has no loader for
> `mobinfo.mi`.** No part of the runtime parses or consumes it. The mob-info / target-info HUD panel
> the client actually renders is driven by other data and by hard-coded layout — not by this file.
> `mobinfo.mi` is therefore a **tool / editor format only**.
>
> The consequence is decisive for the field set: because **no client code reads the records**, the
> meanings of the per-record fields **cannot be recovered from the client**. They are
> **OUT-OF-CLIENT-SCOPE / DBG-pending** and, absent the original content tool, likely permanently
> un-recoverable. This spec deliberately **does not assign meanings** to those fields — the earlier
> hypothesised names/meanings are withdrawn as unverifiable.

---

## Identification

- **Extension:** `.mi`
- **Found in:** the VFS archive (`data.inf` + `data/data.vfs`); see `formats/pak.md` for VFS
  lookup. The single observed instance is `data/ui/mobinfo.mi` (592 bytes).
- **Role:** **tool / editor data file** associated with the mob-info / target-info HUD panel. It is
  **not loaded by the shipped client** (see Status / Loader). Its precise intended use lives with the
  original content tooling, which is out of scope.
- **Magic / signature:** none — the file has no magic bytes. It is identified by its VFS path.
- **Version field:** none.
- **Endianness:** little-endian throughout (32-bit x86 client). All fields read cleanly as 32-bit
  values. Endianness is inferred from the container shape, not debugger-confirmed.
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

> **Note:** the container *shape* (4-byte count + 28-byte stride) is the only part of this format
> that is established. It tells a parser how to walk the bytes — it does **not** establish what the
> bytes mean, and since no client loader consumes them, their meaning is out of client scope.

---

## Header layout

| Offset | Size | Type | Field        | Notes                                          | Confidence       |
|-------:|-----:|------|--------------|------------------------------------------------|------------------|
| 0x00   | 4    | u32  | recordCount  | number of records that follow (= 21 in the sample) | SAMPLE-VERIFIED |

- **Record count source:** the header `recordCount` field at 0x00.
- Body starts at 0x04.

---

## Record layout — 28 bytes per record (7 × u32) — field SEMANTICS OUT-OF-CLIENT-SCOPE / DBG-pending

The record is exactly seven consecutive 32-bit little-endian fields. The **stride (28 bytes) and
the field boundaries (7 × u32) are SAMPLE-VERIFIED** by the container factorization above.

**The meaning of each field is NOT recoverable from the shipped client**, because the client has no
loader that reads these records (loader-resolved: no client loader exists). The seven slots are
therefore documented **only as opaque u32 cells** with no assigned semantics. A parser that needs to
walk this format must read seven u32s per record and **must not** attach any behaviour to them.

| Field | Offset | Size | Type | Meaning | Confidence |
|------:|-------:|-----:|------|---------|------------|
| 0 | +0x00 | 4 | u32 | opaque — out-of-client-scope (no client consumer) | structure SAMPLE-VERIFIED / meaning DBG-pending |
| 1 | +0x04 | 4 | u32 | opaque — out-of-client-scope (no client consumer) | structure SAMPLE-VERIFIED / meaning DBG-pending |
| 2 | +0x08 | 4 | u32 | opaque — out-of-client-scope (no client consumer) | structure SAMPLE-VERIFIED / meaning DBG-pending |
| 3 | +0x0C | 4 | u32 | opaque — out-of-client-scope (no client consumer) | structure SAMPLE-VERIFIED / meaning DBG-pending |
| 4 | +0x10 | 4 | u32 | opaque — out-of-client-scope (no client consumer) | structure SAMPLE-VERIFIED / meaning DBG-pending |
| 5 | +0x14 | 4 | u32 | opaque — out-of-client-scope (no client consumer) | structure SAMPLE-VERIFIED / meaning DBG-pending |
| 6 | +0x18 | 4 | u32 | opaque — out-of-client-scope (no client consumer) | structure SAMPLE-VERIFIED / meaning DBG-pending |

- **Record stride:** 28 bytes (SAMPLE-VERIFIED).
- **No field meanings are assigned.** Any prior hypothesised names (ordinal / caption-id /
  icon-id / kind-link) are withdrawn: with no client loader they cannot be confirmed from the
  client and would be guesses. They are left blank by design.

---

## Loader — RESOLVED: NO CLIENT LOADER

The CAMPAIGN VFS-MASTERY two-witness pass resolved the previously-open loader question. The
resolution is that **no client loader exists**: the shipped client never opens, parses, or consumes
`mobinfo.mi`. The reasons it has no client consumer are consistent with the earlier static findings:

- **No path literal exists** for the file in the client (no `mobinfo`, `mobinfo.mi`, `%s.mi`,
  `data/ui/...mi`, or any `.mi` path literal). The only superficial `.mi` substring hit is an
  unrelated C-runtime section-name fragment (a false positive).
- **The mob-info / target-info HUD panel the client renders is driven by other data and by
  hard-coded layout** (hard-coded captions and hard-coded screen positions), not by this file. None
  of the data values inside `mobinfo.mi` appear as compiled-in immediates, and no runtime code reads
  them — consistent with the file simply not being a client asset.

Net: the loader question is **resolved as "no client loader."** This is not a deferred unknown about
*which* function loads it; it is the settled conclusion that **the shipped client loads nothing from
this file.** `mobinfo.mi` is a tool / editor artefact. The associated parser (`Assets.Parsers`) need
implement nothing for it; a faithful 1:1 client port does **not** consume `.mi`.

---

## Implications for a faithful port

- **Do not implement a runtime `.mi` consumer.** The original client does not, so a 1:1 port must
  not either. The mob-info panel is rendered from its other, in-code data path.
- If a future toolchain (a content editor) needs to read `.mi`, the **container shape** (4-byte
  count + 28-byte stride) above is the only confirmed contract; the field meanings would have to be
  recovered from the original tool, not the client.

---

## Known unknowns

- **All seven record fields' meanings** are OUT-OF-CLIENT-SCOPE / DBG-pending. With no client
  loader, they cannot be recovered from the client; they are likely permanently un-recoverable
  unless the original content tool surfaces. They are intentionally left unnamed and unassigned
  above.
- **Endianness** is inferred from the clean 32-bit container factorization, not debugger-confirmed.
- **Single sample.** Only one `.mi` file exists, so the stride/header invariants cannot be
  cross-checked against a second instance.

---

## Cross-references

- Container/VFS lookup: `Docs/RE/formats/pak.md`.
- Companion spawn-data spec (also notes `mobinfo.mi` has no client loader):
  `Docs/RE/formats/npc_spawns.md`.
- The client's actual mob-data / mob-info path is data-driven from other sources (e.g.
  `Docs/RE/formats/config_tables.md` for `mobs.scr`); `.mi` is not part of it.
- Glossary: see `Docs/RE/names.yaml`.
- Provenance: see `Docs/RE/journal.md`.
