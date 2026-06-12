---
name: godot-run-headless
description: Use to verify the Martial Heroes Godot client's scripts and asset wiring WITHOUT the user driving the editor — runs the Godot 4.6.3 console exe headless against the project, lets it tick a few seconds, then captures all GD.Print output and engine errors from stdout. The fast inner loop for "does this scene/script/asset load cleanly?".
allowed-tools: Bash(pwsh *) Bash(powershell *) Read
model: sonnet
---

# godot-run-headless — verify scripts/assets without the user

The single most useful capability for iterating on the Godot presentation layer: run the
project **headless** (no window, no GPU surface) so the engine boots, parses the main scene,
instantiates every node's C# script, runs `_Ready`/`_Process` for a few frames, and then quits —
streaming every `GD.Print`, `GD.PrintErr`, and engine diagnostic to stdout where this skill can
read it. No human has to open the editor or watch a window.

Use it to answer questions like: *does `World.tscn` load without a parse error? Did the autoload
crash? Did the terrain/asset resolver find the VFS? Are there `SCRIPT ERROR` / `Failed to load`
lines?* It is the headless half of the verify loop — pair it with `/godot-screenshot` when you
need to confirm something **visually**.

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

## Steps

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

## Hard rules

- Always use the **console** exe; the non-console `Godot_v4.6.3-stable_mono_win64.exe` returns
  immediately without surfacing stdout, so you would capture nothing.
- Headless cannot capture a screenshot of the rendered frame (no GPU surface). For pixels, use
  `/godot-screenshot` (windowed) — do not try to coax an image out of a headless run.
- This skill is **read-only verification**: it never edits the project, the scene, or `project.godot`.
- Treat any path under the real client VFS (`D:/MartialHeroesClient`) as bring-your-own-assets:
  the log may mention asset filenames, but never copy asset bytes anywhere.
