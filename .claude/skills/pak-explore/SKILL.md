---
name: pak-explore
description: Use to INSPECT Martial Heroes archives & asset data without ever exporting copyrighted payload — three modes. PAK-INDEX lists a legacy .pak archive's directory (name/offset/size only, never payload bytes) from the Docs/RE/formats/pak.md spec. VFS-INSPECT opens the REAL client VFS (data.inf + data/data.vfs) through the production Assets.Vfs/Parsers API via the bundled net10.0 vfsls harness — list/contains/census/head, plus decode/extract/convert/hexdump/coverage and 12 family-census subcommands (scan-mot/bnd/skn/ui/xeff/sound/fx/minimap/quest, dump-msgxdb/uitex/do). DATA-FORMAT recovers a CP949 text/tab data-file format (skin.txt, actormotion.txt, bgtexture.txt, *.csv, text *.scr) by observing real bytes through that harness — NOT by decompiling — and writes neutral column/delimiter findings to Docs/RE/_dirty/formats/<name>.raw.md for later promotion. Read-only over originals; metadata/counts/short previews only; no IDA, no _dirty/ decompiler output.
allowed-tools: Read Write Bash(python *) Bash(dotnet *) Bash(mkdir *) Bash(copy *) Bash(xcopy *)
model: sonnet
effort: high
---

# pak-explore — inspect archives & asset data (no payload export)

One skill, three inspection modes over the user's own legally-owned client files. All are
**read-only over the originals**, emit **metadata / counts / short previews only** (never a full
asset dump or copyrighted payload), and touch **no IDA and no `_dirty/` decompiler output**.

| Mode | Target | Tool | Output |
|---|---|---|---|
| **PAK-INDEX** | a legacy `.pak` archive's directory | `scripts/pak_index.py` | `index offset size name` listing |
| **VFS-INSPECT** | the real VFS (`data.inf` + `data/data.vfs`) | `scripts/vfsls/` net10.0 harness (+ sibling harnesses) | metadata / decode summary / census |
| **DATA-FORMAT** | a CP949 text/tab data-file format | the `vfsls` harness (observe) | `Docs/RE/_dirty/formats/<name>.raw.md` |

**Ground-truth doctrine.** An archive's/asset's real layout is the truth — proved in the original's
loader routines inside `doida.exe` and/or witnessed directly in the maintainer's own sample bytes,
then captured in the committed `Docs/RE/formats/*.md` specs (the **derived truth** these modes read
from and cite). A `decode error` or a census that disagrees with the original is a spec-or-parser bug
measured against the real bytes — surface it, never "fix" it by inventing a layout here.

# Mode A — PAK-INDEX (list a .pak directory, never its payload)

Read-only inspection of a legacy `.pak` archive's **directory/index** so an engineer can sanity-check
`Docs/RE/formats/pak.md`, plan `Assets.Vfs` mounting, and count/locate logical files — **without ever
reading, copying, or emitting the compressed/raw asset bytes**.

## Hard rules (non-negotiable)

1. **NEVER extract, decompress, decode, hexdump, or print payload bytes** from a `.pak`. The only data
   that may leave is, **per directory entry**: the logical name (a path string), the payload byte
   offset, and the payload byte length (plus optional index-only flags/CRC the spec marks). Sizes and
   offsets are numbers, not content.
2. The bundled `pak_index.py` is a **listing tool only** with no extract mode. If the user asks to
   "extract / unpack / dump the files / get the bytes out", **REFUSE** and explain that extracting
   copyrighted payload is outside this project's clean-room/preservation scope; offer the index listing.
3. **Read-only on disk.** Open the archive read-only; never modify/truncate/rewrite a `.pak`.
4. **Output to stdout or a gitignored scratch file only** (e.g. `Docs/RE/_dirty/scratch/pak-index.txt`
   or `.work/`); never into a committed/tracked path.
5. **Spec-driven, not guess-driven.** The header magic, endianness, and entry record layout come from
   `Docs/RE/formats/pak.md`; pass them via flags. Don't hardcode RE'd offsets without citing that spec.

## Steps

1. **Confirm intent is inspection, not extraction** (apply Hard Rule #2 if not).
2. **Read the spec.** From `Docs/RE/formats/pak.md` extract the index parameters: magic/version,
   endianness, where the directory lives (header-after-magic vs footer index), and the per-entry record
   (name encoding + field order/sizes for `name`/`offset`/`size` and any flags/CRC). If `pak.md` does
   **not exist yet**, don't invent a layout — run the script's `--probe` mode (size + first magic bytes
   only) to bootstrap, then hand to `asset-format-doc` to seed the spec.
3. **List the index:**
   ```bash
   python "${CLAUDE_SKILL_DIR}/scripts/pak_index.py" --pak "<path-to.pak>" \
     --magic "<sig from spec>" --endian <little|big> --index <header|footer> \
     --layout "<field=size,... per spec>"
   ```
   `--help` shows the flag grammar + built-in layout presets. It validates the magic, walks the
   directory, prints one line per entry (`index offset size name`), and refuses to read beyond the index.
4. **Optionally save** the listing to a gitignored scratch file (Hard Rule #4).
5. **Summarize**: entry count, total payload bytes covered, names that look like directory roots (VFS
   mount points), and whether offsets are monotonic and within the file size (a cheap integrity check
   that needs no payload reads).

**Decide:** offsets non-monotonic / out of range → a layout/endianness mismatch with the spec, not a
corrupt file — recheck `--endian` + record sizes against `pak.md`. "Where does mob/skin/terrain id X
resolve on disk?" → that's `/asset-chain-trace`, not this mode.

# Mode B — VFS-INSPECT (open the real VFS via the vfsls harness)

The single home for the throwaway VFS browser that has been hand-rebuilt many times. It mounts the
real client archive through the **production** VFS API and lets you list entries, count by extension,
test a path, peek at head bytes, and drive the production `Assets.Parsers` decoders directly — so
observations match what the shipping client sees.

It drives `MartialHeroes.Assets.Vfs.MappedVfsArchive.Open(infPath, vfsPath)` with
`infPath = "D:/MartialHeroesClient/data.inf"`, `vfsPath = "D:/MartialHeroesClient/data/data.vfs"`.
`GetEntries()` → `ReadOnlySpan<VfsEntry>` (`.Name` = lower-cased virtual path,
`.DataOffset`/`.DataSize` = blob slice); `GetFileContent(path)` → zero-copy `ReadOnlyMemory<byte>`;
`Contains(path)` tests membership. The VFS holds ~43,347 entries; all text is **CP949** (Korean), so
the harness registers the code-pages provider.

**This is a THROWAWAY diagnostic harness** bundled under `scripts/vfsls/` — **never** added to
`MartialHeroes.slnx`, never committed as a solution member, never `git add` its `bin/`/`obj/`. It is
read-only over the VFS (never extracts/rewrites payloads in place) and **not** a clean-room concern:
it reads the user's own legally-owned client files, the same bytes production reads. No IDA, no `_dirty/`.

## Preconditions

1. The real client present at `D:/MartialHeroesClient/` with `data.inf` + `data/data.vfs` (or pass
   `--inf`/`--vfs` overrides). If absent, STOP and tell the user the bring-your-own client must be mounted.
2. A .NET 10 SDK (`dotnet --version` ≥ 10); the harness targets `net10.0`.
3. The harness `ProjectReference`s `Assets.Vfs` + `Assets.Parsers` (+ `Assets.Mapping` for `convert`) by
   absolute path, so it always tracks the live API — if the API drifted, it fails to compile (a feature:
   fix the call site, never patch the production library).

## Run it in place

```powershell
dotnet run -c Release --project "${CLAUDE_SKILL_DIR}/scripts/vfsls" -- <args>
```
Use `-c Release` so the memory-mapped read path is not debug-throttled. With no args it prints a
summary + extension census.

**Generic filtering:** `<substring> …` (AND-match names) · `--ext .skn` (repeatable) · `--count` ·
`--census` (count by extension) · `--head <path>` (first 256 bytes as hex + CP949 preview) ·
`--head-bytes <n>` · `--contains <path>` (true/false) · `--limit <n>` (default 200; `0`=unlimited) ·
`--inf <path>` / `--vfs <path>`.

**Understand-a-file subcommands** (registry-driven; one `FormatRegistry.cs` extension→capability table):

| Subcommand | What it does |
|---|---|
| `decode <vfs-path>` | Auto-detect format (extension; path/magic for ambiguous `.eff`) → matching `Assets.Parsers` decoder; prints a STRUCTURED summary (record/vertex/face counts, dimensions, key header fields, cited spec). Never raw bytes; a parser exception is a `decode error`, not a crash. |
| `extract <vfs-path> <out-file>` | Write the entry's RAW bytes to an EXTERNAL out-file. GUARD: refuses any path inside the repo/`.git`; prints a never-commit warning. |
| `convert <vfs-path> <out-dir>` | Convert via `Assets.Mapping` (mesh→GLB, texture→PNG, `.xeff`→JSON, image/audio passthrough); `.skn` auto-resolves its companion `g{id_b}.bnd`. Same external-only guard. |
| `hexdump <vfs-path> [--at <off>] [--len <n>] [--header]` | Windowed hexdump (default 64B, capped 512); `--header` adds a structural annotation. No full dumps. |
| `coverage` | Print the registry (extension → decode?/convert? + cited spec) and cross-reference `Docs/RE/formats/*.md` for documented-but-unparsed formats. No live VFS needed. |

**Family-census subcommands** (each accepts `--inf`/`--vfs`):

| Subcommand | Census | Spec |
|---|---|---|
| `scan-mot [--id <id_a>]` | .mot animation: real vs stub, BANI-variant, frame/track histograms | `formats/animation.md` |
| `scan-bnd` | .bnd skeletons: bone-count distribution, base_id, single vs multi-bone | `formats/mesh.md` |
| `scan-skn` | .skn skinned meshes: id_b skeleton link, vertex/face totals, multi-weight ratio | `formats/mesh.md` |
| `scan-ui` | data/ui/ DDS: dimensions + fourCC, grouped by subdir, NPOT flags | `formats/texture.md`, `ui_manifests.md` |
| `dump-msgxdb [--id N] [--range A B]` | msg.xdb records (u32 id + CP949 string, 516B stride) | `formats/misc_data.md §6` |
| `dump-uitex` | UiTex.txt manifest (tex_id → vfs_path), each path verified against the VFS | `formats/ui_manifests.md §1` |
| `scan-xeff` | .xeff particle effects: element_count distribution, 9-digit skill-code, duplicate effect_ids | `formats/effects.md §A` |
| `scan-sound` | .ogg under sound/2d\|3d + per-area sound tables (.bgm/.bge/.eff/.wlk/.run) w/ VFS existence | `formats/sound_tables.md` |
| `scan-fx` | .fx1–.fx7 terrain layers: count + bytes/layer + header field histograms (arbitrates FX2 field[3]) | `formats/terrain_layers.md §1` |
| `dump-do` | 12 per-class stance `.do` (stride 116/0x74): record count + iconSrcX/Y validity + per-offset field census | `formats/ui_manifests.md §2.7` |
| `scan-minimap` | minimap chain: `mapsetting.scr` 84B zones + `regiontableNNN.bin` 32B + ui map DDS | `formats/misc_data.md` |
| `scan-quest` | quest/dialog tables by stride: `quests.scr` 3720B (sparse), `npc.scr` 404B, `autoquestion_cl.scr` 92B, `discript.sc` 68B | `formats/misc_data.md` |

Examples:
```powershell
dotnet run -c Release --project "${CLAUDE_SKILL_DIR}/scripts/vfsls" -- decode data/char/skin/g200002620.skn
dotnet run -c Release --project "${CLAUDE_SKILL_DIR}/scripts/vfsls" -- coverage
dotnet run -c Release --project "${CLAUDE_SKILL_DIR}/scripts/vfsls" -- convert data/char/skin/g200002620.skn D:/dump
dotnet run -c Release --project "${CLAUDE_SKILL_DIR}/scripts/vfsls" -- --census
dotnet run -c Release --project "${CLAUDE_SKILL_DIR}/scripts/vfsls" -- --contains data/map000/bgtexture.txt
dotnet run -c Release --project "${CLAUDE_SKILL_DIR}/scripts/vfsls" -- scan-mot --id 170354502
dotnet run -c Release --project "${CLAUDE_SKILL_DIR}/scripts/vfsls" -- scan-fx
```

Report findings as **metadata** (counts, names, sizes, and for `--head` the decoded preview); quote
the exact virtual path strings so the next caller can re-query. Don't paste large dumps.

## Sibling research harnesses (`scripts/`)

Alongside `scripts/vfsls/`, this skill bundles ~35 **standalone single-purpose harnesses** — the
original research probes that first recovered each format (e.g. `scripts/do-census`,
`scripts/minimap-scan`, `scripts/quest-dialog-scan`, `scripts/msgxdb`, `scripts/skill-icon-scan`,
`scripts/skillcat-scan`, `scripts/terrain-format-witness`, `scripts/dds-to-png`, `scripts/xeff-val`,
…). Several `vfsls` subcommands are production mirrors of these — for routine work prefer the
subcommand; reach for a standalone as the focused original or a cross-check when a census looks off.
Each is its own `net10.0` exe (`dotnet run -c Release --project "${CLAUDE_SKILL_DIR}/scripts/<name>" -- …`),
carries the same hard rules, and is **never** registered in `MartialHeroes.slnx`.

**Decide:** existence/size → `--contains` / `<substring>`; "what IS this file" → `decode`; inventory →
`--census`; a whole family → the matching `scan-*`/`dump-*`; structural peek → `hexdump --header`;
capability map → `coverage`. Tracing an id through a chain → `/asset-chain-trace` (use this harness to
*confirm each hop* via `--contains` and `decode` the endpoint). A census looks off → cross-check the
sibling harness. Need a real file on disk for `asset-format-doc` → `extract` to an EXTERNAL path.

# Mode C — DATA-FORMAT (recover a CP949 text table by observation)

Many client data tables are **plain CP949 text** — tab/comma-separated rows under `data/char/`
(`skin.txt`, `actormotion.txt`), `data/map000/` (`bgtexture.txt`), and item CSVs. Their structure is
recoverable **without IDA at all**, by reading the real bytes through the Mode B harness and observing
the columns. This *data-file observation* is an explicitly sanctioned RE path (no decompiler in the
loop, so no Hex-Rays taint) — but findings still land in `_dirty/` and are promoted by `re-promote`, so
the audit trail and the "rewrite, never copy" discipline stay intact.

```
real client text bytes (VFS) ──► [vfsls reads & previews] ──► observe columns/delimiter/encoding
   ──► Docs/RE/_dirty/formats/<name>.raw.md ──► [re-promote rewrites] ──► Docs/RE/formats/<name>.md
```

**Use this** for **text tables** (`.txt`, `.csv`, text-mode `.scr`); use `asset-format-doc` for
**binary** assets (byte-offset headers, annotated hexdumps).

## Steps

1. **Confirm + size the entry** via the harness:
   ```powershell
   dotnet run -c Release --project "${CLAUDE_SKILL_DIR}/scripts/vfsls" -- --contains data/char/skin.txt
   dotnet run -c Release --project "${CLAUDE_SKILL_DIR}/scripts/vfsls" -- data/char/skin.txt
   ```
2. **Read the head as CP949** (the harness decodes via code page 949 so Korean columns are legible):
   ```powershell
   dotnet run -c Release --project "${CLAUDE_SKILL_DIR}/scripts/vfsls" -- --head data/char/skin.txt --head-bytes 1024
   ```
   ALL game text is CP949 (EUC-KR), never UTF-8.
3. **Characterise the table by observation** (the recovery work): the **delimiter** (tab/comma/ws) and
   whether row 0 is a header or data; **comment/blank conventions** (`//`, `#`, `;`, a sentinel);
   **column count + per-column type** (integer id, float, CP949 label, a relative path like `tex_id` →
   `data/char/tex512512/{id}.png`, a flag); the **key column(s)** other tables join on (`skin IdA` →
   `skin.txt` col 4; `actormotion.txt` col 1 = mob_id, col 2 = skin_class) — capture cross-file
   relationships, these tables form a join graph (skin ↔ actormotion ↔ bnd ↔ mot); **encoding/terminator**
   (`\r\n` vs `\n`, trailing delimiter, CSV quoting).
4. **Write neutral findings** to `Docs/RE/_dirty/formats/<name>.raw.md` (matching the existing
   `*.raw.md` convention), leading with:
   `> DIRTY — recovered by harness observation of real client text; never commit; rewrite to promote.`
   Then record: the virtual path, byte size, delimiter, header presence, a **column table**
   (`col# | inferred meaning | type | example value | confidence`), comment/blank conventions, the
   cross-file join keys, and CP949 confirmation. Quote only a *few* representative field values to
   anchor meaning — never dump the whole file (it is copyrighted data).
5. **Hand off to promotion.** Run `re-promote` to rewrite the `.raw.md` into `Docs/RE/formats/<name>.md`
   (neutral, citable, `status`/confidence tags), and journal it via the `preservation` session-log mode.
   Engineers then implement the parser citing `// spec: Docs/RE/formats/<name>.md`.
6. **Report** the entry path + size, the delimiter/header/encoding verdict, the column count, the key
   join columns, and the `.raw.md` path — stating promotion is the next, separate step.

**Decide:** non-printable bytes / a magic signature instead of CP949 rows → STOP, it's binary → use
`asset-format-doc`. Row 0 values don't type-match the rows below → it's a header. A Korean column reads
as garbage → the read wasn't CP949, fix the decode; never record mojibake as the value. A column's
meaning needs the loader code → mark it `UNVERIFIED` and stop (don't cross into IDA — that's the
`re-*-analyst` lane).

## Verify / Done when

- **Mode A:** magic validated against `pak.md`; one `index offset size name` line per entry; offsets
  monotonic + within file size; entry count + total covered bytes reported; **zero payload bytes** read;
  any saved listing under `_dirty/scratch/` or `.work/`.
- **Mode B:** the query ran under `-c Release`; output is metadata / short head previews / structured
  summaries only (no full asset dump); any `extract`/`convert` wrote to an EXTERNAL path; exact virtual
  path strings quoted.
- **Mode C:** `_dirty/formats/<name>.raw.md` exists with the DIRTY banner, the virtual path + byte size,
  delimiter/header/encoding verdict, a column table, the cross-file join keys, and CP949 confirmation;
  only short illustrative values quoted; promotion via `re-promote` named as the next step.

## Pitfalls (anti-patterns)

- **Never** seek to a `.pak` entry `offset` and read `size` payload bytes, or hexdump/decompress/decode
  entry content — Mode A is a listing tool with no extract mode.
- **Never** register the `vfsls`/sibling harness in `MartialHeroes.slnx` or `git add` its `bin/`/`obj/`.
- **Never** print a full asset or dump an entire file's bytes — inspect, don't export (all modes).
- **Never** patch the production `Assets.Vfs`/`Assets.Parsers`/`Assets.Mapping` to suit the harness —
  if the API drifted, fix the harness call site (the compile failure is a feature).
- **Never** write Mode C findings into the committed `Docs/RE/formats/` tree — they go to
  `_dirty/formats/` only; promotion (rewrite, never copy) is `re-promote`'s job.
- **Never** assume UTF-8 — all game text is CP949 (EUC-KR); a mojibake preview means binary or a wrong
  decode, not a wrong encoding guess.
- **Never** open IDA or read `_dirty/` decompiler output here — these are observation-of-own-bytes lanes.

> North star N2: knowing the archive index, confirming the *production* parsers read the real VFS bytes
> exactly, and recovering the CP949 join tables are what let the re-implemented client reproduce the
> original asset set and resolve its asset chains faithfully.

## Hard rules

- Read-only over originals; **metadata / counts / short head previews only** — never extract a full
  asset, dump an entire file, or emit copyrighted payload (Mode A index region only; Modes B/C previews
  only). Mode B `extract`/`convert` write to EXTERNAL paths under a repo-tree guard, with a never-commit warning.
- Spec-driven: Mode A cites `pak.md` via flags; Mode B `decode`/`scan-*` cite their `formats/*.md`; Mode
  C writes only under `Docs/RE/_dirty/formats/` and promotes via `re-promote` (rewrite, never copy).
- Always decode text via the CP949 provider (`Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)`
  then `Encoding.GetEncoding(949)`).
- The `vfsls`/sibling harnesses are never `MartialHeroes.slnx` members; if one won't compile because the
  VFS API changed, fix the harness call site, not the production library. No IDA, no `_dirty/` decompiler output.
