---
name: godot-engine
description: Use when editing or running the Godot 4.6.3-mono client in 05.Presentation/MartialHeroes.Client.Godot — .tscn scene files, C# nodes/scripts, ArrayMesh/skinning/mesh builders, HUD/menus, camera/input, world/terrain coordinates, or the headless verify / screenshot loop. Carries the layer-05 conventions and the hard-won pitfalls (gray-screen .tscn binding, global::Godot namespace collisions, the GltfDocument crash, Z/X coordinate negation, passive-rendering rule).
user-invocable: false
paths: 05.Presentation/**
---

# godot-engine — layer 05 conventions (Godot 4.6.3-mono)

Background knowledge for the Godot client (`05.Presentation/MartialHeroes.Client.Godot/`). These are
neutral, already-public conventions — mirrors of `CLAUDE.md`. Cite, don't duplicate; go there for
detail. Apply this checklist on every edit/run in layer 05.

## Pitfalls (each cost real time)

1. **`.tscn` script = PROPERTY LINE, not a header attribute.** Write `script = ExtResource("1")` on its
   own line under the `[node ...]` header. `[node ... script=ExtResource(...)]` is **silently ignored**
   → no script → no `_Ready` → gray screen. (CLAUDE.md "Known Godot Pitfalls".)
2. **Namespace collision.** Inside `namespace MartialHeroes.Client.Godot.*`, a bare `Input.` /
   `Environment.` / `Time.` binds to the sibling project namespace, not the Godot class → **CS0234**.
   Use `global::Godot.Input`, `global::Godot.Environment`, `global::Godot.Time`, etc.
3. **Never `GltfDocument.AppendFromBuffer`** — native crash on this project's generated GLBs. Build a
   Godot `ArrayMesh` directly (the `BudMeshBuilder` / `SknMeshBuilder` pattern).

## Coordinate conventions (get wrong → world mirrors)

- **World geometry negates Z** — `Helpers/WorldCoordinates.ToGodot`: `(x,y,z) → (x,y,-z)`. Route all
  world placement through it; do not negate ad hoc.
- **Mesh-local `.skn` geometry negates X.**
- Cells are **1024 units**, on a **65×65** grid, spacing **16**.
- (CLAUDE.md "Coordinate conventions".)

## Passive-rendering rule (architecture)

- Layer 05 has **zero game-rule authority**. It renders state and forwards input — no formulas, no
  combat/movement decisions, no canonical state.
- **Subscribe** to `Client.Application` event channels/buses for state updates; **route** raw input
  back as use-case intents. Game truth lives in layers 01–04. Never add `using Godot;` below 05.

## Verify without the user

- **Headless (logic/scripts/assets):** run the console exe to flush every `GD.Print`/error to stdout —
  `<console-exe> --headless --path 05.Presentation/MartialHeroes.Client.Godot --quit-after 150`.
- **Screenshot (eyes-on):** run windowed with a temporary GDScript autoload that calls
  `get_viewport().get_texture().get_image().save_png(...)` — a GDScript autoload is the most reliable
  in-engine probe. (Godot MCP `mcp__godot__*` is the alternative when the editor is open.)
- (CLAUDE.md "Headless Verify Loop" / "Godot Pipeline" for the console-exe path and what already works.)

## Firewall

Neutral conventions only. No decompiler output, no copyrighted assets/bytes — render from clean specs
in `Docs/RE/` and the recovered asset chains documented in `CLAUDE.md`.
