---
name: godot-run-headless
description: Use to VERIFY the Martial Heroes Godot client without the user driving the editor — two modes. HEADLESS runs the Godot 4.6.3 console exe headless against the project, ticks a few frames, and captures all GD.Print output and engine errors from stdout (the fast inner loop for "does this scene/script/asset load cleanly?"). SCREENSHOT runs the client WINDOWED with a temporary GDScript autoload that grabs the viewport after a few frames, saves a PNG, and quits, so you can SEE what renders (is the terrain textured / town populated / character upright or mirrored?). Headless can't capture pixels; screenshot is the visual half of the verify loop.
allowed-tools: Bash(pwsh *) Bash(powershell *) Read Write Edit
model: sonnet
effort: medium
---

# godot-run-headless — verify scripts/assets without the user

The two halves of the Godot verify loop, in one skill:

- **Mode A — HEADLESS** (no window, no GPU surface): boot the engine, parse the main scene,
  instantiate every node's C# script, tick a few frames, quit — streaming every `GD.Print` /
  `GD.PrintErr` / engine diagnostic to stdout. Answers *does it LOAD cleanly?*.
- **Mode B — SCREENSHOT** (windowed, real GPU frames): boot the client windowed with a temporary
  GDScript autoload that grabs the viewport after a warmup and saves a PNG. Answers *what does it
  RENDER?* — the pixels headless cannot capture.

Use Mode A for *does `World.tscn` load without a parse error? did the autoload crash? did the asset
resolver find the VFS?*; use Mode B for *is the terrain textured? are buildings placed? is the
character upright or exploded?*.

## Key facts (this project)

- **Console exe** (prints to stdout — use this, NOT the plain `.exe` which detaches):
  `C:/Users/Arius/Desktop/Godot_v4.6.3-stable_mono_win64/Godot_v4.6.3-stable_mono_win64_console.exe`
- **Project dir** (contains `project.godot`):
  `C:/Users/Arius/RiderProjects/MartialHeroes/05.Presentation/MartialHeroes.Client.Godot`
- **Main scene**: `res://Scenes/World.tscn` (set in `project.godot`). Autoloads:
  `ClientContext` (C#) and `McpBridgeGame` (GDScript).
- Invocation shape: `--headless --path <proj> --quit-after <N>` where `N` is a **frame count**
  (the engine quits after N rendered frames). ~150 frames ≈ a few seconds at 60 fps — enough for
  async terrain/asset loads to surface their prints.

## Preconditions

1. The C# assembly must be **built** first — a headless run uses the compiled managed DLL, not
   source. If C# was just edited, run `/godot-build` (or `dotnet build` the csproj) before this.
   A stale/missing build manifests as `Failed to load assembly` or missing script behaviour.
2. Nothing else should hold the project's `.godot/mono` build lock (close a running editor game).

## Mode A — HEADLESS (steps)

1. Run the bundled helper, which wraps the exact invocation and tees stdout+stderr to a log:

   ```
   pwsh -File ${CLAUDE_SKILL_DIR}/scripts/run_headless.ps1 -Frames 150
   ```

   Optional flags: `-Project <path>` (defaults to the client project above), `-Godot <path>`
   (defaults to the console exe above), `-Scene res://Scenes/Foo.tscn` (override the main scene
   for a focused test), `-TimeoutSec 90` (hard wall-clock kill so a hung load can't block forever),
   `-Log <file>` (defaults to a timestamped file under the system temp dir). The script prints the
   captured output to stdout AND writes it to the log file, then exits with Godot's exit code.

2. **Read the captured output.** Scan for, in order of severity:
   - `SCRIPT ERROR`, `Unhandled exception`, `ERROR:`, stack traces → a real failure; report the
     first one with its file/line, that is almost always the root cause.
   - `Failed to load`, `Cannot open file`, `res://... does not exist` → a missing/misnamed asset
     or scene path.
   - `Parse Error` / `Invalid` near a `.tscn`/`.tres` line → a malformed scene (see
     `/godot-scene-author` for the silently-ignored-script trap).
   - Your own `GD.Print` breadcrumbs confirming the happy path (e.g. "terrain loaded", counts).
3. **Distinguish "clean" from "quietly broken".** A zero exit code with no error lines is a pass.
   But a **gray-screen** scene (a node whose `script` was put in the `.tscn` header instead of as a
   property line) loads with **no error at all** — `_Ready` simply never runs. If you expected
   specific prints and they are absent, suspect that, and confirm script attachment with
   `/godot-scene-author`'s DIAG dump rather than trusting the exit code.
4. Report: the exit code, the log path, and the salient lines (errors first, then the
   confirming breadcrumbs). Keep it tight — quote the load-bearing lines, don't dump the whole log.

## Mode B — SCREENSHOT (windowed, capture a real frame)

Headless has no GPU surface, so to actually **see** the world the client must run **windowed** long
enough to render real frames, then save the viewport to a PNG. The most reliable in-engine probe is a
**temporary GDScript autoload** (`_shot.gd`): it waits a few frames (so async terrain/asset streaming
populates the scene), grabs `get_viewport().get_texture().get_image()`, saves a PNG, and quits. A
GDScript autoload beats a C# capture node here — it loads before any scene script and needs no rebuild.

1. **Stage the autoload script.** Copy the bundled `${CLAUDE_SKILL_DIR}/scripts/_shot.gd` into the
   project as `res://Dev/_shot.gd` (create `Dev/` if needed). It reads the output path + warmup frame
   count from `MH_SHOT_PNG` / `MH_SHOT_FRAMES` (sane defaults), waits, saves the PNG, and calls
   `get_tree().quit()`.
2. **Register it as a temporary autoload** by adding ONE line under `[autoload]` in `project.godot`,
   AFTER the existing autoloads (so it runs last): `ShotCapture="*res://Dev/_shot.gd"`. Do not disturb
   `ClientContext` / `McpBridgeGame`.
3. **Run windowed and wait for the PNG** with the bundled helper (sets the env vars, launches windowed
   — NOT headless — and blocks until the file appears or a timeout trips):
   ```
   pwsh -File ${CLAUDE_SKILL_DIR}/scripts/screenshot.ps1 -Frames 180
   ```
   Flags: `-Out <png>` (default a timestamped temp file), `-Frames <N>` (warmup, default 180 — bump it
   if terrain streams in late), `-Project`/`-Godot` overrides, `-TimeoutSec <s>` (default 90). A
   windowed run still needs the GPU (D3D12 on Windows) — do **not** pass `--headless` (blank viewport).
4. **Read the PNG back** with the Read tool to inspect the frame: is the expected subject present and
   correctly placed (textured terrain, walled town, upright character)? A gray/empty frame usually means
   a scene-script attachment bug (`/godot-scene-author`) or too-short warmup (retry with more `-Frames`).
5. **CLEANUP — mandatory.** Remove the `ShotCapture="*res://Dev/_shot.gd"` line from `project.godot` and
   delete `res://Dev/_shot.gd` (+ its `.uid`). The autoload calls `quit()` on every run; leaving it in
   makes every future editor/game launch self-quit after a few frames. Do this before reporting done.

Capturing requires a desktop session the window can open in; on a locked/headless host the windowed run
may fail — fall back to Mode A for non-visual checks. One PNG per run; for several angles drive the
camera via the `godot` MCP game tools (`/godot-mcp-connect`) instead of re-running.

## Decision points

- **If C# was just edited** → `/godot-build` first, else you verify a stale DLL (a green load that
  doesn't reflect your change).
- **If the run hangs past `-TimeoutSec`** → an async asset load is wedged (VFS missing / wrong path);
  re-run with a `-Scene` override on a minimal scene to isolate the offending loader.
- **If no errors but your expected `GD.Print`s are absent** → suspect the silent gray-screen
  (`.tscn` script in the header, not a property line) → confirm with `/godot-scene-author`'s DIAG dump.
- **If you need pixels** (is it textured? upright? mirrored?) → use Mode B (screenshot) above; headless
  cannot. A mirrored / ~1000+ units-off world is a coordinate bug → `/godot-scene-author`'s
  coordinate-check mode (global-AABB diagnosis). For a 1:1 fidelity comparison against the original,
  hand to `/godot-fidelity-check`.

## Verify / Done when

- Exit code captured, log path reported, and EITHER the first real error (with file:line) OR the
  expected happy-path breadcrumbs are confirmed present — not merely "exit 0".
- A "clean" verdict is only claimed after ruling out the silent gray-screen (expected prints present).

## Pitfalls

- Never trust a `0` exit code alone — gray-screen scenes load clean with no error.
- Never use the non-console `.exe` (detaches, no stdout) or pass `--headless` when you actually need
  an image (Mode B requires the GPU — `--headless` gives a blank viewport).
- **Mode B: ALWAYS clean up the temporary `ShotCapture` autoload + `_shot.gd` afterwards** — every
  future launch would self-quit after a few frames (this skill's single worst failure mode).
- Never declare a Mode B defect from a too-short warmup — rule out late streaming (`-Frames`) before blaming geometry.
- Never copy any asset filename/byte the log mentions, or the captured PNG, out of temp; the VFS is
  bring-your-own and client `*.png` are gitignored.

*North star: N2 — this is the fast inner loop that keeps the Godot client's 1:1 re-creation honest;
pixels are the verdict on the visual re-creation.*

## Hard rules

- Always use the **console** exe; the non-console `Godot_v4.6.3-stable_mono_win64.exe` returns
  immediately without surfacing stdout, so you would capture nothing.
- Mode A (headless) cannot capture a screenshot (no GPU surface); use Mode B (windowed) for pixels —
  do not try to coax an image out of a headless run, and never `--headless` in Mode B.
- Mode A is **read-only verification** (never edits the project/scene/`project.godot`). Mode B writes
  ONLY the temporary `_shot.gd` + the one `[autoload]` line, both of which it **removes afterward**.
- Treat any path under the real client VFS (`D:/MartialHeroesClient`) as bring-your-own-assets: the log
  or PNG may depict asset filenames/bytes, but never copy asset bytes or the PNG anywhere (client `*.png`
  are gitignored for exactly this reason).
