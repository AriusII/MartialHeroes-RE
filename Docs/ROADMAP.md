# Martial Heroes — Reconstruction Campaign Roadmap

> **Status:** ACTIVE — campaign launched 2026-06-12, resumed from render anchor commit `c266e7e`
> (tooling baseline `36f213b`). This is the authoritative multi-phase plan for taking the Godot
> client from "renders a populated town" to "looks and behaves like the original client".
>
> Orchestration model: each phase is a **wave of parallel specialist agents** (dirty-room RE
> analysts, spec authors, clean-room engineers, reviewers) coordinated by the orchestrator.
> The clean-room firewall (`Docs/RE/README.md`) governs every hand-off: IDA findings land in
> `_dirty/` (gitignored), get rewritten into neutral committed specs, and only then are
> implemented fresh in C#.

---

## Campaign objectives (user mandate)

1. **Pay down the four render debts** left at `c266e7e`:
   - D1 — character **skinning explodes the mesh** (legacy bind/weight convention unrecovered → avatar static);
   - D2 — **NPCs spawn at a fallback Y** before async terrain finishes (ground race);
   - D3 — **EnvironmentNode too dark** (atmosphere/lighting not faithful);
   - D4 — **water unwired** (`WaterRenderer.cs` exists, never instantiated).
2. **Reconstruct the original client's GUI/UI** — login screen, character select, in-game
   windows — and understand the original's **scenes & cameras** (view modes, defaults, clamps).
3. **Make the legacy client data native to the Godot project** — `data.inf` + `data/data.vfs`
   live inside the Godot project folder (gitignored, never committed), zero hunting, zero env vars.
4. **Continue IDA-side research & tooling** (IDAPython via the IDA MCP, results to `_dirty/`).
5. **Continue format-understanding tooling** (the `vfs-inspect` harness family) so every byte
   in the VFS has a documented purpose.

**Not in scope (explicitly deferred by the maintainer):** the game server. The client must be
built so a future `MartialHeroes.Server.Console` can reuse layers 01–04, but no server work now.

---

## Evidence baseline (what we know going in)

- **48 committed specs** under `Docs/RE/` (14 formats, 13 subsystem specs, 19 packet YAMLs,
  6 structs, opcodes.md with 182 rows). `names.yaml`: 150+ canonical names, 49 VFS extensions.
- **Real VFS**: `data.inf` (6.2 MB index) + `data/data.vfs` (3.8 GB, 43,347 entries) — validated
  byte-for-byte against `Docs/RE/formats/pak.md`.
- **The skinning blocker is an evidence problem**: `Docs/RE/formats/animation.md` and `mesh.md`
  fully describe the *containers* (.skn 24B vertices + 12B weights, .bnd 36B bone records,
  .mot 28B keyframes @10fps) but every sample inspected so far was a stub (`frame_count=0`)
  or single-bone item skin. The **bind/weight convention for multi-bone character bodies is
  unrecovered**, and the renderer math (inverse-bind, compose order, axis conventions) was
  never read out of `Main.exe`.
- **UI knowledge gap**: `specs/login_flow.md` + `specs/input_ui.md` cover the *protocol* and
  *input dispatch*, but nothing documents the widget system, interface textures, fonts, or
  window layouts.
- **Water/sky/lighting**: zero committed knowledge. Suspects: `.map` per-cell fields, `.fx*`
  layers, hardcoded constants, or Lua-driven settings.
- Known partial formats awaiting closure: `bgtexture.lst` 76B record fields, `.bud` vertex
  bytes 12–31, `.xeff` element semantics, `.fx2–.fx7` headers, `.bin` lightmaps.

---

## Phase 0 — Client data goes native (✅ DONE 2026-06-12)

**Goal:** `data.inf` + `data/data.vfs` live at
`05.Presentation/MartialHeroes.Client.Godot/clientdata/` and every tool finds them there first.

> **Outcome:** byte-verified copy in place (6,241,992 / 3,802,182,193 B; `D:\` kept as backup);
> `clientdata/.gitignore` + `README.md` committed-able; `ClientPathResolver` resolves relative
> `client_dir.cfg` values against `res://` and auto-probes `res://clientdata`; vfsls harness
> probes the same chain. Verified: harness mounts 43,347 entries from `clientdata/`; headless
> boot logs `[ClientPath] Resolved from client_dir.cfg: …/clientdata`.

| # | Task | Files touched | Acceptance |
|---|------|---------------|------------|
| 0.1 | Copy `data.inf` → `clientdata/data.inf` and `data/data.vfs` → `clientdata/data/data.vfs` (robocopy, background; D:\ original kept as backup until verified) | — | byte sizes match (6,241,992 / 3,802,182,193) |
| 0.2 | Defense-in-depth `clientdata/.gitignore` (`*` except `.gitignore`+`README.md`) + a committed `README.md` documenting the bring-your-own-assets contract | `clientdata/.gitignore`, `clientdata/README.md` | `git status` shows only the two meta files |
| 0.3 | `ClientPathResolver` resolves **project-local `res://clientdata` first** among auto-detect candidates; relative `client_dir=` values in `client_dir.cfg` resolve against `res://` | `Dev/ClientPathResolver.cs`, `client_dir.cfg` | headless run logs `[ClientPath] Resolved … clientdata` |
| 0.4 | `vfs-inspect` harness default chain: `MH_CLIENT_DIR` → repo `clientdata/` → `D:/MartialHeroesClient` | `.claude/skills/vfs-inspect/scripts/vfsls/Program.cs` | `vfsls count` works with no args from repo |
| 0.5 | Update `CLAUDE.md` (VFS location note) + memory (`vfs-ground-truth-and-roadmap`) | `CLAUDE.md` | docs match disk reality |

**Non-distribution invariant:** `*.vfs` / `*.inf` are already gitignored repo-wide; 0.2 adds a
folder-level catch-all so *nothing* under `clientdata/` can ever be staged.

---

## Phase 1 — Evidence wave (dirty room, ~9 parallel agents)

All output lands under `Docs/RE/_dirty/` only. IDA MCP must be UP (it is). VFS observation
agents read the user's own files through the harness — sanctioned, never committed.

### 1A — Skinning math recovery (IDA) — `re-animation-analyst` ⚑ CRITICAL PATH
Recover from `Main.exe`'s renderer: how `.skn` vertices bind to `.bnd` bones (bind pose,
inverse-bind-matrix construction, weight application order), how `.mot` keyframes are sampled
and composed up the hierarchy (translation+quaternion per track, 10 fps, mixer layers), and the
**axis/handedness/major-order conventions** used to deform the mesh. Deliverable:
`_dirty/formats/skinning-math.raw.md` — prose + math, zero pseudo-C.

### 1B — Animation sample hunt (VFS) — `vfs-data-analyst` ⚑ CRITICAL PATH
Scan **every** `.mot`, `.bnd`, `.skn` in the real VFS (headers only, via a throwaway harness
loop): census of `frame_count>0` motions, multi-bone skeletons, multi-weight character bodies.
Cross-reference `actormotion.txt` rows → verify column semantics empirically (col2 skin_class →
g{id}.bnd exists; col16 idle motion exists). Deliverable: `_dirty/formats/animation-samples.raw.md`
with the named best test specimens (player body, one mob, one idle .mot with real keyframes).

### 1C — UI subsystem recovery (IDA) — `re-static-analyst`
Map the original client's widget/window system: the UI object classes (hit-test tree from
`input_ui.md`'s UI→world chain), how the login scene / character select / in-game windows are
constructed, which VFS assets (interface textures, fonts, cursors) each screen loads, whether
layouts are hardcoded coordinates or data-driven. Deliverable: `_dirty/recon/ui-system.raw.md`.

### 1D — UI asset census (VFS) — `vfs-data-analyst`
Enumerate every interface-related VFS entry: `interface/`-like paths, fonts, cursors, login/
char-select backgrounds, window frames, icons, button states. Group by screen. Deliverable:
`_dirty/formats/ui-asset-census.raw.md` (paths + sizes + format guesses, no payload bytes).

### 1E — Water / sky / lighting recovery (IDA + VFS) — `re-asset-format-analyst`
Find how the original decides *where water is* (`.map` fields? a per-cell flag? `.fx*` layers?),
what textures/shader it uses, the sky rendering path (skybox? dome? textures), and the global
lighting constants (ambient/directional/fog values D3D9-side). Deliverable:
`_dirty/formats/water-sky-light.raw.md`.

### 1F — Format gap closers (IDA + VFS) — `re-asset-format-analyst`
Close the known partials, in priority order: **(a)** `bgtexture.lst` 76B GHTex record layout
(terrain texture fidelity), **(b)** `.bud` vertex bytes 12–31 (suspect: lightmap UVs / vertex
color — building fidelity), **(c)** `.bin` lightmaps. Deliverable: per-format
`_dirty/formats/*.raw.md` updates.

### 1G — Camera & scene deep-dive (IDA) — `re-static-analyst`
Extend `specs/camera_movement.md`'s evidence: per-mode parameters (orbital distance/pitch
clamps, follow smoothing, default in-town mode), scene transitions (login → select → world),
cut-scene camera data. Deliverable: `_dirty/recon/camera-scene.raw.md`.

**Wave-1 exit criteria:** 1A+1B both delivered (skinning unblocked on paper) and at least 3 of
1C–1G delivered. Conflicts between IDA reading and sample observation are flagged, never
silently reconciled.

---

## Phase 2 — Spec promotion (firewall crossing, 2–3 agents)

`asset-spec-author` / `protocol-spec-author` REWRITE (never copy) the `_dirty/` findings into:

| New/updated spec | Source | Unblocks |
|---|---|---|
| `Docs/RE/specs/skinning.md` (NEW) | 1A + 1B | D1 — the Godot skinning fix |
| `Docs/RE/formats/animation.md` (UPDATE: verified samples, actormotion semantics) | 1B | D1 |
| `Docs/RE/specs/ui_system.md` (NEW: widget tree, screens, asset manifest per screen) | 1C + 1D | GUI work |
| `Docs/RE/specs/environment.md` (NEW: water placement, sky, lighting constants) | 1E | D3 + D4 |
| `Docs/RE/formats/texture.md` + `terrain_scene.md` (UPDATE: GHTex fields, .bud bytes) | 1F | terrain/building fidelity |
| `Docs/RE/specs/camera_movement.md` (UPDATE: parameters, defaults) | 1G | camera fidelity |

Orchestrator then records the session in `Docs/RE/journal.md` and merges new canonical names
into `names.yaml`. **Every constant that will appear in C# must be citable to one of these.**

---

## Phase 3 — Engineering wave (clean room, ~7 parallel agents)

No agent here reads `_dirty/` or touches IDA. Each implements from the Phase-2 specs.

### 3A — Character skinning & idle animation — `godot-skinning-specialist` (D1) — ✅ DONE (2026-06-12)
Implemented as **faithful CPU linear-blend skinning** (per-frame ArrayMesh rebuild, matching the
original renderer per `specs/skinning.md`): engine-free `World/SkinningMath.cs` (unit-testable),
`SkinnedCharacterNode.cs` (thin Node glue), bones addressed by **bone ID** (the old array-index
assumption was the explosion's co-cause), load-time inverse-bind bake, single unified handedness
conversion (world Z-negate applied once post-deform; quat remap `(x,y,z,w)→(-x,-y,z,w)`).
**Evidence:** rest-pose cancellation residual 1.47e-6 (<1e-3 gate); live-animation delta 0.016;
finite human-sized AABB; two windowed screenshots verified — upright, textured, articulated,
different poses. Open: mobs/NPCs still render via the static path (skinned-mob rollout = sub-wave
II); stand-up pivot (+90° about Z) validated on the g1 player rig only; renormalized-alpha
interpolation chosen over raw-seconds (documented deviation); BANI .mot files skipped (dead data).

### 3B — NPC ground placement fix — `godot-presentation-engineer` (D2) — ✅ STRUCTURALLY DONE (2026-06-12, 3 agents)
What landed: **(1)** the race itself — `TerrainNode.SectorBecameResident` event + pending-snap
queue in `NpcRenderer` (snap immediately if the cell is already resident, else on sector arrival;
no per-frame polling); **(2)** ring-center selection fixed — `RealWorldRenderer.ComputeSpawnAnchor`
anchors streaming on the **spawn-density peak** (3×3 density neighbourhood) instead of the terrain
centroid (which pointed 5 cells away from the NPC cluster); **(3)** ring upgraded to **5×5
high-quality** per `terrain.md §12.2`, configurable via `ring_radius=` in `client_dir.cfg`.
Result: 0/40 → **23/40 actors grounded**; the remaining 17 live at mapZ ≤ 10002 (3–7 cells south
of the dense cluster) — genuinely outside any single static ring. **Residual is a content-coverage
matter, not a race**: it resolves itself once streaming follows the player (future work item
"player-following streaming"; the pending-snap mechanism already handles late-arriving sectors).

### 3C — Environment & lighting fidelity — `godot-presentation-engineer` (D3) — ✅ DONE (2026-06-12)
`EnvironmentNode.cs` rewritten over the parsed `.bin` family via new `Adapters/VfsEnvironmentSource.cs`:
48-keyframe day/night cycle seeded at noon (slow loop, freezable), fog start/end as fractions of the
15000u far plane with real BGRA colors, directional/ambient from `light%d.bin` (fallback dir (−7,7,20)),
material-table sky tint, Filmic tonemap. Energy floors flagged in-code as readability choices (hue is
always legacy data). Screenshot verdict: town readable in daylight. Open: sky/cloud/star DOMES not
reconstructed (flat sky tint); indoor lighting detail partial per spec.

### 3D — Water wiring — `godot-presentation-engineer` (D4) — ✅ DONE (2026-06-12)
`WaterRenderer` instantiated by `RealWorldRenderer` only when `map_option` `water_enable=1`, plane at
world Y=`water_y`, sized to the streaming ring. Original renders NO water (RESOLVED-NEGATIVE) — visuals
are our free choice. Verified by screenshot on area 11 (water visible at Y=300 exactly per
`map_option11.bin`); area 2 correctly has none. cfg restored to `area=2` after verification.

### 3E — Login & character-select screens — `godot-ui-engineer` — ✅ DONE (2026-06-12)
New `Screens/` (BootFlow, LoginScreen, CharacterSelectScreen, UiAssetLoader): real `loginwindow.dds`/
`mainwindow.dds` atlas chrome, spec pixel layout (1024×768 reference canvas scaled), CP949 strings via
`MsgXdbParser` (2,644 records loaded from the real VFS; CJK renders without tofu), offline stubs
(login accepts locally; one demo slot 'Musa' → Enter Game → world). `client_dir.cfg boot_flow=login`
(default, original-like) / `world` preserves the direct town boot byte-for-byte. Screenshots verified.
Open (spec §7 items): per-widget msg.xdb caption ids unrecovered (English placeholders); window-chrome
atlas SOURCE sub-rects partially unknown (slight bleed); 3D slot previews skipped; exact CJK font faces
(DotumChe/Dotum/BatangChe table) not bundled.

### 3E-bis — msg.xdb + UI manifest parsers — `assets-parser-engineer` — ✅ DONE (2026-06-12)
`MsgXdbParser` (516B records — layout SAMPLE-VERIFIED against the real file: exact multiple, ids
9001–9050 present), `UiTexManifestParser`, `SkillIconManifestParser` + 31 new tests (207 total green).

### 3I — Skinned mobs/NPCs — `godot-skinning-specialist` — ✅ DONE (2026-06-12)
NpcRenderer now builds `SkinnedCharacterNode` actors: **40/40 skinned, 0 static fallback**, rest-pose
invariant ~1e-6 on all 40 rigs, per-rig stand-up pivot derived empirically (not hardcoded g1), idle
clips phase-randomized, mob skinning ticks at ~10 Hz staggered (player stays per-frame). Grounding
23/40 preserved. Screenshot: articulated actors, zero explosion.

### 3F — In-game window fidelity — `godot-ui-engineer`
`GameHud.cs`, `InventoryWindow.cs`, `SkillWindow.cs`: re-skin to the original interface textures
and layout per `ui_system.md`. Acceptance: side-by-side screenshot vs. documented layout.

### 3G — Camera modes — `godot-input-engineer` — ✅ DONE (2026-06-12)
`World/CameraController.cs` now parameter-true to `camera_movement.md`: fixed-radius orbit
(radius 901.4u from eye-offset, zoom moves ELEVATION not radius), FOV 65°/near 5/far 15000,
default pitch −30°, elevation clamp [−90°,−12°], asymmetric third-person yaw clamp, modes
Third(default)/First/Static + ESC reset + Tab dev free-fly (non-original, marked), ground
collision vertical slide (terrain+3.8, +2.0 bias). Build 0/0, headless clean.

### 3H — Environment .bin parsers — `assets-parser-engineer` — ✅ DONE (2026-06-12)
`Assets.Parsers/EnvironmentBinParsers.cs` + `Models/EnvironmentBinData.cs`: all 7 family members
(MapOption 40B, Fog 204B, Light 5312B §9 revised layout, Material 9792B, StarDome, CloudDome,
CloudCycle), zero engine deps, every offset spec-cited. 176 tests green (45 new) incl. 4 real-VFS
area-2 smoke tests. Open: weather%d.bin (all-zero samples), Assets.Mapping/Godot bridge is the
consumer's job (sub-wave II).

**Supporting:** `assets-parser-engineer` updates `Assets.Parsers` for any Phase-2 format deltas
(GHTex fields, .bud extras, .mot sampling helpers) + `test-engineer` adds xUnit coverage
(deterministic, real-VFS tests skip when `clientdata/` absent).

---

## Phase 4 — Verification wave (read-only, 4 agents)

| Agent | Verifies |
|---|---|
| `godot-render-reviewer` | headless + screenshot loop: character animates, NPCs grounded, lighting sane, water present, login/select screens render; AABB/coordinate dumps for regressions |
| `csharp-reviewer` | all new C# under layers 03–05 (correctness, nullability, conventions) |
| `clean-room-auditor` | no decompiler artifacts, every new constant cites its spec, `_dirty/` untracked |
| `architecture-guardian` | DAG still downward-only; no `using Godot;` below 05; references legal |

Plus `dotnet build` 0 errors / `dotnet test` green as a hard gate.

---

## Phase 5 — Tooling & knowledge consolidation

1. **Promote the sample-hunt scanners into `vfs-inspect`** as reusable subcommands
   (`vfsls scan-mot`, `scan-bnd`, `scan-skn`, `scan-ui`) so the next format question is a
   one-liner. (This is the "tools to understand formats" mandate.)
2. New skills only where a real gap appeared during the campaign (via `skill-author`).
3. `re-session-log` → `journal.md` provenance entries; memory updates
   (`godot-render-integration.md` rewritten to the new state; `vfs-ground-truth-and-roadmap`
   updated with the clientdata move).
4. `preservation-archivist` pre-commit pass; commits **only at the maintainer's request**.

---

## Dependency graph (what blocks what)

```
0 (clientdata native) ──────────────┐ (convenience, not a blocker)
1A skinning math (IDA) ──┐          │
1B sample hunt (VFS) ────┼─→ 2:skinning.md ─→ 3A animate ─┐
1C UI system (IDA) ──┐   │                                │
1D UI assets (VFS) ──┼───┼─→ 2:ui_system.md ─→ 3E/3F UI ──┼─→ 4 verify ─→ 5 consolidate
1E water/sky (IDA) ──┼───┼─→ 2:environment.md ─→ 3C/3D ───┤
1F formats (IDA) ────┼───┼─→ 2:format updates ─→ parsers ─┤
1G camera (IDA) ─────┘   └─→ 2:camera update ─→ 3G ───────┘
3B NPC ground fix: independent (needs only existing terrain.md) — can start immediately
```

## Phase 4 — review wave results (2026-06-12)

First pass: **clean-room PASS** (zero real leaks; the flagged offsets were false positives with
citations on the line above), **C# PASS_WITH_NOTES**, **render FAIL** (1 blocker: duplicate
`WorldEnvironment`/sun when `boot_flow=login` wraps the world under a `Boot` node — the
`GetSceneRoot`+direct-children search missed the scene's own env; 3 majors: liveness screenshots
pixel-identical, character silhouette too dark pre-env-fix, char-select canvas not scaling),
**architecture FAIL** on one finding.

**Fix wave (3 agents) — ✅ ALL CONFIRMED FINDINGS FIXED (2026-06-12):** single env+sun proven under
BOTH boot flows (explicit node injection + recursive fallback; `created(env=False,light=False)`);
zero per-frame `Environment` allocation; `_ExitTree` drains the 30 Hz loop bounded (2 s);
streaming `Task.Run` cancellation threaded; player idle clip proven loaded (30 frames / 3.0 s) and
animation proven live by **programmatic pixel-diff (5,230 pixels changed in the character bbox over
1.56 s)**; the "black character" was a misread (correct directional self-shadow; highlights reach
RGB 255); screens scale correctly (full window at 1280×960, proper letterbox at 1600×900); atlas
bleed eliminated (all slices via `UiAssetLoader.Slice` with `FilterClip`); Quit/ServerList overlap
fixed; `SkillIconManifestParser` no longer truncates on a malformed line (+ regression test);
`RegisterProvider` moved to static ctors. **Final: build 0 err/0 warn, 1,025 tests green.**
Remaining fidelity gaps are spec open items (per-widget caption ids, window-chrome source rects,
char-select right panel / 3D previews, exact CJK font faces, button hover/pressed states).

### ⚖️ PENDING MAINTAINER DECISION — `Client.Godot → Client.Infrastructure` direct reference
Pre-dates this campaign (introduced in a prior session for catalogue access; csproj comment documents
intent). Options: **(a)** purity refactor — surface catalogues through `Client.Application`
interfaces (touches a 1,000-test working system; the standing memory note says do NOT refactor
without the maintainer's go-ahead), or **(b)** document as an accepted deviation in
`PRESERVATION_AND_ARCHITECTURE.md` + the guardian's accepted list (+ fix the DAG checker which
currently skips Client.Godot entirely). Related doc-only drift: the blueprint still says
`Network.Transport.Pipe` in 3 places (real project is `.Pipelines`; disk reality wins).

## Standing risks & mitigations

- **R1 — No real animation samples exist in the VFS** (all stubs): then `.mot` keyframes may be
  generated/streamed elsewhere — 1A must also check whether `Main.exe` procedurally generates
  motion or loads from another container (`motion.cache`?). Mitigation: 1B also hashes
  `motion.cache` / `effect.cache` headers.
- **R2 — UI layouts are hardcoded** in the binary (no data files): then `ui_system.md` documents
  coordinates as recovered constants (legal: facts/interop data) and 3E/3F rebuild from those.
- **R3 — Skinning convention ambiguous even after 1A/1B**: fall back to hypothesis matrix
  (row-vs-column major × pre-vs-post multiply × negate-X variants) tested empirically in an
  isolated Godot probe scene until the mesh stops exploding — documented in the spec as
  "empirically verified against renderer behavior".
- **R4 — 3.8 GB copy interrupted**: robocopy is resumable; sizes verified before cutover;
  D:\ original retained until Phase 0 acceptance passes.

— *Maintained by the orchestrator. Update phase statuses in place as waves complete.*
