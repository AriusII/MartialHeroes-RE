---
name: csharp-tooling
description: Use when building, validating, or extending the Martial Heroes C# Tools/ projects or 00.SourcesGenerators — dev harnesses, codegen, the vfs explorer — on the downward DAG, engine-free.
allowed-tools: Read Write Edit Grep Glob Bash(dotnet *)
model: sonnet
effort: high
---

# csharp-tooling — build/validate/extend the C# Tools/ + generators

The single home for working ON the project's C# *tooling tier*: the promoted `Tools/` projects
(net10.0 console/lib harnesses — vfs explorer, codegen drivers, asset dumpers, validators) and
**`00.SourcesGenerators`** (the Roslyn source generators, netstandard2.0 analyzers). This is the
`tooling-engineer`'s working procedure for O5. It does NOT scaffold *new* projects (that is
`scaffold-project`, Modes GENERATOR-PROJECT / TOOLS-PROJECT) and it is NOT the build/test gate for the
core (that is `dotnet-build-test`). Use it to **change** existing tooling code and prove it still builds,
runs, and produces correct output — on the DAG, engine-free, clean-room.

## Where the tooling tier sits (two tiers OUTSIDE the 01→05 layer stack)

| Tier | Disk | TFM | DAG rule |
|---|---|---|---|
| `00.SourcesGenerators` | `00.SourcesGenerators/` | netstandard2.0 (analyzer) | flows INTO layers 01–04 as `OutputItemType=Analyzer` — referenced *by* a layer, **never** the reverse; never references a layer |
| `Tools/` | `Tools/` | net10.0 | sits ABOVE the core — references DOWN into 00–04 only, **never** referenced back; like tests |

Both are **engine-free** (no `using Godot;`, no Godot package) and **clean-room** (read only committed
`Docs/RE/` specs; cite every magic constant `// spec: …`; never paste decompiler output).

## Ground truth

IDA / `doida.exe` is the single absolute truth; the committed `Docs/RE/` specs are its derived truth
and the **only** thing this tooling may encode. A generator that emits a wire-struct/opcode table, or a
harness that parses a VFS format, reproduces what a spec proved — never a fact pulled from memory. A
missing fact is NOT invented here: route it back to the RE domain. For *rendered* output a tool may
produce, the captures are the pixel oracle, but tooling normally governs neither behavior nor pixels —
it serves them.

## Procedure (read need → implement on the DAG → build → run → validate)

1. **Read the need + the existing project.** Identify the exact target: a `Tools/` harness or a
   `00.SourcesGenerators` generator. `Read` its csproj (TFM, references, analyzer packaging) and the
   code you will touch. `Grep` for the spec(s) it already cites so your change stays consistent. Never
   guess a project's references — read them.
2. **Implement strictly on the DAG, engine-free, clean-room.**
   - **Generator (00):** keep it `netstandard2.0`, analyzer-packaged
     (`<IsRoslynComponent>true</IsRoslynComponent>` / packed as an analyzer), and **pure compile-time**
     — it reads its consumer's syntax/spec input and emits source; it must NOT reference any layer
     project (that would invert the flow). New emitted constants carry a `// spec:` provenance comment.
   - **Tools harness:** net10.0; reference **down** into the core (`Assets.Vfs`, `Assets.Parsers`,
     `Network.Protocol`, …) only — never add a reference *from* the core *to* a tool, and never pull
     Godot in. Reuse the real core parsers rather than re-implementing a format inside the tool.
   - **Resolver / no hardcoded paths (see below).**
3. **Build.** Scope tight to iterate fast, then confirm in the solution:
   ```powershell
   dotnet build Tools/MartialHeroes.<Tool>/MartialHeroes.<Tool>.csproj
   dotnet build 00.SourcesGenerators/MartialHeroes.<Gen>/MartialHeroes.<Gen>.csproj
   ```
   For a generator the real proof is the **consumer building** (the generator runs at the consumer's
   compile): `dotnet build 02.Network.Layer/.../MartialHeroes.Network.Protocol.csproj`. Read the
   summary line; quote the first `error CSxxxx` with `file(line,col)` if it fails. (For the
   whole-solution gate, hand to `dotnet-build-test`.)
4. **Run the tool / exercise the harness** and capture output:
   ```powershell
   dotnet run --project Tools/MartialHeroes.<Tool>/MartialHeroes.<Tool>.csproj -- <args>
   ```
   For a generator, inspect the emitted source (the `obj/.../generated/` tree, or a snapshot test) to
   confirm the right code came out.
5. **Validate the output against the spec/expected.** A vfs-explorer dump must match the format doc's
   field layout; a codegen table must match `opcodes.md` / the packet YAMLs; record counts/offsets must
   match the recovered chains (e.g. `npc{tag}.arr` 28-byte, `mob{tag}.arr` 20-byte records). If the
   tool's output disagrees with the spec, the **tool** is wrong (unless the spec is what IDA just
   disproved — then it is an RE-domain escalation, not a quiet tool patch). Report the exact command,
   the verdict, and any drift.

## DAG / engine-free / clean-room rules (non-negotiable)

- **Downward-only.** A `Tools/` project references DOWN into 00–04 and is **never** referenced back; a
  generator flows IN to 01–04 as an analyzer and **never** references a layer. No upward/sideways edge
  "just to compile" — if you need one, the design is wrong; escalate.
- **Engine-free.** No `using Godot;`, no Godot package, in any tool or generator. Tooling never depends
  on layer 05.
- **Clean-room.** Read only committed `Docs/RE/` specs; never read `_dirty/`; never paste IDA/Hex-Rays
  pseudo-C (`sub_`/`loc_`/`_DWORD`/`__thiscall`/mangled names/addresses); never emit copyrighted bytes.
  Every emitted/encoded magic constant cites its source spec (`// spec: Docs/RE/...`).
- **Transport naming:** it is `Network.Transport.Pipelines`, never `.Pipe` (disk reality wins).

## Resolver / no-absolute-path rule

A tool that touches the real client VFS **must resolve the path via the project resolver**
(`Dev/ClientPathResolver.cs` order: `MH_CLIENT_DIR` env → `client_dir.cfg` → project-local
`clientdata/` → external installs) — **never** hardcode `D:/MartialHeroesClient`, a `C:/...Godot...exe`,
or any machine-specific path. Take the VFS/output path as an argument or read the resolver; the
originals are user-supplied and gitignored, so a baked-in path breaks on every other machine and risks
committing a path to a non-distributable asset. Outputs go to an argument-supplied or temp dir, never a
hardcoded absolute.

## Relationship to neighbouring skills

- **`scaffold-project`** *creates* the projects — Mode **GENERATOR-PROJECT** (a new generator in
  `00.SourcesGenerators` + the analyzer ref into a consumer) and Mode **TOOLS-PROJECT** (a new `Tools/`
  console/lib). Use it to add a project; use **this** skill to build/validate/extend one that exists. If
  the work needs a brand-new project, hand the scaffolding to `scaffold-project` first, then return here.
- **`dotnet-build-test`** is the canonical whole-solution build/test gate. This skill builds the *tool*
  tightly to iterate; for the authoritative solution-wide verdict (and any xUnit run), defer to it.
- **`python-tooling`** is the sibling for the Python scripts/harnesses (`check_dag.py`, codegen
  drivers). When a change spans both C# tooling and a Python driver, this skill owns the C# half only.

## Decision points

- **Need a project that doesn't exist** → `scaffold-project` (GENERATOR/TOOLS mode), then come back.
- **Generator won't run** → build the **consumer**, not just the generator project (it runs at the
  consumer's compile); a green generator build alone proves nothing.
- **Tool output disagrees with a spec** → the tool is wrong; fix it. If you suspect the *spec* is wrong,
  STOP — that is an IDA/RE-domain question, not a tool patch.
- **Tempted to hardcode a VFS/exe path** → use the resolver or an argument instead, always.
- **Whole-solution / test verdict wanted** → hand to `dotnet-build-test`.

## Verify / Done when

- [ ] The target builds (tool csproj green; generator proven via the **consumer** building).
- [ ] The tool runs / the generator emits the expected source, and the **output is validated** against
      the cited spec/expected (command quoted).
- [ ] DAG intact: downward-only refs, no engine pulled into tooling, generator never references a layer.
- [ ] Clean-room intact: no decompiler output, every magic constant `// spec:`-cited; no `_dirty/` read.
- [ ] No hardcoded absolute/machine paths — the resolver or an argument supplies every path.

## Pitfalls (anti-patterns)

- **Never** add an upward/sideways edge or pull Godot into a tool/generator to make it compile.
- **Never** make a generator reference a layer project (it inverts the analyzer flow).
- **Never** hardcode `D:/MartialHeroesClient`, a Godot exe path, or any machine-specific path.
- **Never** paste decompiler pseudo-C or read `_dirty/`; never emit a constant without a `// spec:` cite.
- **Never** claim a generator works off a green generator-only build — prove it via the consumer.
- **Never** scaffold a NEW project here (that is `scaffold-project`) or run the solution test gate here
  (that is `dotnet-build-test`).

> North star: serves **N1 + N2** — sharp, DAG-clean, clean-room tooling (the generators that emit the
> wire/asset tables, the harnesses that validate the VFS) keeps the map honest and the faithful 1:1
> port's substrate correct.
