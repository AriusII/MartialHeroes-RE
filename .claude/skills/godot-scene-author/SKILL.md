---
name: godot-scene-author
description: Use to author or repair a Godot .tscn scene for the Martial Heroes client CORRECTLY — format 3, ext_resource with uid+path, and (the trap that costs hours) the 'script' attached as a PROPERTY LINE under the node header, NOT an inline header attribute. Includes the gray-screen failure mode and a temp-autoload DIAG dump to verify every node actually got its script.
allowed-tools: Read Write Edit Bash(pwsh *) Bash(powershell *)
model: sonnet
---

# godot-scene-author — write/repair a .tscn without the gray-screen trap

Hand-authoring or fixing a `.tscn` for the Martial Heroes client. The format is unforgiving in one
specific way that has cost this project hours: **how a script is attached to a node**. Get that
wrong and the scene loads with NO error — the node simply has no script, `_Ready` never runs, and you
get a gray/empty screen with nothing to debug.

## The one rule that matters most

In Godot 4, a node's script is a **property assignment on its own line BELOW the `[node ...]`
header** — it is NOT an attribute inside the header brackets.

✅ CORRECT — `script` is a property line under the header:
```
[node name="GameLoop" type="Node3D"]
script = ExtResource("1")
```

❌ WRONG — `script=...` jammed into the header is **silently ignored**; the node ends up with no
script (gray screen, no `_Ready`, no error):
```
[node name="GameLoop" type="Node3D" script=ExtResource("1")]
```

This is confirmed in the project's own `Scenes/World.tscn` header comment. When a scene "loads but
does nothing", suspect this first.

## .tscn anatomy (format 3, C# scripts)

- **Header line**: `[gd_scene load_steps=<N> format=3 uid="uid://<scene-uid>"]`. `load_steps` is one
  more than the number of resources (ext + sub) — Godot recomputes it on save, so a slightly-off
  value is tolerated, but keep it sane.
- **ext_resource** (external file — a C# script, a texture, a packed sub-scene). For a script:
  ```
  [ext_resource type="Script" uid="uid://<script-uid>" path="res://World/GameLoop.cs" id="1"]
  ```
  The `uid` comes from the script's sibling `.cs.uid` file (Godot generates it). The `path` is the
  authoritative locator; `uid` is a stable alias. Reference it later as `ExtResource("1")`.
- **sub_resource** (inline resource: materials, meshes, environments) —
  `[sub_resource type="..." id="..."]` then its properties, referenced as `SubResource("...")`.
- **node**: `[node name="..." type="..." parent="..."]` then property lines, including `script =`.
  The root node has no `parent`; children use `parent="."` or `parent="SomeNode"`.

## Steps

1. **Read the model scene first.** Open `05.Presentation/MartialHeroes.Client.Godot/Scenes/World.tscn`
   and match its exact conventions (header form, ext_resource lines, the `script = ExtResource(...)`
   property-line pattern). Mirror them — do not invent a different style.
2. **Get each script's UID.** For every `.cs` you attach, read its sibling `<name>.cs.uid` to get the
   `uid://...` value for the `ext_resource` line. If a `.cs` has no `.uid` yet, Godot generates one
   the first time the editor imports it; the `path=` is what actually resolves, so a missing/wrong
   uid degrades gracefully but should be fixed.
3. **Author / edit the scene.** Add `ext_resource` entries for each script, declare the nodes, and
   attach scripts as **property lines** (`script = ExtResource("id")`) under each node header. Never
   put `script=` inside a `[node ...]` header.
4. **VERIFY script attachment with the DIAG autoload.** This is the only reliable way to catch the
   silent gray-screen failure. Stage the bundled `${CLAUDE_SKILL_DIR}/scripts/_diag_scene.gd` as
   `res://Dev/_diag_scene.gd`, register it as a temporary autoload in `project.godot`
   (`SceneDiag="*res://Dev/_diag_scene.gd"`), then run headless:
   ```
   pwsh -File <godot-run-headless skill>/scripts/run_headless.ps1 -Frames 60
   ```
   `_diag_scene.gd` walks the active scene tree and prints, per node,
   `SCENE-DIAG: <path>  type=<Class>  script=<res path | NONE>`. Any node you expected to be scripted
   that prints `script=NONE` is the gray-screen bug — fix its `.tscn` line. (You can also drive this
   live via the `godot` MCP `get_scene_tree`; see `/godot-mcp-connect`.)
5. **CLEANUP.** Remove the `SceneDiag` autoload line from `project.godot` and delete
   `res://Dev/_diag_scene.gd` (+ `.uid`). It is a diagnostic, not a shipped node.
6. Report what you authored/fixed and the DIAG result (which nodes have scripts, which were `NONE`).

## Hard rules

- `script` is ALWAYS a property line under the node header — never a header attribute. This is the
  whole point of the skill.
- Keep `format=3` and `uid="uid://..."` on the `gd_scene` header; Godot 4 expects them.
- Always clean up the temporary `SceneDiag` autoload + script afterwards.
- Scenes belong to the presentation layer only; never let a `.tscn` pull in copyrighted client assets
  by absolute path — assets resolve through the VFS at runtime, not baked into the scene.
