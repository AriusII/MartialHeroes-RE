---
name: wire-references
description: Use to apply the intended ProjectReference graph onto the existing placeholder MartialHeroes projects, verify it is acyclic and downward-only, build to confirm, and report any drift.
allowed-tools: Read Write Bash(dotnet add *) Bash(dotnet list *) Bash(dotnet build *)
model: sonnet
effort: medium
---

# wire-references

The 12 core class libraries ship with no `ProjectReference`s. This skill stamps the
*exact* intended dependency graph onto them, proves the graph is acyclic and strictly
downward, then builds the solution to confirm nothing broke. Use it after the projects
exist (they already do) but before writing real cross-project code.

## The intended edges (the single source of truth)

Lower-numbered layers must never reference higher ones. Within layer 02/03 there is
also a strict sub-order. These are the ONLY edges allowed:

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

> The transport project is `Network.Transport.Pipelines`, never `.Pipe`. `check_dag.py`
> will fail loudly if a `.Pipe`-named project or path ever appears.

## Hard rules

1. Apply only the edges in the table — no extras, no shortcuts (e.g. `Application`
   must NOT reference `Network.Protocol` or `Network.Crypto` directly; it only knows
   `Network.Abstractions`).
2. `Client.Godot` (layer 05) is intentionally absent from this skill — its wiring is
   owned by `godot-csproj-bootstrap`.
3. Never introduce an upward edge (higher layer -> lower is allowed; the reverse and
   sideways-out-of-order are forbidden) and never create a cycle.

## Steps

1. **Read each csproj** you are about to touch to see what (if any) references already
   exist, so you do not add duplicates.

2. **Apply each missing edge** with one CLI call per reference. Path templates:

   ```powershell
   dotnet add "02.Network.Layer/MartialHeroes.Network.Transport.Pipelines/MartialHeroes.Network.Transport.Pipelines.csproj" reference "02.Network.Layer/MartialHeroes.Network.Abstractions/MartialHeroes.Network.Abstractions.csproj"
   dotnet add "03.Storage.Assets/MartialHeroes.Assets.Parsers/MartialHeroes.Assets.Parsers.csproj" reference "03.Storage.Assets/MartialHeroes.Assets.Vfs/MartialHeroes.Assets.Vfs.csproj"
   dotnet add "03.Storage.Assets/MartialHeroes.Assets.Mapping/MartialHeroes.Assets.Mapping.csproj" reference "03.Storage.Assets/MartialHeroes.Assets.Parsers/MartialHeroes.Assets.Parsers.csproj"
   dotnet add "02.Network.Layer/MartialHeroes.Network.Abstractions/MartialHeroes.Network.Abstractions.csproj" reference "01.Infrastructure.Shared/MartialHeroes.Shared.Kernel/MartialHeroes.Shared.Kernel.csproj"
   dotnet add "02.Network.Layer/MartialHeroes.Network.Protocol/MartialHeroes.Network.Protocol.csproj" reference "01.Infrastructure.Shared/MartialHeroes.Shared.Kernel/MartialHeroes.Shared.Kernel.csproj"
   dotnet add "02.Network.Layer/MartialHeroes.Network.Crypto/MartialHeroes.Network.Crypto.csproj" reference "01.Infrastructure.Shared/MartialHeroes.Shared.Kernel/MartialHeroes.Shared.Kernel.csproj"
   dotnet add "04.Client.Core/MartialHeroes.Client.Domain/MartialHeroes.Client.Domain.csproj" reference "01.Infrastructure.Shared/MartialHeroes.Shared.Kernel/MartialHeroes.Shared.Kernel.csproj"
   dotnet add "04.Client.Core/MartialHeroes.Client.Application/MartialHeroes.Client.Application.csproj" reference "04.Client.Core/MartialHeroes.Client.Domain/MartialHeroes.Client.Domain.csproj"
   dotnet add "04.Client.Core/MartialHeroes.Client.Application/MartialHeroes.Client.Application.csproj" reference "02.Network.Layer/MartialHeroes.Network.Abstractions/MartialHeroes.Network.Abstractions.csproj"
   dotnet add "04.Client.Core/MartialHeroes.Client.Infrastructure/MartialHeroes.Client.Infrastructure.csproj" reference "04.Client.Core/MartialHeroes.Client.Application/MartialHeroes.Client.Application.csproj"
   ```

   `Shared.Kernel`, `Shared.Diagnostics`, and `Assets.Vfs` get NO project references.
   The two package-only dependencies (`Shared.Diagnostics`, and `System.IO.Pipelines`
   on the transport project) are not added by this skill unless explicitly asked — it
   governs the *ProjectReference* graph.

3. **Verify the graph with the bundled checker.** It parses every csproj's
   `<ProjectReference>` set, compares it against the intended DAG above, and asserts
   the whole thing is acyclic, downward-only, and free of any `.Pipe` naming:

   ```powershell
   python ${CLAUDE_SKILL_DIR}/scripts/check_dag.py .
   ```

   (A relative `python scripts/check_dag.py .` also works from the skill folder.) The
   first argument is the repo root. Exit code 0 = graph matches; non-zero = drift, and
   the script prints each missing edge, each unexpected/upward/cyclic edge, and any
   `.Pipe` sighting. Do not proceed past a non-zero result — fix the csproj first.

4. **Build to confirm** the wiring compiles end to end:

   ```powershell
   dotnet build MartialHeroes.slnx
   ```

5. **Report drift, if any.** Summarize: edges added, the checker verdict, and the build
   result. If `check_dag.py` flagged extra or upward edges that you did not add (pre-
   existing drift), list them explicitly and recommend removal rather than silently
   editing unrelated references.

## Decision points

- **If a csproj already carries the intended edge**, skip it (idempotent) — do not re-add,
  which produces a duplicate `<ProjectReference>` warning.
- **If `check_dag.py` reports an UNEXPECTED/upward/cyclic edge you did not add**, do NOT
  silently delete it: report it as pre-existing drift and recommend removal, so the
  intentional-vs-accidental distinction is preserved for the caller.
- **If the build fails after wiring**, distinguish a *missing reference* (architecture gap —
  add the legal edge) from a *compile error in code* (not this skill's job — report it).
- **If asked to wire `Network.Protocol`/`Network.Crypto` into `Application`**, refuse:
  Application knows only `Network.Abstractions`. The hidden edge would couple use-cases to
  wire layout and break the seam.

## Verify / Done when

- [ ] Every edge in the table is present; no edge outside it exists.
- [ ] `check_dag.py .` exits 0 (acyclic, downward-only, no `.Pipe` sighting).
- [ ] `dotnet build MartialHeroes.slnx` succeeds.
- [ ] `Shared.Kernel`, `Shared.Diagnostics`, `Assets.Vfs` carry zero `ProjectReference`s.

## Pitfalls (anti-patterns)

- **Never** add an edge not in the table — extras are drift even if they compile.
- **Never** let `Application` reference `Network.Protocol`/`Network.Crypto`.
- **Never** edit unrelated references to make `check_dag.py` pass; fix only the in-scope csproj.
- **Never** trust a green build over a red `check_dag.py` — the DAG checker is authoritative.

> North star: serves **N2** — the exact downward graph keeps the core engine-free and
> headless-reusable, the foundation the faithful 1:1 client port builds on.

## Do not

- Do not wire `Client.Godot` here.
- Do not add `Network.Protocol`/`Network.Crypto` as references of `Client.Application`.
- Do not run `git` or IDA tooling.
