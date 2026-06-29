---
name: godot-fidelity-check
description: Use when you need to VERIFY the Martial Heroes Godot client renders 1:1 vs the original — "does the world look right?", "is the terrain textured / the town populated / the player upright?", "is the world mirrored?", a fidelity / visual-regression / render-parity pass on an area or scene. FIDELITY mode builds layer 05, runs headless + a windowed screenshot, then scores the frame against the recovered-fact oracle (asset chains + coordinate conventions) and REPORTS fidelity gaps by class (visual, coordinate, material, missing-asset, behavior) — read-only, it reports and the godot engineers fix. ASSET-PREVIEW mode visually previews ONE real asset (.bud building scene, .skn skinned mesh, .ted/.map terrain cell) from the VFS as a Godot ArrayMesh (NEVER GltfDocument, which crashes) and screenshots it — the way to eyeball a parser's output against its format spec.
allowed-tools: Read Write Edit Grep Glob Bash(dotnet *) Bash(godot *) Bash(pwsh *) Bash(powershell *)
model: sonnet
effort: high
---

# godot-fidelity-check — verify the client renders 1:1 vs the original

**North star N2.** Fidelity to the original — its visuals, asset chains, coordinate conventions, and
behavior — *is the measure of success* for the re-creation. This skill MEASURES that on the Godot
client in two modes:

- **Mode A — FIDELITY** (read-only): boot the client, capture what it actually renders, and score it
  point-by-point against the recovered facts (the oracle), producing a precise, class-tagged gap report
  the godot engineers act on. It does **not** fix anything.
- **Mode B — ASSET-PREVIEW** (temp scratch only): load ONE real asset from the VFS, build Godot
  geometry from the *parsed* data with the project's own mesh builders, isolate it in a tiny preview
  scene, and screenshot it — the fast way to eyeball a parser's output against its format spec.

**Mode A is read-only verification.** Never edit a node, transform, scene, or shader "to make it look
right" — that is the engineers' job and editing there destroys the measurement. (Mode B is the one
exception: it stages a *temporary* preview harness it **removes afterward**, the same posture as the
screenshot autoload — it still never edits production scenes/world code to flatter a result.)

## The two oracles (Ground-Truth Doctrine — read first)

Fidelity is graded against **two** ground truths, and they govern different things:

1. **Behavior & data → IDA + the committed `Docs/RE/` specs.** Asset chains, coordinate conventions,
   strides, opcodes, runtime responses are settled by the binary and recorded in the specs. These are
   the answer key for *what is correct*, and the client is measured against them — never the reverse.
2. **The final image → the official screenshots/captures.** For how a scene actually *looks* (color,
   lighting, atmosphere, effect placement, what's visible), the **official captures are the visual
   oracle and `oracle > spec`.** A render can be perfectly spec-faithful and still be WRONG vs the real
   client — this is exactly what CAMPAIGN 9c/12 proved (a spec-driven camera/fog regression that the
   captures caught). When a capture and a spec disagree on pixels, **the capture wins**; flag the
   spec-vs-capture divergence so RE corrects the spec. When no capture exists for the area, fall back to
   the recovered-fact oracle below and say so — a "spec-faithful" verdict is provisional until a capture
   confirms the pixels.

## Preconditions (in order)

1. **A target scene/area to check** — e.g. the populated walled town (area 2). Know what you expect to
   see before you look, or you cannot grade the result.
2. **Both oracles in hand (see "The two oracles" above).** Hold the answer key side-by-side:
   - **Behavior/data oracle (IDA-derived specs):** `CLAUDE.md` -> "Recovered asset mappings" (the
     terrain / skin / bind / mot / spawn / collision chains) and "Coordinate conventions" (negate-Z
     world, negate-X mesh, 1024 / 65x65 / spacing 16); the committed `Docs/RE/formats/*.md` and
     `Docs/RE/specs/*.md` for the area's assets — start at `Docs/RE/INDEX.md`, the navigable entry
     point to the spec corpus (by-subsystem / by-file-extension / by-runtime-struct maps + the
     definitive-negatives list), to locate the right terrain/mesh/environment spec rather than globbing.
   - **Pixel oracle (the official captures):** the maintainer's official screenshots/captures of this
     scene, when available — they govern the final image and **outrank the spec on appearance**
     (`oracle > spec`). If no capture exists for the area, grade pixels against the recovered-fact
     oracle and mark the visual verdict provisional.
   Do NOT consult anything under `Docs/RE/_dirty/` — this skill is clean-side.
3. **The Godot client must compile.** A headless/windowed run uses the compiled managed DLL, not
   source, so build layer 05 first (Step 1). A stale build silently renders old behavior.

## Mode A — FIDELITY (steps)

1. **Build layer 05.** Build the client assembly so the run reflects current source:
   ```
   dotnet build 05.Presentation/MartialHeroes.Client.Godot/MartialHeroes.Client.Godot.csproj
   ```
   (or let `/godot-run-headless` build it for you — it builds layer 05 before its headless/screenshot
   modes). A `Failed to load assembly` at boot means this step was skipped.

2. **Headless run — capture prints + errors.** Run the Godot **console** exe headless against the
   project and capture every `GD.Print` / `GD.PrintErr` / engine error to stdout (no editor needed):
   ```
   godot --headless --path 05.Presentation/MartialHeroes.Client.Godot --quit-after 150
   ```
   Prefer `/godot-run-headless` (it wraps the exact console-exe invocation and tees to a log). `150`
   is a frame count — enough for async terrain/asset streaming to surface its prints. Scan the log
   for `SCRIPT ERROR`, `Unhandled exception`, `Failed to load`, `res://... does not exist`,
   `Parse Error`. **Decision point:** a clean load with NO error lines but ALSO none of your expected
   breadcrumb prints = a quietly-broken scene (a `.tscn` `script` placed as a header attribute instead
   of a property line loads with no error and never runs `_Ready`). Suspect that before trusting the
   exit code.

3. **Windowed screenshot — capture a real frame.** Headless has no GPU surface, so to SEE pixels run
   **windowed** with a temporary GDScript screenshot autoload that, after a warmup, does
   `get_viewport().get_texture().get_image().save_png(...)` then quits. A GDScript autoload is the most
   reliable in-engine probe (it loads before any scene script, needs no rebuild). Use
   `/godot-run-headless`'s screenshot mode (`-Frames 180`, bump it if terrain streams in late), then
   **Read the PNG back** to inspect it. The PNG is a scratch artifact in temp — never commit it. **The
   autoload is temporary: it MUST be removed and the `_shot.gd` deleted afterward** (that mode's cleanup
   step does this — do not skip it).

4. **Compare against EXPECTED, point by point.** Grade structure/placement/data against the
   **spec oracle** (the chains + coordinates below); grade *appearance* (color, lighting, atmosphere,
   effect look, what's visible) against the **official capture** — and when the capture disagrees with
   what the spec predicts, the **capture wins** (`oracle > spec`): report it as a spec-vs-capture
   divergence to send back to RE, not as a render bug to "fix toward the spec".
   - **Terrain textures** present and correct per the chain `.ted TextureIndexGrid` ->
     `.map TERRAIN/BUILDING TEXTURES[idx-1].intTexId` -> `bgtexture.txt[id]` ->
     `data/map000/texture/<rel>.dds` (textures are **global under `map000`** for all areas). Blank /
     wrong-tile patches mean a hop in this chain failed.
   - **Population:** buildings + actors placed. Area 2 = **779 buildings + 40 mob/NPC**. A near-empty
     town = spawn arrays (`npc{tag}.arr` 28-byte / `mob{tag}.arr` 20-byte) not loaded.
   - **Player** upright + textured (not exploded — see the known skinning debt below).
   - **Coordinate conventions honored:** world geometry negates Z `(x,y,z)->(x,y,-z)`; mesh-local
     `.skn` negates X; cells **1024** units on a **65x65** grid, spacing **16**. A world that
     **MIRRORS** (geometry flipped across an axis, or buildings ~1000+ units off) is a Z/X **sign
     bug** — confirm the axis with `/godot-scene-author`'s coordinate-check mode (it dumps a node's
     global AABB vs its expected cell position).

5. **Enumerate gaps by CLASS.** Tag every divergence so the report is actionable:
   - **visual** — missing / blank / wrong texture, missing geometry.
   - **coordinate** — mirrored or offset placement (a sign/handedness bug).
   - **material** — wrong shader, alpha, or lighting (too dark / no blend).
   - **missing-asset** — a chain hop fails (the file the chain resolves to is absent or unmapped).
   - **behavior** — wrong runtime response (input, animation trigger, event reaction).
   Feed your observations to the bundled scorer, which also re-prints the oracle and the known debts:
   ```
   python ${CLAUDE_SKILL_DIR}/scripts/fidelity_report.py --area 2 \
     --log <captured_headless_log> \
     --gap "visual:terrain patch (3,4) blank:World.tscn:TerrainNode" \
     --gap "coordinate:town mirrored across X, buildings ~1100u off:WorldCoordinates.cs"
   ```
   It reads only the TEXT log you pass (Godot stdout) — never asset bytes — and emits each gap with a
   likely-fix class and the owning engineer. `scripts/fidelity_report.py` is the relative path to the
   same file.

6. **Cross-check the KNOWN open debts — do not re-report them as new bugs.** These are already tracked
   (CLAUDE.md "Debts"); the scorer flags any gap that matches one as `*** KNOWN DEBT ***`:
   - Character **skinning explodes the mesh** -> avatar is rendered **static** (no animation).
   - **NPCs spawn at a fallback Y** before async terrain height resolves (placement race).
   - **`EnvironmentNode` too dark** (atmosphere/lighting needs tuning).
   - **Water is unwired.**
   If a gap you see IS one of these, note it as a known debt, not a new finding.

7. **Report.** For each NEW gap give: class, `file:line` / node path, the likely fix, and the owning
   engineer (`godot-world-engineer` for placement/world + material/lighting/water, `godot-character-specialist`
   for the avatar, `godot-ui-engineer` for input/behavior). State whether the area is **at fidelity**
   (clean) or list the divergences. Hand the report to `@render-reviewer`; the named engineer fixes;
   re-run this mode to confirm the fix closed the gap.

## Mode B — ASSET-PREVIEW (eyeball one parsed asset as a mesh)

When you have changed (or are validating) an asset parser, the fastest sanity check is to **see one
asset rendered**: is the building scene the right shape, is the character mesh inside-out, does the
terrain cell tile correctly? This mode loads a single real asset from the VFS, builds Godot geometry
from the *parsed* data with the project's own mesh builders, isolates it in a tiny preview scene, and
captures a screenshot. You are **eyeballing the parser's output against the IDA-derived format spec**
(`Docs/RE/formats/`) — "right shape" means *the shape the spec predicts*; a divergence is a
parser-vs-spec defect to name, not an excuse to tweak the preview until it looks plausible.

### The two iron rules

1. **NEVER use `GltfDocument.AppendFromBuffer`** — it crashes natively on this project's generated GLBs.
   Build a Godot `ArrayMesh` directly with the existing builders: `World/BudMeshBuilder.cs`
   (`BudMeshBuilder.Build(BudScene, textureResolver?)` → a `Node3D`), `World/SknMeshBuilder.cs`
   (`SknMeshBuilder.Build(SkinnedMesh, albedoTexture?)` → a static-bind-pose `MeshInstance3D`; runtime
   skinning is a known TODO — the mesh renders static), or the `World/TerrainNode` per-patch ArrayMesh
   pattern (16×16 patches, 65×65 grid) for `.ted`/`.map`.
2. **Match the coordinate conventions exactly:** WORLD geometry negates Z (`.bud` vertices are pre-baked
   absolute world-space, terrain is world-space — use `Helpers/WorldCoordinates.ToGodot`; `BudMeshBuilder`
   already does this); MESH-LOCAL `.skn` geometry negates X (model-local, inside `SknMeshBuilder`, with
   the CW→CCW winding swap + UV v-flip — do not also negate Z on a `.skn`). Cell = 1024 units; grid 65×65;
   spacing 16.

### Steps

1. **Pick and confirm the asset.** Use `/pak-explore`'s vfs-inspect mode to find the exact VFS path of
   the `.bud`, `.skn`, or `.ted` (e.g. a building from `map000`, a character `.skn`); note its size.
2. **Stage the preview harness.** Copy `${CLAUDE_SKILL_DIR}/scripts/AssetPreviewNode.cs` into the project
   as `res://Dev/AssetPreviewNode.cs`. It reads the VFS path + asset kind from env vars
   (`MH_PREVIEW_PATH`, `MH_PREVIEW_KIND`), opens the VFS, parses the asset via `Assets.Parsers`, builds
   geometry with the matching builder (Bud/Skn — **never GltfDocument**), and adds a camera framing the
   mesh's AABB + a key light. Adjust the parser/builder calls to the current parser API as needed.
3. **Wire a one-node preview scene.** Either author a tiny `res://Dev/AssetPreview.tscn` with a single
   root carrying `AssetPreviewNode.cs` (attach the script as a PROPERTY LINE — Mode A above), or register
   it as a temporary autoload. Set the env vars to the chosen asset.
4. **Build, then screenshot.** Run `/godot-run-headless`'s screenshot mode (windowed) against the
   preview scene — it builds layer 05 first, then captures; Read the PNG back.
5. **Inspect.** Is the shape sane and right-side-out? Buildings near origin (not mirrored ~1000+ units
   away — a Z/X negate mix-up)? Character upright (the known skinning-explosion debt renders it static,
   which is expected)? UVs tiling rather than smeared?
6. **CLEANUP.** Remove the temporary preview scene/autoload + `AssetPreviewNode.cs` (+ `.uid`) and the
   `_shot.gd` artifacts. Report the asset previewed, the PNG path, and what you observed.

**Decide:** mesh mirrored or ~1000+ units off → a Z/X negate mix-up (world negates Z, mesh `.skn`
negates X — never both, never swapped) → quantify with `/godot-scene-author`'s coordinate-check mode.
Character exploded/static → the known skinning debt (expected for a `.skn`). Empty frame → warmup too
short (bump `-Frames`) or the parser threw (check the headless output first). Reaching for
`GltfDocument.AppendFromBuffer` → STOP; use the ArrayMesh builders. For a 1:1 verdict vs the original
asset (not just "shape is sane") → use Mode A.

## Hard rules

- **Mode A is read-only.** Never edit a scene, node, transform, material, or C# to "make it look
  right" — that destroys the measurement and is the engineers' job. The only things written are the
  *temporary* screenshot autoload (removed afterward) and, in **Mode B**, the temporary preview harness
  (`AssetPreviewNode.cs` + a one-node scene/autoload), which you **remove afterward** — never edit
  production scenes/world code to flatter a result.
- **Build `ArrayMesh` directly** in Mode B via `BudMeshBuilder` / `SknMeshBuilder` / the TerrainNode
  pattern; `GltfDocument.AppendFromBuffer` is BANNED (native crash). WORLD assets negate Z; MESH-LOCAL
  `.skn` negates X — never swap them.
- **Passive-rendering rule.** The client holds **zero game-rule authority**; a fidelity gap is a
  *rendering/placement* divergence, not a reason to add game logic to layer 05. If correctness depends
  on a rule, the fix belongs below 05 (Application/Domain), not in the Godot project.
- **Heed the Godot pitfalls** when diagnosing: a gray/blank scene is usually a `.tscn` `script` written
  as a header attribute instead of a property line (silently ignored → no `_Ready`); inside
  `MartialHeroes.Client.Godot.*` a bare `Input.`/`Environment.`/`Time.` collides with a sibling
  namespace (use `global::Godot.*`); never `GltfDocument.AppendFromBuffer` (native crash). Do not propose
  a "fix" that reintroduces one of these.
- **Clean-side firewall.** This skill never reads `Docs/RE/_dirty/`, never calls IDA, and never
  transcribes decompiler output. Forbidden tokens — `sub_`, `loc_`, `_DWORD`, `__thiscall`, raw image
  addresses, Hex-Rays pseudo-C — must NOT appear in this skill's report. The oracle is the committed
  clean specs + `CLAUDE.md`, nothing dirty.
- **Never commit originals.** A screenshot or previewed asset may depict copyrighted, user-supplied
  material — it stays a scratch artifact in temp (client `*.png` are gitignored). Never copy asset bytes,
  `.pak`/`.vfs`/`.dds`/`.mot`/`.skn` payloads, or the PNG into the repo. The bundled scorer reads only
  the text stdout log, never asset bytes.

## Pairs with

`godot-run-headless` (Mode A capture + the windowed screenshot mode) · `godot-scene-author`
(coordinate-check mode for axis confirmation; the `.tscn` script-binding trap) · `pak-explore`
(vfs-inspect mode to confirm an asset path) · `asset-chain-trace` (resolve a failed chain hop to its
on-disk VFS file) · the `@render-reviewer` agent (receives this report and routes the fix).
