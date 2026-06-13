# Martial Heroes — Reverse-Engineering Campaign Template

> **What this is.** A reusable, copy-paste blueprint for running ONE complete reverse-engineering
> campaign cycle against the legacy Martial Heroes client (`Main.exe` / *D.O. Online*, 2003–2008),
> end to end: from "we don't understand subsystem X yet" to "subsystem X is documented, implemented,
> verified, and committed". It encodes the orchestration pattern proven across Cycles 1–3
> (see `Docs/ROADMAP.md` for the concrete, dated instances) and scales it up to a disciplined
> **50-agent fleet** driven by a multi-tier command hierarchy.
>
> **How to use it.** Copy the [§14 Cycle Skeleton](#14--cycle-skeleton-copy-paste-to-start-a-new-cycle)
> into `Docs/ROADMAP.md` under a new `# CYCLE N — <theme>` heading, fill every `<placeholder>`, then
> run the phases in order. This template explains *the method*; the ROADMAP records *the runs*.
>
> **The single goal this template serves.** Keep pushing until the entire Martial Heroes client is
> reverse-engineered, cleanly re-implemented in .NET 10 / C# 14, and playable again under the Godot
> 4.6.3 presentation layer — without ever copying a byte of the original binary or assets.
>
> **Legal backbone.** EU Software Directive **2009/24/EC Art. 6** (decompilation for
> interoperability). The clean-room firewall (§4) is what makes that lawful. It is non-negotiable.

---

## Table of contents

0. [Mission & charter — why this campaign exists](#0--mission--charter--why-this-campaign-exists)
1. [The campaign hierarchy — Campaign ▸ Phase ▸ Objective ▸ Sub-objective ▸ Task](#1--the-campaign-hierarchy)
2. [Orchestration doctrine — the three command tiers](#2--orchestration-doctrine--the-three-command-tiers)
3. [Concurrency & race-condition control](#3--concurrency--race-condition-control)
4. [The clean-room firewall — non-negotiable invariants](#4--the-clean-room-firewall--non-negotiable-invariants)
5. [Campaign anatomy — the phase pipeline](#5--campaign-anatomy--the-phase-pipeline)
6. [Phase 0 — Mandate & Pre-flight](#6--phase-0--mandate--pre-flight)
7. [Phase 1 (W) — Giga-Research wave (dirty room)](#7--phase-1-w--giga-research-wave-dirty-room)
8. [Phase 2 (P) — Spec Promotion (firewall crossing)](#8--phase-2-p--spec-promotion-firewall-crossing)
9. [Phase 3 (E) — Engineering wave (clean room)](#9--phase-3-e--engineering-wave-clean-room)
10. [Phase 4 (T) — Tooling wave](#10--phase-4-t--tooling-wave)
11. [Phase 5 (R) — Review, Fix & Hard Gates](#11--phase-5-r--review-fix--hard-gates)
12. [Phase 6 (C) — Consolidation & Commit](#12--phase-6-c--consolidation--commit)
13. [The fleet — ~50 reusable agents](#13--the-fleet--50-reusable-agents)
14. [Cycle Skeleton (copy-paste to start a new cycle)](#14--cycle-skeleton-copy-paste-to-start-a-new-cycle)
15. [Risk register template](#15--risk-register-template)

---

## 0 · Mission & charter — why this campaign exists

> **Read this first. Every phase, every objective, every agent serves the will stated here.**

### 0.1 The will of the project

**Martial Heroes** (originally *D.O. Online*) was an Asian martial-arts MMORPG that ran from roughly
**2003 to 2008**. Its servers were shut down, it was **never re-published in Europe**, and it became a
**dead game** — unplayable, unpreserved, slipping out of cultural memory. The only surviving
authoritative artifact of *how the game actually worked* is the legacy 32-bit MSVC game client,
**`Main.exe`** (a.k.a. `doida.exe`), together with the client data archive (`data.inf` + `data.vfs`).

**This project's will is to bring that dead game back to life.** Concretely:

- Take the original game **client** (`Main.exe`) of this dead game and **understand it completely** —
  every subsystem that is observable and exploitable: boot, the main loop, the scene machine, the
  network protocol, the crypto, the asset/file pipeline, rendering, skinning/animation, UI, sound,
  combat and the rest of the gameplay systems.
- From that understanding, **re-create the client 1:1** — a faithful, behaviour-accurate
  re-implementation in **.NET 10 / C# 14** with a **Godot 4.6.3** presentation layer — so a player can
  **launch Martial Heroes again** and have it look and behave like the original.
- Do it **lawfully and clean** (EU Art. 6, §0.4 + §4): nothing is copied from the binary or the
  assets; everything is re-implemented fresh from neutral specs.

This is a **labour-of-love preservation project**. Fidelity *is* the deliverable.

### 0.2 The 1:1 fidelity mandate

"1:1" is not a slogan — it is the acceptance bar. The re-implementation must match the **original
client's observable behaviour and its on-disk / on-wire formats exactly**, because a preservation
that drifts from the original is not preservation. In practice the 1:1 target spans:

- **File formats** — `data.vfs`/`data.inf` archive layout, and every asset blob inside it
  (mesh/terrain/animation/texture/sound/config), parsed byte-for-byte as the original parses them.
- **Wire protocol** — opcodes, packet field layouts, the packet cipher and key schedule, framing and
  timings, so the client speaks the original's language on the wire.
- **Runtime behaviour** — scene transitions, camera math, skinning/animation deformation, combat
  cadence and cooldowns, UI layout — matching what the original `Main.exe` does, confirmed against it.

Every cycle pushes more of the client across the line from "approximated" to "1:1, verified against
the original".

### 0.3 IDA Pro 9.3 + the `ida` MCP — the primary instrument

We do not guess how the client works — **we read it and we watch it run.** The primary instrument is
**IDA Pro 9.3** driving the legacy `Main.exe`, exposed to the fleet through the **`ida` MCP server**
(tools `mcp__ida__*`). Two complementary modes, both first-class:

- **Static analysis** — read the disassembly/decompilation to map functions, dispatch tables, struct
  and vtable layouts, and the asset/VFS loader code (`mcp__ida__decompile`, `disasm`, `xrefs_to`,
  `callgraph`, `read_struct`, `find_bytes`, the `re-*` analyst agents, the `/ida-*` skills).
- **Dynamic analysis (the IDA debugger)** — *run the original client under IDA* and observe ground
  truth that static reading can only hypothesize: actual field values, packet bytes **before and
  after** decryption, the VFS index records as they sit in memory, decompression buffers, real
  timings/cadence, and which branch is actually taken (`mcp__ida__dbg_*`: `dbg_start`, `dbg_add_bp`,
  `dbg_run_to`, `dbg_step_into/over`, `dbg_read`, `dbg_gpregs`, `dbg_stacktrace`, `dbg_continue`).

The doctrine: **static analysis forms the hypothesis; the debugger confirms it.** A finding confirmed
at runtime is high-confidence and resolves `CONFLICT:` markers; a finding only read statically stays
a hypothesis until a sample or a debugger session corroborates it. Both modes feed the dirty room and
**both obey the firewall** — see §4.4.

> **⚠️ Two MCP endpoints — they do NOT expose the same tools. Prioritize `?ext=dbg`.**
> The `ida` server is reachable on two URLs:
> - `http://127.0.0.1:13337/mcp` — base endpoint (static analysis tools).
> - `http://127.0.0.1:13337/mcp?ext=dbg` — **debugger-extended endpoint**, which surfaces the
>   debugger toolset (`mcp__ida__dbg_*`) **in addition to** the static tools.
>
> **The committed `.mcp.json` registers the `?ext=dbg` endpoint, and it is the one to use** — it is a
> superset for our purposes (static recovery *and* dynamic confirmation from a single connection).
> The two endpoints' toolsets can differ, so **never assume a tool is present** — the `mcp__ida__*`
> tools are deferred and discovered at runtime. If the `dbg_*` tools are missing, the session is on
> the wrong (base) endpoint: re-register on `?ext=dbg`
> (`claude mcp add --transport http ida "http://127.0.0.1:13337/mcp?ext=dbg"`) and restart the session.

### 0.4 Understanding `data.vfs` / `data.inf` *through* `Main.exe`

The client data lives in two files: **`data.inf`** (the archive index/directory) and
**`data/data.vfs`** (the ~3.8 GB packed payload, 43k+ entries). We understand this format **not by
staring at the bytes in isolation, but by recovering how `Main.exe` itself mounts and serves it** —
the binary is the specification. The recurring research objective (it recurs because the VFS touches
everything) is to recover, from `Main.exe`:

- how the archive is **opened and the index parsed** (the `data.inf` record layout, name hashing,
  offset/size fields, any compression flag);
- how a logical path is **looked up** to an entry;
- how an entry is **read / decompressed** (memory-mapped view? LZ4? raw slice?) and handed to the
  rest of the engine as a usable buffer;
- the lifetime/caching policy (the streamer thread, the sync ring).

This is recovered **statically** (read the loader functions + the index struct) **and confirmed
dynamically** (breakpoint the open/lookup/read functions, run the client, read the in-memory index
records and the returned buffers). The maintainer's own `data.inf`/`data.vfs` are observed through
the `vfs-inspect` harness to cross-check — never committed, never pasted as bytes.

### 0.5 Scope of the revival

- **In scope:** the **client**, in its entirety, recovered and re-implemented 1:1.
- **Out of scope (deferred):** the **game server**. No server code is written now — but the core
  (layers 01–04) is kept engine-free and reusable so a future `MartialHeroes.Server.Console` can be
  built on it. (Each cycle restates its own out-of-scope list in Phase 0.)

---

## 1 · The campaign hierarchy

A campaign is a **strict containment tree**. Every level has its own owner, granularity, lifetime,
and "done" test. Never blur two levels: an *objective* is not a *task*, and a *phase* is not an
*objective*. The whole template exists to keep these five levels crisp across a 50-agent fan-out.

```
CAMPAIGN  (1 per maintainer mandate — the whole cycle)
│
├── PHASE        a, b, c …      (a stage of the pipeline: W, P, E, T, R, C)
│   │
│   ├── OBJECTIVE      a.1, a.2 …   (a self-contained outcome the phase must deliver)
│   │   │
│   │   ├── SUB-OBJECTIVE   a.1.i, a.1.ii …   (one indivisible deliverable = one agent's mission)
│   │   │   │
│   │   │   └── TASK        a.1.i.① …          (a concrete step inside a single agent's run)
```

### 1.1 Level definitions

| Level | What it is | Owner | Granularity | Typical count | "Done" test |
|-------|-----------|-------|-------------|---------------|-------------|
| **CAMPAIGN** | One full cycle answering one maintainer mandate (theme + "why now") | **Top Orchestrator** | A whole subsystem family (e.g. "world-scene gameplay systems") | 1 | All phases passed their exit gates; ROADMAP cycle section is a complete dated record |
| **PHASE** | A stage of the W▸P▸E▸T▸R▸C pipeline | **Top Orchestrator**, optionally delegated to a **Phase Orchestrator-Agent** | A mode of work (research / promotion / engineering / tooling / review / consolidation) | 6 per cycle | The phase's **exit criteria** all pass |
| **OBJECTIVE** | A self-contained outcome the phase owes the campaign | **Phase Orchestrator-Agent** (or Top Orchestrator) | A coherent capability ("skinning math recovered", "HUD reskinned") | 3–10 per phase | Its sub-objectives are all delivered and reconciled |
| **SUB-OBJECTIVE** | **One indivisible deliverable** — exactly one agent's mission | **One sub-agent** (single owner) | One file / one tightly-scoped change / one `_dirty/` note | 1–20 per objective | The single deliverable exists, passes its acceptance, and is reported back |
| **TASK** | A concrete step inside one agent's own run | **The sub-agent itself** | A tool call or a small sequence | many | The step succeeded; the agent moves on |

### 1.2 The cardinal rule of decomposition

> **One sub-objective = one deliverable = one owning agent = one writable path.**

This single equivalence is what makes 50 agents safe to run at once. If two deliverables would touch
the same file, they are **the same sub-objective** (merge them) or they must be **sequenced**
(different waves). There is never a sub-objective with two writers. This is the foundation of §3.

### 1.3 Worked example (from Cycle 3)

```
CAMPAIGN  "World-scene gameplay systems + icon/window fidelity + deeper VFS tooling"
└─ PHASE   C3-W1  Giga-research (dirty room, 20 lanes)
   └─ OBJECTIVE  "Recover the combat flow end to end"
      └─ SUB-OBJECTIVE  lane combat-flow → re-static-analyst → _dirty/world/combat-flow.md
         ├─ TASK  locate BattleHandler dispatch
         ├─ TASK  trace C2S 2/52 slot-byte 0xFF (melee-is-a-skill)
         ├─ TASK  map incoming 5/52 ActorSkillAction + 5/53 vitals
         └─ TASK  recover cooldown math (swing-ready + 100ms·cadence + 550ms lockout)
```

The sub-agent owns one note; the objective rolls up several such notes; the phase rolls up several
objectives; the campaign rolls up the six phases.

---

## 2 · Orchestration doctrine — the three command tiers

Large campaigns are run by a **three-tier command hierarchy**. The point of the tiers is *span of
control*: no single actor should be juggling more than ~10 live children. One Top Orchestrator
cannot personally babysit 50 sub-agents — it delegates whole blocks of orchestration logic to
**Orchestrator-Agents**, each of which monitors one big chunk of work.

```
                          ┌─────────────────────────────┐
        TIER 1            │      TOP ORCHESTRATOR        │   the main session — owns the campaign,
        (campaign)        │   (you, the main session)    │   the ROADMAP, the firewall-shared files,
                          └──────────────┬──────────────┘   the gates, and the commit.
                                         │ delegates whole phases / big logic blocks
            ┌────────────────────────────┼────────────────────────────┐
            ▼                            ▼                             ▼
   ┌─────────────────┐         ┌─────────────────┐          ┌─────────────────┐
   │ ORCHESTRATOR-   │         │ ORCHESTRATOR-   │          │ ORCHESTRATOR-   │   TIER 2
   │ AGENT           │         │ AGENT           │          │ AGENT           │   (big block)
   │ "W1 research    │         │ "E engineering  │          │ "R review &     │   owns one phase
   │  fleet, 20 lanes"│        │  pipeline"      │          │  gates"         │   or one big block,
   └───┬───┬───┬─────┘         └───┬───┬───┬─────┘          └───┬───┬─────────┘   monitors it, rolls
       ▼   ▼   ▼                   ▼   ▼   ▼                    ▼   ▼               up ONE result.
     sub  sub  sub …             sub  sub  sub …             sub  sub …            TIER 3 (workers)
   (single-deliverable specialists from the §13 fleet — one file / one mission each)
```

### 2.1 Tier 1 — the Top Orchestrator (the main session)

The single point of campaign coordination. It is the **only** actor that:

- owns and updates `Docs/ROADMAP.md` (phase statuses changed **in place** as waves land);
- decides the **phase sequence** and enforces each phase's **exit gate** before the next starts;
- decomposes the campaign into phases and either runs a phase directly or **delegates it to a
  Tier-2 Orchestrator-Agent** with a clear charter;
- owns the **firewall-shared, serialized files** — `Docs/RE/journal.md`, `Docs/RE/names.yaml`,
  `Docs/RE/opcodes.md` (final reconcile), `settings.json`, `.mcp.json`, `CLAUDE.md`,
  `client_dir.cfg` cutover — which **no lower tier may edit** (see §3.4);
- runs the **hard gates** (build 0/0, full test suite, firewall PASS) and is the **only** actor that
  commits, and only **when the maintainer explicitly asks**.

The Top Orchestrator does **not** do deep work. Its job is decomposition, delegation, reconciliation,
gatekeeping, and provenance.

### 2.2 Tier 2 — Orchestrator-Agents (own a big block of logic)

An **Orchestrator-Agent** is an agent the Top Orchestrator spawns to **own and monitor a whole block
of orchestration** — typically an entire phase, or a large sub-wave within a phase. This is the tier
the maintainer asked to be made explicit: agents that *control big blocks of orchestration logic and
monitor big tasks*.

An Orchestrator-Agent:

- receives a **charter**: the block's objective, the lanes/sub-objectives it must drive, the
  firewall side it operates on, its **writable path namespace**, and its exit criteria;
- **fans out Tier-3 sub-agents** for each sub-objective in its block, in correctly-sized batches
  (IDA in sub-waves of ~3; clean-room/VFS wide — see §3.2);
- **monitors** the block: tracks which sub-objectives are in-flight / returned / failed, retries or
  reassigns a dead lane, and keeps a running **file-ownership ledger** for its namespace (§3.1);
- **reconciles** its sub-agents' returns into **one rolled-up block result** (headlines, conflicts,
  confidence, open items) handed back to the Top Orchestrator — not 20 raw dumps;
- **never** edits Tier-1 serialized files and **never** crosses the firewall its block sits on.

**When to use one (vs. the Top Orchestrator driving lanes directly):**

| Use an Orchestrator-Agent when… | Drive directly when… |
|---|---|
| The block has **>~8 sub-objectives** (span-of-control relief) | The block has ≤ ~6 lanes |
| The block needs **internal sequencing** (sub-waves, pipelines, retries) | Lanes are flat and independent |
| The block is **long-running** and benefits from a dedicated monitor | The work is short |
| You want a **single rolled-up summary** instead of N returns to triage | You want every raw return |

**Nesting rule:** an Orchestrator-Agent may spawn Tier-3 sub-agents but **must not** spawn another
Orchestrator-Agent. Keep the tree to **two levels of orchestration** (Tier 1 → Tier 2 → Tier 3). If a
block is so big it wants sub-orchestrators, that is a signal to **split it into more Tier-2 blocks**
under the Top Orchestrator instead. (The `Workflow` tool enforces this: `workflow()` nesting is one
level only.)

**Charter template (what the Top Orchestrator hands a Tier-2 Orchestrator-Agent):**

```
ROLE: Orchestrator-Agent for BLOCK "<block name>" (Phase <N>-<X>).
FIREWALL SIDE: <dirty | bridge | clean | review>. You inherit and enforce all firewall rules on your fan-out.
WRITABLE NAMESPACE: <exact path prefix your block + its sub-agents may write, e.g. Docs/RE/_dirty/world/**>.
                    You and your sub-agents write NOWHERE else. Tier-1 serialized files are off-limits.
SUB-OBJECTIVES (one sub-agent each, one deliverable each):
  - <lane 1> → <agent type> → <deliverable path>
  - <lane 2> → <agent type> → <deliverable path>
  - …
BATCHING: IDA lanes in sub-waves of 3 (single IDB); VFS/clean lanes wide. Maintain a file-ownership
          ledger: never let two live sub-agents target the same path.
MONITOR: track in-flight/returned/failed; retry a dead lane once; flag any lane that can't return.
RECONCILE: produce ONE block summary — headlines, per-lane confidence, CONFLICT markers, open items.
EXIT CRITERIA: <the block's gate>.
REPORT BACK: the single rolled-up block result (not the raw per-lane artifacts).
```

### 2.3 Tier 3 — Sub-agents (single-deliverable specialists)

The workers. Each is a **single-purpose, single-deliverable** agent drawn from the §13 fleet. Every
sub-agent:

- has the **narrowest tool allowlist** for its job (a dirty-room analyst has `mcp__ida__*` and may
  write only under `_dirty/`; a clean-room engineer has **no IDA at all**);
- owns **exactly one deliverable** and **one writable path** (the §1.2 cardinal rule);
- **stays on its firewall side** (§4): a dirty-room agent never produces committed code; a clean-room
  agent never reads `_dirty/` or IDA;
- returns a **structured mission report**, not the raw artifact dump.

**Mission-brief template (what an orchestrator hands a Tier-3 sub-agent — be lavishly specific):**

```
ROLE: <exact agent type from the fleet>.
FIREWALL: <dirty: write ONLY Docs/RE/_dirty/<path> | clean: read ONLY committed specs, NEVER _dirty/IDA>.
SINGLE DELIVERABLE: <the one file / one change you own> at <exact path>. You write nowhere else.
MISSION (the one sharp question / outcome): <precise, single-sentence objective>.
INPUTS / EVIDENCE: <IDA functions, strings, VFS paths/extensions, capture .tsv, or committed specs to start from>.
CONSTRAINTS: <layer/DAG rules, engine-free if <05, zero-alloc hot path, cite // spec:, CP949 text, etc.>.
CROSS-CHECK: <sibling deliverable / capture oracle / existing spec to reconcile against; flag CONFLICT:, don't silently merge>.
ACCEPTANCE: <the concrete done-test: build 0/0 for this project, sample-verified offsets, screenshot, test green…>.
REPORT BACK: 1-paragraph summary + confidence (high/med/low) + open questions + conflicts + files written.
```

### 2.4 The fleet is the point — use it, reuse it

There is a **standing fleet of ~50 agents** (see §13): ~44 project specialists (dirty analysts, the
spec-author bridge, one clean-room engineer **per project**, the quality reviewers, the meta-authors)
plus the generic utility tier (`Explore`, `Plan`, `general-purpose`, `claude`, `godot-mcp-operator`,
`claude-code-guide`). **This fleet is a primary asset of the project, not a convenience.** Treat it
accordingly:

- **Reach for a specialist before doing the work inline.** Each engineer agent already knows its
  project's layer, conventions, and traps. Re-using `network-protocol-engineer` for *every* packet
  struct keeps the wire layer consistent across cycles; hand-rolling it inline does not.
- **Reuse the same agent type across cycles** — that is how conventions compound. The fleet is
  designed to be re-invoked run after run.
- **One agent per project is deliberate**: it guarantees a single writer per project per wave (§3)
  and a single point of expertise. Don't spawn two engineers into the same project in one wave.
- **Right tier for the job:** broad fan-out search → `Explore`; multi-step plan → `Plan`; a real
  specialist task → the named specialist; an unfit task → `general-purpose`. Don't burn a specialist
  on a one-line lookup, and don't burn `general-purpose` on something a specialist owns.
- **Meta-authors maintain the fleet itself**: `agent-author` adds/repairs an agent, `skill-author` a
  skill, `hook-author` a hook, and `tooling-auditor` audits the result. The fleet is **extensible** —
  when a recurring specialist role appears mid-campaign, mint it (§10) and reuse it forever after.

---

## 3 · Concurrency & race-condition control

Fanning out 50 agents is only safe with disciplined concurrency. Every race in this project reduces
to **two agents touching one resource at once**. The rules below make that structurally impossible.
This section is mandatory reading for any orchestrator (Tier 1 or Tier 2) before a fan-out.

### 3.1 The file-ownership ledger (the core invariant)

Every orchestrator maintains a **ledger**: a live map of `path → owning sub-agent` for the current
wave. Rules:

- **Exactly one writer per path per wave.** Before launching a wave, list every sub-agent's writable
  path; if any path appears twice, the wave is **invalid** — merge or sequence those lanes.
- **Disjoint path namespaces.** Partition deliverables so each lane writes under a distinct prefix
  (`_dirty/world/combat.md` vs `_dirty/world/chat.md`; `Assets.Parsers/Foo.cs` vs `Client.Godot/Bar.cs`).
- **One engineer per project per wave.** Because there is one clean-room agent per project, never
  spawn two writers into the same `.csproj` in one wave — sequence them across stages instead.
- **Readers are unbounded.** Many agents may *read* the same spec/file concurrently. Only *writes*
  are arbitrated. (This is why the four Phase-5 reviewers run fully parallel — they edit nothing.)

### 3.2 Batching by shared scarce resource

| Resource | Constraint | Batching rule |
|----------|-----------|---------------|
| **IDA database (single IDB + bounded MCP)** | One live database; MCP concurrency saturates at ~3 heavy consumers | **IDA lanes run in sub-waves of ~3.** Never launch 10 `mcp__ida__*` agents at once — they starve each other. |
| **Godot editor / MCP bridge** | Single editor instance, single running game | **Serialize interactive Godot work.** One `godot-mcp-operator` / scene-mutating run at a time. Headless console runs (read-only smoke) may overlap but should not fight over `client_dir.cfg` (§3.4). |
| **Disk / VFS reads** | No write contention (read-only observation) | **VFS lanes fan out wide** — `vfs-data-analyst` agents have no shared scarce resource. |
| **Engine-free C# build** | No shared runtime state | **Clean-room lanes fan out wide**, one project each. |
| **Token / cost budget** | Shared pool across the run | Scale fleet width to the work and any stated budget; loop-until-done patterns guard on `budget.remaining()`. |

### 3.3 Wave barriers vs. pipelines (when to synchronize)

- **Default to pipelines.** When work is multi-stage per item (research → verify; build → test;
  parse → map → render), pipeline it so item A can advance to stage 2 while item B is still in
  stage 1. Wall-clock = slowest single chain, not sum-of-stages.
- **Use a barrier only when a stage genuinely needs ALL prior results**: deduping findings across the
  whole set, an early-exit decision ("0 findings → skip verification"), or a synthesis that must read
  every input (the Phase-2 master spec is written **after** all subject specs land).
- **Phase boundaries are always barriers.** A phase's exit gate is a hard synchronization point: no
  Phase-3 engineer starts until Phase-2 promotion has produced the spec it reads.

### 3.4 Serialized shared files (Tier-1-only, never concurrent)

A small set of files are **append/merge points many lanes feed**. These are **never** written by a
fan-out. The Top Orchestrator collects all lanes' contributions and applies them **serially, once**:

| File | Why it's a race magnet | Discipline |
|------|------------------------|------------|
| `Docs/RE/journal.md` | append-only provenance; concurrent appends interleave/corrupt | Orchestrator writes one entry per session, after the wave |
| `Docs/RE/names.yaml` | many lanes propose names; concurrent merges clobber | Orchestrator merges all proposals once, post-promotion |
| `Docs/RE/opcodes.md` | many packet lanes add rows; needs sorted, dedup'd order | Orchestrator does the final reconcile/sort/dedup |
| `client_dir.cfg` | smoke runs mutate `area=`/`boot_flow=`; parallel edits race | **Serialize Godot smokes; restore byte-exact after each.** Never two cfg-mutating runs at once |
| `settings.json`, `.mcp.json`, `CLAUDE.md` | harness/orchestrator config | Orchestrator-only, never delegated |

### 3.5 Isolation for parallel file mutators

When two lanes genuinely must mutate overlapping working-tree files in parallel (rare — usually a
sign they should be one lane), give each its **own git worktree** (`isolation: "worktree"` on the
`Agent` call, or `EnterWorktree`). The worktree is auto-cleaned if unchanged. This is expensive
(~200–500 ms + disk per agent) — use it **only** when path partitioning (§3.1) truly cannot.

### 3.6 Detection & recovery

- **Pre-flight ledger check** (§3.1) catches the overwhelming majority of races *before* launch.
- **A lane that returns "I had to touch a file outside my namespace"** is a red flag — the
  orchestrator re-scopes and re-runs it, never accepts the stray write.
- **After every wave**, the orchestrator diffs the working tree against the expected writable set;
  any out-of-namespace change is investigated, not committed.
- **`client_dir.cfg` is verified byte-exact** after any Godot smoke; a drifted cfg means a smoke
  didn't restore it — fix before proceeding.

---

## 4 · The clean-room firewall — non-negotiable invariants

This is the legal heart of the project. **Every** phase, **every** tier obeys it. A campaign that
violates it is worse than a campaign not run.

### 4.1 The pipeline: dirty → spec → clean

```
  IDA / Hex-Rays            neutral committed             fresh C# / Godot
  + VFS observation   ──►   prose & tables         ──►    implementation
  (dirty-room agents)       (spec-author bridge)          (clean-room engineers)

  writes ONLY to            REWRITES (never copies)       reads ONLY clean specs,
  Docs/RE/_dirty/           into Docs/RE/{specs,           never _dirty/, never IDA;
  (gitignored, tainted)     formats,structs,packets,      cites // spec: on every
                            opcodes.md}                    magic constant
```

### 4.2 Hard rules (apply to every agent, every file, every tier)

- **Never paste IDA / Hex-Rays pseudo-C** anywhere committed: no `sub_xxxx`, `loc_xxxx`, `_DWORD`,
  `__thiscall`, `*(_DWORD *)…`, mangled names, or raw virtual addresses in any spec or any C#.
- **Dirty-room agents write ONLY under `Docs/RE/_dirty/`** — gitignored, tainted, never shipped.
- **Spec-author agents REWRITE, never copy.** They translate dirty findings into neutral prose,
  byte/offset tables, opcode catalogues, and packet YAMLs — stripped of every binary artifact.
- **Clean-room engineers read ONLY the committed specs.** They never open `_dirty/`, never touch IDA.
- **Every magic constant / byte offset in C# cites its spec:** `// spec: Docs/RE/formats/terrain.md`.
- **Never commit originals:** `*.pak`, `*.vfs`, `*.inf`, `*.exe`, `*.dll`, `*.pcapng`, `*.tsv`,
  `*.scr`, `*.mot`, `*.ted`, `*.bud`, client `*.png/*.dds`, `Main.exe`, anything under `_dirty/`, or
  the Godot `.godot/` cache. VFS observation reads the maintainer's own legally-owned files; the
  *bytes* never enter the repo or the conversation context.
- **Orchestrator-owned files** (§3.4) are edited only by the Top Orchestrator, never by a sub-agent
  or a Tier-2 Orchestrator-Agent.

### 4.3 Firewall verification (built into Phase 5)

The clean-room is not assumed — it is **audited every cycle** by `clean-room-auditor` +
`/clean-room-firewall-check` + `/clean-room-audit` before any commit. A leak is a release blocker.

### 4.4 IDA Pro 9.3 + `ida` MCP — recovery rules (legal + method)

IDA is the project's primary instrument (§0.3). Its power is exactly why it is fenced by the strictest
rules. These bind every dirty-room agent, in **both** static and dynamic mode.

**Legal rules (the Art. 6 frame):**

- Decompilation **and** debugging of `Main.exe` are performed **solely to achieve interoperability** —
  to recover formats, protocol and behaviour so a fresh, independent client can interoperate with the
  original's data and (eventually) servers. No other purpose.
- Only the **maintainer's own legally-owned copy** of `Main.exe`, `data.inf` and `data.vfs` is ever
  loaded. The binary and the assets are **never committed** (gitignored; see §4.2).
- Findings cross the firewall **only as neutral prose / tables** (§4.1). Hex-Rays pseudo-C, mangled
  names, raw virtual addresses, byte dumps, and runtime memory captures **never** enter a committed
  spec or any C#.

**Static-mode rules:**

- IDA MCP must be UP before any IDA lane (`/ida-mcp-connect`). Pin the binary's SHA-256 in recon.
- Use the typed tools and skills (`mcp__ida__decompile`/`disasm`/`xrefs_to`/`callgraph`/`read_struct`;
  `/ida-recon`, `/ida-opcode-map`, `/ida-struct-recovery`, `/ida-crypto-hunt`, `/ida-xref-map`).
- Output lands in `Docs/RE/_dirty/` as **behaviour described in words** + **offset/size/type tables**,
  with `CONFLICT:`/confidence markers. Never transcribe pseudo-C — *describe* it.

**Dynamic-mode rules (the IDA debugger — `mcp__ida__dbg_*`):**

- **Connect on the debugger-extended endpoint `http://127.0.0.1:13337/mcp?ext=dbg`** (the one the
  committed `.mcp.json` registers, §0.3). The base `/mcp` endpoint does **not** surface the `dbg_*`
  tools. Verify the `dbg_*` toolset is actually present before opening a debugger lane (`/ida-mcp-connect`);
  if it is missing, you are on the wrong endpoint — re-register on `?ext=dbg` and restart the session.
- The debugger is used to **confirm and refine** static hypotheses against the *running original*:
  set breakpoints at recovered functions (`dbg_add_bp`), run to them (`dbg_run_to`/`dbg_continue`),
  read registers / memory / stack (`dbg_gpregs`, `dbg_read`, `dbg_stacktrace`), and single-step
  (`dbg_step_into`/`dbg_step_over`) to watch real control flow, real field values, real timings.
- **Highest-value debugger targets:** packet buffers **pre/post decrypt** (crypto ground truth),
  the VFS open/lookup/read path (`data.inf` records and returned buffers in memory, §0.4), asset
  parser inputs/outputs, skinning/animation matrices at the deform call, and combat cadence timers.
- **Runtime observations are dirty evidence.** Register/memory/buffer dumps are quarantined to
  `Docs/RE/_dirty/` and are **rewritten as neutral facts** ("the index record is 24 bytes: a 4-byte
  offset, a 4-byte packed size, …"), never pasted verbatim. **Captured live game bytes are originals
  — never committed, never brought into conversation context.**
- A finding **confirmed dynamically** is promoted with high confidence and the spec notes it as
  *"verified against the running client under the IDA debugger"* (interop fact, not copied code).

**Promotion is still mandatory.** Whether a fact came from static reading or a debugger session, it
crosses to a committed spec **only** through the §8 spec-author bridge (REWRITE, never copy).

---

## 5 · Campaign anatomy — the phase pipeline

A cycle is six phases. The middle four (W → P → E → R) are the load-bearing loop; Phase 0 frames it
and Phase 6 closes it. Tooling (T) runs **in parallel** with research/promotion, not after.

```
  ┌────────────────────────────────────────────────────────────────────────────┐
  │ PHASE 0  Mandate & Pre-flight            (Top Orchestrator)                   │
  │   define theme · scope objectives · verify MCP/build/test baseline · ROADMAP  │
  └───────────────┬────────────────────────────────────────────────────────────┘
                  ▼
  ┌──────────────────────────────┐        ┌─────────────────────────────────────┐
  │ PHASE 1 (W)  GIGA-RESEARCH    │        │ PHASE 4 (T)  TOOLING                 │
  │   dirty room · Orchestrator-  │◄──────►│   vfsls subcommands · new skills/    │
  │   Agent drives N lanes (IDA   │ runs   │   agents · parsers for new formats   │
  │   ×3 sub-waves + VFS wide)    │ along  │   (parallel with W/P/E)             │
  └───────────────┬──────────────┘        └─────────────────────────────────────┘
                  ▼
  ┌──────────────────────────────┐
  │ PHASE 2 (P)  PROMOTION        │   spec-authors REWRITE _dirty/ → committed specs
  │   firewall crossing · 1 file  │   master synthesis (barrier) · Tier-1 reconcile
  │   per author · master synth   │   orchestrator: firewall scan · journal · names.yaml
  └───────────────┬──────────────┘
                  ▼
  ┌──────────────────────────────┐
  │ PHASE 3 (E)  ENGINEERING      │   clean room · read specs only · cite // spec:
  │   staged pipeline (contracts  │   one engineer per project · DAG downward-only
  │   → components → integration) │   + test-engineer xUnit coverage
  └───────────────┬──────────────┘
                  ▼
  ┌──────────────────────────────┐
  │ PHASE 5 (R)  REVIEW + GATES   │   4 read-only reviewers (render/C#/clean-room/arch)
  │   → fix wave → HARD GATES     │   → fix wave → build 0/0 + full test suite green
  └───────────────┬──────────────┘
                  ▼
  ┌──────────────────────────────┐
  │ PHASE 6 (C)  CONSOLIDATION    │   journal.md · names.yaml · memory · ROADMAP status
  │   + COMMIT (maintainer asks)  │   preservation-archivist pass · commit ONLY on request
  └──────────────────────────────┘
```

**Golden rule of sequencing:** a phase starts only when the previous phase's **exit criteria** pass
(§3.3 — phase boundaries are barriers). Within a phase, fan out maximally; between phases, gate
strictly.

---

## 6 · Phase 0 — Mandate & Pre-flight

**Tier:** Top Orchestrator. **Output:** a new `# CYCLE N` section in `Docs/ROADMAP.md`.

### 6.1 Objectives & sub-objectives

| Objective | Sub-objectives | Done when |
|-----------|----------------|-----------|
| **0.1 Capture the mandate** | quote the maintainer verbatim; record the "why now" | Quoted at the top of the cycle section |
| **0.2 Define theme & scope** | one-paragraph in-scope; explicit out-of-scope list | Both written; out-of-scope is non-empty |
| **0.3 State the evidence baseline** | inventory relevant committed specs; list known gaps this cycle closes | Baseline table in the cycle section |
| **0.4 Enumerate the lanes** | draft the Phase-1 research lane table and the Phase-3 engineering lane table; decide which phases get a Tier-2 Orchestrator-Agent | Lane tables drafted (§7, §9 templates) |
| **0.5 Verify the tool baseline** | IDA MCP UP; Godot MCP UP (if needed); build 0/0; tests green; VFS reachable | All pre-flight boxes (§6.2) ticked |
| **0.6 Name the master deliverable** | pick the `Docs/RE/specs/<theme>.md` synthesis target | Named in the cycle section |

### 6.2 Pre-flight checklist (run before launching any wave)

- [ ] `/ida-mcp-connect` → IDA MCP reachable with the database open (required for IDA lanes).
  **Confirm the connection is on `http://127.0.0.1:13337/mcp?ext=dbg`** and that the `mcp__ida__dbg_*`
  tools are present (the base `/mcp` endpoint omits them) — see §0.3. No `dbg_*` tools ⇒ wrong endpoint.
- [ ] `/godot-mcp-connect` → editor (9600) + game (9601) bridges UP (only if interactive Godot work).
- [ ] `dotnet build MartialHeroes.slnx` → **0 err / 0 warn** (clean starting line).
- [ ] `dotnet test MartialHeroes.slnx` → **all suites green** (known-good baseline to regress against).
- [ ] VFS reachable (`clientdata/` present, or `MH_CLIENT_DIR` / `client_dir.cfg` resolves).
- [ ] `Docs/RE/_dirty/` exists and is gitignored (`/re-workspace-init` if missing).
- [ ] `git status` clean (or a dedicated branch created if on `master`).
- [ ] **Decide the command structure:** which phases the Top Orchestrator drives directly vs. delegates
  to a Tier-2 Orchestrator-Agent (use the §2.2 decision table).

### 6.3 Out-of-scope discipline

Every cycle **explicitly lists what it will NOT do** (e.g. "the game server is deferred; build for
reuse by a future `MartialHeroes.Server.Console` but write no server code"). Scope creep across a
50-agent fan-out is the fastest way to burn a cycle (risk R6).

---

## 7 · Phase 1 (W) — Giga-Research wave (dirty room)

**Tier:** Top Orchestrator → (recommended for >8 lanes) a **W-Orchestrator-Agent** → dirty-room
sub-agents. **Output:** `Docs/RE/_dirty/<area>/*.md` **only**. Nothing here is committed; nothing
here is read by an engineer.

### 7.1 Doctrine — three complementary research modalities

We attack each question with up to **three modalities**, and cross-validate between them (§0.3):

1. **Static IDA — read the code.** Map functions, dispatch tables, struct/vtable layouts, loader
   logic from the disassembly/decompilation. *Forms the hypothesis.*
2. **Dynamic IDA — watch the code run (the debugger).** Breakpoint the recovered functions, run the
   original client, read registers/memory/stack, single-step. *Confirms the hypothesis* with ground
   truth (real field values, pre/post-decrypt bytes, in-memory VFS records, real timings). See §4.4.
3. **VFS / harness observation — read the maintainer's own files.** Census and field-recovery on the
   real `data.vfs` through the `vfs-inspect` harness (non-IDA). *Corroborates against real samples.*

The strongest findings are **triangulated**: a static hypothesis, confirmed under the debugger, and
matched against a real VFS sample. A finding from one modality only is promoted at lower confidence.

- **Decompose the theme into independent questions**, one per lane = one sub-objective = one agent =
  one `_dirty/` note ("how does the client decide where water is?", "what is the full N-byte layout
  of record type X?", "what opcode does action Y send?", "what are the packet bytes before and after
  decryption at runtime?").
- **The W-Orchestrator-Agent monitors the whole research block**: it batches IDA lanes (static *and*
  debugger) in sub-waves of 3 (§3.2 — they share the single IDB), fans out VFS lanes wide, keeps the
  file-ownership ledger over `_dirty/<area>/**`, retries a dead lane once, and rolls up one summary.
- **Debugger lanes share the IDB and the running process** — never run two debugger sessions at once;
  serialize them within the IDA sub-wave budget.
- **Conflicts are flagged, never silently reconciled.** Static-reading vs. debugger-observation vs.
  byte-observation disagreements get a `CONFLICT:` marker for the spec-author to arbitrate in Phase 2.
- **Confidence is reported** per lane (high / medium / low), and **whether it was debugger-confirmed**,
  so promotion can weight it.

### 7.1bis Flagship recurring objective — VFS understood through `Main.exe`

Because the VFS underpins every asset, **most cycles carry (or build on) the `data.vfs`/`data.inf`
recovery objective** (§0.4). Its canonical sub-objective decomposition:

| Sub-objective | Modality | Deliverable |
|---------------|----------|-------------|
| `data.inf` index record layout (offset/size/name-hash/flags) | static IDA + debugger-confirmed (read records in memory) | `_dirty/formats/vfs-index.raw.md` |
| Archive open + mount sequence | static IDA | `_dirty/recon/vfs-mount.md` |
| Path → entry lookup (hashing/search) | static IDA + debugger (watch a real lookup) | `_dirty/recon/vfs-lookup.md` |
| Entry read / decompress (mmap? LZ4? raw slice?) → engine buffer | static IDA + debugger (inspect the returned buffer) | `_dirty/formats/vfs-read.raw.md` |
| Lifetime/caching (streamer thread, sync ring) | static IDA | `_dirty/recon/vfs-cache.md` |

Promoted to `Docs/RE/formats/pak.md` + a VFS subsystem spec, this is what lets `Assets.Vfs` open the
archive **the way the original does**, byte-for-byte (the existing `pak.md` was validated this way).

### 7.2 Objective → sub-objective lane table template

> One row = one sub-objective = one agent = one deliverable. **Type** is the modality (§7.1):
> `IDA-S` = static IDA, `IDA-D` = dynamic / debugger, `VFS` = harness observation. IDA lanes (static
> *and* debugger) → dirty-room IDA analysts (batched ×3, single IDB); VFS lanes → `vfs-data-analyst`
> (wide). Many lanes are best run as a static lane *then* a debugger lane that confirms it.

| # | Lane (sub-objective) | Type | Agent | Question (single, sharp) | Deliverable (`_dirty/…`) | Confidence |
|---|----------------------|------|-------|--------------------------|--------------------------|-----------|
| 1 | `<lane-slug>` | IDA-S | `re-static-analyst` | `<one precise question>` | `_dirty/<area>/<lane>.md` | — |
| 2 | `<lane-slug>` | IDA-S | `re-protocol-analyst` | `<opcode/packet question>` | `_dirty/<area>/<lane>.md` | — |
| 3 | `<lane-slug>` | IDA-S | `re-crypto-analyst` | `<cipher/key-schedule question>` | `_dirty/crypto/<lane>.md` | — |
| 4 | `<lane-slug>` | IDA-S | `re-struct-cartographer` | `<object layout / vtable question>` | `_dirty/structs/<lane>.md` | — |
| 5 | `<lane-slug>` | IDA-S | `re-asset-format-analyst` | `<binary asset format question>` | `_dirty/formats/<lane>.raw.md` | — |
| 6 | `<lane-slug>` | IDA-S | `re-animation-analyst` | `<skinning/animation math question>` | `_dirty/formats/<lane>.raw.md` | — |
| 7 | `<lane-slug>` | IDA-S | `ida-script-author` | `<mass classification / bespoke IDAPython>` | `_dirty/static/<lane>.md` | — |
| 8 | `<lane-slug>` | **IDA-D** | `re-protocol-analyst` / `re-crypto-analyst` | `<runtime confirmation: real values/bytes/timings via the debugger>` | `_dirty/<area>/<lane>.dyn.md` | — |
| 9 | `<lane-slug>` | VFS | `vfs-data-analyst` | `<data-file / census question>` | `_dirty/formats/<lane>.raw.md` | — |

### 7.3 Exit criteria (barrier into Phase 2)

- [ ] The **critical-path lanes** (those that unblock the most engineering) have all returned.
- [ ] At least the agreed quorum of remaining lanes returned (e.g. "≥ N of M").
- [ ] All `_dirty/` notes carry a confidence rating; all conflicts are flagged.
- [ ] The W-Orchestrator-Agent (or Top Orchestrator) has read every return and drafted the
  Phase-2 promotion map.

---

## 8 · Phase 2 (P) — Spec Promotion (firewall crossing)

**Tier:** Top Orchestrator dispatches spec-authors (one file each); Top Orchestrator does the
serialized merges (§3.4). **Output:** committed neutral specs under
`Docs/RE/{specs,formats,structs,packets}/` + `opcodes.md`.

### 8.1 Doctrine

- **One author owns one spec file** → zero write contention, clean parallelism (§3.1).
- **REWRITE, never copy.** The author reads the `_dirty/` note(s) and writes fresh neutral prose:
  behavior in words, layouts as offset/size/type tables, opcodes as catalogue rows, wire fields as
  YAML. Every Hex-Rays artifact and raw VA is stripped.
- **One master synthesis doc** ties the subject specs together (e.g. `specs/world_systems.md`). It is
  written **last (a §3.3 barrier)**, from the *cleaned* subject specs — never from `_dirty/` directly.
- **The serialized post-promotion steps are Tier-1-only** (not delegated).

### 8.2 Promotion map template

| New / updated spec | Source `_dirty/` lane(s) | Author | Unblocks (Phase-3 lane) |
|--------------------|--------------------------|--------|-------------------------|
| `specs/<subject>.md` (NEW/UPD) | lane 1 + lane 8 | `asset-spec-author` / `protocol-spec-author` | `<engineering lane>` |
| `formats/<fmt>.md` (UPD) | lane 5 | `asset-spec-author` | parser / mapping work |
| `structs/<obj>.md` (NEW) | lane 4 | `asset-spec-author` | domain / protocol work |
| `packets/<dir>_<op>.yaml` (NEW) | lane 2 | `protocol-spec-author` | protocol handler work |
| `opcodes.md` (UPD rows) | lanes 2, 5 | `protocol-spec-author` | router / handler work |
| `specs/<theme>.md` (MASTER) | all of the above (cleaned) | orchestrator or a synthesis author | — |

### 8.3 Top-Orchestrator post-promotion steps (serialized, NOT delegated)

- [ ] **Firewall scan** the new specs (no autonames, no raw VAs; image-base / sample-data values OK).
- [ ] **Reconcile opcode catalogue**: add omitted rows in sorted order; check for duplicate ids.
- [ ] **Merge canonical names** into `Docs/RE/names.yaml` (new function/opcode/subsystem names).
- [ ] **Append a provenance entry** to `Docs/RE/journal.md` (`/re-session-log`): date, binary sha
  prefix, subsystems touched by canonical name, spec files produced. Append-only; no pseudo-code.

### 8.4 Exit criteria (barrier into Phase 3)

- [ ] Every Phase-3 lane has at least one committed spec to read from.
- [ ] `/clean-room-firewall-check` passes on the new/changed specs.
- [ ] Every constant that will appear in C# is citable to a committed spec.
- [ ] `journal.md` + `names.yaml` updated by the Top Orchestrator.

---

## 9 · Phase 3 (E) — Engineering wave (clean room)

**Tier:** Top Orchestrator → (recommended) an **E-Orchestrator-Agent** driving the staged pipeline →
clean-room engineers + `test-engineer`. **Output:** fresh C# / Godot, layer-respecting, every
constant cited. **No agent here reads `_dirty/` or touches IDA.**

### 9.1 Doctrine

- **Staged pipeline, not a flat blast.** The proven shape is three stages (a §3.3 pipeline; later
  stages of one feature can run while earlier stages of another finish):
  1. **Contracts / foundations** — engine-free primitives, parsers, Application event-hub channels,
     interfaces. Distinct projects → maximum parallelism, zero contention.
  2. **Components** — the consumers built against Stage-1 contracts (Godot nodes, use-cases, handlers).
  3. **Integration** — wire components into the live scene/HUD/context + input map; full build + smoke.
- **One engineer per project per wave** (§3.1) — the fleet has exactly one specialist per project for
  this reason.
- **Respect the layer DAG** (downward-only): `01.Infrastructure.Shared` → `02.Network.Layer` →
  `03.Storage.Assets` → `04.Client.Core` → `05.Presentation`. A lower layer never references a higher.
- **Engine-free below layer 05** — no `using Godot;` in layers 01–04. Use `global::Godot.*` inside
  layer-05 namespaces to dodge the sibling-namespace collision trap.
- **Zero-alloc on hot paths** — `Span<byte>`/`ReadOnlyMemory<byte>` slices, `[StructLayout(Pack=1)]`
  + `[InlineArray]` wire/asset structs, no LINQ/closures/boxing on per-frame or per-packet paths.
- **Every magic number cites `// spec:`.** No uncited offsets, sizes, or opcodes.
- **`test-engineer` runs alongside**, adding deterministic xUnit coverage (real-VFS tests skip
  gracefully when `clientdata/` is absent; capture-derived vectors for crypto/protocol).

### 9.2 Engineering lane table template (objective → stage → sub-objective)

| Stage | Lane (sub-objective) | Agent | Project (layer) | Deliverable | Spec(s) read |
|-------|----------------------|-------|-----------------|-------------|--------------|
| A | `<contracts>` | `domain-engineer` / `assets-parser-engineer` / `application-engineer` | `<project>` | `<types/parsers/channels>` | `specs/<x>.md` |
| A | `<contracts>` | `network-protocol-engineer` / `network-crypto-engineer` | `02.Network.Layer` | `<packet structs / cipher>` | `packets/*.yaml`, `opcodes.md`, `specs/crypto.md` |
| B | `<component>` | `godot-presentation-engineer` / `godot-ui-engineer` / `godot-input-engineer` | `05.Presentation` | `<scene/node/HUD/window>` | `specs/<x>.md` |
| B | `<component>` | `godot-skinning-specialist` | `05.Presentation` | `<skinning/animation>` | `specs/skinning.md`, `formats/animation.md` |
| C | `<integration>` | `godot-presentation-engineer` | `05.Presentation` | wire + input map + smoke | — |
| ∥ | tests | `test-engineer` | `tests/*` | xUnit coverage | — |

### 9.3 Exit criteria (barrier into Phase 5)

- [ ] `dotnet build MartialHeroes.slnx` → **0 err / 0 warn**.
- [ ] New code respects the DAG (no upward/sideways refs, no `using Godot;` below 05).
- [ ] Every new constant cites a committed spec (`/spec-citation-audit` clean).
- [ ] `test-engineer` coverage added; `dotnet test` green.
- [ ] Headless boot smoke clean (Godot console exe, both boot flows if applicable; cfg restored).

---

## 10 · Phase 4 (T) — Tooling wave

**Tier:** Top Orchestrator dispatches tooling/skill/agent authors. **Runs in parallel with W/P/E** —
not gated behind them. **Output:** new/extended skills, `vfsls` subcommands, parsers, agents.

### 10.1 Objectives

- **Promote one-off research scanners into reusable tooling.** If a lane wrote a throwaway harness to
  census `.mot` headers, fold it into `vfs-inspect` as a `vfsls scan-<x>` subcommand so the next
  format question is a one-liner. ("Tools to understand formats" is a standing mandate.)
- **New skills only where a real gap appeared** during the cycle (`skill-author`), never speculative.
- **New agents only where a recurring specialist role emerged** (`agent-author`) — this is how the
  **fleet grows** (§2.4). Place it correctly on the firewall (dirty analyst / spec bridge / clean
  engineer) with a right-sized tool allowlist, then reuse it every cycle thereafter.
- **Parsers for newly-spec'd formats** (`assets-parser-engineer`) with `test-engineer` coverage.
- **Audit the tooling itself** (`tooling-auditor`) after any `.claude/` change.

### 10.2 Lane table template

| # | Lane | Agent | Deliverable | Verified by |
|---|------|-------|-------------|-------------|
| T1 | `vfsls` subcommand `scan-<x>` | (orchestrator / tooling author) | new subcommand | real-VFS smoke run |
| T2 | new skill `/<name>` | `skill-author` | `SKILL.md` + bundled script | `tooling-auditor` PASS |
| T3 | new agent `@<name>` | `agent-author` | `.claude/agents/<name>.md` | `tooling-auditor` PASS |
| T4 | parser for `<fmt>` | `assets-parser-engineer` | parser + tests | `dotnet test` green |

### 10.3 Exit criteria

- [ ] New subcommands smoke-tested on the real VFS (print counts/headers, never payload bytes).
- [ ] `tooling-auditor` PASS (every hook fail-open & advisory; valid YAML frontmatter; no dupes;
  `settings.json` wires only existing hooks; `CLAUDE.md` inventory matches disk).
- [ ] No copyrighted bytes printed or committed by any tool.

---

## 11 · Phase 5 (R) — Review, Fix & Hard Gates

**Tier:** Top Orchestrator → (recommended) an **R-Orchestrator-Agent** that runs the four reviewers
in parallel, confirms findings, and drives the fix wave. **Output:** a PASS verdict on every
dimension + a green build/test gate.

### 11.1 The four review dimensions (read-only, fully parallel — they edit nothing, §3.1)

| Reviewer | Verifies | Verdict form |
|----------|----------|--------------|
| `godot-render-reviewer` | headless + screenshot loop: the feature actually renders; AABB/coordinate dumps; no town/character regression | PASS / PASS_WITH_NOTES / FAIL + file:line |
| `csharp-reviewer` | correctness, nullability, C# 14 idioms, conventions on all new layer C# | PASS / … + file:line |
| `clean-room-auditor` | no decompiler artifacts; every constant cites its spec; `_dirty/` untracked; journal/names staged | PASS / FAIL |
| `architecture-guardian` | DAG still downward-only; no `using Godot;` below 05; naming (`.Pipelines`); references legal | PASS / FAIL |

(Add `perf-reviewer` when the cycle touched `Network.*` / `Assets.*` hot paths.)

### 11.2 The fix wave

The orchestrator collects all findings, **confirms** them (rejecting false positives — e.g. a flagged
offset that *does* have a citation on the line above), and dispatches a small fix wave (typically 1–3
agents, one writable scope each per §3.1) that closes **all confirmed findings**. Re-review or
spot-check after.

### 11.3 Hard gates (a cycle is NOT done until all pass)

- [ ] `dotnet build MartialHeroes.slnx --no-incremental` → **0 err / 0 warn**.
- [ ] `dotnet test MartialHeroes.slnx` → **all suites green, 0 failures**.
- [ ] `clean-room-auditor` → **PASS** (zero leaks).
- [ ] `architecture-guardian` → **PASS** (or documented, maintainer-blessed deviation).
- [ ] `godot-render-reviewer` → at least **PASS_WITH_NOTES** (notes = spec open items, not defects).
- [ ] Headless boot clean in all boot flows; `client_dir.cfg` restored byte-exact after any test edit.
- [ ] **1:1 fidelity check (§0.2):** new behaviour/format matches the documented original; any
  intentional divergence is flagged as a deviation, never shipped silently.

### 11.4 Deviation handling

When `architecture-guardian` flags a **pre-existing, downward-legal** edge the maintainer hasn't
ruled on, the orchestrator default is: **keep code as-is, document as an accepted deviation** in
`PRESERVATION_AND_ARCHITECTURE.md` + the guardian's accepted list, and surface a
`⚖️ PENDING MAINTAINER DECISION` note in the ROADMAP. Never silently refactor a dependency edge
without the maintainer's go-ahead.

---

## 12 · Phase 6 (C) — Consolidation & Commit

**Tier:** Top Orchestrator. **Output:** updated knowledge base + (on request) a commit.

### 12.1 Consolidation checklist

- [ ] `Docs/ROADMAP.md` — update every phase status **in place** with outcomes + dates + open items.
- [ ] `Docs/RE/journal.md` — final provenance entry for the cycle (`/re-session-log`).
- [ ] `Docs/RE/names.yaml` — all new canonical names merged.
- [ ] **Memory** — update the relevant `memory/*.md` facts (implementation state, render integration,
  VFS ground truth) + the `MEMORY.md` index line. Convert relative dates to absolute.
- [ ] `preservation-archivist` pass — legal/provenance docs intact, no originals staged, firewall held.

### 12.2 Commit discipline (HARD)

- **Commit ONLY when the maintainer explicitly asks.** Never on your own initiative.
- If on `master`, **branch first**.
- Re-verify the gitignore guards before staging: no `*.pak/*.vfs/*.exe/*.dll/*.pcapng/*.tsv/*.scr/`
  `*.mot/*.ted/*.bud`, no client `*.png/*.dds`, no `_dirty/`, no `.godot/`.
- Commit message: factual, scoped to the cycle theme; English. End with the required co-author trailer.

### 12.3 Cycle close-out

- [ ] ROADMAP cycle section reads as a complete, dated record (a future session can resume from it).
- [ ] All `⚖️ PENDING MAINTAINER DECISION` items are surfaced to the maintainer.
- [ ] The next cycle's candidate theme(s) noted (what is *still* not understood) — fueling §14 again.

---

## 13 · The fleet — ~50 reusable agents

The project ships a **standing fleet of ~50 agents**: ~44 project specialists + the generic utility
tier. **This roster is a primary asset — use it and reuse it every cycle (§2.4).** Pick agents by
**firewall side** first, then by project.

### 13.1 Dirty-room analysts (Tier-3) — have `mcp__ida__*`, write ONLY `_dirty/`

These agents drive IDA Pro 9.3 over the maintainer's own `Main.exe`, in **static** mode
(`decompile`/`disasm`/`xrefs_to`/`read_struct`/…) and, where it confirms ground truth, **dynamic**
mode (the IDA debugger — `dbg_start`/`dbg_add_bp`/`dbg_run_to`/`dbg_read`/`dbg_gpregs`/…). All output
is quarantined to `_dirty/` and crosses the firewall only as neutral prose (§4.4).

| Agent | Use for | Debugger-strong targets |
|-------|---------|-------------------------|
| `re-static-analyst` | function/subsystem mapping, call graphs, scene/loop/UI internals | scene-machine state at runtime |
| `re-protocol-analyst` | opcode dispatch table, packet field layouts vs. capture oracle | live packet buffers at the recv/dispatch site |
| `re-crypto-analyst` | packet cipher + key schedule (neutral algorithm description) | **bytes pre/post decrypt**, rolling key state |
| `re-struct-cartographer` | object/struct field layouts + vtables | struct instances in memory |
| `re-asset-format-analyst` | `.pak`/VFS container + binary asset blob layouts (IDA + hexdump) | **`data.inf` records + returned buffers** (§0.4) |
| `re-animation-analyst` | skinning/animation math (bind pose, inverse-bind, `.mot` sampling) | deform matrices at the skinning call |
| `vfs-data-analyst` | **non-IDA** data-file recovery by observing the maintainer's own VFS files | — (harness, not the debugger) |
| `ida-script-author` | bespoke IDAPython for questions no fixed skill covers | scripted breakpoint/dump sweeps |

### 13.2 Spec-author bridge (Tier-3) — REWRITE dirty → clean (the firewall crossing)

| Agent | Use for |
|-------|---------|
| `protocol-spec-author` | promote opcode/packet findings → `opcodes.md` + `packets/*.yaml` |
| `asset-spec-author` | promote asset-format findings → `formats/*.md` (and struct/spec prose) |

### 13.3 Clean-room engineers (Tier-3) — NO IDA, read committed specs only (one per project)

| Agent | Project (layer) |
|-------|-----------------|
| `kernel-engineer` | `Shared.Kernel`, `Shared.Diagnostics` (01) |
| `network-abstractions-engineer` | `Network.Abstractions` (02) |
| `network-protocol-engineer` | `Network.Protocol` (02) |
| `network-crypto-engineer` | `Network.Crypto` (02) |
| `network-transport-engineer` | `Network.Transport.Pipelines` (02) |
| `assets-vfs-engineer` | `Assets.Vfs` (03) |
| `assets-parser-engineer` | `Assets.Parsers` (03) |
| `assets-mapping-engineer` | `Assets.Mapping` (03) |
| `domain-engineer` | `Client.Domain` (04) |
| `application-engineer` | `Client.Application` (04) |
| `client-infrastructure-engineer` | `Client.Infrastructure` (04) |
| `godot-presentation-engineer` | `Client.Godot` 3D world (05) |
| `godot-ui-engineer` | `Client.Godot` HUD/windows/menus (05) |
| `godot-input-engineer` | `Client.Godot` camera/input (05) |
| `godot-skinning-specialist` | `Client.Godot` skinning/animation debt (05) |

### 13.4 Quality & verification (Tier-3, read-only — run in parallel)

| Agent | Role |
|-------|------|
| `clean-room-auditor` | firewall leak audit (PASS/FAIL) before any commit |
| `architecture-guardian` | DAG / engine-free / naming enforcement |
| `csharp-reviewer` | C# correctness/nullability/idiom review |
| `perf-reviewer` | zero-alloc discipline on Network.*/Assets.* hot paths |
| `godot-render-reviewer` | eyes-on render QA via headless + screenshot |
| `test-engineer` | xUnit coverage incl. capture/spec-derived vectors |
| `build-doctor` | diagnose build/restore/SDK/slnx/csproj failures |
| `preservation-archivist` | legal/provenance docs + no-commit-of-originals guard |
| `tooling-auditor` | audit `.claude/` harness consistency after tooling changes |

### 13.5 Meta-authors — grow & maintain the fleet itself

| Agent | Role |
|-------|------|
| `agent-author` | author/repair an agent `.md` (right firewall placement + tool allowlist) |
| `skill-author` | author/repair a `/skill` |
| `hook-author` | author/repair an advisory, fail-open hook |

### 13.6 Generic utility tier — for work no specialist owns

| Agent | Role |
|-------|------|
| `Explore` | broad read-only fan-out search across many files (returns conclusions, not dumps) |
| `Plan` | design a step-by-step implementation plan for a task |
| `godot-mcp-operator` | drive the live Godot editor/game through the `godot` MCP |
| `general-purpose` | catch-all for multi-step tasks that fit no specialist |
| `claude-code-guide` | answer Claude Code / Agent SDK / Anthropic API questions |

> **Tier-2 Orchestrator-Agents** are not a separate roster entry — any capable agent (commonly
> `general-purpose`, or a specialist for a same-domain block) can be **chartered as an
> Orchestrator-Agent** per §2.2 to own and monitor a big block and fan out the Tier-3 specialists.

---

## 14 · Cycle Skeleton (copy-paste to start a new cycle)

> Paste under a new `# CYCLE N — <theme>` heading in `Docs/ROADMAP.md`, fill every `<placeholder>`,
> delete the guidance comments. Keep statuses updated **in place** as waves land.

```markdown
# CYCLE <N> — <theme: which subsystem(s) this cycle conquers> (launched <YYYY-MM-DD>)

**Mandate (maintainer):** "<verbatim quote of what the maintainer asked for, and why now>".
<one paragraph: what this cycle attacks and the end state it targets>.

**Master deliverable:** `Docs/RE/specs/<theme>.md` — <the authoritative synthesis this cycle produces>.

**Out of scope (deferred):** <explicit non-goals — e.g. the game server>.

**Command structure:** Top Orchestrator + Tier-2 Orchestrator-Agents for: <phases that get one, e.g.
W1 (20 lanes), E (3-stage pipeline), R (review+fix)>. Other phases driven directly.

## Evidence baseline
- Relevant committed specs going in: <list>.
- Known gaps this cycle closes: <list>.
- Tool baseline verified: IDA MCP <UP/DOWN> · build <0/0> · tests <green> · VFS <reachable>.

## Phase <N>-W1 — GIGA RESEARCH (dirty room, <K> lanes) — driven by W-Orchestrator-Agent
IDA lanes (sub-waves of 3, single IDB) + VFS lanes (parallel, harness-only).
Output: `Docs/RE/_dirty/<area>/*.md` ONLY. Ledger: one writer per `_dirty/<area>/<lane>.md`.

| # | Lane (sub-objective) | Type | Agent | Question | Deliverable | Conf |
|---|----------------------|------|-------|----------|-------------|------|
| 1 | <slug> | IDA | <agent> | <question> | _dirty/<area>/<lane>.md | — |
| … | … | … | … | … | … | — |

**W1 EXIT:** critical-path lanes returned + quorum of the rest; confidence rated; conflicts flagged.
**W1 STATUS — <PENDING/DONE date>:** <headline findings fed to W2>.

## Phase <N>-W2 — PROMOTION (spec-authors, 1 file each)
| New/updated spec | Source lane(s) | Author | Unblocks |
|---|---|---|---|
| specs/<subject>.md | lane <n> | <author> | <eng lane> |
| … | … | … | … |
Master synthesis written LAST (barrier). Orchestrator post-promotion (serialized, Tier-1 only):
firewall scan · opcode reconcile · names.yaml · journal.md.
**W2 STATUS — <PENDING/DONE date>:** <specs/packets written; counts>.

## Phase <N>-E — ENGINEERING (clean room, staged pipeline) — driven by E-Orchestrator-Agent
One engineer per project per stage (no two writers in one project per wave).
- **Stage A (contracts):** <parsers / channels / packet structs> — <agents>.
- **Stage B (components):** <Godot nodes / use-cases / handlers> — <agents>.
- **Stage C (integration):** wire + input map + headless smoke — <agent>.
- **∥ tests:** test-engineer xUnit coverage.
**E EXIT:** build 0/0 · DAG clean · constants cited · tests green · headless boot clean.
**E STATUS — <PENDING/DONE date>:** <what landed; test count>.

## Phase <N>-T — TOOLING (parallel with W/P/E)
| # | Lane | Agent | Deliverable |
|---|------|-------|-------------|
| T1 | <vfsls scan-x / new skill / new agent / parser> | <agent> | <deliverable> |
**T STATUS — <PENDING/DONE date>:** <subcommands/skills/agents added; smoke results>.

## Phase <N>-R — REVIEW + FIX + GATES — driven by R-Orchestrator-Agent
4 read-only reviewers in parallel (render / C# / clean-room / architecture) → confirm findings →
fix wave (one writable scope each) → hard gates.
**R STATUS — <PENDING/DONE date>:** <verdicts; fixes applied>.
**FINAL GATE:** build 0/0 (--no-incremental) · all suites green · firewall PASS.

### ⚖️ PENDING MAINTAINER DECISION (if any)
<edge / deviation surfaced this cycle, with options (a) bless / (b) refactor>.

## Phase <N>-C — CONSOLIDATION
ROADMAP statuses updated in place · journal.md · names.yaml · memory · preservation pass.
Commit ONLY on maintainer request (branch first if on master).

— *Maintained by the orchestrator. Update phase statuses in place as waves complete.*
```

---

## 15 · Risk register template

Carry a short risk list per cycle. The recurring ones:

| ID | Risk | Trigger | Mitigation |
|----|------|---------|------------|
| R1 | **Evidence simply isn't in the binary/VFS** (e.g. all `.mot` samples are stubs) | A lane finds only empty/placeholder data | Add a lane that checks alternate sources (procedural generation? another container? network-fetched?); document the negative result explicitly |
| R2 | **Layout/behavior hardcoded in the binary** (no data file) | No data-driven source found | Document the recovered constants as facts/interop data (legal) and implement from those |
| R3 | **Convention ambiguous even after IDA + VFS** (e.g. matrix major-order) | Spec can't pin a single interpretation | Hypothesis matrix tested empirically in an isolated Godot probe scene until behavior matches; document as "empirically verified against renderer behavior" |
| R4 | **IDA / Godot MCP unreachable mid-cycle** | `mcp__*` tools fail | Re-register per CLAUDE.md (`claude mcp add …`); fall back to bundled IDAPython snippets / headless console exe |
| R4b | **Wrong IDA MCP endpoint — `dbg_*` tools absent** | Connected to base `/mcp` instead of `?ext=dbg`; debugger lanes can't run | Re-register on `?ext=dbg` (`claude mcp add --transport http ida "http://127.0.0.1:13337/mcp?ext=dbg"`) + restart session; verify `dbg_*` present in pre-flight (§6.2) |
| R5 | **Firewall leak slips into a spec or C#** | Auditor flags pseudo-C / uncited offset | Block the commit; route back to the spec-author to rewrite; never patch by deleting the evidence trail |
| R6 | **Scope creep across the fan-out** | Lanes drift beyond the cycle theme | Enforce the §6.3 out-of-scope list; defer drift to a future cycle's candidate-theme note |
| R7 | **Large asset copy / long op interrupted** | Partial state | Use resumable ops (robocopy); verify sizes before cutover; keep the original as backup until acceptance |
| R8 | **Write collision between two agents** (race condition) | Two lanes target one path / two engineers one project | Pre-flight ledger check (§3.1); one writer per path per wave; worktree isolation only if partition impossible (§3.5) |
| R9 | **IDA sub-wave starvation** | >3 `mcp__ida__*` agents launched at once | Batch IDA lanes in sub-waves of 3 (§3.2) |
| R10 | **`client_dir.cfg` drift** | Parallel/aborted Godot smokes leave cfg mutated | Serialize cfg-mutating smokes; verify byte-exact restore after each (§3.4) |
| R11 | **Debugger session instability / anti-debug** (`Main.exe` ships XTrap anti-cheat) | `dbg_*` lanes hang, detach, or the process resists debugging | Serialize debugger lanes (one process at a time, §7.1); fall back to static + VFS triangulation; document a runtime fact as "static-only, unconfirmed" rather than guess |
| R12 | **Drift from 1:1 fidelity** | A re-implementation "works" but diverges from the original's behaviour/format | Hold the §0.2 bar: verify against the documented original (and, where decisive, against the running client under the debugger); mark deviations explicitly as deviations, never silently |

---

*This template is the method. `Docs/ROADMAP.md` is the record of every run. `CLAUDE.md` and
`PRESERVATION_AND_ARCHITECTURE.md` are the ground truth for layers, conventions, and the legal basis.
When this template and disk reality disagree, **disk reality wins** — then fix the template.*
