---
name: godot-csproj-bootstrap
description: Use once a C# script is attached in Godot and MartialHeroes.Client.Godot.csproj has been generated, to normalize its TFM/SDK, wire ONLY Client.Application and Assets.Mapping, register it in the slnx /05.Presentation/ folder, and keep .godot/ ignored.
allowed-tools: Read Write Bash(dotnet *)
model: sonnet
effort: medium
---

# godot-csproj-bootstrap

The Godot client at `05.Presentation/MartialHeroes.Client.Godot/` has a
`project.godot` but no `.csproj` yet — Godot 4.6 generates one the first time a C#
script is attached and a C# build is triggered from the editor. Once that generated
csproj exists, run this skill to normalize it to the project's conventions, give it
its two legal downward references, and register it in the solution (its
`/05.Presentation/` slnx folder is currently empty).

Layer 05 is the only project NOT handled by `new-layer-project` / `wire-references`,
precisely because Godot owns the csproj's creation. This skill adopts that generated
file rather than fighting it.

## What "normalized" means here

1. **SDK / TFM.** Godot emits `<Project Sdk="Godot.NET.Sdk/4.x.y">` with
   `<TargetFramework>net8.0</TargetFramework>` (or whatever the editor default is).
   Keep the **`Godot.NET.Sdk`** (required — do not swap it for `Microsoft.NET.Sdk`),
   set `<TargetFramework>net10.0</TargetFramework>` to match the rest of the solution,
   and ensure `<EnableDynamicLoading>true</EnableDynamicLoading>` (Godot needs it).
   Add `<Nullable>enable</Nullable>` and `<ImplicitUsings>enable</ImplicitUsings>` to
   match repo style.
2. **References — ONLY two.** The presentation layer may reference exactly
   `MartialHeroes.Client.Application` and `MartialHeroes.Assets.Mapping`, and nothing
   else from the core. It must NOT reference `Network.*`, `Assets.Vfs`,
   `Assets.Parsers`, `Client.Domain`, `Client.Infrastructure`, or any `Shared.*`
   project directly — those arrive transitively. Godot is a passive renderer with zero
   game-rule authority.
3. **slnx registration.** Add the project under the (currently empty)
   `<Folder Name="/05.Presentation/">` in `MartialHeroes.slnx`.
4. **.godot/ stays ignored.** The `.godot/` editor cache and Godot build artefacts are
   gitignored; never stage or reference them. The `.mono`/`obj` build outputs are
   likewise transient.

## Hard rules

1. The csproj MUST already exist (Godot generated it). If it does not, STOP and tell
   the user to attach a C# script in the Godot editor and trigger a build first — do
   NOT hand-fabricate a Godot csproj from scratch.
2. Preserve the `Godot.NET.Sdk` SDK reference and its version exactly as Godot wrote
   it; only change the TFM and add the conventional properties.
3. Exactly two ProjectReferences: `Client.Application` and `Assets.Mapping`. No more.
4. Never add `using Godot;` to, or reference Godot from, any layer 01-04 project. The
   engine boundary is one-directional and stops at layer 05.

## Steps

1. **Confirm the csproj exists.**

   ```powershell
   dotnet --version   # sanity: SDK present
   ```

   Verify `05.Presentation/MartialHeroes.Client.Godot/MartialHeroes.Client.Godot.csproj`
   is on disk (Glob/Read). If absent, stop with the instruction in rule 1.

2. **Read** the generated csproj to capture the exact `Godot.NET.Sdk/<version>` string
   and whatever Godot put in `PropertyGroup`.

3. **Rewrite the csproj** to the normalized shape, keeping Godot's SDK version. Target:

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

   Substitute the real `Godot.NET.Sdk/<version>` you read in step 2 (do not hard-code
   4.6.0 if Godot wrote a different patch). Use backslash relative paths to match how
   Godot/MSBuild write `Include` on Windows; forward slashes also build.

4. **Or apply references via CLI** instead of hand-writing the ItemGroup (either is
   fine; CLI is less error-prone for the relative paths):

   ```powershell
   dotnet add "05.Presentation/MartialHeroes.Client.Godot/MartialHeroes.Client.Godot.csproj" reference "04.Client.Core/MartialHeroes.Client.Application/MartialHeroes.Client.Application.csproj"
   dotnet add "05.Presentation/MartialHeroes.Client.Godot/MartialHeroes.Client.Godot.csproj" reference "03.Storage.Assets/MartialHeroes.Assets.Mapping/MartialHeroes.Assets.Mapping.csproj"
   ```

5. **Register in the slnx** under `/05.Presentation/`:

   ```powershell
   dotnet sln MartialHeroes.slnx add "05.Presentation/MartialHeroes.Client.Godot/MartialHeroes.Client.Godot.csproj"
   ```

   Re-Read `MartialHeroes.slnx` and confirm the `<Project Path=...>` now sits inside
   `<Folder Name="/05.Presentation/">` (which was previously self-closing/empty), with a
   forward-slash path. If `dotnet sln add` left the folder self-closed or misplaced the
   entry, fix the slnx with a precise Write: change
   `<Folder Name="/05.Presentation/" />` into an open/close pair containing the project,
   leaving every other line verbatim.

6. **Confirm .godot/ is ignored.** Check `.gitignore` already excludes `.godot/` (and
   Godot's `*.mono`/`obj` outputs). If `.godot/` is not covered, add it. Never stage
   that directory.

7. **Build the Godot project** to confirm the SDK and references resolve:

   ```powershell
   dotnet build "05.Presentation/MartialHeroes.Client.Godot/MartialHeroes.Client.Godot.csproj"
   ```

   (Building the single project avoids needing the Godot editor; the Godot.NET.Sdk
   restores its own props.)

8. **Report** the normalized SDK/TFM, the two references applied (and an explicit note
   that no other core references were added), the slnx folder it now lives in, and the
   build result.

## Decision points

- **If the csproj does not exist** → STOP (rule 1); tell the user to attach a C# script in the editor
  and trigger a build. Never hand-fabricate a Godot csproj.
- **If Godot wrote a different `Godot.NET.Sdk/<patch>`** → keep that exact version; only change the TFM
  to `net10.0` and add the conventional props. Never swap to `Microsoft.NET.Sdk`.
- **If a tempting extra reference appears** (`Network.*`, `Assets.Vfs`/`Parsers`, `Client.Domain`/
  `Infrastructure`, any `Shared.*`) → REFUSE it; those arrive transitively. Exactly two refs.
- **If `dotnet sln add` self-closes or misplaces the `/05.Presentation/` folder** → fix the slnx with a
  precise Write, leaving every other line verbatim (step 5).

## Verify / Done when

- SDK is `Godot.NET.Sdk/<Godot's version>`, TFM `net10.0`, `EnableDynamicLoading` true; exactly two
  ProjectReferences (`Client.Application` + `Assets.Mapping`); the project sits inside
  `<Folder Name="/05.Presentation/">` with a forward-slash path; `.godot/` is gitignored; and the
  single-project `dotnet build` succeeds.

*North star: N2 — wiring layer 05 as a strictly-passive, two-reference renderer keeps the 1:1 client's
engine boundary one-directional (no game-rule authority leaks upward).*

## Do not

- Do not create the csproj if Godot hasn't generated it.
- Do not add any reference beyond `Client.Application` + `Assets.Mapping`.
- Do not commit or reference `.godot/`.
- Do not run `git` or IDA tooling.
