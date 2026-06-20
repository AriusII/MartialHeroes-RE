---
name: asset-chain-trace
description: Use when you need to walk a Martial Heroes asset id through its RECOVERED mapping chain to the on-disk VFS file — "where does terrain texture index N come from?", "which .dds / .png / .bnd / .mot does this skin / mob / cell resolve to?", "why does this asset render wrong / not load?", "is every hop in the chain present in the VFS?". Resolves and EXISTENCE-checks each hop (terrain .ted→.map→bgtexture.txt→.dds; character skin .skn→skin.txt→tex; bind/idle .bnd/.mot; mob→skin; spawns .arr; collision .sod) against the real client VFS via Assets.Vfs, citing the chain spec at each hop. Index/existence/size only — never payload bytes. A reproduction & debugging aid for the faithful 1:1 port (N2).
allowed-tools: Read, Write, Bash(dotnet *)
model: sonnet
effort: medium
---

# asset-chain-trace — follow an asset id to its on-disk VFS file

Takes one asset id (a terrain texture index, a `.skn` id, a `mob_id`, a cell tag, …), walks it
through the project's **recovered mapping chain** hop by hop to the concrete on-disk VFS path, and
**confirms each hop exists** in the real client archive — reporting the fully-resolved chain and
flagging any broken link (a missing file, an out-of-range index) with the exact spec/mapping to
recheck.

This is a **reproduction & debugging aid for North Star N2** (a faithful 1:1 re-creation of the
original client). When the Godot client renders the wrong texture, an exploded mesh, a missing
animation, or a misplaced spawn, the cause is almost always a **broken hop** in one of these chains.
This skill makes the chain legible end-to-end so the gap is named precisely instead of guessed at,
and so an assets engineer can fix the exact resolver step that drifted.

It is **neutral tooling**: it reads only the **index** of the user's own legally-owned client VFS
(entry name, offset, size, and membership) — it **never reads, decodes, or prints payload bytes**.
The chains themselves are the recovered facts in `CLAUDE.md` "Recovered asset mappings" and the
committed `Docs/RE/formats/` specs; this skill cites them, it does not duplicate copyrighted data.

## Preconditions

1. **The real client VFS must be reachable.** Either the project-local
   `05.Presentation/MartialHeroes.Client.Godot/clientdata/` (`data.inf` + `data/data.vfs`, resolved
   the way `Dev/ClientPathResolver.cs` resolves it: `MH_CLIENT_DIR` env → `client_dir.cfg` →
   `clientdata/` → external installs) or the legacy fallback `D:/MartialHeroesClient`. A dir is valid
   only when it contains BOTH `data.inf` AND `data/data.vfs`. If neither is present, STOP and tell the
   user the trace needs the bring-your-own client mounted — do not fabricate a resolution.
2. **A .NET 10 SDK** (`dotnet --version` ≥ 10). The tool targets `net10.0`.
3. **The chain specs are available**: `CLAUDE.md` "Recovered asset mappings" (the canonical chain
   list) and the per-format docs under `Docs/RE/formats/` (`mesh.md`, `terrain.md`, `animation.md`,
   `misc_data.md`, …). Read the hop's spec before you cite it — you cite the spec, not your memory.

## The chains (pick the one that matches the asset kind)

Each hop below ends in an on-disk VFS path. Cite the listed spec for that hop.

- **TERRAIN texture** — cell `.ted` `TextureIndexGrid` byte → cell `.map`
  `TERRAIN/BUILDING TEXTURES[idx-1].intTexId` → `bgtexture.txt[id]` →
  `data/map000/texture/<rel>.dds`. Textures are **global under `map000`** for all areas (not per-area).
  *spec:* `Docs/RE/formats/terrain.md`, and the `bgtexture.txt` table format in `Docs/RE/formats/`.
  Decision point: the `.ted` byte indexes the `.map` table **at `idx-1`** (1-based); a `0` byte means
  "no texture", not index 0.
- **CHARACTER skin** — `.skn` `IdA` → `data/char/skin.txt` col4 → col5 `tex_id` →
  `data/char/tex{512512|10241024|…}/{id}.png`. The tex resolution bucket (`tex512512`, `tex10241024`,
  …) is chosen by the texture's size class as recorded in the table.
  *spec:* `Docs/RE/formats/mesh.md` (`.skn`), and the `skin.txt` table format in `Docs/RE/formats/`.
- **BIND / IDLE** — `.skn` `IdB` → `data/char/bind/g{IdB}.bnd` (the skeleton); idle motion via
  `data/char/actormotion.txt` (the row whose **col2 == IdB** → col16) → `data/char/mot/g{id}.mot`.
  *spec:* `Docs/RE/formats/mesh.md` (`.bnd`), `Docs/RE/formats/animation.md` (`.mot`), and the
  `actormotion.txt` table format in `Docs/RE/formats/`.
- **MOB → skin** — `mob_id` → `actormotion.txt` col1 → col2 `skin_class` → `g{skin_class}.bnd` AND the
  `.skn` whose **`IdB == skin_class`**. From there, re-enter the CHARACTER skin / BIND-IDLE chains.
  *spec:* `Docs/RE/formats/animation.md` / the `actormotion.txt` table format, `Docs/RE/formats/mesh.md`.
- **SPAWNS** — per-area spawn arrays: `npc{tag}.arr` = **28-byte** records, `mob{tag}.arr` =
  **20-byte** records (`{tag}` = the area/cell tag). The trace confirms the `.arr` exists and reports
  its record count (size ÷ stride); each record's `mob_id`/`npc` id then re-enters the MOB chain.
  *spec:* `Docs/RE/formats/misc_data.md` (or the spawn-array section under `Docs/RE/formats/`).
- **COLLISION** — `.sod` = 2D XZ wall segments (ray-parity point-in-polygon). The trace confirms the
  `.sod` for a cell exists and reports its size; ground height is `.ted` bilinear, not `.sod`.
  *spec:* the `.sod` collision section under `Docs/RE/formats/`.

## Steps

1. **Confirm intent is a trace, not an extraction.** This skill resolves and existence-checks paths.
   If the request is "extract / unpack / dump the bytes", REFUSE and point at the project's
   non-distribution rule (and `vfs-inspect`'s guarded `extract` for an *external* dump if the user
   truly needs raw bytes outside the tree). This skill never emits payload.
2. **Pick the chain** for the asset kind from the list above. If the asset kind is ambiguous, ask
   which chain (a `.skn` id and a `mob_id` enter different chains).
3. **Read the hop's spec** in `Docs/RE/formats/` (and the chain summary in `CLAUDE.md`) so each hop
   you resolve is grounded and citable. Note the table column / record stride / 1-vs-0-based index
   convention for that hop.
4. **Read the tool source** so you can re-parameterize it without touching the production library:
   - `${CLAUDE_PROJECT_DIR}/Tools/MartialHeroes.Tools.AssetChainTrace/Program.cs` — the `# === CONFIG ===`-headed tracer.
   - `${CLAUDE_PROJECT_DIR}/Tools/MartialHeroes.Tools.AssetChainTrace/MartialHeroes.Tools.AssetChainTrace.csproj` — a
     `net10.0` exe (a solution member) that `ProjectReference`s `Assets.Vfs`, so it always tracks the
     live `MappedVfsArchive` surface.
5. **Resolve each hop** using the documented mapping, then **confirm the resolved file EXISTS in the
   VFS** through `Assets.Vfs` — reporting **only** `Contains(path)`, `DataSize` (bytes), and
   `DataOffset`. Run the harness from its own dir:

   ```powershell
   dotnet run -c Release --project "${CLAUDE_PROJECT_DIR}/Tools/MartialHeroes.Tools.AssetChainTrace" -- exists data/map000/bgtexture.txt
   dotnet run -c Release --project "${CLAUDE_PROJECT_DIR}/Tools/MartialHeroes.Tools.AssetChainTrace" -- exists-many data/char/bind/g3045.bnd data/char/mot/g170354502.mot
   ```

   - `exists <vfs-path>` — print `true/false` + size + offset for one path.
   - `exists-many <p1> <p2> …` — the same, one line per path (a whole resolved chain at once).
   - `stride <vfs-path> <bytes>` — confirm existence and print `DataSize / bytes` as the record count
     (use for `.arr` spawns: `stride … 28` for npc, `… 20` for mob).
   - Pass `--inf`/`--vfs` to override the auto-resolved client locations. With no override the harness
     follows the `ClientPathResolver` order (`clientdata/` then `D:/MartialHeroesClient`).
6. **Report the fully-resolved chain** as `id → hop → … → on-disk path`, with the existence/size of
   each hop and the cited spec per hop. **Flag any BROKEN hop**: a missing file, an out-of-range table
   index, a `0`/sentinel byte, a record-count that doesn't divide evenly — and say **which spec /
   mapping to recheck** (e.g. "`.map` table has 7 entries but `.ted` byte is `0x09` → recheck the
   `idx-1` convention in `Docs/RE/formats/terrain.md`"). A clean trace ends at an existing path; a
   broken one names the exact hop that drifted.

## Hard rules

- **Index / metadata ONLY — never extract or print payload bytes** (the `pak-explore` rule). The only
  data that may leave this skill per hop is: the resolved virtual path string, `Contains` (true/false),
  `DataSize`, `DataOffset`, and a derived record count. The harness has **no** `GetFileContent` /
  decode / dump path and must never grow one. If asked to dump bytes, REFUSE.
- **Cite each hop to its spec (Ground-Truth Doctrine).** The mapping chains are the **IDA-derived
  truth** recorded in the committed specs — what `doida.exe` proved, rewritten clean. Every resolved
  hop names the `Docs/RE/formats/<file>` (or `CLAUDE.md` chain) it came from. A hop you can't cite is a
  hop you haven't verified — read the spec first; never invent a mapping from memory or analogy.
- **A broken hop is a spec/asset bug to SURFACE, not to guess around.** When a hop fails (missing file,
  out-of-range index, bad stride), report the exact failing hop and the spec/mapping to recheck — do
  **not** silently substitute a "close enough" path or patch the index to make the trace complete. If
  the chain itself looks wrong (the spec disagrees with what the VFS actually contains), that is an RE
  finding: the binary settles it and the spec is corrected. The trace's job is to name the gap
  precisely, not to paper over it.
- **All legacy text is CP949.** Any table the harness reads (`skin.txt`, `actormotion.txt`,
  `bgtexture.txt`, …) is Korean code page 949; the harness registers the provider once
  (`Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)` → `Encoding.GetEncoding(949)`).
  Never assume UTF-8 or the text mojibakes and the lookup misses.
- **The trace note is neutral** (paths, sizes, offsets, spec citations — no payload). If you save a
  trace, stage it under `Docs/RE/_dirty/scratch/` or a `.work/` scratch — **never commit asset bytes**
  and never write a trace into a committed spec. This skill reads the user's own VFS, so it is not a
  clean-room concern, but the no-payload + no-commit-originals invariant always holds.
- **Read-only over the VFS.** Open the archive read-only; never modify `data.inf` / `data/data.vfs`.
- **The tool is a maintained solution member** at `Tools/MartialHeroes.Tools.AssetChainTrace`; it builds
  with `dotnet build MartialHeroes.slnx` and its `bin/`/`obj/` stay gitignored. If the live
  `MappedVfsArchive` API drifts and it won't compile, fix the call site — never patch the production
  library to suit it.

## Pairs with

- **`vfs-inspect`** — the broader VFS browser (decode / census / convert). Use it to discover candidate
  paths and table contents; use *this* skill to walk one id through its chain and existence-check it.
- **`godot-fidelity-check`** (N2) — when a chain resolves cleanly but the client still renders wrong,
  the gap is visual/coordinate/material, not a missing file — hand off there.
- **The assets engineers** (`assets-vfs-`, `assets-parser-`, `assets-mapping-`, `data-tables-engineer`)
  — a broken hop this skill flags is the precise resolver step they fix, with the cited spec in hand.
