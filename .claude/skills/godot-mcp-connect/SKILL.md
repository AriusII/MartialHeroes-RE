---
name: godot-mcp-connect
description: Use before driving the Martial Heroes Godot client through the 'godot' MCP, or when mcp__godot__* tools are unavailable / failing. Probes the editor bridge (port 9600) and game bridge (port 9601), reports UP/DOWN, enumerates the live mcp__godot__* toolset, and refuses to let interactive scene/runtime work proceed until the bridge is reachable with the editor open.
allowed-tools: Bash(python *) Bash(claude *) Read
model: sonnet
effort: medium
---

# godot-mcp-connect — preflight for the live Godot bridge

The Martial Heroes client can be driven live through the **`godot` MCP** (slangwald/godot-mcp,
registered in the repo's `.mcp.json`). It exposes two bridges from the running Godot editor:

- **Editor tools** on **port 9600** — `get_scene_tree`, `run_project`, `get_output`, `modify_node`, …
- **Game tools** on **port 9601** — `screenshot`, `click`, `get_runtime_tree`, … (the running game)

These `mcp__godot__*` tools only exist when **all** of the following hold, which is why this preflight
exists: never start interactive scene-tree or runtime work without a green check here.

## Preconditions (the bridge is fragile — verify all four)

1. **Godot editor is OPEN** on this project
   (`05.Presentation/MartialHeroes.Client.Godot`). The bridge runs *inside* the editor; if the
   editor is closed, both ports are dead.
2. **The `mcp_bridge` editor plugin is ENABLED.** `project.godot` already lists it
   (`enabled=PackedStringArray("res://addons/mcp_bridge/plugin.cfg")`) and the ports are fixed in
   `mcp_ports.cfg` (editor 9600, game 9601). If the plugin is disabled, enable it in
   Project ▸ Project Settings ▸ Plugins and reload the project.
3. **The MCP server process can start.** `.mcp.json` launches it as
   `uv run --directory C:/Users/Arius/godot-mcp/mcp python godot_mcp_server.py` — `uv` must be on
   PATH and that directory must exist.
4. **A FRESH Claude Code session.** MCP servers load at session startup. If the editor was opened
   *after* this session began, the `mcp__godot__*` tools will not be present until you restart the
   session.

## Steps

1. **Probe both ports** with the bundled stdlib probe:
   ```
   python ${CLAUDE_SKILL_DIR}/scripts/check_godot.py
   ```
   It TCP-connects to 9600 and 9601 and prints `GODOT EDITOR BRIDGE: UP/DOWN` and
   `GODOT GAME BRIDGE: UP/DOWN`. Exit 0 only when the editor bridge (9600) is up. The game bridge
   (9601) is only up while a game is actually running (`run_project`), so a DOWN there is normal
   when the editor is idle — the probe says so.

2. **If the editor bridge (9600) is DOWN — stop.** Relay the remediation the probe prints:
   - Open the Godot 4.6.3-mono editor on
     `05.Presentation/MartialHeroes.Client.Godot` and confirm the `mcp_bridge` plugin is enabled.
   - If the editor is open but `mcp__godot__*` tools are absent from this session, the MCP server
     is not registered/loaded. It is in `.mcp.json` as `godot`; if missing or stale, (re)add it:
     ```
     claude mcp add godot -- uv run --directory C:/Users/Arius/godot-mcp/mcp python godot_mcp_server.py
     ```
     then **restart the Claude Code session** so the tools load.
   **Do NOT fabricate scene-tree / runtime results while DOWN.**

3. **If UP — enumerate the live toolset.** The exact `mcp__godot__*` tool names depend on the
   installed bridge build, so discover them at runtime (like `/ida-mcp-connect` does for IDA) from
   the tool manifest / system reminders. Classify them:
   - **Editor (9600)**: scene inspection/edit + project run — `get_scene_tree`, `modify_node`,
     `run_project`, `get_output`, …
   - **Game (9601)**: live runtime — `screenshot`, `click`, `get_runtime_tree`, …
   Report which are present so the caller knows what is callable.

4. **Confirm it is really this project.** Call the cheapest editor tool (e.g. `get_scene_tree`) and
   confirm the root scene is the Martial Heroes `World` scene, not some other editor instance. If it
   looks wrong, warn before continuing.

5. **Green light.** When 9600 is up, `mcp__godot__*` tools are present, and the open project checks
   out: report "Godot bridge ready", note which ports/tools are live, and hand off. If the caller
   needs a screenshot of the running game, first `run_project` (which brings up the 9601 game bridge)
   then use the game `screenshot` tool — or fall back to the standalone `/godot-screenshot` skill.

## Decision points

- **If 9600 DOWN** → STOP; relay remediation (open the editor / enable `mcp_bridge` / re-add the MCP +
  restart session). Do NOT fabricate results.
- **If 9600 UP but `mcp__godot__*` tools absent from this session** → the editor was opened AFTER the
  session started → restart the Claude session (the bridge can't be hot-attached).
- **If 9601 DOWN while 9600 UP** → normal (no game running); `run_project` to raise it before any game
  tool (`screenshot`, `click`, `get_runtime_tree`).
- **If `get_scene_tree` shows a non-`World` root** → some other editor instance is bound; warn before
  driving it.
- **If the editor isn't open at all and you only need a non-interactive check** → don't wait on the
  bridge; fall back to `/godot-run-headless` (load check) or `/godot-screenshot` (pixels).

## Verify / Done when

- 9600 reported UP/DOWN with remediation if down; on UP, the live `mcp__godot__*` toolset is
  enumerated and classified (editor vs game), and the open project is confirmed to be Martial Heroes
  `World` — then "bridge ready" with the next interactive skill named.

## Pitfalls

- Never invent scene-tree / runtime results while the bridge is DOWN — refusing is the correct outcome.
- Never assume tool names — discover them at runtime (the bridge build varies, like `/ida-mcp-connect`).
- Never treat a DOWN 9601 with UP 9600 as a failure — it only listens during a running game.

*North star: N2 — the live bridge lets you drive and inspect the running 1:1 client interactively.*

## Hard rules

- Connectivity only — this skill never edits scenes or scripts; that is `/godot-scene-author`.
- Never invent `mcp__godot__*` results when the bridge is DOWN; refusing is correct.
- The game bridge (9601) being DOWN while the editor bridge (9600) is UP is normal and not an error —
  it only listens while a game is running.
