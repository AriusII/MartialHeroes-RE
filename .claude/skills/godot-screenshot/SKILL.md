---
name: godot-screenshot
description: Use to SEE what the Martial Heroes Godot client actually renders — captures a PNG of the running scene by booting the client WINDOWED with a temporary GDScript autoload that grabs the viewport after a few frames, saves it, and quits. Read the PNG back to confirm terrain/buildings/character actually appear. The visual half of the verify loop (headless can't capture pixels).
allowed-tools: Bash(pwsh *) Bash(powershell *) Read Write Edit
model: sonnet
---

# godot-screenshot — capture what the client renders

Headless runs (`/godot-run-headless`) prove a scene *loads*, but they have no GPU surface and so
cannot produce an image. To actually **see** the world — is the terrain textured? are the buildings
placed correctly? is the character upright or exploded? — the client must run **windowed** long
enough to render real frames, then save the viewport to a PNG.

The most reliable in-engine probe is a **temporary GDScript autoload**: a tiny `_shot.gd` that waits
a few frames (so async terrain/asset streaming has a chance to populate the scene), grabs
`get_viewport().get_texture().get_image()`, saves it to a temp PNG, and quits the tree. A GDScript
autoload beats a C# capture node here because it loads before any scene script and needs no rebuild.

## Key facts (this project)

- **Console exe**: `C:/Users/Arius/Desktop/Godot_v4.6.3-stable_mono_win64/Godot_v4.6.3-stable_mono_win64_console.exe`
- **Project dir**: `C:/Users/Arius/RiderProjects/MartialHeroes/05.Presentation/MartialHeroes.Client.Godot`
- Autoloads live in `project.godot` under `[autoload]` as `Name="*res://path.gd"` (the `*` marks a
  singleton). The project already registers `ClientContext` and `McpBridgeGame` — we ADD a
  temporary `ShotCapture` line, then REMOVE it again on cleanup.
- A windowed run still needs the GPU (D3D12 on Windows). Do **not** pass `--headless`; with
  `--headless` the viewport texture is blank/unavailable.

## Steps

1. **Stage the autoload script.** Copy the bundled `${CLAUDE_SKILL_DIR}/scripts/_shot.gd` into the
   project as `res://Dev/_shot.gd` (create `Dev/` if needed). It reads the output path and warmup
   frame count from environment variables `MH_SHOT_PNG` and `MH_SHOT_FRAMES` (with sane defaults),
   waits that many frames, saves the PNG, and calls `get_tree().quit()`.
2. **Register it as an autoload** by adding ONE line under `[autoload]` in `project.godot`:
   ```
   ShotCapture="*res://Dev/_shot.gd"
   ```
   Add it AFTER the existing autoloads so it runs last. Do not disturb `ClientContext` /
   `McpBridgeGame`.
3. **Run windowed and wait for the PNG.** Use the bundled helper, which sets the env vars, launches
   the client windowed (NOT headless), and blocks until the file appears or a timeout trips:
   ```
   pwsh -File ${CLAUDE_SKILL_DIR}/scripts/screenshot.ps1 -Frames 180
   ```
   Flags: `-Out <png>` (default a timestamped file under `$env:TEMP`), `-Frames <N>` (warmup frames
   before capture, default 180 — bump it if terrain streams in late), `-Project`/`-Godot` overrides,
   `-TimeoutSec <s>` (default 90). It prints the final PNG path.
4. **Read the PNG back** with the Read tool to actually inspect the frame. Check the expected
   subject is present and correctly placed (textured terrain, walled town, upright character). A
   gray/empty frame usually means either a scene-script attachment bug (see `/godot-scene-author`)
   or the warmup was too short — retry with more `-Frames`.
5. **CLEANUP — mandatory.** Remove the `ShotCapture="*res://Dev/_shot.gd"` line from
   `project.godot` and delete `res://Dev/_shot.gd` (and its `.uid` if Godot generated one). The
   temporary autoload calls `quit()` on every run; leaving it in would make every future editor/game
   launch quit itself after a few frames. The helper prints this reminder; do it before reporting done.

## Notes & gotchas

- If the saved image is all background-sky with no geometry, the scene likely streamed its content
  AFTER capture — increase `-Frames`. The async terrain/NPC loaders are a known late-populate source
  (NPCs can even spawn at a fallback Y before terrain height resolves).
- One PNG per run. To capture several angles, drive the camera via the `godot` MCP game tools
  (`/godot-mcp-connect`) instead of re-running this each time.
- Capturing requires a desktop session the window can open in; on a locked/headless host the
  windowed run may fail — that is expected, fall back to `/godot-run-headless` for non-visual checks.

## Hard rules

- ALWAYS clean up the temporary autoload and script afterwards. This is the single most important
  rule of this skill.
- Never `--headless` here; that defeats the purpose (blank viewport).
- The PNG may depict copyrighted assets the user supplied — keep it in temp, inspect it, do not
  commit it or copy it into the repo (client `*.png` are gitignored for exactly this reason).
