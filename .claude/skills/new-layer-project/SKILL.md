---
name: new-layer-project
description: Use to add a new class library into the correct numbered layer folder of the MartialHeroes solution and register it in MartialHeroes.slnx with canonical csproj props and only legal downward ProjectReferences.
allowed-tools: Read Write Bash(dotnet new *) Bash(dotnet sln *) Bash(dotnet add *)
---

# new-layer-project

Add a new .NET 10 class library to the MartialHeroes solution so it lands in the
right numbered layer folder on disk, is registered in `MartialHeroes.slnx`, carries
the canonical csproj properties, and references only projects that sit *below* it in
the dependency graph (never above, never sideways out of order).

The solution has five physical layer folders, each mirrored by a `<Folder>` in the
slnx. Lower numbers must never reference higher numbers.

| # | Disk folder | Projects (prefix `MartialHeroes.`) |
|---|---|---|
| 01 | `01.Infrastructure.Shared` | `Shared.Kernel`, `Shared.Diagnostics` |
| 02 | `02.Network.Layer` | `Network.Abstractions`, `Network.Protocol`, `Network.Crypto`, `Network.Transport.Pipelines` |
| 03 | `03.Storage.Assets` | `Assets.Vfs`, `Assets.Parsers`, `Assets.Mapping` |
| 04 | `04.Client.Core` | `Client.Domain`, `Client.Application`, `Client.Infrastructure` |
| 05 | `05.Presentation` | `Client.Godot` (see `godot-csproj-bootstrap`; do NOT create with this skill) |

> The on-disk transport project is `Network.Transport.Pipelines` (NOT `.Pipe`). The
> blueprint text is stale here; disk reality wins.

## Hard rules

1. The project name is always `MartialHeroes.<Suffix>` (e.g. `MartialHeroes.Network.Sessions`).
2. The csproj lives at `<NN.LayerFolder>/MartialHeroes.<Suffix>/MartialHeroes.<Suffix>.csproj`.
3. References point only DOWNWARD. A layer-03 project may reference layer-01/02/03
   projects that already exist; it may never reference layer-04/05. Within a layer,
   respect the established sub-order (e.g. `Parsers` -> `Vfs`, never the reverse).
4. Do NOT create a Godot/presentation project here. Layer 05 is special-cased by the
   `godot-csproj-bootstrap` skill because Godot generates its own csproj.
5. Never add package or project references that pull an engine (`Godot`) into layers
   01-04. The core stays engine-free and headlessly testable.

## Steps

1. **Gather inputs.** Confirm with the requester (or infer from the task):
   - the full project name `MartialHeroes.<Suffix>`,
   - the target layer number `NN` and matching folder name from the table above,
   - the set of intended `ProjectReference` targets.

2. **Validate the references are downward-only BEFORE creating anything.** Map each
   intended target to its layer number. Reject (stop and report) if any target sits
   in a higher-numbered layer than the new project, or if it violates the intra-layer
   order. If a requested reference would create an upward or cyclic edge, do not
   proceed — explain the violation instead.

3. **Read** `MartialHeroes.slnx` so you preserve its exact XML shape (one
   `<Folder Name="/NN.LayerFolder/">` per layer; child
   `<Project Path="NN.LayerFolder/MartialHeroes.X/MartialHeroes.X.csproj" />`
   entries with forward slashes, kept alphabetical).

4. **Create the library** with the .NET CLI (this scaffolds a default csproj and a
   placeholder class):

   ```powershell
   dotnet new classlib --name MartialHeroes.<Suffix> --output "NN.LayerFolder/MartialHeroes.<Suffix>" --framework net10.0
   ```

5. **Overwrite the generated csproj** with the canonical shape (the SDK template adds
   extra noise; the repo convention is minimal). Write exactly:

   ```xml
   <Project Sdk="Microsoft.NET.Sdk">

       <PropertyGroup>
           <TargetFramework>net10.0</TargetFramework>
           <ImplicitUsings>enable</ImplicitUsings>
           <Nullable>enable</Nullable>
       </PropertyGroup>

   </Project>
   ```

   Match the 4-space indentation used by every existing csproj in the repo.

6. **Add the legal downward ProjectReferences** (one call per edge), e.g.:

   ```powershell
   dotnet add "NN.LayerFolder/MartialHeroes.<Suffix>/MartialHeroes.<Suffix>.csproj" reference "01.Infrastructure.Shared/MartialHeroes.Shared.Kernel/MartialHeroes.Shared.Kernel.csproj"
   ```

   If the new project is a hot-path member of `Network.*` or `Assets.*`, do not add
   package references that allocate on the data path; keep it `Span<byte>`-friendly.

7. **Register the project in the slnx** under its layer `<Folder>`. Prefer the CLI so
   the slnx XML stays valid:

   ```powershell
   dotnet sln MartialHeroes.slnx add "NN.LayerFolder/MartialHeroes.<Suffix>/MartialHeroes.<Suffix>.csproj"
   ```

   Then re-Read the slnx and confirm the new `<Project Path=...>` sits inside the
   correct `<Folder Name="/NN.LayerFolder/">` block, with a forward-slash path, and is
   alphabetically ordered among its siblings. If `dotnet sln add` placed it wrong or
   the slnx format drifted, fix it with a precise Write that preserves every other
   line verbatim.

8. **Report** the created csproj path, the slnx folder it was registered under, and
   the exact reference edges applied — so the caller can run `wire-references`'
   `check_dag.py` to confirm the global graph is still acyclic and downward-only.

## Do not

- Do not run `dotnet build`, `git`, or any IDA tooling here.
- Do not invent new layer folders or rename existing ones.
- Do not leave the default `Class1.cs` named as-is if the caller specified a real
  first type; otherwise leaving the placeholder is acceptable for a pure scaffold.
