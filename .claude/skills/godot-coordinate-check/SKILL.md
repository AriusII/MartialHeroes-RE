---
name: godot-coordinate-check
description: Use to catch coordinate-convention bugs in the Martial Heroes client — dumps a placed node's global AABB via a temporary autoload and checks it lands where its cell/world position says it should. The numeric guard against the negate-Z / negate-X mix-ups (the historical gray-world bug where buildings landed ~1000+ units away, mirrored across an axis).
allowed-tools: Read Write Edit Bash(pwsh *) Bash(powershell *) Bash(python *)
model: sonnet
effort: high
---

# godot-coordinate-check — verify a node sits where its cell says

A screenshot tells you something looks wrong; this skill tells you *by how much and on which axis*.
It dumps a placed node's **global AABB** (centre + min/max) at runtime and compares it to the
expected world position computed from the asset's cell/legacy coordinates. A mismatch that is a clean
sign-flip on one axis is the signature of a handedness bug.

## The conventions being checked (IDA-derived ground truth)

These negate-Z / negate-X conventions are **not arbitrary engine choices** — they are facts recovered
from `doida.exe` and recorded in the committed `Docs/RE/` specs + `CLAUDE.md` "Coordinate conventions".
This skill does not *decide* the convention; it **verifies the render obeys** the spec-recorded one.
The actual placement code is the operative reference (the helper below is only a hypothesis); if code
and spec disagree, that is an RE/spec question, not a number to fudge.


- **WORLD geometry negates Z**: `Helpers/WorldCoordinates.ToGodot`: `(x, y, z) → (x, y, -z)`. Used by
  `BudMeshBuilder`, terrain, and world placement. Getting this backwards (negating X instead) is the
  historical **gray-world bug**: buildings landed at `(-X, +Z)` instead of `(+X, -Z)`, ~1000+ units
  from the terrain they belong to.
- **MESH-LOCAL `.skn` geometry negates X** (model-local, handled inside `SknMeshBuilder`). That is a
  *vertex* convention; a placed character's *world position* still goes through the world negate-Z.
- **Cell grid**: a cell is **1024 world units**; the height grid is **65×65** with **spacing 16**
  (16 × 64 = 1024). So cell `(cx, cz)` maps to a world origin near `(cx*1024, _, -(cz*1024))` after
  the Z negate (exact corner-vs-centre offset depends on the placement code — confirm against it).

## Steps

1. **Decide the expected world position.** From the asset's cell indices / legacy coordinates, compute
   the expected Godot position. Use the bundled helper for the arithmetic and the Z-negate:
   ```
   python ${CLAUDE_SKILL_DIR}/scripts/expected_pos.py --cell 2,3
   python ${CLAUDE_SKILL_DIR}/scripts/expected_pos.py --legacy 1536.0,0,2048.0
   ```
   It prints the expected `(x, y, z)` applying the cell→units math and `(x,y,z)->(x,y,-z)`. Treat its
   output as the hypothesis, not gospel — reconcile with the actual placement code if they differ.
2. **Stage the AABB dump autoload.** Copy `${CLAUDE_SKILL_DIR}/scripts/_aabb_probe.gd` to
   `res://Dev/_aabb_probe.gd`, register it as a temporary autoload
   (`AabbProbe="*res://Dev/_aabb_probe.gd"`), and set `MH_PROBE_NODE` to the target node's path
   (e.g. `/root/World/BudSceneNode` or a node name to search for). It waits a few frames (so async
   placement settles), finds the node, and prints its **global** AABB:
   ```
   AABB-PROBE: <node path>  center=(x,y,z)  min=(x,y,z)  max=(x,y,z)  size=(x,y,z)
   ```
3. **Run headless and capture.** Use `/godot-run-headless` (`-Frames 150` so streamed placement
   completes). Read the `AABB-PROBE:` line from the output.
4. **Compare.** Does the AABB **centre** match the expected position within a cell-sized tolerance?
   Diagnose the residual:
   - Clean sign flip on **Z** → the world negate-Z was dropped (or applied twice).
   - Clean sign flip on **X**, offset by ~1000+ units → the gray-world bug (negated X on absolute
     world coords instead of Z).
   - Off by a constant multiple → a unit-scale / spacing error (check 1024 / 16 / 65).
   - Y far from expected → the known "NPCs spawn at fallback Y before async terrain height resolves"
     debt; re-check after terrain load, or probe a frame later.
5. **CLEANUP.** Remove the `AabbProbe` autoload line from `project.godot` and delete
   `res://Dev/_aabb_probe.gd` (+ `.uid`). Report the expected position, the measured AABB centre, the
   per-axis delta, and the likely convention bug (if any).

## Verify / Done when

- The measured **global** AABB centre matches the expected world position within a cell-sized
  tolerance — OR a single-axis sign-flip / constant-multiple residual is named with its cause (Z drop,
  X gray-world, 1024/16/65 scale, or fallback-Y race), pointing at the ONE source file to fix; and the
  temporary `AabbProbe` autoload + script (+ `.uid`) are removed.

## Pitfalls

- Never compare the LOCAL AABB — it ignores the placement transform you are validating.
- Never fix a mismatch by adding a second flip elsewhere — correct it at the single source
  (`WorldCoordinates` / the builder / the placement code).
- Never trust the helper's expected value over the actual placement code if they diverge — it's a
  hypothesis (world negates Z, mesh `.skn` negates X; cells 1024, 65×65, spacing 16).

*North star: N2 — exact coordinates are non-negotiable for a 1:1 world; a mirrored axis is a fidelity
defect, not cosmetics. Pairs with `/godot-fidelity-check`.*

## Hard rules

- Always compare the **global** AABB (`get_global_transform() * mesh.get_aabb()`), not the local one —
  a local AABB ignores the very placement transform you are validating.
- The conventions are: WORLD negates Z, MESH-LOCAL `.skn` negates X. Do not "fix" a mismatch by adding
  a second flip in the wrong place — fix it at the single source (`WorldCoordinates` / the builder /
  the placement code), per the project rule "update only that one file".
- Always clean up the temporary probe autoload + script afterwards.
