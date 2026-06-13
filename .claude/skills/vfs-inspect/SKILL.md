---
name: vfs-inspect
description: Use to open the REAL Martial Heroes VFS (data.inf + data/data.vfs at D:/MartialHeroesClient) and list/inspect entries by substring — the throwaway console harness for "does this path exist?", "how big is it?", "what's the first N bytes?", "how many .skn/.ted/.txt files are there?". Bundles a ready-made net10.0 harness with 12 dedicated subcommands for census/analysis of the major asset formats. Subcommands: scan-mot, scan-bnd, scan-skn, scan-ui, dump-msgxdb, dump-uitex, scan-xeff, scan-sound, scan-fx, dump-do, scan-minimap, scan-quest.
allowed-tools: Read Write Bash(dotnet *) Bash(mkdir *) Bash(copy *) Bash(xcopy *)
model: sonnet
---

# vfs-inspect — open the real VFS and inspect entries

The single home for the throwaway VFS browser that has been hand-rebuilt five times. It mounts the
real client archive through the production VFS API and lets you list entries, count by extension,
test for a path, and peek at the head bytes of any file — without writing a test or touching Godot.
It also provides 12 structured subcommands that census the major asset families and drive the
production parsers (`Assets.Parsers`) directly, so observations match what the shipping client
will see. Several of these subcommands mirror standalone research harnesses that live as siblings
under `scripts/` (see "Sibling research harnesses" below).

It drives the production library directly:
`MartialHeroes.Assets.Vfs.MappedVfsArchive.Open(infPath, vfsPath)` with
`infPath = "D:/MartialHeroesClient/data.inf"` and `vfsPath = "D:/MartialHeroesClient/data/data.vfs"`.
`GetEntries()` returns a `ReadOnlySpan<VfsEntry>` (each `.Name` is the lower-cased virtual path,
`.DataOffset`/`.DataSize` describe the blob slice); `GetFileContent(path)` returns a zero-copy
`ReadOnlyMemory<byte>`; `Contains(path)` tests membership. The VFS holds ~43,347 entries and all
text payloads are CP949 (Korean code page 949), so the harness registers the code-pages provider.

## What this is — and is NOT

- It is a **THROWAWAY diagnostic harness**. It is bundled under this skill's `scripts/vfsls/`, NOT
  under the five numbered layer folders, and it is **never added to `MartialHeroes.slnx`** and
  **never committed** as a solution member. It exists to answer one-off questions fast.
- It is **read-only** over the VFS. It never extracts or rewrites archive payloads, and it prints
  only metadata plus, on request, a short hex/decoded **head** preview — never a full file dump and
  never a copyrighted asset in full.
- It is **not** a clean-room concern: it reads the user's own legally-owned client files at
  `D:/MartialHeroesClient/`, the same bytes the production VFS reads. No IDA, no `_dirty/`.

## Preconditions

1. The real client must be present at `D:/MartialHeroesClient/` with both `data.inf` and
   `data/data.vfs`. If the drive/path differs, pass `--inf` / `--vfs` overrides (see below). If the
   files are absent, STOP and tell the user the harness needs the bring-your-own client mounted.
2. A .NET 10 SDK must be installed (`dotnet --version` ≥ 10). The harness targets `net10.0`.
3. The production VFS project must build: the harness `ProjectReference`s
   `03.Storage.Assets/MartialHeroes.Assets.Vfs/MartialHeroes.Assets.Vfs.csproj` and
   `03.Storage.Assets/MartialHeroes.Assets.Parsers/MartialHeroes.Assets.Parsers.csproj` by absolute
   path, so it always tracks the live API. If the API drifted, the harness fails to compile —
   that is a feature, fix the call site.

## Steps

1. **Read the bundled harness** so you know what it does and can tweak it if needed:
   - `${CLAUDE_SKILL_DIR}/scripts/vfsls/Program.cs` — the parametrized inspector.
   - `${CLAUDE_SKILL_DIR}/scripts/vfsls/vfsls.csproj` — `net10.0` exe with the two absolute
     `ProjectReference`s.

2. **Run it in place** with `dotnet run -c Release`. The skill dir IS the project dir — run from
   `scripts/vfsls/`:

   ```powershell
   dotnet run -c Release --project "${CLAUDE_SKILL_DIR}/scripts/vfsls" -- <args>
   ```

   First build pulls in the two referenced projects; subsequent runs are fast. Use `-c Release`
   so the memory-mapped read path is not debug-throttled.

3. **Pick the query via args** (all optional; with no args it prints a summary + extension census):

   **Generic filtering options:**

   | Arg | Effect |
   |---|---|
   | `<substring> [<substring> …]` | List entries whose lower-cased name contains EVERY given substring (AND). |
   | `--ext .skn` | Filter to a single extension (repeatable: `--ext .ted --ext .map`). |
   | `--count` | Print only the match count, not the per-entry lines. |
   | `--census` | Print the entry count grouped by extension (the inventory view). |
   | `--head <path>` | Print the first 256 bytes of one entry as hex + a CP949-decoded preview. |
   | `--head-bytes <n>` | Change the head preview length (default 256). |
   | `--contains <path>` | Print just `true`/`false` for one exact virtual path. |
   | `--limit <n>` | Cap the number of listed entries (default 200; `0` = unlimited). |
   | `--inf <path>` / `--vfs <path>` | Override the default `D:/MartialHeroesClient` locations. |

   **Subcommands** (each accepts `--inf`/`--vfs` overrides):

   | Subcommand | What it does | Spec |
   |---|---|---|
   | `scan-mot [--id <id_a>]` | Census .mot animation files: real vs stub, BANI-variant detection, frame/track count histograms. | `Docs/RE/formats/animation.md` |
   | `scan-bnd` | Census .bnd skeletons: bone count distribution, base_id check, single vs multi-bone breakdown. | `Docs/RE/formats/mesh.md` |
   | `scan-skn` | Census .skn skinned meshes: id_b (skeleton link), vertex/face totals, multi-weight ratio, max weights/vert. | `Docs/RE/formats/mesh.md` |
   | `scan-ui` | Census data/ui/ DDS files: dimensions + fourCC decoded from DDS header, grouped by subdirectory, flags NPOT textures. | `Docs/RE/formats/texture.md`, `ui_manifests.md` |
   | `dump-msgxdb [--id N] [--range A B]` | Dump msg.xdb records (u32 id + CP949 string, 516B stride) — all, one, or a range. | `Docs/RE/formats/misc_data.md §6` |
   | `dump-uitex` | Parse UiTex.txt manifest (tex_id → vfs_path) and verify each path against the VFS. | `Docs/RE/formats/ui_manifests.md §1` |
   | `scan-xeff` | Census .xeff particle effects: element_count distribution, 9-digit skill-code pattern, duplicate effect_ids. | `Docs/RE/formats/effects.md §A` |
   | `scan-sound` | Census .ogg under data/sound/2d\|3d + parse per-area sound tables (.bgm/.bge/.eff/.wlk/.run), listing non-null entries with VFS existence check. | `Docs/RE/formats/sound_tables.md` |
   | `scan-fx` | Census .fx1–.fx7 terrain layer files: count + total bytes per layer, plus header field histograms (field[0] type_tag @0x00, field[3] @0x0C, field[4] @0x10, field[5] @0x14) to arbitrate the disputed FX2 field[3] value. | `Docs/RE/formats/terrain_layers.md §1` |
   | `dump-do` | Census the 12 per-class stance `.do` files (icon/skill sprite-sheet records, stride 116/0x74): per-file record count + iconSrcX/Y validity (in [0..489]) + classStanceRef set + a compact per-u32-offset field census (distinct/min/max/zero%). Counts and short decoded fields only — no raw bytes. | `Docs/RE/formats/ui_manifests.md §2.7` |
   | `scan-minimap` | Census the minimap/worldmap chain: `mapsetting.scr` 84B zone table (id + CP949 name + bounds), `regiontableNNN.bin` 32B sub-region records, and the data/ui map DDS inventory (dimensions + fourCC). Strides + counts only. | `Docs/RE/formats/misc_data.md` |
   | `scan-quest` | Census the quest/dialog script tables by fixed stride: `quests.scr` 3720B (sparse — u16 id@0 marks an occupied slot), `npc.scr` 404B, `autoquestion_cl.scr` 92B, `discript.sc` 68B. Record/slot/occupied counts + a few decoded ids. | `Docs/RE/formats/misc_data.md` |

   Examples:

   ```powershell
   # Census of every extension (the 49-extension inventory):
   dotnet run -c Release --project "${CLAUDE_SKILL_DIR}/scripts/vfsls" -- --census

   # Every skin-table-ish text file under data/char:
   dotnet run -c Release --project "${CLAUDE_SKILL_DIR}/scripts/vfsls" -- data/char .txt

   # Does the global texture catalog exist, and what's in its head?
   dotnet run -c Release --project "${CLAUDE_SKILL_DIR}/scripts/vfsls" -- --contains data/map000/bgtexture.txt
   dotnet run -c Release --project "${CLAUDE_SKILL_DIR}/scripts/vfsls" -- --head data/map000/bgtexture.txt

   # Full .mot census (real vs stubs, BANI, frame/track distribution):
   dotnet run -c Release --project "${CLAUDE_SKILL_DIR}/scripts/vfsls" -- scan-mot

   # Decode one specific .mot by id_a:
   dotnet run -c Release --project "${CLAUDE_SKILL_DIR}/scripts/vfsls" -- scan-mot --id 170354502

   # Dump first 20 msg.xdb entries starting from id 200:
   dotnet run -c Release --project "${CLAUDE_SKILL_DIR}/scripts/vfsls" -- dump-msgxdb --range 200 220

   # Check one specific message:
   dotnet run -c Release --project "${CLAUDE_SKILL_DIR}/scripts/vfsls" -- dump-msgxdb --id 4025

   # Census all .xeff effect files:
   dotnet run -c Release --project "${CLAUDE_SKILL_DIR}/scripts/vfsls" -- scan-xeff

   # Sound table census + per-area non-null entry listing:
   dotnet run -c Release --project "${CLAUDE_SKILL_DIR}/scripts/vfsls" -- scan-sound

   # Terrain layer (.fx1–.fx7) census incl. the contested field[3] @0x0C histogram:
   dotnet run -c Release --project "${CLAUDE_SKILL_DIR}/scripts/vfsls" -- scan-fx

   # Per-class stance .do census (record counts + iconSrcX/Y validity + per-offset field census):
   dotnet run -c Release --project "${CLAUDE_SKILL_DIR}/scripts/vfsls" -- dump-do

   # Minimap/worldmap chain: mapsetting.scr zones + regiontable*.bin + ui map DDS:
   dotnet run -c Release --project "${CLAUDE_SKILL_DIR}/scripts/vfsls" -- scan-minimap

   # Quest/dialog script tables (quests.scr / npc.scr / autoquestion_cl.scr / discript.sc):
   dotnet run -c Release --project "${CLAUDE_SKILL_DIR}/scripts/vfsls" -- scan-quest
   ```

4. **Report findings as metadata**: counts, names, sizes (and, for `--head`, the decoded preview).
   Quote the exact virtual path strings so the next caller can re-query. Do not paste large dumps.

## Sibling research harnesses (`scripts/`)

Alongside `scripts/vfsls/`, this skill bundles a handful of **standalone single-purpose harnesses**
— the original research probes that first recovered each format. Several `vfsls` subcommands are now
production mirrors of these, so for routine work prefer the subcommand. The standalones remain as the
focused originals (and as a cross-check when a subcommand's census looks off). Each is its own
`net10.0` exe; run it the same way (`dotnet run -c Release --project "${CLAUDE_SKILL_DIR}/scripts/<name>" -- …`).

| Sibling harness | Mirrored by | Focus |
|---|---|---|
| `scripts/do-census` | `dump-do` | The 12 per-class stance `.do` files (stride 116/0x74): record counts + per-offset field census. |
| `scripts/minimap-scan` | `scan-minimap` | `mapsetting.scr` zone table + `regiontableNNN.bin` sub-regions + ui map DDS inventory. |
| `scripts/quest-dialog-scan` | `scan-quest` | `quests.scr` / `npc.scr` / `autoquestion_cl.scr` / `discript.sc` fixed-stride census. |
| `scripts/msgxdb` | `dump-msgxdb` | `msg.xdb` 516B records (u32 id + CP949 string). |
| `scripts/skill-icon-scan` | `dump-do` (icon side) | Skill/icon sprite-sheet coordinate validation against the 512×512 sheet. |
| `scripts/skillcat-scan` | — | Skill-category table census (no vfsls subcommand mirror yet). |

These siblings carry the same hard rules as `vfsls`: read-only over the VFS, metadata/counts and
short decoded structural fields only, never a full asset dump, and they are never registered in
`MartialHeroes.slnx`.

## Hard rules

- **Never register this harness in `MartialHeroes.slnx`** and never `git add` its build output. It
  is intentionally a loose, gitignored-by-convention diagnostic under `.claude/skills/`. (Its
  `bin/`/`obj/` are the usual transient build dirs.)
- Print **metadata and short head previews only** — never extract a full asset or dump an entire
  file's bytes. This harness inspects; it does not export.
- Always decode text via the CP949 provider the harness wires up
  (`Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)` then `Encoding.GetEncoding(949)`).
  Do not assume UTF-8 — Korean game text will mojibake.
- If the harness will not compile because the VFS API changed, fix the harness call site to match
  the live `MappedVfsArchive` surface; do not patch the production library to suit the harness.
