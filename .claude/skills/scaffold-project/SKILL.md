---
name: scaffold-project
description: Use to SCAFFOLD .NET projects and their dependency graph in the MartialHeroes solution — one skill, five modes. LAYER-PROJECT adds a new class library into the correct numbered layer folder (01–04), registers it in MartialHeroes.slnx with canonical csproj props, and applies only legal downward ProjectReferences. GENERATOR-PROJECT adds a Roslyn source-generator into 00.SourcesGenerators (netstandard2.0, analyzer-packaged) and wires it into a layer-01–04 consumer as an OutputItemType=Analyzer ProjectReference. TOOLS-PROJECT adds a promoted Tools/ console/library project (net10.0) that may reference DOWN into the core but is never referenced BY it. TEST-PROJECT adds an xUnit MartialHeroes.<Project>.Tests under tests/, registers it in the slnx /Tests/ folder, references the SUT, and writes one passing smoke test. WIRE-REFERENCES stamps the intended ProjectReference graph onto existing projects, verifies it is acyclic + downward-only with check_dag.py, and builds to confirm. All enforce the downward-only DAG (lower layers never reference higher; no engine pulled into 00–04; generators flow IN as analyzers only; Tools sit ABOVE the core and are never referenced back; transport is .Pipelines not .Pipe).
allowed-tools: Read Write Bash(dotnet new *) Bash(dotnet sln *) Bash(dotnet add *) Bash(dotnet build *) Bash(dotnet test *) Bash(dotnet list *) Bash(python *)
model: sonnet
effort: medium
---

# scaffold-project — scaffold .NET projects + wire the dependency DAG

One skill for the five scaffolding jobs of the MartialHeroes solution. All share one invariant: the
solution is a **downward-only, acyclic DAG** of numbered layers — lower numbers must never reference
higher, the core (01–04) stays engine-free (`no using Godot;`), and the transport project is
`Network.Transport.Pipelines` (NEVER `.Pipe`; disk reality wins over stale blueprint text). Two tiers
sit OUTSIDE the 01→05 layer stack: **`00.SourcesGenerators`** (Roslyn generators that flow INTO layers
01–04 as compile-time analyzers — a generator is referenced *by* a layer, never the reverse) and
**`Tools/`** (promoted standalone executables/libraries that may reference DOWN into the core but are
never referenced BY it — they sit ABOVE the DAG, like tests).

| Mode | Adds | slnx folder | Verifier |
|---|---|---|---|
| **LAYER-PROJECT** | a class library in layer 01–04 | `/NN.LayerFolder/` | downward-ref pre-check → `check_dag.py` (caller) |
| **GENERATOR-PROJECT** | a source generator in 00 + an analyzer ref into a consumer | `/00.SourcesGenerators/` | `dotnet build` of the consumer (generator runs) |
| **TOOLS-PROJECT** | a promoted console/lib under `Tools/` | `/Tools/` | `dotnet build` (downward refs only) |
| **TEST-PROJECT** | an xUnit `…​.Tests` under `tests/` | `/Tests/` | `dotnet test` (one smoke test) |
| **WIRE-REFERENCES** | the intended ProjectReference edges | — | `check_dag.py` + `dotnet build` |

The numbered layers (lower never references higher; within 02/03 a strict sub-order holds):

| # | Disk folder | Projects (prefix `MartialHeroes.`) |
|---|---|---|
| 00 | `00.SourcesGenerators` | the Roslyn source-generator projects (netstandard2.0 analyzers; flow INTO 01–04, never out) |
| 01 | `01.Infrastructure.Shared` | `Shared.Kernel`, `Shared.Diagnostics` |
| 02 | `02.Network.Layer` | `Network.Abstractions`, `Network.Protocol`, `Network.Crypto`, `Network.Transport.Pipelines` |
| 03 | `03.Storage.Assets` | `Assets.Vfs`, `Assets.Parsers`, `Assets.Mapping` |
| 04 | `04.Client.Core` | `Client.Domain`, `Client.Application`, `Client.Infrastructure` |
| 05 | `05.Presentation` | `Client.Godot` — special-cased; **never created/wired by this skill** (Godot generates its own csproj — see `godot-scene-author`'s bootstrap mode) |
| — | `Tools/` | promoted standalone tools (net10.0 console/lib; sit ABOVE the core — reference DOWN only, never referenced back) |

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

## Mode D — GENERATOR-PROJECT (Roslyn source generator in layer 00)

Add a source generator under `00.SourcesGenerators` and wire it INTO a layer-01–04 consumer as a
compile-time analyzer. A generator is a `netstandard2.0` analyzer assembly: it is **referenced by** the
core, never references it (the dependency arrow points UP into 00 from the consumer's compile, and 00
itself depends on nothing in 01–05). This keeps layer 00 cleanly outside the runtime DAG.

**Hard rules:** generator csproj targets `netstandard2.0` (the Roslyn analyzer requirement — NOT
net10.0); it carries `<EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>` and the
analyzer packages; it has **no** runtime `ProjectReference` into 01–05; the **consumer** references it
with `OutputItemType="Analyzer" ReferenceOutputAssembly="false"` so the generator runs at compile but
ships nothing. Never let a generator reference a core layer (that inverts the arrow and breaks 00's
isolation) — generators receive their input via the consumer's syntax tree, not project refs.

1. **Gather inputs:** the generator name `MartialHeroes.<Suffix>.Generators` (or the established naming),
   and the **one** consumer layer project it feeds (e.g. `Network.Protocol` for opcode→handler routing,
   `Shared.Diagnostics` for `[LoggerMessage]`-adjacent gen). One generator → its consumer; if it must
   feed several, wire each analyzer edge explicitly.
2. **Create:**
   `dotnet new classlib --name MartialHeroes.<Suffix>.Generators --output "00.SourcesGenerators/MartialHeroes.<Suffix>.Generators" --framework netstandard2.0`
3. **Overwrite the csproj** with the canonical analyzer shape:
   ```xml
   <Project Sdk="Microsoft.NET.Sdk">

       <PropertyGroup>
           <TargetFramework>netstandard2.0</TargetFramework>
           <ImplicitUsings>enable</ImplicitUsings>
           <Nullable>enable</Nullable>
           <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
           <IsRoslynComponent>true</IsRoslynComponent>
       </PropertyGroup>

       <ItemGroup>
           <PackageReference Include="Microsoft.CodeAnalysis.CSharp" PrivateAssets="all" />
           <PackageReference Include="Microsoft.CodeAnalysis.Analyzers" PrivateAssets="all" />
       </ItemGroup>

   </Project>
   ```
4. **Wire the analyzer edge into the consumer** — add to the consumer csproj (NOT via `dotnet add
   reference`, which omits the analyzer attributes; use a precise Write):
   ```xml
   <ProjectReference Include="../../00.SourcesGenerators/MartialHeroes.<Suffix>.Generators/MartialHeroes.<Suffix>.Generators.csproj"
                     OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
   ```
5. **Register in the slnx** under a `<Folder Name="/00.SourcesGenerators/">` (create it if missing,
   ordered first): `dotnet sln MartialHeroes.slnx add "00.SourcesGenerators/…​.csproj"`; re-Read +
   confirm placement; fix precisely if drifted.
6. **Build the consumer** to prove the generator loads and runs (a generator failure surfaces as a
   consumer build error/warning): `dotnet build "NN.LayerFolder/MartialHeroes.<Consumer>/…​.csproj"`.
7. **Report** the generator csproj, the analyzer edge applied, the slnx folder, and the build result.
   Note for the caller: `check_dag.py` treats `OutputItemType="Analyzer"` edges as a 00-inbound edge,
   not a runtime upward edge — so it stays green.

## Mode E — TOOLS-PROJECT (promoted standalone tool)

Add a promoted `Tools/` project — a net10.0 console app or library that consumes the core (parsers,
codegen drivers, the vfs harness, a DAG-style utility) but is **never referenced back by it**. Tools
sit ABOVE the layer DAG exactly like tests: they may reference DOWN into any core layer 01–04, but no
01–05 project may reference a Tool.

**Hard rules:** location `Tools/MartialHeroes.<Suffix>/` (NOT a numbered layer); name
`MartialHeroes.<Suffix>` (or the established Tools convention); a console tool uses `--name … console`
with `<OutputType>Exe</OutputType>`, a library tool stays a classlib; references point only DOWNWARD
into 01–04 (never into 05/Godot, never another Tool unless that Tool is itself lower); registered under
a `/Tools/` slnx folder; **no core layer may ever reference a Tool** (that would drag a tool into the
runtime DAG — refuse and report).

1. **Gather inputs:** the name `MartialHeroes.<Suffix>`, whether it is a **console** (Exe) or a
   **library**, and the core projects it consumes (downward into 01–04 only).
2. **Create** — console:
   `dotnet new console --name MartialHeroes.<Suffix> --output "Tools/MartialHeroes.<Suffix>" --framework net10.0`
   (library: swap `console` for `classlib`, drop `<OutputType>`).
3. **Normalize the csproj** to the minimal shape (console keeps `<OutputType>Exe</OutputType>`):
   ```xml
   <Project Sdk="Microsoft.NET.Sdk">

       <PropertyGroup>
           <OutputType>Exe</OutputType>
           <TargetFramework>net10.0</TargetFramework>
           <ImplicitUsings>enable</ImplicitUsings>
           <Nullable>enable</Nullable>
       </PropertyGroup>

   </Project>
   ```
4. **Add the downward ProjectReferences** (one `dotnet add … reference …` per edge into 01–04). Validate
   each target is a core layer, NOT layer 05 and NOT a higher-or-equal Tool; reject upward/Godot edges.
5. **Register in the slnx** under a `<Folder Name="/Tools/">` (create it sibling to `/Tests/` if
   missing): `dotnet sln MartialHeroes.slnx add "Tools/…​.csproj"`; re-Read + confirm placement; fix
   precisely if drifted.
6. **Build:** `dotnet build "Tools/MartialHeroes.<Suffix>/…​.csproj"`.
7. **Report** the Tool csproj, the slnx folder, the downward edges, and the build result. Remind the
   caller `check_dag.py` allows Tools to reference DOWN but flags any core→Tool edge as upward.

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

   **Layer 00 + Tools handling** (so the checker stays correct beyond 01–04):
   - **`00.SourcesGenerators`** edges are recognized by `OutputItemType="Analyzer"` (and/or
     `ReferenceOutputAssembly="false"`): a *consumer → generator* analyzer edge is a legal **00-inbound**
     edge (compile-time, not runtime), NOT counted as an upward reference. Conversely, a **generator →
     any 01–05 layer** edge is illegal (it inverts the arrow / breaks 00's isolation) and must be
     flagged. Layer 00 itself must reference nothing in 01–05.
   - **`Tools/`** projects sit ABOVE the DAG like `tests/`: a *Tool → 01–04* edge is legal (downward); a
     **Tool → layer 05/Godot** edge or a **Tool → Tool** upward edge is flagged; and **any core (01–05)
     → Tool** edge is an illegal upward reference. Tools and tests are excluded from the core acyclic-DAG
     intended-edge table but still pass the no-`.Pipe`, no-upward, no-cycle assertions.
   If the bundled `check_dag.py` predates these tiers and misclassifies an analyzer or Tool edge, that is
   a checker bug — report it to `tooling-engineer` (who owns the script); do not weaken a real edge to
   silence it.
4. **Build to confirm:** `dotnet build MartialHeroes.slnx`.
5. **Report drift, if any:** edges added, the checker verdict, the build result. If `check_dag.py`
   flags an UNEXPECTED/upward/cyclic edge you did not add (pre-existing drift), list it and recommend
   removal — never silently edit unrelated references.

## Decision points (all modes)

- **A layer-05 / Godot request** → hand off to `godot-scene-author`'s bootstrap mode and stop; Godot
  generates its own csproj.
- **A generator targeting net10.0** → wrong; a Roslyn analyzer must be `netstandard2.0`. Fix the TFM
  before wiring (Mode D).
- **Asked to make a generator reference a core layer, or a core layer to reference a Tool** → refuse;
  both invert the arrow (generators flow IN as analyzers; Tools sit ABOVE and are never referenced back).
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
- **Mode D:** generator csproj at `00.SourcesGenerators/…`, `netstandard2.0` + `IsRoslynComponent`, no
  runtime ref into 01–05; the consumer carries an `OutputItemType="Analyzer" ReferenceOutputAssembly="false"`
  edge; slnx `<Project>` inside `/00.SourcesGenerators/`; the consumer builds with the generator running.
- **Mode E:** Tool csproj at `Tools/…` (not a numbered layer); references point only DOWN into 01–04
  (no Godot, no upward Tool); no core layer references it; slnx `<Project>` inside `/Tools/`; `dotnet
  build` succeeds.
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
- **Never** target a source generator at `net10.0` — it must be `netstandard2.0`, or Roslyn won't load it.
- **Never** add a runtime `ProjectReference` from a generator into a core layer, nor a `dotnet add
  reference` (plain) for an analyzer edge — that drops `OutputItemType="Analyzer"` and ships the
  generator as a runtime dependency.
- **Never** let a core layer (01–05) reference a `Tools/` project, nor a Tool reference layer 05/Godot —
  Tools sit ABOVE the DAG (reference DOWN only).
- **Never** create or wire a layer-05/Godot project here; **never** run `git` or IDA tooling.

> North star: serves **N2** — a clean downward-DAG, engine-free, headlessly-testable core is the
> substrate the faithful 1:1 client port stands on.
