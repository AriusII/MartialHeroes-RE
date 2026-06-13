---
name: godot-fidelity-check
description: Use when you need to VERIFY the Martial Heroes Godot client renders and behaves 1:1 vs the original — "does the world look right?", "is the terrain textured / the town populated / the player upright?", "is the world mirrored?", a fidelity / visual-regression / render-parity pass on an area or scene. Builds layer 05, runs headless + a windowed screenshot, then scores the frame against the recovered-fact oracle (asset chains + coordinate conventions) and REPORTS fidelity gaps by class (visual, coordinate, material, missing-asset, behavior). Read-only — it reports; the godot engineers fix.
allowed-tools: Read, Grep, Glob, Bash(dotnet *), Bash(godot *)
model: sonnet
effort: high
---

# godot-fidelity-check — verify the client renders 1:1 vs the original

**North star N2.** Fidelity to the original — its visuals, asset chains, coordinate conventions, and
behavior — *is the measure of success* for the re-creation. This skill is how we MEASURE that on the
Godot client: it boots the client, captures what it actually renders, and scores it point-by-point
against the recovered facts (the oracle). It does **not** fix anything — it produces a precise,
class-tagged gap report that the godot engineers then act on. A clean report = the client matches the
original for that area; gaps = exactly where and how it diverges, with the likely fix and the owner.

This is **read-only verification.** Never edit a node, transform, scene, or shader "to make it look
right" — that is the engineers' job and editing here destroys the measurement.

## Preconditions (in order)

1. **A target scene/area to check** — e.g. the populated walled town (area 2). Know what you expect to
   see before you look, or you cannot grade the result.
2. **The recovered-fact oracle is the reference.** Hold these three sources side-by-side as ground
   truth (they are the answer key):
   - `CLAUDE.md` -> "Recovered asset mappings" (the terrain / skin / bind / mot / spawn / collision
     chains) and "Coordinate conventions" (negate-Z world, negate-X mesh, 1024 / 65x65 / spacing 16).
   - The committed `Docs/RE/formats/*.md` and `Docs/RE/specs/*.md` for the area's assets.
   Do NOT consult anything under `Docs/RE/_dirty/` — this skill is clean-side.
3. **The Godot client must compile.** A headless/windowed run uses the compiled managed DLL, not
   source, so build layer 05 first (Step 1). A stale build silently renders old behavior.

## Steps

1. **Build layer 05.** Build the client assembly so the run reflects current source:
   ```
   dotnet build 05.Presentation/MartialHeroes.Client.Godot/MartialHeroes.Client.Godot.csproj
   ```
   (or run `/godot-build`). A `Failed to load assembly` at boot means this step was skipped.

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
   reliable in-engine probe (it loads before any scene script, needs no rebuild). Use `/godot-screenshot`
   (`-Frames 180`, bump it if terrain streams in late), then **Read the PNG back** to inspect it. The
   PNG is a scratch artifact in temp — never commit it. **The autoload is temporary: it MUST be removed
   and the `_shot.gd` deleted afterward** (`/godot-screenshot` Step 5 does this — do not skip it).

4. **Compare against EXPECTED, point by point.** Grade the frame + log against the oracle:
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
     bug** — confirm the axis with `/godot-coordinate-check` (it dumps a node's global AABB vs its
     expected cell position).

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
   engineer (`godot-presentation-engineer` for placement/world, `godot-skinning-specialist` for the
   avatar, `godot-shader-specialist` for material/lighting/water, `godot-input-engineer` for behavior).
   State whether the area is **at fidelity** (clean) or list the divergences. Hand the report to
   `@godot-render-reviewer`; the named engineer fixes; re-run this skill to confirm the fix closed the
   gap.

## Hard rules

- **Read-only.** Never edit a scene, node, transform, material, or C# to "make it look right" — that
  destroys the measurement and is the engineers' job. The only thing you write is the temporary
  screenshot autoload, which you **remove afterward** (Step 3).
- **Passive-rendering rule.** The client holds **zero game-rule authority**; a fidelity gap is a
  *rendering/placement* divergence, not a reason to add game logic to layer 05. If correctness depends
  on a rule, the fix belongs below 05 (Application/Domain), not in the Godot project.
- **Heed the Godot pitfalls** when diagnosing: a gray/blank scene is usually a `.tscn` `script` written
  as a header attribute instead of a property line (silently ignored -> no `_Ready`); inside
  `MartialHeroes.Client.Godot.*` a bare `Input.`/`Environment.`/`Time.` collides with a sibling
  namespace (use `global::Godot.*`); never `GltfDocument.AppendFromBuffer` (native crash — meshes are
  built as `ArrayMesh`). Do not propose a "fix" that reintroduces one of these.
- **Clean-side firewall.** This skill never reads `Docs/RE/_dirty/`, never calls IDA, and never
  transcribes decompiler output. Forbidden tokens — `sub_`, `loc_`, `_DWORD`, `__thiscall`, raw image
  addresses, Hex-Rays pseudo-C — must NOT appear in this skill's report (this list exists only to
  prohibit them). The oracle is the committed clean specs + `CLAUDE.md`, nothing dirty.
- **Never commit originals.** The screenshot may depict copyrighted, user-supplied assets — it stays a
  scratch artifact in temp (client `*.png` are gitignored for exactly this reason). Never copy asset
  bytes, `.pak`/`.vfs`/`.dds`/`.mot`/`.skn` payloads, or the PNG into the repo. The bundled scorer
  reads only the text stdout log, never asset bytes.

## Pairs with

`godot-run-headless` (Step 2 capture) · `godot-screenshot` (Step 3 frame) ·
`godot-coordinate-check` (Step 4 axis confirmation) · `asset-chain-trace` (resolve a failed chain hop
to its on-disk VFS file) · the `@godot-render-reviewer` agent (receives this report and routes the fix).
