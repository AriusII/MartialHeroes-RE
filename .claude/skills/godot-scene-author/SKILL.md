---
name: godot-scene-author
description: Use to author/repair Godot .tscn scenes AND bootstrap/verify the layer-05 client wiring — three modes. SCENE-AUTHOR writes/fixes a .tscn correctly (format 3, ext_resource with uid+path, and the trap that costs hours — the 'script' attached as a PROPERTY LINE under the node header, NOT an inline header attribute), with the gray-screen failure mode and a temp-autoload DIAG dump that verifies every node actually got its script. CSPROJ-BOOTSTRAP normalizes the Godot-generated MartialHeroes.Client.Godot.csproj (Godot.NET.Sdk, net10.0, EnableDynamicLoading), wires ONLY Client.Application + Assets.Mapping, and registers it in the slnx /05.Presentation/ folder. COORDINATE-CHECK dumps a placed node's global AABB via a temp autoload and checks it lands where its cell/world position says — the numeric guard against the negate-Z / negate-X mix-ups (the gray-world bug).
allowed-tools: Read Write Edit Bash(pwsh *) Bash(powershell *) Bash(dotnet *) Bash(python *)
model: sonnet
effort: high
---

# godot-scene-author — author scenes, bootstrap the csproj, verify coordinates

Three layer-05 authoring/verification jobs in one skill, all sharing the project's hard-won Godot
traps (gray-screen script binding, the two-reference engine boundary, the negate-Z/negate-X
conventions). The scene structure serves the **spec-derived presentation** (the IDA-derived
`Docs/RE/` specs + recovered chains govern what the scene must show; the official captures are the
pixel oracle) — never invent nodes/content the spec doesn't call for.

| Mode | Job | Bundled script |
|---|---|---|
| **SCENE-AUTHOR** | write/repair a `.tscn` (avoid the gray-screen trap) | `_diag_scene.gd` |
| **CSPROJ-BOOTSTRAP** | normalize the Godot-generated csproj + wire 2 refs + slnx register | — |
| **COORDINATE-CHECK** | verify a node's global AABB sits where its cell says | `_aabb_probe.gd`, `expected_pos.py` |

# Mode A — SCENE-AUTHOR (write/repair a .tscn without the gray-screen trap)

The `.tscn` format is unforgiving in one specific way that has cost this project hours: **how a script
is attached to a node**. Get it wrong and the scene loads with NO error — the node simply has no
script, `_Ready` never runs, and you get a gray/empty screen with nothing to debug.

## The one rule that matters most

In Godot 4, a node's script is a **property assignment on its own line BELOW the `[node ...]` header**
— NOT an attribute inside the header brackets.

✅ CORRECT — `script` is a property line under the header:
```
[node name="GameLoop" type="Node3D"]
script = ExtResource("1")
```
❌ WRONG — `script=...` jammed into the header is **silently ignored** (no script, no `_Ready`, gray screen):
```
[node name="GameLoop" type="Node3D" script=ExtResource("1")]
```
This is confirmed in the project's own `Scenes/World.tscn` header comment. When a scene "loads but does
nothing", suspect this first.

## .tscn anatomy (format 3, C# scripts)

- **Header line**: `[gd_scene load_steps=<N> format=3 uid="uid://<scene-uid>"]` (`load_steps` = one more
  than the resource count; Godot recomputes on save — keep it sane).
- **ext_resource** (external file): for a script,
  `[ext_resource type="Script" uid="uid://<script-uid>" path="res://World/GameLoop.cs" id="1"]`. The
  `uid` comes from the script's sibling `.cs.uid` (Godot-generated); the `path` is the authoritative
  locator. Reference later as `ExtResource("1")`.
- **sub_resource** (inline: materials/meshes/environments): `[sub_resource type="..." id="..."]` then
  properties, referenced as `SubResource("...")`.
- **node**: `[node name="..." type="..." parent="..."]` then property lines incl. `script =`. The root
  has no `parent`; children use `parent="."` or `parent="SomeNode"`.

## Steps

1. **Read the model scene first** — `05.Presentation/MartialHeroes.Client.Godot/Scenes/World.tscn` — and
   match its exact conventions (header form, ext_resource lines, the `script = ExtResource(...)`
   property-line pattern). Mirror them; do not invent a different style.
2. **Get each script's UID** from its sibling `<name>.cs.uid` for the `ext_resource` line. A missing/wrong
   uid degrades gracefully (the `path=` resolves), but should be fixed.
3. **Author / edit the scene.** Add `ext_resource` entries, declare nodes, and attach scripts as
   **property lines** (`script = ExtResource("id")`) under each node header. Never put `script=` inside a
   `[node ...]` header.
4. **VERIFY script attachment with the DIAG autoload** (the only reliable way to catch the silent
   gray-screen). Stage `${CLAUDE_SKILL_DIR}/scripts/_diag_scene.gd` as `res://Dev/_diag_scene.gd`,
   register it as a temporary autoload (`SceneDiag="*res://Dev/_diag_scene.gd"`), then run Mode A of
   `/godot-run-headless` (`-Frames 60`). It walks the active scene tree and prints, per node,
   `SCENE-DIAG: <path>  type=<Class>  script=<res path | NONE>`. Any node you expected scripted that
   prints `script=NONE` is the gray-screen bug — fix its `.tscn` line. (You can also drive this live via
   the `godot` MCP `get_scene_tree`; see `/godot-mcp-connect`.)
5. **CLEANUP.** Remove the `SceneDiag` autoload line from `project.godot` and delete
   `res://Dev/_diag_scene.gd` (+ `.uid`).
6. Report what you authored/fixed and the DIAG result (which nodes have scripts, which were `NONE`).

**Decide:** a node you expected scripted prints `script=NONE` → its `script=` was jammed into the
`[node]` header → move it to a property line. A scene that loads with no error yet does nothing → almost
always this trap; run the DIAG dump rather than trusting the clean load. A node's C# references bare
`Input.`/`Environment.`/`Time.` → a build-side namespace collision (use `global::Godot.*`), not a scene
bug → `/godot-build`.

# Mode B — CSPROJ-BOOTSTRAP (normalize the Godot-generated csproj)

The Godot client has a `project.godot` but Godot 4.6 generates the `.csproj` the first time a C# script
is attached and a C# build runs from the editor. Once that generated csproj exists, normalize it to the
project's conventions, give it its two legal downward references, and register it in the (currently
empty) `/05.Presentation/` slnx folder. Layer 05 is the only project NOT handled by `scaffold-project`,
precisely because Godot owns the csproj's creation — adopt the generated file rather than fight it.

**Hard rules:** the csproj MUST already exist (if not, STOP and tell the user to attach a C# script in
the editor and trigger a build — never hand-fabricate a Godot csproj). Keep the **`Godot.NET.Sdk`** SDK
+ its exact version (never swap for `Microsoft.NET.Sdk`); set `<TargetFramework>net10.0</TargetFramework>`
and ensure `<EnableDynamicLoading>true</EnableDynamicLoading>` (+ `Nullable`/`ImplicitUsings`). Exactly
**two** ProjectReferences: `MartialHeroes.Client.Application` and `MartialHeroes.Assets.Mapping` — never
`Network.*`, `Assets.Vfs`/`Parsers`, `Client.Domain`/`Infrastructure`, or any `Shared.*` (those arrive
transitively). Never add `using Godot;` to / reference Godot from any layer 01–04 project.

1. **Confirm the csproj exists** at `05.Presentation/MartialHeroes.Client.Godot/MartialHeroes.Client.Godot.csproj`
   (Glob/Read). If absent, stop with the instruction above.
2. **Read** it to capture the exact `Godot.NET.Sdk/<version>` string and Godot's `PropertyGroup`.
3. **Rewrite to the normalized shape**, keeping Godot's SDK version (do not hard-code a patch):
   ```xml
   <Project Sdk="Godot.NET.Sdk/4.6.0">

       <PropertyGroup>
           <TargetFramework>net10.0</TargetFramework>
           <EnableDynamicLoading>true</EnableDynamicLoading>
           <ImplicitUsings>enable</ImplicitUsings>
           <Nullable>enable</Nullable>
       </PropertyGroup>

       <ItemGroup>
           <ProjectReference Include="..\..\04.Client.Core\MartialHeroes.Client.Application\MartialHeroes.Client.Application.csproj" />
           <ProjectReference Include="..\..\03.Storage.Assets\MartialHeroes.Assets.Mapping\MartialHeroes.Assets.Mapping.csproj" />
       </ItemGroup>

   </Project>
   ```
   (Or apply the two refs via `dotnet add … reference …`.)
4. **Register in the slnx** under `/05.Presentation/`:
   `dotnet sln MartialHeroes.slnx add "05.Presentation/MartialHeroes.Client.Godot/MartialHeroes.Client.Godot.csproj"`,
   then re-Read and confirm the `<Project>` sits inside `<Folder Name="/05.Presentation/">` (previously
   self-closing/empty) with a forward-slash path; if `dotnet sln add` self-closed or misplaced it, fix
   the slnx with a precise Write (open/close the folder pair around the project), leaving every other
   line verbatim.
5. **Confirm `.godot/` is gitignored** (and Godot's `*.mono`/`obj`); never stage it.
6. **Build the single project** to confirm the SDK + refs resolve:
   `dotnet build "05.Presentation/MartialHeroes.Client.Godot/MartialHeroes.Client.Godot.csproj"`.
7. **Report** the normalized SDK/TFM, the two refs applied (and that no other core refs were added), the
   slnx folder, and the build result.

**Decide:** csproj doesn't exist → STOP (attach a C# script + build first). Godot wrote a different
`Godot.NET.Sdk/<patch>` → keep that version, only change the TFM. A tempting extra reference appears →
REFUSE it (exactly two refs).

# Mode C — COORDINATE-CHECK (verify a node sits where its cell says)

A screenshot tells you something looks wrong; this mode tells you *by how much and on which axis*. It
dumps a placed node's **global AABB** (centre + min/max) at runtime and compares it to the expected
world position from the asset's cell/legacy coordinates. A clean single-axis sign-flip is the signature
of a handedness bug.

## The conventions being checked (IDA-derived ground truth)

These negate-Z / negate-X conventions are **facts recovered from `doida.exe`**, recorded in the
committed `Docs/RE/` specs + `CLAUDE.md` "Coordinate conventions" — this mode does not *decide* them, it
**verifies the render obeys** the spec-recorded one (if the placement code and spec disagree, that's an
RE/spec question, not a number to fudge):

- **WORLD geometry negates Z**: `Helpers/WorldCoordinates.ToGodot`: `(x,y,z) → (x,y,-z)`. Negating X
  instead is the historical **gray-world bug** (buildings at `(-X,+Z)` instead of `(+X,-Z)`, ~1000+ units off).
- **MESH-LOCAL `.skn` geometry negates X** (model-local, inside `SknMeshBuilder`) — a *vertex* convention;
  a placed character's *world* position still goes through the world negate-Z.
- **Cell grid**: a cell is **1024** world units; the height grid is **65×65** with **spacing 16** (16×64=1024).

## Steps

1. **Decide the expected world position** from the asset's cell indices / legacy coordinates, using the helper:
   ```
   python ${CLAUDE_SKILL_DIR}/scripts/expected_pos.py --cell 2,3
   python ${CLAUDE_SKILL_DIR}/scripts/expected_pos.py --legacy 1536.0,0,2048.0
   ```
   It applies the cell→units math and `(x,y,z)->(x,y,-z)`. Treat its output as the **hypothesis**;
   reconcile with the actual placement code if they differ.
2. **Stage the AABB dump autoload.** Copy `${CLAUDE_SKILL_DIR}/scripts/_aabb_probe.gd` to
   `res://Dev/_aabb_probe.gd`, register a temporary autoload (`AabbProbe="*res://Dev/_aabb_probe.gd"`),
   and set `MH_PROBE_NODE` to the target node path (e.g. `/root/World/BudSceneNode`). After a few frames
   it finds the node and prints its **global** AABB:
   `AABB-PROBE: <node path>  center=(x,y,z)  min=(...)  max=(...)  size=(...)`.
3. **Run headless and capture** via `/godot-run-headless` (`-Frames 150` so streamed placement
   completes); read the `AABB-PROBE:` line.
4. **Compare** the AABB **centre** to the expected position within a cell-sized tolerance, and diagnose
   the residual: clean sign flip on **Z** → world negate-Z dropped (or applied twice); clean sign flip on
   **X**, ~1000+ units off → the gray-world bug (negated X on absolute world coords instead of Z); off by
   a constant multiple → a unit-scale error (check 1024/16/65); Y far off → the known "NPCs spawn at
   fallback Y before async terrain height resolves" debt.
5. **CLEANUP.** Remove the `AabbProbe` autoload line and delete `res://Dev/_aabb_probe.gd` (+ `.uid`).
   Report the expected position, the measured AABB centre, the per-axis delta, and the likely convention
   bug (fix at the ONE source — `WorldCoordinates` / the builder / the placement code — never a second flip).

## Verify / Done when

- **Mode A:** the DIAG autoload reports every node you intended to script as `script=<res path>` (none
  `NONE`); `format=3` + `uid="uid://..."` on the `gd_scene` header; the temporary `SceneDiag` autoload +
  `_diag_scene.gd` (+ `.uid`) removed.
- **Mode B:** SDK `Godot.NET.Sdk/<Godot's version>`, TFM `net10.0`, `EnableDynamicLoading` true; exactly
  two ProjectReferences (`Client.Application` + `Assets.Mapping`); the project inside `/05.Presentation/`
  with a forward-slash path; `.godot/` gitignored; the single-project `dotnet build` succeeds.
- **Mode C:** the measured **global** AABB centre matches the expected world position within a cell-sized
  tolerance — OR a single-axis sign-flip / constant-multiple residual is named with its cause and the ONE
  source file to fix; the temporary `AabbProbe` autoload + script (+ `.uid`) removed.

## Pitfalls (anti-patterns)

- **Never** put `script=` inside a `[node ...]` header — silently ignored, no `_Ready`, gray screen (Mode A).
- **Never** leave a temporary autoload (`SceneDiag` / `AabbProbe` / a shot autoload) behind — every future launch would self-quit.
- **Never** add a Godot reference beyond `Client.Application` + `Assets.Mapping`, swap off `Godot.NET.Sdk`, or commit/reference `.godot/` (Mode B).
- **Never** compare the LOCAL AABB (it ignores the placement transform you are validating), and never fix a coordinate mismatch by adding a second flip elsewhere — correct it at the single source (Mode C).
- **Never** trust the `expected_pos.py` value over the actual placement code if they diverge — it's a hypothesis (world negates Z, mesh `.skn` negates X; cells 1024, 65×65, spacing 16).
- **Never** bake a copyrighted client asset into a `.tscn` by absolute path — assets resolve through the VFS at runtime.

*North star: N2 — a correctly-wired scene + a strictly-passive two-reference engine boundary + exact
coordinates are the preconditions for the 1:1 client to render at all; a mirrored axis is a fidelity
defect, not cosmetics. Mode C pairs with `/godot-fidelity-check`.*

## Hard rules

- `script` is ALWAYS a property line under the node header — never a header attribute (Mode A). Keep
  `format=3` + `uid="uid://..."` on the `gd_scene` header. Always clean up temporary autoloads + scripts.
- Layer 05 wires exactly `Client.Application` + `Assets.Mapping`, keeps `Godot.NET.Sdk`, and never lets a
  game-rule or an engine dependency cross the 01–04 boundary (Mode B).
- Always compare the **global** AABB (`get_global_transform() * mesh.get_aabb()`), not the local one;
  fix a convention mismatch at the single source per the project rule "update only that one file" (Mode C).
- Scenes belong to the presentation layer only; never let a `.tscn` pull in copyrighted client assets by
  absolute path — they resolve through the VFS at runtime. No `git`/IDA tooling here.
