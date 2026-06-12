---
name: godot-build
description: Use to build the Martial Heroes Godot client's C# assembly (MartialHeroes.Client.Godot.csproj) before a headless run or screenshot — the managed DLL the engine loads must be current. Surfaces compiler errors plainly, and documents the clean-rebuild (delete .godot/mono) for when Godot's generated bindings look stale (CS0103 on Godot types, "type not found" after an API change).
allowed-tools: Bash("C:/Program Files/dotnet/dotnet.EXE" *) Bash(pwsh *) Bash(powershell *) Read
model: sonnet
---

# godot-build — compile the Godot client assembly

The Godot client is a C# project (`Godot.NET.Sdk/4.6.3`, `net10.0`). When it runs, Godot loads the
**compiled** `MartialHeroes.Client.Godot.dll`, not the `.cs` sources — so any C# change must be
rebuilt before `/godot-run-headless` or `/godot-screenshot`, or you will silently verify stale code.
This skill is that rebuild step, with the stale-bindings clean-rebuild documented.

## Key facts (this project)

- **csproj**: `05.Presentation/MartialHeroes.Client.Godot/MartialHeroes.Client.Godot.csproj`
  (SDK `Godot.NET.Sdk/4.6.3`, `TargetFramework net10.0`, `LangVersion 14.0`, `EnableDynamicLoading`).
- **dotnet**: `C:/Program Files/dotnet/dotnet.EXE` (a .NET 10 SDK).
- It references layer 04 (`Client.Application`, `Client.Infrastructure`) and layer 03
  (`Assets.Mapping`); everything else (Network.*, Assets.Vfs/Parsers, Client.Domain) arrives
  transitively. A build error there can originate downstream — read the full error, it names the file.
- Godot generates C# **bindings/glue** (in `.godot/mono/`) for the engine API. After upgrading the
  Godot SDK, editing `project.godot`, or touching things the generator keys off, those can go stale
  and produce phantom errors on Godot types (`CS0103`, "The type or namespace 'Godot' …").

## Steps

1. **Normal build** — fast, incremental. Use the bundled helper (it pins the csproj + dotnet path):
   ```
   pwsh -File ${CLAUDE_SKILL_DIR}/scripts/build_godot.ps1
   ```
   or directly:
   ```
   "C:/Program Files/dotnet/dotnet.EXE" build 05.Presentation/MartialHeroes.Client.Godot/MartialHeroes.Client.Godot.csproj
   ```
   Read the output. `Build succeeded` with `0 Error(s)` → done; proceed to a headless run / screenshot.
2. **On compiler errors**, fix them in the source. Note: errors can come from a referenced layer
   (the file path in the error tells you which). Two project-specific traps to recognise:
   - `CS0234`/wrong-type resolution on bare `Input.`, `Environment.`, `Time.` inside a
     `MartialHeroes.Client.Godot.*` namespace — the bare name binds to the sibling project namespace,
     not the Godot class. Fix by fully qualifying: `global::Godot.Input`, `global::Godot.Environment`,
     `global::Godot.Time`. (This is a source fix, not a build-flag issue.)
   - References to `GltfDocument.AppendFromBuffer` — banned in this project (native crash); use the
     `ArrayMesh` builders instead. Not a compiler error, but flag it if you see it.
3. **Clean rebuild — when bindings look stale.** If you get errors on Godot types that *should* exist
   (the API is real, the editor is happy, but `dotnet build` disagrees), the generated glue is stale.
   Run the helper with `-Clean`, which removes the generated mono glue and rebuilds:
   ```
   pwsh -File ${CLAUDE_SKILL_DIR}/scripts/build_godot.ps1 -Clean
   ```
   It deletes `05.Presentation/MartialHeroes.Client.Godot/.godot/mono/` and `bin/`+`obj/`, then
   rebuilds. (`.godot/` is editor cache and gitignored — deleting it is safe; the editor and the next
   build regenerate it.) If the glue files themselves are missing, open the project once in the Godot
   editor to regenerate them, then build again.
4. Report: succeeded/failed, error count, and (on failure) the first few errors with file:line.

## Hard rules

- Build the **Godot csproj**, not the whole `.slnx`, for this loop — it is faster and is what the
  engine actually loads. (Use `/dotnet-build-test` for full-solution / test runs.)
- Never delete anything OUTSIDE `.godot/`, `bin/`, `obj/` during a clean — those are the only safe-to-
  nuke generated dirs. Never touch committed sources or `project.godot` to "fix" a build.
- A green build proves it *compiles*, not that it *renders* — pair with `/godot-run-headless`
  (loads cleanly) and `/godot-screenshot` (looks right).
