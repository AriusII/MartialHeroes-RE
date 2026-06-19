# PLAN — Campaign Charter

> **Method/charter doc.** This file specialises `Docs/CAMPAIGN_TEMPLATE.md` for the active
> campaign. The **live run record** (dated phase statuses, evidence baselines, what actually
> happened) lives in `Docs/ROADMAP.md` under the matching `# CYCLE` heading. This file states
> *the method*; the ROADMAP records *the runs*. Read `CLAUDE.md` and
> `PRESERVATION_AND_ARCHITECTURE.md` first for the project-wide doctrine.

## Active campaign — Runtime Inter-Format Assembly Graph

**Theme.** The repository already knows *what each VFS format is* (campaigns 8 & 10 two-witness
re-derived ~32 format specs, ~39 subsystem specs, ~10 struct tables) and how the `data.vfs`
archive is *opened*. What is **not** yet captured in any single committed artifact is how the
runtime **wires those formats into each other** — which format loads/references/composes the next,
in what order, with what keying rule — to assemble a complete **World** and a complete **Actor**
from a handful of VFS ids. This campaign closes that **assembly / relationship layer**.

### Mandate (maintainer, reframed)
Do a large **static** IDA pass (unbridled parallel reads + IDB-legibility writes, SHA-pinned
`263bd994`) recovering the **runtime inter-format assembly graph** across three axes, then build
the matching C# composition layer, then render it:

1. **World assembly** — `area → cells (.ted/.map) → terrain layers/textures (bgtexture → .dds under
   map000) → buildings (.bud) → effects/particles (.xeff/.fx) → spawns (.arr) → collision (.sod) →
   region/zone → sky/environment`, and the full map-construction sequence.
2. **Character / 3D-model assembly** — `class → .skn → bind (g{SkinClassId}.bnd) → motion
   (actormotion → .mot) → texture (skin.txt) → material / equipment overlay`; rig/bone hierarchy,
   bind / inverse-bind, weight packing, attachment composition, mob→skin resolution.
3. **All other formats** — close the remaining linkage gaps (events, items, xdb, sound, UI
   manifests…) so the cross-format vision is complete.

Then **improve the C# code**: complete layer-03 parsers against the deepened specs, add the
net-new engine-free `AreaComposer` (03) and `ActorComposer` (04), cover them with deterministic
xUnit, and finally **un-freeze the Godot World scene** (05) to render the assembled world 1:1.

### In scope
- Static-IDA recovery of the assembly graph (axes A/B/C above) + targeted deepening of
  **linkage-thin** specs only.
- A net-new master spec `Docs/RE/specs/assembly_graph.md` (the cross-format wiring synthesis).
- IDB legibility annotation of the assembly/loader/composer functions.
- C# layer 03 (parser/catalogue completion + `AreaComposer`) and 04 (`ActorComposer` + tests).
- A final layer-05 World-scene un-freeze rendering the composed area/actors.

### Out of scope (hard)
- **Re-RE of solid byte formats** — strides/layouts already two-witness-confirmed stand; a format
  spec is touched *only* to add its linkage layer.
- **The live debugger** — STATIC IDA only this campaign; every DBG-pending fact stays an **OPEN
  RISK**, never a baked-in guess. No phase depends on the maintainer F9-launching the client.
- **The VFS container open path** (`.inf`/`.vfs` byte layout, open dispatch) — solved; consumed.
- **Server / networking / crypto / login / front-end scenes** (Boot/Login/PIN/ServerList/Load/
  Opening) — out of band.
- **New gameplay subsystems** (combat formulas, quests, trade logic) beyond what asset assembly
  requires.
- **Per-pixel oracle polish** as a separate deliverable — Phase 6 renders 1:1 and avoids
  regressions; exhaustive per-area screenshot tuning is a follow-on.
- **Worker edits to orchestrator-owned files** (`journal.md`, `names.yaml`, `settings.json`,
  `.mcp.json`) and any committed Hex-Rays pseudo-C.

## Doctrine (this campaign's application)

- **Ground-truth.** `doida.exe` via IDA (static, SHA `263bd994`) is the single absolute truth.
  Dirty-room findings land ONLY in `Docs/RE/_dirty/assembly/{world,char,other}/` (gitignored), then
  get **rewritten** (never copied) into clean `Docs/RE/` specs; C# reads ONLY the clean specs.
- **Clean-room firewall.** No Hex-Rays pseudo-C (`sub_xxxx`, `_DWORD`, `__thiscall`, mangled names)
  in any committed file or C#. Every magic constant in C# cites `// spec: Docs/RE/...`.
- **IDA unbridled.** Research waves fan out read analysts AND (in Phase 4) IDB-legibility writes
  massively in parallel; retry on conflict. Only live MCP throughput limits concurrency.
- **STOP gate.** If the IDA MCP is down or the wrong/empty DB is loaded, STOP and report — never
  fabricate IDA output. No RE lane proceeds unless the anchor SHA == `263bd994`.

## Command structure (two levels of orchestration max)

```
Tier-1 (main session)
  ├─ Phase 0 preflight + all serialized writes (journal.md, names.yaml, PLAN/ROADMAP statuses)
  ├─ re-orchestrator   (Tier-2)  → Phases 0.5, 1, 2, 3, 4
  │     └─ re-function-analyst, re-struct-analyst, re-asset-format-analyst, spec-author, ida-toolsmith
  └─ port-orchestrator (Tier-2)  → Phases 5, 6, 7
        └─ assets-engineer, core-engineer, test-engineer, code-reviewer,
           godot-world-engineer, godot-character-specialist, render-reviewer
```
A lane captain never spawns another captain. One writer per path per wave (file-ownership ledger).

## Phase pipeline (statuses live in ROADMAP)

- **P0 — Preflight** (T/R): MCP health + SHA pin, nuked build/test baseline + suite enumeration,
  branch/ledger, `_dirty/assembly/*`, fresh PLAN/ROADMAP, no-IDA stale-spec fixes (A3-6, A3-3).
- **P0.5 — Lane-anchor scout** (W-lite): each A-lane → a real IDA anchor `@263bd994` or
  NO-ANCHOR→OPEN-RISK.
- **P1/P2/P3 — Giga-research** (W, parallel): world / character / other assembly graphs, dirty-room
  only.
- **P4 — Consolidation + firewall + IDB** (P/R/C): promote each lane (one author per file),
  `assembly_graph.md` written LAST (barrier), IDB annotate, Tier-1 firewall + journal + names.yaml.
- **P5 — Core 03–04** (E): parser completion + contracts → `AreaComposer` + `ActorComposer`
  (engine-free, ActorComposer **bind-agnostic**) → integration wiring + xUnit.
- **P6 — Godot 05 World un-freeze** (E): render the assembled area/actors 1:1; fix fallback-Y;
  lighting non-regression.
- **P7 — Verification + closeout** (R/C): hard gates, residual-risk ledger, preservation.

## Success criteria (the gate)

- A committed `assembly_graph.md` that traces, hop-by-hop, the World-boot chain and the Actor-bake
  chain, with the loader/resolving function + keying rule + order on each edge, plus a format→format
  edge table and an OPEN-RISK ledger.
- Net-new engine-free `AreaComposer` / `ActorComposer` that build an Area and an Actor from VFS ids
  end-to-end, covered by deterministic xUnit (no VFS, synthetic fixtures); `ActorComposer` emits a
  valid actor with or without recovered inverse-bind.
- The Godot World renders the assembled area (terrain multi-texture + buildings + effects + spawned
  actors) without the "too dark" / mesh-explosion / fallback-Y regressions.
- Hard gates: nuked build 0/0 · all xUnit suites green AND **enumerated** (count diffed vs the P0
  baseline — `dotnet test` on `.slnx` can silently omit suites) · clean-room firewall PASS · layer
  DAG clean (no upward refs, no `using Godot;` in 01–04) · Godot headless World boot clean.

## Concurrency & file-ownership

- RE waves write only to disjoint `_dirty/assembly/{world,char,other}/<lane>.md`.
- P4 promotion = one author per committed spec file; `assembly_graph.md` authored by exactly one
  agent **after** all axis promotions land (barrier).
- P5 = one engineer per project per stage on disjoint files (`AreaComposer.cs` ≠ `ActorComposer.cs`).
- Tier-1-serialized (never delegated): `journal.md`, `names.yaml`, `settings.json`, `.mcp.json`,
  `Docs/PLAN.md` / `Docs/ROADMAP.md` status writes.

## Anchors / baseline

- IDB: `doida.exe.i64`, module `doida.exe`, **SHA-256 `263bd994c927c20a38624cf0ca452eaef365057fa9db1543d8f668c14a6fd8ee`**,
  imagebase `0x400000`, 25 792 functions (4 871 named / 1 901 library / 19 020 `sub_`).
- VFS: `clientdata/data.inf` present (archive reachable).
- Tooling/build baseline recorded in `Docs/ROADMAP.md` at cycle start.
