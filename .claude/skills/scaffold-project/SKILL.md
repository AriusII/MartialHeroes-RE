---
name: scaffold-project
description: Use to SCAFFOLD .NET projects and their dependency graph in the MartialHeroes solution — one skill, three modes. LAYER-PROJECT adds a new class library into the correct numbered layer folder (01–04), registers it in MartialHeroes.slnx with canonical csproj props, and applies only legal downward ProjectReferences. TEST-PROJECT adds an xUnit MartialHeroes.<Project>.Tests under tests/, registers it in the slnx /Tests/ folder, references the SUT, and writes one passing smoke test. WIRE-REFERENCES stamps the intended ProjectReference graph onto existing projects, verifies it is acyclic + downward-only with check_dag.py, and builds to confirm. All enforce the downward-only DAG (lower layers never reference higher; no engine pulled into 01–04; transport is .Pipelines not .Pipe).
allowed-tools: Read Write Bash(dotnet new *) Bash(dotnet sln *) Bash(dotnet add *) Bash(dotnet build *) Bash(dotnet test *) Bash(dotnet list *) Bash(python *)
model: sonnet
effort: medium
---

# scaffold-project — scaffold .NET projects + wire the dependency DAG

One skill for the three scaffolding jobs of the MartialHeroes solution. All share one invariant: the
solution is a **downward-only, acyclic DAG** of five numbered layers — lower numbers must never
reference higher, the core (01–04) stays engine-free (`no using Godot;`), and the transport project is
`Network.Transport.Pipelines` (NEVER `.Pipe`; disk reality wins over stale blueprint text).

| Mode | Adds | slnx folder | Verifier |
|---|---|---|---|
| **LAYER-PROJECT** | a class library in layer 01–04 | `/NN.LayerFolder/` | downward-ref pre-check → `check_dag.py` (caller) |
| **TEST-PROJECT** | an xUnit `…​.Tests` under `tests/` | `/Tests/` | `dotnet test` (one smoke test) |
| **WIRE-REFERENCES** | the intended ProjectReference edges | — | `check_dag.py` + `dotnet build` |

The five layers (lower never references higher; within 02/03 a strict sub-order holds):

| # | Disk folder | Projects (prefix `MartialHeroes.`) |
|---|---|---|
| 01 | `01.Infrastructure.Shared` | `Shared.Kernel`, `Shared.Diagnostics` |
| 02 | `02.Network.Layer` | `Network.Abstractions`, `Network.Protocol`, `Network.Crypto`, `Network.Transport.Pipelines` |
| 03 | `03.Storage.Assets` | `Assets.Vfs`, `Assets.Parsers`, `Assets.Mapping` |
| 04 | `04.Client.Core` | `Client.Domain`, `Client.Application`, `Client.Infrastructure` |
| 05 | `05.Presentation` | `Client.Godot` — special-cased; **never created/wired by this skill** (Godot generates its own csproj — see `godot-scene-author`'s bootstrap mode) |

## Mode A — LAYER-PROJECT (new class library)

Add a `MartialHeroes.<Suffix>` class library into its numbered layer, with the canonical minimal
csproj and only legal downward references.

**Hard rules:** project name always `MartialHeroes.<Suffix>`; csproj at
`NN.LayerFolder/MartialHeroes.<Suffix>/MartialHeroes.<Suffix>.csproj`; references point only DOWNWARD
(within a layer respect the sub-order — `Parsers`→`Vfs`, `Mapping`→`Parsers`,
`Transport.Pipelines`→`Abstractions`); never pull an engine (`Godot`) into 01–04; never create a
layer-05 project here.

1. **Gather inputs:** the full name `MartialHeroes.<Suffix>`, the target layer `NN` + folder, and the
   intended `ProjectReference` targets.
2. **Validate references are downward-only BEFORE creating anything.** Map each target to its layer
   number; reject (stop + report) any target in a higher layer or violating the intra-layer order.
   When unclear or boundary-spanning, **stop and ask** — a wrong upward edge is far costlier to unwind
   than a one-line clarification. When in doubt, reference only `Shared.Kernel` (01) — always legal.
3. **Read `MartialHeroes.slnx`** to preserve its exact XML shape (one
   `<Folder Name="/NN.LayerFolder/">` per layer; child `<Project Path="…" />` with forward slashes,
   alphabetical).
4. **Create the library:**
   `dotnet new classlib --name MartialHeroes.<Suffix> --output "NN.LayerFolder/MartialHeroes.<Suffix>" --framework net10.0`
5. **Overwrite the generated csproj** with the canonical minimal shape (4-space indent, no SDK-template noise):
   ```xml
   <Project Sdk="Microsoft.NET.Sdk">

       <PropertyGroup>
           <TargetFramework>net10.0</TargetFramework>
           <ImplicitUsings>enable</ImplicitUsings>
           <Nullable>enable</Nullable>
       </PropertyGroup>

   </Project>
   ```
6. **Add the legal downward ProjectReferences** (one `dotnet add … reference …` call per edge). For a
   hot-path `Network.*`/`Assets.*` member, add no package that allocates on the data path — keep it
   `Span<byte>`-friendly.
7. **Register in the slnx** under its layer `<Folder>`:
   `dotnet sln MartialHeroes.slnx add "NN.LayerFolder/MartialHeroes.<Suffix>/MartialHeroes.<Suffix>.csproj"`,
   then re-Read and confirm the `<Project>` sits inside the correct `<Folder>`, forward-slashed,
   alphabetically ordered. If `dotnet sln add` placed it wrong or the XML drifted, fix with a precise
   Write that preserves every other line verbatim.
8. **Report** the created csproj path, the slnx folder, and the exact edges applied — so the caller
   can run Mode C's `check_dag.py` to confirm the graph stays acyclic/downward-only.

## Mode B — TEST-PROJECT (xUnit smoke project)

Everything below layer 05 is engine-free, so the whole core is headlessly testable with `dotnet test`.
This adds one xUnit project per SUT.

**The convention:** location `tests/MartialHeroes.<Project>.Tests/` (NOT beside the SUT in a numbered
layer); name `MartialHeroes.<Project>.Tests`; framework **xUnit** (the mandated framework); a
`ProjectReference` to the SUT and nothing higher, never `Godot`; registered under a `/Tests/` slnx
folder; exactly one trivial passing smoke test that actually **touches a SUT type** (so a broken
reference fails the build, not `Assert.True(true)`). One test project per SUT — if it exists, STOP and
add cases to it instead.

1. **Resolve the SUT** short name + csproj path; derive `MartialHeroes.<Project>.Tests`.
2. **Create:** `dotnet new xunit --name MartialHeroes.<Project>.Tests --output "tests/MartialHeroes.<Project>.Tests" --framework net10.0`
3. **Normalize the csproj** to the repo's minimal shape, keeping the test packages:
   ```xml
   <Project Sdk="Microsoft.NET.Sdk">

       <PropertyGroup>
           <TargetFramework>net10.0</TargetFramework>
           <ImplicitUsings>enable</ImplicitUsings>
           <Nullable>enable</Nullable>
           <IsPackable>false</IsPackable>
       </PropertyGroup>

       <ItemGroup>
           <PackageReference Include="Microsoft.NET.Test.Sdk" />
           <PackageReference Include="xunit" />
           <PackageReference Include="xunit.runner.visualstudio" />
       </ItemGroup>

   </Project>
   ```
4. **Reference the SUT:**
   `dotnet add "tests/MartialHeroes.<Project>.Tests/MartialHeroes.<Project>.Tests.csproj" reference "NN.LayerFolder/MartialHeroes.<Project>/MartialHeroes.<Project>.csproj"`
5. **Write one smoke test** in `SmokeTests.cs` that touches a public SUT type (delete the template's
   `UnitTest1.cs`); if the SUT still has only the placeholder `Class1`, touch it with a `// TODO` to retarget.
6. **Register in the slnx** under a `/Tests/` `<Folder>` (create it sibling to `/Docs/` if missing):
   `dotnet sln MartialHeroes.slnx add "tests/…​.Tests.csproj"`; re-Read + confirm placement; fix
   precisely if drifted.
7. **Run:** `dotnet test "tests/MartialHeroes.<Project>.Tests/…​.csproj"` to prove green.
8. **Report** the test csproj, the SUT, the slnx folder, and the result (1 passing smoke test).

## Mode C — WIRE-REFERENCES (stamp + verify the DAG)

Apply the *exact* intended ProjectReference graph onto the existing core libraries, prove it acyclic +
downward, then build. The intended edges are the single source of truth — **only** these are allowed:

| Project (layer) | References |
|---|---|
| `Shared.Kernel` (01) | — none — |
| `Shared.Diagnostics` (01) | — none — (packages only: `Microsoft.Extensions.Logging.Abstractions`, `System.Diagnostics.DiagnosticSource`) |
| `Network.Abstractions` (02) | `Shared.Kernel` |
| `Network.Protocol` (02) | `Shared.Kernel` |
| `Network.Crypto` (02) | `Shared.Kernel` |
| `Network.Transport.Pipelines` (02) | `Network.Abstractions` (+ pkg `System.IO.Pipelines`) |
| `Assets.Vfs` (03) | — none — |
| `Assets.Parsers` (03) | `Assets.Vfs` |
| `Assets.Mapping` (03) | `Assets.Parsers` |
| `Client.Domain` (04) | `Shared.Kernel` |
| `Client.Application` (04) | `Client.Domain`, `Network.Abstractions` |
| `Client.Infrastructure` (04) | `Client.Application` |

> `Application` must NOT reference `Network.Protocol`/`Network.Crypto` directly — it knows only
> `Network.Abstractions`. `Client.Godot` (05) is intentionally absent. `check_dag.py` fails loudly on
> any `.Pipe`-named project/path.

1. **Read each csproj** you will touch to avoid duplicate edges.
2. **Apply each missing edge** with one `dotnet add … reference …` per edge (idempotent — skip an edge
   already present). `Shared.Kernel`, `Shared.Diagnostics`, `Assets.Vfs` get NO project references.
3. **Verify the graph:** `python ${CLAUDE_SKILL_DIR}/scripts/check_dag.py .` (first arg = repo root).
   It parses every csproj's `<ProjectReference>` set, compares against the intended DAG, and asserts
   acyclic + downward-only + no `.Pipe` sighting. Exit 0 = match; non-zero prints each missing /
   unexpected / upward / cyclic edge. **Do not proceed past a non-zero result** — fix the csproj first.
4. **Build to confirm:** `dotnet build MartialHeroes.slnx`.
5. **Report drift, if any:** edges added, the checker verdict, the build result. If `check_dag.py`
   flags an UNEXPECTED/upward/cyclic edge you did not add (pre-existing drift), list it and recommend
   removal — never silently edit unrelated references.

## Decision points (all modes)

- **A layer-05 / Godot request** → hand off to `godot-scene-author`'s bootstrap mode and stop; Godot
  generates its own csproj.
- **`dotnet sln add` lands a `<Project>` in the wrong `<Folder>`** (or flattens the XML) → fix with a
  precise Write that preserves every other line, then re-Read.
- **Build fails after wiring** → distinguish a *missing reference* (architecture gap — add the legal
  edge) from a *compile error in code* (not this skill's job — report it).
- **Asked to wire `Network.Protocol`/`Network.Crypto` into `Application`** → refuse; the hidden edge
  couples use-cases to wire layout and breaks the seam.

## Verify / Done when

- **Mode A:** csproj at `NN.LayerFolder/…`, canonical 4-property shape; every `ProjectReference`
  strictly downward; no engine-pulling package in 01–04; the slnx `<Project>` inside the correct layer
  `<Folder>`, forward-slashed, alphabetical.
- **Mode B:** test project under `tests/` (not a numbered layer); references the SUT and nothing
  higher; no Godot reference; slnx `<Project>` inside `/Tests/`; `dotnet test` shows exactly one
  passing test that touches a SUT type.
- **Mode C:** every table edge present, none outside it; `check_dag.py .` exits 0; `dotnet build
  MartialHeroes.slnx` succeeds; `Shared.Kernel`/`Shared.Diagnostics`/`Assets.Vfs` carry zero refs.

## Pitfalls (anti-patterns)

- **Never** add an upward/sideways-out-of-order edge "just to compile" — it breaks the downward-only DAG.
- **Never** name the transport project `.Pipe`; it is `Network.Transport.Pipelines`.
- **Never** leave SDK-template `PropertyGroup` noise; match the minimal repo shape.
- **Never** place a test project in a numbered production layer, reference Godot/a layer above the SUT,
  or write a smoke test that asserts `true` without touching the SUT.
- **Never** trust a green build over a red `check_dag.py` — the DAG checker is authoritative.
- **Never** create or wire a layer-05/Godot project here; **never** run `git` or IDA tooling.

> North star: serves **N2** — a clean downward-DAG, engine-free, headlessly-testable core is the
> substrate the faithful 1:1 client port stands on.
