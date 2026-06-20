# PLAN — CAMPAIGN: STRICT 1:1 RECONSTRUCTION & C#/GODOT EXCELLENCE

> Method & charter. The live run record is `Docs/ROADMAP.md`. This campaign
> specialises `Docs/CAMPAIGN_TEMPLATE.md`. Branch: `major-campaign`.

## 1. Mandate (reformulated)

Turn the codebase into a **strict 1:1 reconstruction of the official Martial
Heroes client** — nothing in the tree may exist only to make the game "run"
without the real server and the real `data.vfs`. In one pass we:

1. **Purge every dev/offline/synthetic artifact.** No synthetic feeders, no
   "OFFLINE-DEMO" world assembly, no placeholder avatars, no fake catalogues,
   no English placeholder strings, no "run without VFS" substitutes. The client
   may legitimately *require* the real VFS and a live/replica server to render.
2. **Re-architect aggressively for excellence.** A single, legible taxonomy
   — *Solution-folder → Project → pattern-folder → file* — with god-classes
   split, dead/duplicate references removed, oversized files decomposed, new
   projects added where they raise clarity, and comments/perf brought to a
   professional bar.
3. **No test projects.** None exist on disk today; this campaign keeps it that
   way and removes every dangling test reference (`InternalsVisibleTo …Tests`).
   Verification is build + headless + live, not xUnit.

This is **reconstruct, not validate** (memory `strict-1to1-reconstruct-not-validate`):
we adversarially hunt and delete scaffolding rather than rationalise it as
"legitimate degradation". When a fallback's legitimacy is genuinely in doubt,
the arbiter is `doida.exe` in IDA, then the committed spec — never "it seems
reasonable".

## 2. Scope

**In scope**
- All C# under `01.`–`05.` numbered layers (core + Godot).
- The `.slnx` project graph, `ProjectReference` edges, solution-folder layout.
- New projects, file splits, pattern-folder organisation, dead-code deletion.
- Comments, naming, zero-alloc hot-path hygiene, file-size discipline.

**Out of scope (this campaign)**
- New RE of `doida.exe` — only spawned on-demand when a purge/refactor reveals a
  real behavioural gap the specs don't answer (route to `re-orchestrator`).
- New gameplay features. `// PHASE-2: world-campaign` stubs are deferred, not
  scaffolding — they stay (but are clearly tagged, not generic `TODO`).
- Committing originals or `_dirty/` (firewall unchanged).

## 3. Non-negotiable invariants (a re-architecture must preserve ALL)

- **Downward-only layer DAG** 01→02→03→04→05; no upward edges, no cycles
  (`.claude/skills/scaffold-project/scripts/check_dag.py`).
- **Engine-free below 05** — no `using Godot;` in layers 01–04. Layer 05 uses
  `global::Godot.*` to dodge sibling-namespace collisions.
- **Clean-room firewall** — no IDA/Hex-Rays artifacts (`sub_`, `loc_`, `_DWORD`,
  `__thiscall`) in committed code; every magic constant cites `// spec:`.
- **Wire/zero-alloc discipline** — `[StructLayout(Pack=1)]` + `[InlineArray]`
  packet structs, `Span<byte>` hot paths, no LINQ/alloc/boxing per packet.
- **CP949** for all game text; registered once.
- **Layer-05 single-assembly reality** — Godot loads one game assembly; Node-
  derived scripts stay in `MartialHeroes.Client.Godot`. New *projects* are added
  primarily in layers 01–04; layer 05 is re-organised by *pattern folders*, not
  split into many assemblies (non-Node helpers may move to a referenced lib only
  if it stays acyclic and engine-free).

## 4. Verification gate (replaces tests)

Every phase exit and the final consolidation must pass:
1. **Build-nuke 0/0** — delete all `bin/`+`obj/`, then `dotnet build
   MartialHeroes.slnx` with **zero** errors/warnings (incremental build cache is
   unreliable here — memory `build-cache-unreliable`).
2. **Godot headless spine** — console exe `--headless` walks scene states 0→5
   with no GD errors; `0/0` Godot C# build.
3. **Live login (when replica up)** — Login→CharSelect→enter-world against the
   replica `211.196.150.4:10000` (credentials via env). Optional if replica down
   → fall back to build + headless.
4. **Firewall + DAG audit** — `/clean-room-check` PASS, `check_dag.py` clean.

## 5. Routing (two-levels-max orchestration)

| Lane | Tier-2 orchestrator | Workers |
|---|---|---|
| C#/Godot purge, re-arch, quality | `port-orchestrator` | network/assets/core/dotnet-foundation engineers, godot world/ui/character specialists, `code-reviewer`, `render-reviewer` |
| Architecture design / decomposition | `planning-orchestrator` | `requirement-analyst`, `todo-architect`, `plan-reviewer` |
| On-demand behaviour gaps | `re-orchestrator` | analysts + `spec-author` + (debugger) `re-validator` |

Tier-1 (main session) owns sequencing, the file-ownership ledger, decision
gates (e.g. which new projects to add), and commits (only on explicit request).

## 6. Risk register

| # | Risk | Mitigation |
|---|---|---|
| R1 | Deleting a fallback that mirrors real `doida.exe` behaviour | Arbiter = IDA then spec; flag-don't-guess; ambiguous → `re-orchestrator` |
| R2 | Re-arch breaks the DAG / engine-free / firewall | Run `check_dag.py` + `/clean-room-check` each wave; `code-reviewer` gate |
| R3 | Build-cache false greens/reds | Always nuke `bin/obj` before an authoritative verdict |
| R4 | Splitting Node scripts breaks Godot script binding | Keep Node scripts in the Godot assembly; verify headless after each split |
| R5 | Two writers on one file in a parallel wave | One-writer-per-path ledger in ROADMAP; disjoint lanes |
| R6 | Losing the live-enter ghost window (~40 min account lock) | Use `MH_LOGIN_SLOT`; don't spam live enters during verification |
