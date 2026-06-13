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

---

# CYCLE 2 — Client Workflow Comprehension + UI/GUI Perfection + VFS Tooling (launched 2026-06-12)

**Mandate (maintainer):** recover the COMPLETE legacy client runtime workflow from `Main.exe`
(doida.exe) via massive IDA + IDAPython research — boot, main loop, scene machine, and every
scene step (Login → Server-List Selection → Character Selection → World), plus the cross-cutting
modules (Effects, UI/GUI toolkit, Sound, Network lifecycle, Resource/loading) — and consolidate
it into one authoritative markdown. Then perfect the UI/GUI implementation (C# + Godot) and the
VFS format tooling. Many phases, wide agent fan-out (50–70 agents incl. sub-waves).

**Master deliverable:** `Docs/RE/specs/client_workflow.md` — the end-to-end workflow document
(every system & module, scene-by-scene), promoted clean from `_dirty/workflow/` lane notes.

## Phase W1 — GIGA RESEARCH (dirty-room, 19 lanes)

IDA lanes (sub-waves of 3 — one IDB, bounded MCP concurrency), each writing
`Docs/RE/_dirty/workflow/<lane>.md`:

| # | Lane | Agent | Question |
|---|---|---|---|
| 1 | boot-init | re-static-analyst | WinMain → window/D3D → VFS mount → config → singletons → first scene |
| 2 | main-loop | re-static-analyst | frame anatomy: pump order (input/network/scene update/render/present), timing |
| 3 | scene-machine | re-static-analyst | exhaustive state machine: enter/update/exit/render per state, all transition triggers |
| 4 | login-scene | re-static-analyst | login widget actions, validation, login send, transition + error dialogs |
| 5 | server-select | re-protocol-analyst | server-list source (file/net), selection → connect flow |
| 6 | char-select | re-static-analyst | char-list source, 3D preview canvas, create/delete, → world transition |
| 7 | world-scene | re-static-analyst | world entry sequence (map load/spawn/HUD build) + per-frame world update order |
| 8 | effects-system | re-asset-format-analyst | effect manager, .xeff/.fx1–7 loading, particle structs, trigger/render paths |
| 9 | gui-internals | re-static-analyst | GU* toolkit: render/hit-test/z-order/action dispatch/window mgr/focus/editbox/fonts |
| 10 | sound-system | re-static-analyst | sound/music module, formats, per-scene triggers |
| 11 | network-module | re-protocol-analyst | per-scene connection lifecycle, packet pump ↔ scene integration |
| 12 | resource-manager | re-static-analyst | load/cache pipeline, async, loading screens |
| 13 | module-cartography | ida-script-author | IDAPython mass classification of functions into subsystems; inventory tables |
| 14 | singletons-map | re-struct-cartographer | global manager objects, roles, init order, key struct layouts |

VFS lanes (parallel, no IDA — `vfs-data-analyst`): 15 effects-samples census (.xeff/.fx1–7),
16 sound-samples census, 17 ui-asset full census (DDS ↔ screens, msg.xdb id ranges),
18 serverlist/config data hunt, 19 world-scene per-area data inventory.

**W1 STATUS — ✅ DONE (2026-06-12):** 19/19 lanes returned (14 IDA in 5 sub-waves + 5 VFS), all
high confidence (network-module medium). Headlines: WinMain IS the 9-state machine (full
state×trigger transition table recovered); lobby protocol on port 10000 (8-byte server records,
LZ4, no cipher; channel endpoint on port 10000+server_id); 5 char-select C2S sends incl. an
UNRESOLVED 1/6 login-vs-create collision; GU* toolkit internals (sprite render path, button state
frames, IME, fonts); DirectSound+Ogg Vorbis sound stack (footsteps come from actor-visual fields,
NOT mud cells); XEffect class family + .xeff byte-verified layout; resource pipeline (no file
cache, 3×3 sync ring + streamer thread); 19 singletons + module map (engine = "Diamond",
MSVC 2005, Lua 5.1.2, LZ4, XTrap anti-cheat); 63-area census; server list is NETWORK-fetched
(clean negative on VFS config). Follow-up lane: pixel-exact widget-rect sweep ✅ (login ~100%,
char-select ~100%, 117 HUD builders inventoried; 7-state ctor actually yields 3 distinct frames;
login form lives on login_slice1.dds; char-select action ids Create=4/Delete=5/Enter=6).

## Phase W2 — PROMOTION (spec-authors)
`specs/client_workflow.md` (master synthesis) + new `specs/effects.md`, `specs/sound.md`,
`specs/frontend_scenes.md` (login/server-select/char-select); updates to `client_runtime.md`,
`ui_system.md`. Orchestrator: firewall scan, journal.md, names.yaml.

**W2 STATUS — ✅ DONE (2026-06-13, first pass 8/11 + recovery 5/5):** WRITTEN: opcodes.md
(+Appendix A lobby protocol, 193 rows validator-clean) + 8 packets/*.yaml (1/6 collision doc,
1/7, 1/13, 1/14, 2/10000 keepalive, 3/4, 4/1, lobby), specs/sound.md, specs/frontend_scenes.md,
specs/resource_pipeline.md, specs/client_workflow.md (master), formats/area_inventory.md,
structs/runtime_singletons.md, formats/ui_manifests.md+misc_data.md (msg.xdb SAMPLE-VERIFIED).
3 satellites died on API socket errors → W2-bis recovery re-runs effects /
client_runtime / ui_system (folding the widget-rects sweep + 3-frame correction) + surgical
fixes (sound_tables 2d/3d split, ui_manifests DXT2) + master refresh.

## Phase W4 (early start) — VFS TOOLING — ✅ vfsls subcommands DONE (2026-06-12)
8 new subcommands smoke-tested on the real VFS: scan-mot, scan-bnd, scan-skn, scan-ui,
dump-msgxdb, dump-uitex, scan-xeff, scan-sound. SKILL.md updated.

## Phase W3 — UI/GUI PERFECTION (engineering)
Per-widget msg.xdb captions; window-chrome atlas source sub-rects; button hover/pressed states;
char-select right panel (+3D previews if spec'd); in-game HUD original skin (3F); font fidelity.

**W3 PLAN (launched 2026-06-13, 2-stage pipeline):** Stage 1 foundations — widgets kit
(3-frame state buttons per ui_system.md §1.5/§8, press/release-inside semantics), uitex.txt+msg.xdb
runtime catalogs adapter, SoundTableParser (Assets.Parsers + tests). Stage 2 consumers — login
fidelity (21-widget table, login_slice1.dds), char-select fidelity (77 widgets, action ids 4/5/6,
stat grid 191/24, 5×3D previews), HUD reskin via uitex binding contract, audio wiring (VFS .ogg:
click SFX 861010101, spawn 862010105, entry BGM cue 910066000, per-area .bgm tables).

## Phase W4 — VFS TOOLING
vfsls subcommands (scan-mot/scan-bnd/scan-skn/scan-ui + dump-msgxdb/dump-uitex/scan-xeff);
parsers for newly spec'd formats (effects et al.) + tests.

**W3 STATUS — ✅ DONE (2026-06-13, 7 agents in 2 stages):** Stage 1: Screens/Widgets kit
(StateButton 3-frame + legacy press/release semantics, WidgetFactory, AlphaFadeDriver ±64/tick),
Adapters/UiCatalogs (uitex 37 entries + msg.xdb 2,644 records live from VFS), SoundTableParser
(+38 tests, real-VFS smoke on map002). Stage 2: LoginScreen pixel-faithful (login_slice1.dds,
quit-confirm modal, local validation msg 4025/4026), CharacterSelectScreen (41 widgets, action
ids 4/5/6, stat grid 191/24, **5 slots with live 3D skinned previews** via SubViewports),
HUD reskin (mainwindow/inventwindow/skillwindow chrome via uitex binding), Audio/AudioService
(Music+Sfx buses, VFS .ogg LoadFromBuffer cache, auto-wired StateButton click SFX, world-entry
cues, per-area .bgm). Bonus research closed the icon chain: skill icons = per-skill (srcX,srcY)
pairs stored in the per-class stance .do files (116-byte records, +0x18/+0x1C), fixed 23×23
cells; item icons = one whole DDS per icon via flat texturelist.txt (1,335 entries, 100% present).

## Phase W5 — REVIEW + FIX
godot-render-reviewer (screenshots), csharp-reviewer, clean-room-auditor, architecture-guardian;
fix wave; build 0/0 + full test suite green as hard gate.

**W5 STATUS — ✅ DONE (2026-06-13):** Render PASS_WITH_NOTES (2 majors), C# PASS_WITH_NOTES
(9 majors), architecture FAIL (pre-existing edges only — see decision block below). Fix wave
(3 agents) closed ALL confirmed findings: login form-band backing (PLAUSIBLE dark panel —
legacy panel art region unrecovered), GameHud full-rect root + hotbar at true viewport bottom
(screenshot-verified), chrome bind moved post-Initialise (uitex path now live), AudioService
idempotent click-SFX wiring + Callable.From BGM dispatch + nullable stream cache, terrain VFS
disposal, CharPreview3D static-flag save/restore in finally + RegisterProvider static ctor,
GameHud event-path allocations removed, SoundTableEntry.HourSchedule → [InlineArray(24)] (zero
per-parse heap), UiTexManifestParserTests xUnit2013 ×5 + SessionHandshake CA2014 cleaned by the
orchestrator. **Final gate: build 0 err/0 warn (non-incremental), 1,066 tests green (10/10 suites).**

### ⚖️ PENDING MAINTAINER DECISION (extended 2026-06-13) — off-table dependency edges
All pre-existing (none introduced by Cycle 2; today's wave touched no csproj), all downward-legal,
all flagged by `check_dag.py` against the CLAUDE.md table:
1. `Client.Application → Network.Protocol` (csproj:15) — table allows Network.Abstractions only.
2. `Client.Application → Network.Crypto` (csproj:19) — same rule.
3. `Client.Infrastructure → Assets.Parsers` (csproj:14) — table allows Client.Application only.
4. `Client.Infrastructure → Assets.Vfs` (csproj:15) — same rule.
5. `Shared.Diagnostics → Shared.Kernel` (csproj:17) — table says packages-only (csproj comment
   calls it an intentional optional downward ref).
6. (Carried over) `Client.Godot → Client.Infrastructure` — the original pending decision; also
   enables layer-05 *source-level transitive* use of Assets.Parsers types that the table routes via
   Assets.Mapping. **Confirmed pervasive & pre-existing (2026-06-13 C3-R review):** 21 existing
   layer-05 files already `using MartialHeroes.Assets.Parsers` transitively (IconCatalogs,
   ScrStatCatalogueSource, UiCatalogs, Vfs*Source, NpcRenderer, TerrainNode, SkinnedCharacter*, …);
   the Cycle-3 review flagged 3 new files in the SAME category (BuffIconCatalog, ZoneCatalog,
   MinimapPanel). The csproj DAG stays clean (Client.Godot references only Assets.Mapping directly;
   no new ProjectReference). Refactoring only the 3 new files would be inconsistent with the 21
   existing ones and is a dependency-edge change the standing rule forbids without maintainer
   go-ahead → **accepted as a documented deviation, code kept as-is** (orchestrator default).
Options per edge: (a) bless into the table (update CLAUDE.md/PRESERVATION_AND_ARCHITECTURE.md +
check_dag.py expected set) or (b) refactor through the allowed abstraction. Orchestrator default
if unanswered: keep code as-is, document as accepted deviations. Related doc drift: blueprint
still said `Network.Transport.Pipe` (real name `.Pipelines`) — corrected in
PRESERVATION_AND_ARCHITECTURE.md by the orchestrator on 2026-06-13.

---

# CYCLE 3 — World-Scene Systems + Icon/Window Fidelity + Deeper VFS Tooling (launched 2026-06-13)

**Mandate (maintainer):** "poursuivre très fortement — ce n'est absolument pas suffisant". Push the
IDA research into the WORLD-SCENE gameplay systems (combat, chat, NPC interaction/shops, quests,
party/trade, minimap, buffs, equipment visuals, skill→effect chain, progression), wire the recovered
icon chains into the HUD for real, and keep scaling the VFS tooling. Same clean-room firewall, same
wide agent fan-out as Cycle 2.

## Phase C3-W1 — GIGA RESEARCH (dirty room, 20 lanes) — ✅ DONE (2026-06-13, 20 agents)
IDA lanes (5 sub-waves of 3 — single IDB): combat-flow, chat-system, npc-interaction /
quest-system, party-trade, minimap-worldmap / ingame-windows-art, buff-state-icons,
do-record-fields / equip-visuals, skill-cast-fx, floating-text-target / do-ini-crypto,
drop-pickup, exp-levelup.
VFS lanes (parallel, harness-only): do-census (full 116B field statistics), minimap-assets,
window-art-census, quest-dialog-data, fx-asset-links (fx2 field[3] arbitration data).
Output: `Docs/RE/_dirty/world/*.md` only.

**W1 STATUS — ✅ DONE (2026-06-13):** 20/20 lanes DELIVERED, all high confidence (~92 min,
3.86M tokens). Key headlines fed to W2:
- **combat-flow:** server-authoritative `BattleHandler`; basic melee IS a skill via C2S **2/52 slot
  byte 0xFF** (no separate attack opcode); target two-tier (global hover id @0x7AC1CC + per-controller
  pick); incoming 5/52 ActorSkillAction (anim/FX + floating dmg), 5/53 vitals → HP@actor+176;
  cooldown = swing-ready-ts + 100ms·cadence + 550ms motion lockout.
- **chat-system:** say/party/guild/shout/alliance/whisper ALL **2/7 with first payload byte = channel
  code** (0/1/2/3/6/7/9/15); 36-byte ring records {string,ARGB,channel}; overhead bubbles live on the
  Actor struct (5000ms). Corrects prior 2/82/83/84 misread.
- **npc-interaction:** central click router maps ~30 NPC KIND → 117 MainHandler panel slots; storage
  = **2/142** (16B [i32][u8 op][i64]); shop = 2/115; sell 2/20; repair 2/113; interact-open 2/19;
  shop catalog baked in npc.scr+128 (6 entries). NO client teleport opcode (server-resolved off 2/19).
- **quest-system:** QuestPanel 3-tab browser (active/completable/available); C2S **2/28** quest action
  (send-only); S2C 5/68 QuestList + 5/73 QuestComplete; quest text = msg.xdb ids + npc.scr 6-line records.
- **party-trade:** PARTY 2/35 invite / 2/36 leave-kick / 2/37 leader-op (8B [u8 mode][u32 id]);
  TRADE 2/23 request / 2/24 slot-add 20B / 2/25 confirm-manifest; GUILD 2/30; S2C phase machines
  4/23, 4/36, 5/21 event codes.
- **minimap-worldmap:** dot transform BYTE-VERIFIED px=relX·0.125+66.5, py=relZ·0.125+66.5 (1:8,
  133×133 body); radar streams data/effect/map/d{prefix}x{X}z{Z}.bmp ring; full-screen 'b'-key
  BroodWar map data/ui/map/map%d.dds + broodwarmap.dds + g_LandmarkTable POI pins.
- **ingame-windows-art:** corrects widget-rects S5 mislabels (StatusPanel=char-info 0x5298b2,
  SkillPanel=skill window); in-game chrome binds by **uitex.txt integer id** (1=mainwindow,
  2=inventwindow, 3=skill_window_1, 4=tradekeepwindow, 9=messagewindow, 11=skillpipe_02); skill title
  msg 3027; OptionPanel 4-tab host.
- **buff-state-icons:** file **data/script/buff_icon_position.xdb** (12B records {u32 buffId, i32
  srcX, i32 srcY}); shared atlas data/ui/skillicon/stateicon.dds; 30-slot HUD buff bar driven by
  **4/102** (476B, 30×12B buff records @payload+116); 23×23 cells for id≤80, 25×25 for >80.
- **do-record-fields:** FULL 116-byte .do layout (29 fields + 4 pad = 0x74); **CORRECTION**
  iconSrcX/Y @+0x18/+0x1C are **u32 not i16**; +0x28..+0x73 = 3 optional UI overlay-sprite badges;
  2/52 wire field = hotbar slot index u8, NOT instanceKey.
- **equip-visuals:** avatar changes by PER-PART mesh recomposition (head/face/hair/body/weapon under
  one skeleton @Visual+1300); part table @Visual+204 (16B records); weapon attached to hand bone;
  weapon glow tier from item_actor+231 (1..9).
- **skill-cast-fx:** skill_id→effect_id is DIRECT (SkillData byte 1136); cast FX = looping
  actor-anchored UserXEffect on action 0xC8, stopped on 0xC9/0xCB; FX1/FX2 layout corrected
  (4B group_count + per-group 20B header).
- **floating-text-target:** per-digit billboard quads from att-font.dds/cri-font.dds; 8-kind
  motion/color switch (red phys / blue skill / gold / green heal); actor name labels via
  CharacterBillboardPanel (faction color); overhead HP minibar from minibar.tga.
- **do-ini-crypto:** RESOLVED — DoOption.ini and the .do data files are **PLAINTEXT** (no cipher);
  the only "obfuscation" is FILE_ATTRIBUTE_HIDDEN. Real ciphers (network packet XOR-ROL, anti-cheat
  string) are out of file scope.
- **drop-pickup:** S2C 4/4/5/14/4/14/4/15/5/15 ground-item lifecycle; C2S 2/14 drop (8B) / 2/15
  pickup (12B); ground items = per-template 3D actors (fallback model 201011001); coin id 217000501.
- **exp-levelup:** 5/9 ExpGain (32B, 64-bit xp), 5/11 RankXpGain, 5/32 LevelUp (UserXEffect
  310000002 + class-evolution panels at lvl 12/24), 5/67 StatsUpdate; C2S **2/29** StatAllocate
  (20B five absolute u32 STR/INT/AGI/DEX/CON).
- **VFS lanes:** do-census (3,312 records, stride 116 confirmed, 93.9% icon-valid; classStanceRef
  anomaly in ma-files); minimap-assets (NO baked minimap tiles exist; mapsetting.scr 52×84B zone
  table decoded; regiontableNNN.bin 52×32B); window-art-census (22 dimension/format corrections to
  ui_manifests.md — many "1024² DXT3" are actually 512² ARGB32; 2 undocumented DDS);
  quest-dialog-data (quests.scr 3720B stride / npc.scr 404B / autoquestion_cl.scr 92B / discript.sc
  68B all SAMPLE-VERIFIED); fx-asset-links (fx2 field[3] is VARIABLE not constant — committed spec
  WRONG; itemjointeff.txt 18,580 rows + mobjointeff.txt + totalmugong.txt effect-id registries).
- Raw lane notes: `Docs/RE/_dirty/world/*.md` (gitignored, tainted, awaiting W2 promotion).

## Phase C3-E1 — EARLY ENGINEERING (clean room, ran parallel with W1) — ✅ DONE (2026-06-13, 7 agents)
6/6 lanes DONE + verify PASS. **Build 0 err/0 warn; 1,121 tests green (+55 new); headless boot
clean in BOTH boot flows** (login: icon catalogs proven; world: `[SkyDome] star=built cloud=built`
+ `[StreamFollow] ARMED` proven by the orchestrator, cfg restored byte-exact).
- **B1 parsers:** `DoStanceParser` (116B stance records; tail +0x28..+0x73 preserved as
  [InlineArray(76)]; Map-A instanceKey + Map-B slotIndex lookups) + `TextureListParser`
  (texturelist.txt, tex_id prefix) + 55 xUnit tests incl. real-VFS smokes (musajung 301 records,
  musama 222 + 40 tail bytes). Found a spec discrepancy: §2.7 says "72 bytes unmapped @+0x28" but
  116−0x28=76 — flagged for the W2 spec pass.
- **B2 skill icons:** `Adapters/IconCatalogs.cs` — skillicon.txt sheet (job=1,kind=1) +
  musajung.do Map-B → AtlasTexture 23×23; SkillWindow shows 80 real slots; hotbar slots 0..8
  get real icons. Boot log: 301 records / 12 sheet entries.
- **B3 item icons:** `ItemIconCatalog` (texturelist.txt → whole-DDS icons, 1,336 entries on the
  real file); InventoryWindow demo grid shows real DDS icons. Open: items.csv tex_id column
  unspec'd (§9 #12) — demo maps slot→file order.
- **B4 follow-streaming:** RealWorldRenderer re-anchors the ring on player movement (Chebyshev ≥1
  cell + in-flight guard; eviction bounds resident ≤25 sectors); boot anchor unchanged (23/40
  grounded at boot, south NPCs ground as the player walks via existing pending-snap).
- **B5 sky domes:** `World/SkyDomeNode.cs` — star dome 192 verts + cloud dome 240 verts (2 layers,
  UV scroll from cloud_cycle speed), day/night alpha ramp, indoor suppression, RenderPriority −128.
  Solid-color domes for now; DDS star/cloud textures = follow-up.
- **B6 char-select skins:** per-slot rigs — demo roster now Musa Lv25 / Tao Lv18 / Blader Lv32 /
  Warrior Lv40, class→skin .skn chain (Tao special-cased; mapping PLAUSIBLE, needs a spec entry),
  name/class/level overlays per frontend_scenes.md §3.2.

## Phase C3-W2 — PROMOTION (after W1) — ✅ DONE (2026-06-13, 21 authors + master)
`specs/world_systems.md` (NEW master) + 10 subject specs (combat UPD, chat NEW, social UPD,
inventory_trade UPD, npc_interaction NEW, quests UPD, minimap NEW, progression NEW,
equipment_visuals NEW, ui_system UPD, effects UPD) + 4 format specs (effects/config_tables/
misc_data/ui_manifests UPD) + opcodes.md (UPD, 2/7→CmsgChat) + ~35 new packets/*.yaml. Each author
owned ONE file (zero contention); master synthesised only from cleaned specs.
**Orchestrator post-promotion (all DONE):** firewall scan **CLEAN** (zero autonames/VAs in new files;
only hits = imagebase 0x400000 in provenance docs + sample data values 0x46464558/0x7FFFFFFF/
0x64000007); **consistency fix** — added 16 omitted C2S opcode rows to opcodes.md in sorted order
(2/19,2/20,2/23,2/24,2/25,2/30,2/35,2/36,2/37,2/100,2/110,2/113,2/115,2/142,2/143,2/151-153);
**names.yaml merged** (2/7 rename, 22 C2S opcodes, 6 subsystem entries, 16 constants);
**journal.md** W1+W2 provenance entries written. Specs total now 27, formats 16, packets 62.

## Phase C3-E2 — ENGINEERING (after W2) — ✅ DONE (2026-06-13, 9 agents / 3 stages)
3-stage pipeline, clean-room (engineers read ONLY committed specs, cite `// spec:`):
- **Stage A (contracts, distinct projects):** Client.Application HUD event hub (ChatLine/BuffState/
  CombatText/TargetChanged/ExpLevel/StatAllocationView channels, stub-fed now, live-handler later);
  Assets.Parsers BuffIconPosition(12B)/MapSetting(84B)/RegionTable(32B)/NpcScr(404B)/QuestsScr(3720B)
  + tests.
- **Stage B (6 Godot components):** ChatWindow; CharacterStatsWindow+BuffBar+BuffIconCatalog;
  TargetFrame+FloatingCombatText; MinimapPanel+ZoneCatalog (dot transform px=relX·0.125+66.5);
  OptionsWindow+audio buses; EffectRenderer MVP (cast effect @byte1136, action 0xC8 loop).
- **Stage C (integration):** wire into GameHud + ClientContext + project.godot input map; full
  `dotnet build` 0/0 + headless smoke (both boot flows, cfg restored byte-exact).
Architectural seam: HUD ← Application event channels (passive, zero game authority). Live
packet→Application handlers (4/102, 5/9, 5/52, 5/7, target) are a documented follow-up.

## Phase C3-T — TOOLING — ✅ DONE (2026-06-13, 2 agents, ran parallel with W2)
`vfsls` now exposes **12 subcommands** (was 8): added `scan-fx` (W1), `dump-do`, `scan-minimap`,
`scan-quest`. Build 0 err/0 warn; real-VFS smoke proven: dump-do = 12 files / **3,312 .do records**
(0 missing, 93.9% icon-valid, classStanceRef single-value per jung/sa file); scan-quest = quests.scr
488 slots / 122 occupied (ids 1..617), npc.scr 2,510, autoquestion_cl.scr 1,300, discript.sc 33;
scan-minimap = mapsetting.scr 52 zones (84B), 60× regiontableNNN.bin (3,120 records, 32B), 3 map DDS.
`vfs-inspect/SKILL.md` updated to the real 12-subcommand surface + a "Sibling research harnesses"
section (do-census, minimap-scan, quest-dialog-scan, msgxdb, skill-icon-scan, skillcat-scan).
Firewall-clean (orchestrator pre-audit PASS; only hit was a sample data value 0x64000007).

## Phase C3-R — REVIEW + FIX + GATES — ✅ DONE (2026-06-13, 4 reviewers + 1 fix agent)
**Review (4 read-only reviewers):** render **PASS_WITH_NOTES** (HUD renders, zero town/character
regression; 1 major leak + 1 minor demo-state), csharp **PASS_WITH_NOTES** (0 blockers; 5 majors =
2 per-frame allocs + GD.Print hot-path + 3 dead usings; XdbParser uint→int confirmed correct;
parsers zero-alloc), clean-room **FAIL→resolved** (leak scan SPOTLESS, 0 leaks, all constants cited;
FAIL was only the journal/names staging gap, cleared by staging them in this commit), architecture
**FAIL→accepted deviation** (3 new layer-05 files use Assets.Parsers transitively — a pre-existing
pervasive pattern across 21 files; csproj DAG clean; documented, not refactored per the no-edge-refactor
rule).
**Fix wave (1 agent, 9 fixes):** MinimapPanel VFS-handle leak → borrow shared ClientContext.ZoneCatalog;
MinimapPanel blip render → pooled ColorRect[64] (zero per-frame alloc); coord label → cached, alloc only
on change; EffectRenderer hot-path GD.Print removed; 3 dead usings + 1 dead ChatWindow computation
removed; TargetFrame demo state cleared on Bind. Stray QA artifacts (client_dir.cfg.bak, screenshot)
deleted; client_dir.cfg restored byte-exact.
**Orchestrator doc pass:** ROADMAP deviation note extended; `Network.Transport.Pipe`→`.Pipelines`
corrected in PRESERVATION_AND_ARCHITECTURE.md (×3).
**FINAL GATE:** `dotnet build MartialHeroes.slnx --no-incremental` = **0 err / 0 warn**; `dotnet test`
= **all 10 suites green, 0 failures**. The 3 LSP "always-true" + 4 CS8019 diagnostics were stale
mid-flight cache (real code/line mismatch; msbuild surfaces neither). Committed after gates.

# CYCLE 4 — Client workflow end-to-end: Boot → Login → ServerList → CharSelect (+ Effect/UI/GUI) (launched 2026-06-13)

**Mandate (maintainer, verbatim, FR):** *"on gèle le World Scene — il y a encore trop de choses qui
ne me vont pas avant de se focaliser dessus. Lance IDA en DEBUGGER (9.3) si tu peux voir/comprendre ce
qui se passe (creds fournis). Prends le temps de bien réfléchir pour poursuivre la compréhension du
WORKFLOW du client de bout en bout — effect, UI & GUI, Login Scene, Server List Selection, Character
Selection, puis World Scene. Énormément de recherches, scripts PYTHON dans IDA, déploie 50-70 agents +
sub-agents. Mets ça dans un markdown qui documente tout le workflow, tous les systèmes et modules. Puis
parfaire UI/GUI (C# + Godot) et surtout le tooling de compréhension des fichiers du data.vfs. Beaucoup
de phases, pleins d'agents partout. Ce n'est absolument pas suffisant !"*

This cycle steps **back upstream** of the world scene to recover the **complete client boot-to-play
workflow** as the original sequences it: process boot → engine/VFS init → **Login scene** (ID/PW) →
**server/channel list selection** → **second-password (PIN)** → **character-select scene** → hand-off
into the world — plus the cross-cutting **Effect** and **UI/GUI framework** layers that every scene
sits on. End state: a single authoritative workflow document, deepened subsystem specs, a faithful
C#/Godot UI/GUI implementation of the front-end scenes, and sharper VFS file-understanding tooling.

**Master deliverable:** `Docs/RE/specs/client_workflow_master.md` — the end-to-end scene/state/module
map (boot → login → serverlist → PIN → charselect → world hand-off), synthesised last from the cleaned
subject specs (extends the Cycle-2 `client_workflow.md`, doesn't duplicate it).

**Out of scope (deferred):** the **World Scene** gameplay deepening (frozen this cycle, per mandate);
the **game server** (core stays engine-free for a future `MartialHeroes.Server.Console`, no server code).

**Command structure:** Top Orchestrator (this session) drives the debugger spine directly (serial,
IDA-exclusive) and owns Tier-1 files; a **W1-Orchestrator** wave fans out the static+VFS research
(IDA in sub-waves of 3, VFS wide); E/R get Tier-2 orchestrators when they launch.

## Evidence baseline
- Going in: 27 specs / 16 formats / 62 packet YAMLs / opcodes.md; Cycle-2 `client_workflow.md` +
  `ui_system.md` + `frontend_scenes.md` already cover the login/char-select **frontend at sketch level**.
- Known gaps this cycle closes: the **scene state-machine** (push/pop/tick + the global state var),
  **server-list/channel** record layout + fetch threads, the **second-password/PIN** sub-flow, the
  **effect manifest pipeline** (XEffect family end-to-end), the **UI/GUI widget framework** (.do/.scr →
  widget tree → action dispatch), and a **debugger-confirmed** VFS open/find/read path (flagship §0.4).
- Tool baseline verified 2026-06-13: IDA MCP **UP** (static; `doida.exe` sha `63fcaf8e…`, 25 973 funcs,
  2 790 named) · build **0/0** · tests green (Cycle-3 gate) · VFS **reachable** (`clientdata/` + `D:`).
- **Debugger status — ✅ WORKING (2026-06-13).** Maintainer armed it in the IDA GUI (Local Windows
  debugger, F9, accept the trust dialog). KEY: **never call `mcp__ida__dbg_start`** (the MCP can't dismiss
  IDA's modal trust dialog, and a session is already active) — the maintainer F9-launches and the
  orchestrator **pilots the live session** via `dbg_*` (reads even through PAGE_NOACCESS). Servers are
  dead → live driving stops at login, but build-time (pre-send) assembly + VFS reads are fully capturable.

## Spine targets recovered (static scout, canonical names — addresses live only in `_dirty/`)
Login: `Diamond_LoginWindow_{BuildScene,TickStateMachine,OnAction,HandleInputEvent,RenderCharacterList}`,
`Diamond_CommonLoginWindow_{ctor,dtor}`, `Diamond_LoginWindow_{FetchServerList_Thread,FetchChannelEndpoint_Thread}`,
`CIPList_GetSelectedRecord`, RTTI `LoginSecondPassword` (the PIN second factor). CharSelect:
`Diamond_SelectWindow_{ctor,BuildScene,BuildSlotActors,BuildCreatePreviewActor,InitFromCharListAndBuildUI,EnterSelectedCharacter,End}`.
Net/auth: `AuthSession_BuildLoginPacket43`, `NetClient_SetLoginEndpoint`, `NetHandler_CharMgmt_SceneEntityUpdate`.
Main/render: `Diamond_MainHandler_BuildSceneGraph`, `Renderer_DrawScene*`, `Scene_UpdateCameraAndCull`.
VFS: `VFS_{OpenArchive,FindEntry,ReadEntryData,LoadFile,IsMounted,SetMounted}`, `VFS_ScopedReader_*`.
Effects: `EffectManager_LoadBmplistAndManifests`, `{User,Joint,Map,Core}XEffect_*`, `XEffect_tickAndDispatch`,
`XEffectManager_LoadXeffectLst_*`, `ParticleEffectManager_*`.

## Phase C4-0 — MANDATE & PRE-FLIGHT — ✅ DONE (2026-06-13)
Mandate captured; scope + out-of-scope set; baseline verified (build 0/0, IDA static UP, VFS reachable);
spine targets scouted; debugger R11 blocker documented. ROADMAP section written.

## Phase C4-W1 — GIGA RESEARCH (dirty room) — ❌ SUPERSEDED, NOT RUN (see reframe in C4-1 below)
> The blind 15-lane research below was designed before the debugger spine + a read of the existing
> specs revealed the workflow is ALREADY comprehensively documented. It was **stopped and superseded**.
> Kept as a historical record only — do NOT resume it. The real Cycle-4 trajectory is C4-1 onward.
IDA static lanes (sub-waves of 3, single IDB) + VFS harness lanes (wide). Output: `_dirty/workflow/*.md`
ONLY. Ledger: one writer per `_dirty/workflow/<lane>.md`.

| # | Lane | Type | Agent | Question | Deliverable | Conf |
|---|------|------|-------|----------|-------------|------|
| 1 | boot-sequence | IDA-S | re-static-analyst | entry→engine init→VFS mount→first scene push order | _dirty/workflow/boot-sequence.md | — |
| 2 | scene-machine | IDA-S | re-static-analyst | global scene/state manager: push/pop/tick + the state var | _dirty/workflow/scene-machine.md | — |
| 3 | login-scene | IDA-S | re-static-analyst | LoginWindow BuildScene + TickStateMachine states + field model | _dirty/workflow/login-scene.md | — |
| 4 | serverlist-selection | IDA-S | re-protocol-analyst | server/channel list fetch threads + record layout + selection | _dirty/workflow/serverlist.md | — |
| 5 | second-password-pin | IDA-S | re-static-analyst | LoginSecondPassword (PIN) sub-flow + where it gates | _dirty/workflow/second-password.md | — |
| 6 | charselect-scene | IDA-S | re-static-analyst | SelectWindow InitFromCharList…→slot actors→EnterSelectedCharacter | _dirty/workflow/charselect-scene.md | — |
| 7 | auth-login-packet | IDA-S | re-protocol-analyst | AuthSession_BuildLoginPacket43 field layout + login crypto init | _dirty/workflow/auth-login-packet.md | — |
| 8 | vfs-deep (flagship §0.4) | IDA-S | re-asset-format-analyst | data.inf record layout + lookup + read/decompress→buffer | _dirty/workflow/vfs-deep.md | — |
| 9 | effect-system | IDA-S | re-asset-format-analyst | XEffect family + manifest load (.xeff/bmplist) + tick/dispatch | _dirty/workflow/effect-system.md | — |
| 10 | ui-gui-framework | IDA-S | re-static-analyst | window/widget base classes + .do/.scr→widget tree + action dispatch | _dirty/workflow/ui-framework.md | — |
| 11 | mainloop-render | IDA-S | re-static-analyst | per-frame loop: BuildSceneGraph→cull→DrawScene order | _dirty/workflow/mainloop-render.md | — |
| 12 | net-session-bootstrap | IDA-S | re-protocol-analyst | SetLoginEndpoint + connection/session setup + endpoint resolve | _dirty/workflow/net-bootstrap.md | — |
| V-A | vfs-ui-manifests | VFS | vfs-data-analyst | login/select `data/ui/*.dds` + `.do/.scr` manifest census | _dirty/workflow/vfs-ui-manifests.md | — |
| V-B | vfs-effect-files | VFS | vfs-data-analyst | effect manifests (.xeff/bmplist/effect/map) census + headers | _dirty/workflow/vfs-effect-files.md | — |
| V-C | vfs-login-assets | VFS | vfs-data-analyst | exact asset set the login/charselect scenes load | _dirty/workflow/vfs-login-assets.md | — |
| D-* | debugger-confirm (boot/scene/vfs/login-packet) | IDA-D | re-* | runtime confirm once backend armed | _dirty/workflow/*.dyn.md | DEFERRED |

**W1 EXIT:** lanes 1-3,6,8 (critical path) returned + ≥10 of 15 total; confidence rated; conflicts flagged.
**W1 STATUS — PENDING.**

## Phase C4-1 — DEBUGGER SPINE + REFRAME — ✅ DONE (2026-06-13)
Debugger unblocked (status above). Drove the live client through account+password+PIN; captured the login
blob `[0x2B][lenpfx account][lenpfx PIN]` pre-encryption from PACKETBUF memory (ground truth), confirmed
the plaintext-8-byte-header / XOR-ROL-payload framing, and identified the long-unnamed optional
login-blob field as the **second-password / PIN** (`DName::isPin`; caller = the §4.1 secure-context
builder). Evidence in `_dirty/workflow/login-packet.dyn.md` (gitignored; creds never transcribed).
**REFRAME:** the existing specs ALREADY document the client workflow comprehensively (`client_workflow.md`
master + ~15 satellites + ~40 packet YAMLs) — so the blind giga-research (C4-W1 above) is SUPERSEDED.
Real Cycle-4 work = front-end IMPLEMENTATION + VFS tooling depth + targeted gap-fill. A 4-axis
gap-assessment (`wf_8651c471-a9e`) found: front-end = disconnected stubs; network crypto/handshake =
solid but UNWIRED (no outbound sink, no inbound LZ4 decompress, lobby absent, 3/7 spawn missing); VFS
tooling = near-complete but inspect-only.

## Phase C4-2 — PIN PROMOTION + FRONT-END WIRING + VFS TOOLING + DDS FIX — ✅ DONE (committed `24151ac`)
- PIN gap promoted → `specs/login_flow.md` + `specs/frontend_scenes.md` (journaled; firewall PASS).
- Front-end WIRED (layer 05): BootFlow orchestrates Login→ServerSelect→PIN→CharSelect→World on
  Client.Application; new ServerSelectScreen + PinModal; char-select driven by CharacterListEvent;
  `DEV_OFFLINE_FLOW=1` replay makes the flow walkable with the servers dead.
- VFS tooling: vfsls gains decode/extract/convert/hexdump/coverage (28-format registry).
- Fix: DDS dwFourCC off-by-4 in Assets.Mapping/PngConverter (DDS→PNG unblocked) + 2 regression tests.
- Gate: build 0/0 · 1153+ tests green · clean-room audit PASS. Branch `cycle4-login-capture-frontend-vfs`.

## Phase C4-E2 — FRONT-END VISUAL FIDELITY — ✅ DONE (committed `033b18c`)
Root cause was no `[display]` content-scale. Added `canvas_items` stretch + `keep` aspect + 1024×768
viewport (3D World unaffected). Plus a project-wide gap fixed: a FontBootstrap autoload sets
`ThemeDB.FallbackFont` = SystemFont of the legacy Korean faces → ALL CP949 UI text renders as real Hangul
(login + char-select + World HUD), not tofu. Login form backing/labels + StateButton fallback bg (the
legacy DXT3 button atlas regions are genuinely alpha=0 — RE follow-up). Verified via screenshots.

## Phase C4-E4 — NETWORK END-TO-END — ✅ DONE (E4-a + E4-b + E4-c; code-complete, server-gated for live)
- E4-a (committed `beb27af`): concrete `CryptoOutboundPacketSink` (cipher→LZ4→header, crypto injected as
  delegates), inbound LZ4 decompress in FrameSplitter, the port-10000 `LobbyClient`, and the login/select
  wire structs (1/7, 3/7, 1/13, 1/14, 1/6-opaque). +30 tests.
- E4-b (committed `c18a68b`): completed enter-game → SPAWN — 3/7 handler + `CharacterSelectionStore`
  (descriptor cache) + @BLANK@ routing + slot≤4 guard + 1/9 version-token derivation (→21149). +8 tests.
- E4-c (committed `e4a8df9`): network completeness — `ILobbyClient` + DTOs (Abstractions), `LobbyClient`
  implements it (Transport), the `3/4`·`3/6`·`3/23` result structs + handlers, lobby orchestration
  use-cases (ServerListReceivedEvent / ChannelEndpointResolvedEvent) + char-mgmt result events, and a
  flagged-OFF `1/6` login-blob emit. +25 tests. ONLY deferred bit: wire the front-end ServerSelect to the
  real ILobbyClient (server-gated — the dev-offline replay stays the demo path with the servers dead).

## Phase C4-E5 — VFS TOOLING DEPTH — ✅ DONE (committed `72e02ef`)
Wired the env/region `.bin` family (map_option/fog/material/light/stardome/clouddome/cloud_cycle/
point_light/wind/regiontable — existing Assets.Parsers, prefix-disambiguated) + area_inventory `.lst`
into the vfsls decode registry. coverage now 41 / 40-decode / 12-convert; smoke-verified on the real VFS.

## Phase C4-E6 — LAYER-05 INTEGRATION + E2E VISUAL — ⏳ IN PROGRESS
Fix the version-token call site (ClientContext passes a zero span that overrides the 21149 derivation),
wire LocalPlayerSpawnedEvent → World entry, extend the dev-offline replay to the full
Login→ServerSelect→PIN→CharSelect→World flow, and screenshot-verify each screen end-to-end.

## Phase C4-R / C4-C — REVIEW + GATES + CONSOLIDATION — ◑ ONGOING
Each phase gated (build 0/0 + tests + clean-room smell scan) before its commit — done for all 6 commits
above; full-solution gate GREEN at **1206 tests, 0 failures**. Final consolidation TODO: CS8019
unnecessary-using sweep (cosmetic, LSP-only — accumulated across E4/E5), memory refresh.

— *Maintained by the orchestrator. Cycle-4 pivoted from blind research to debugger-confirmed
implementation; the C4-W1 giga-research table above is HISTORICAL (superseded, not run). Update phase
statuses in place as waves complete. (Distinct from `ROADMAP-CAMPAGNE2.md` — that is a separate
parallel session.)*
