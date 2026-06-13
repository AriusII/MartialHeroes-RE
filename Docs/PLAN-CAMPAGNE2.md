# Campaign 2 — Making `doida.exe` Fully Legible (Comprehension + IDA Annotation)

> **What this is.** The method blueprint for a dedicated, repeatable reverse-engineering campaign
> whose product is **not C#** but a **fully understood and fully annotated IDA database** of the
> legacy Martial Heroes client (`doida.exe` / `Main.exe`, *D.O. Online*, 2003–2008). Two macro-modes,
> chained: a **READONLY comprehension** wave that understands every function (references, calls,
> usage, utility), then a **WRITE annotation** wave that renames + comments the IDB from that
> understanding — including propagating a rename across every reused site, and running IDAPython.
>
> **Why it exists.** The project's clean specs are mature (27 subsystem specs, 16 formats, 62 packet
> YAMLs, 7 struct tables). The bottleneck is now the **binary itself**: of **25,973 functions, only
> 2,790 are named** — **21,278 are still `sub_xxxx`** with no comments. Every future engineering
> cycle re-pays the cost of re-reading an opaque binary. Campaign 2 hardens the analysis substrate so
> that reading the IDB *is* reading the design.
>
> **Relationship to the other docs.** This sits alongside `Docs/CAMPAIGN_TEMPLATE.md` (the general
> W▸P▸E▸R▸C engineering-cycle method) — it **reuses its doctrine** (§1 hierarchy, §2 three tiers, §3
> concurrency, §4 firewall) and **specializes** it for comprehension+annotation. The run record for
> Campaign 2 lives in **`Docs/ROADMAP-CAMPAGNE2.md`** (this campaign's own tracker). It does **not**
> touch `Docs/ROADMAP.md`, which belongs to the concurrent Cycle 4 session.
>
> **Legal backbone.** EU Software Directive **2009/24/EC Art. 6**. Annotating the IDB is dirty-room
> work and **never commits** — see §3.

---

## Table of contents

0. [Charter & the firewall posture for IDB writes](#0--charter--firewall-posture)
1. [Governing decisions (the four locked choices)](#1--governing-decisions)
2. [The campaign apparatus — three new agents](#2--the-campaign-apparatus--three-new-agents)
3. [Firewall & concurrency invariants (specialized)](#3--firewall--concurrency-invariants)
4. [The phase pipeline (0 · A · B · C · D · E · T)](#4--the-phase-pipeline)
5. [The campaign glossary & the sync-back contract](#5--the-campaign-glossary--sync-back)
6. [Cluster taxonomy & priority order](#6--cluster-taxonomy--priority-order)
7. [Coordination with Cycle 4](#7--coordination-with-cycle-4)
8. [Risk register (specialized)](#8--risk-register)
9. [Run skeleton (paste into ROADMAP-CAMPAGNE2.md)](#9--run-skeleton)

---

## 0 · Charter & firewall posture

### 0.1 The will

Make `doida.exe` **legible**: every function that matters carries a **canonical name** and a
**neutral comment** describing its role, its callers/callees, and how it is used — so that opening
the IDB on any subsystem reads like documentation, not like an unknown binary. Comprehension first,
annotation second; the comprehension *is* what guides the annotation.

### 0.2 The product

| Deliverable | Where | Committed? |
|-------------|-------|-----------|
| Per-function comprehension dossiers | `Docs/RE/_dirty/campaign2/comprehension/<cluster>/*.md` | No (gitignored) |
| The reconciled campaign glossary | `Docs/RE/_dirty/campaign2/glossary.yaml` | No (gitignored) |
| **A renamed + commented IDB** | `doida.exe.i64` (the live database) | **No — never** |
| Canonical names synced into the glossary | `Docs/RE/names.yaml` (`functions:`/`globals:`) | **Yes** |
| Provenance entry | `Docs/RE/journal.md` | **Yes** |

> **No C#.** Campaign 2 writes no implementation code. It is a binary-comprehension campaign. (Its
> output makes the *next* engineering cycle cheaper, but that cycle is out of scope here.)

### 0.3 Why writing to the IDB is firewall-safe

Annotating the IDB (renames, comments, applied struct/enum types) is **dirty-room work**:

- The IDB (`doida.exe.i64`) and the binary (`doida.exe`) are **gitignored and never committed**. No
  annotation ever leaves the dirty room as a file.
- The only things that cross the firewall are **canonical names** (neutral role-words like
  `RecvPacketDispatch`, never `sub_xxxx`, never a mangled name) into `names.yaml`, and **provenance
  prose** into `journal.md` — exactly the project's existing, sanctioned glossary mechanism.
- **No Hex-Rays pseudo-C** is ever pasted into a committed file or into conversation context.
  IDB **comments** may be richer than a glossary line (they are never committed) but must remain
  **neutral interop documentation** — "this function parses the 24-byte VFS index header", never
  "paste this into C#".
- **No copyrighted bytes** (binary, asset, or runtime memory) enter the repo or the conversation.

This is the same posture the project already runs (`ida-naming-sync` is explicitly "allowed to WRITE
to the IDB … firewall-safe because the glossary is clean"). Campaign 2 scales that posture up.

---

## 1 · Governing decisions

Four choices were locked at campaign start; they shape every phase.

| # | Decision | Choice | What it means operationally |
|---|----------|--------|------------------------------|
| **D1** | IDB-write governance | **Direct + sync-back** | Annotators apply renames/comments **directly** in the IDB from the reconciled **campaign glossary** (not gated one-by-one through `names.yaml` first). At the end, applied names are **pulled back and merged into `names.yaml`**. Faster, matches "freely rename incl. reused elements"; legal because the IDB never commits. |
| **D2** | Orchestration substrate | **New Orchestrator-Agents** | Author agents that themselves hold the `Agent` tool and fan out their own Tier-3 workers (§2). The current fleet has none. |
| **D3** | Scope / depth | **Prioritized subsystems** | Attack high-value clusters first (network/dispatch → crypto → VFS/loaders → scene-machine → effects), then expand outward (§6). Not whole-binary breadth-first (21k functions). |
| **D4** | Coordination with Cycle 4 | **Lead for everyone** | Campaign 2 is the **source of truth for IDB naming** — it annotates the whole binary including the boot→login→charselect spine; Cycle 4 consumes the annotated IDB. One writer of `names.yaml` at a time (§7). |

---

## 2 · The campaign apparatus — three new agents

D2 requires agents that deploy their own sub-agents. We mint three (via `agent-author`), placed
precisely on the firewall. They extend the fleet permanently.

### 2.1 `re-comprehension-orchestrator` (Tier-2, READONLY)

- **Tools:** `Agent, Read, Write, Grep, Glob, mcp__ida__*`. Uses IDA **read-only**; writes **only**
  under `Docs/RE/_dirty/campaign2/**`.
- **Charter:** owns ONE cluster block (e.g. "network/dispatch"). Fans out Tier-3 READONLY analysts
  (`re-static-analyst`, `re-protocol-analyst`, `re-crypto-analyst`, `re-struct-cartographer`,
  `re-asset-format-analyst`, `re-animation-analyst`, `ida-script-author`) in **IDA sub-waves of ~3**
  (single IDB, MCP saturation). Maintains a file-ownership ledger over
  `_dirty/campaign2/comprehension/<cluster>/**`. Retries a dead lane once.
- **Reconciles into:** one cluster **comprehension dossier** + two machine-readable manifests for the
  write phase — `names.proposed.yaml` (address → proposed canonical name + note) and
  `comments.proposed.md` (address → proposed neutral comment), each lane carrying a confidence and
  any `CONFLICT:` markers.
- **Never:** renames/comments the IDB; edits Tier-1 serialized files; reads/writes committed specs.

### 2.2 `re-annotation-orchestrator` (Tier-2, WRITE)

- **Tools:** `Agent, Read, Write, mcp__ida__rename, mcp__ida__set_comments,
  mcp__ida__append_comments, mcp__ida__py_exec_file, mcp__ida__py_eval, mcp__ida__set_type,
  mcp__ida__declare_type, mcp__ida__type_apply_batch, mcp__ida__enum_upsert, Bash(claude mcp *)`.
- **Charter:** owns the application of annotations for one or more clusters. **Serializes** its
  `re-ida-annotator` workers — **exactly ONE in flight** (the IDB is a single mutable resource, §3.2).
  For each cluster it drives `/ida-annotate-batch` (dry-run → review → apply). Reconciles the
  per-cluster "what was applied" reports + the pulled-back names; never edits `names.yaml` itself
  (that's the Tier-1 sync-back).
- **Inputs it requires before it may write:** a **clean, gate-passed campaign glossary** (§5). It
  refuses to write from `*.proposed.*` manifests directly — only from the reconciled glossary.

### 2.3 `re-ida-annotator` (Tier-3, WRITE worker)

- **Tools:** the write-IDA subset (`rename`, `set_comments`, `append_comments`, `py_exec_file`,
  `set_type` / `declare_type` / `type_apply_batch`, `enum_upsert`) + `Read, Write` (writes only
  `Docs/RE/_dirty/campaign2/applied/**`).
- **Mission:** for ONE cluster, apply renames (from the glossary) + comments + struct/enum types via a
  **single, re-runnable, idempotent IDAPython script** (dry-run first, apply on confirmation). Stage a
  report of applied symbols + a pull-back of the cluster's current IDB names to `_dirty/`.
- **Propagation:** renaming a single underlying symbol (a shared helper, a global, a struct field)
  propagates to **every xref automatically** — that is how a rename "affects multiple reused
  elements". For struct fields, `declare_type` once + `type_apply_batch` propagates field names to
  every site the type is applied. The annotator deliberately renames the **canonical symbol once**.

> **Why a Tier-3 worker at all, if writes are serialized?** Separation of concerns: the worker owns
> the *mechanics* of one cluster's apply (script authoring, dry-run diff, error handling) and reports
> a clean result; the orchestrator owns *sequencing and reconciliation*. The worker is also reusable
> outside a full campaign (annotate one freshly-understood cluster on demand).

---

## 3 · Firewall & concurrency invariants

Inherits everything in `CAMPAIGN_TEMPLATE.md §3–§4`. The campaign-specific sharpenings:

### 3.1 Firewall (specialized)
- All comprehension output is quarantined to `Docs/RE/_dirty/campaign2/**` (gitignored).
- IDB annotations never commit. Only `names.yaml` (neutral names) + `journal.md` (provenance) do.
- IDB comments are neutral interop documentation; **no pseudo-C, no mangled names, no "copy to C#"**.
- Conversation context never ingests copyrighted bytes (binary/asset/runtime memory).

### 3.2 Concurrency (the load-bearing rule)
- **WRITE to the IDB is strictly serialized.** Exactly **one** `re-ida-annotator` in flight. Never two
  writers on the database. Each apply is **one atomic IDAPython script**, **dry-run before apply**,
  **idempotent** (safe to re-run).
- **READONLY IDA fans out in sub-waves of ~3** (MCP saturates ~3 heavy consumers on one IDB).
- **VFS / non-IDA lanes fan out wide** (no shared scarce resource) — but Campaign 2 is IDA-centric.
- **File-ownership ledger** over `_dirty/campaign2/**`: one writer per path per wave.

### 3.3 Tier-1-only serialized files (never written by a fan-out)
`Docs/RE/names.yaml`, `Docs/RE/journal.md`, `Docs/RE/_dirty/campaign2/glossary.yaml` (the merge
point), `Docs/ROADMAP-CAMPAGNE2.md` status edits, `CLAUDE.md`. The Top Orchestrator (main session)
applies all contributions to these **serially, once**.

---

## 4 · The phase pipeline

```
Phase 0  Mandate & Pre-flight              (Tier-1)
   ▼
Phase A  CARTOGRAPHY  (READONLY macro)     (Tier-1 → 1 re-comprehension-orchestrator)
   ▼
Phase B  DEEP COMPREHENSION (READONLY)     (Tier-2 re-comprehension-orchestrator × clusters)
   ▼
Phase C  RECONCILIATION & GLOSSARY (gate)  (Tier-1)
   ▼
Phase D  IDA ANNOTATION (WRITE, serialized)(Tier-2 re-annotation-orchestrator → re-ida-annotator)
   ▼
Phase E  SYNC-BACK · VERIFY · PROVENANCE   (Tier-1)

Phase T  TOOLING  (parallel with A–E)      author the 3 agents + /ida-annotate-batch · tooling-auditor
```

**Golden rule:** a phase starts only when the prior phase's exit gate passes. Within a phase, fan out
maximally (subject to §3.2); between phases, gate strictly. **Phase C is a hard gate** — nothing is
written to the IDB until the glossary is clean.

### Phase 0 — Mandate & Pre-flight (Tier-1)
- Capture the mandate verbatim into `ROADMAP-CAMPAGNE2.md`.
- `/ida-mcp-connect` on `http://127.0.0.1:13337/mcp?ext=dbg`; confirm `mcp__ida__dbg_*` present.
- Confirm `sha256 == names.yaml.binary.sha256`; record the **named/unnamed baseline census**.
- Create `Docs/RE/_dirty/campaign2/{cartography,comprehension,applied}/`.
- Kick off Phase T (author the apparatus).
- **Exit:** baseline recorded; apparatus authored & `tooling-auditor` PASS; namespace exists.

### Phase A — Cartography (READONLY macro)
- One `re-comprehension-orchestrator` runs the macro pass: `survey_binary`, `list_funcs`, `imports`,
  string census (`/ida-recon`, `/ida-string-hunt`), function→subsystem clustering, a call-graph
  skeleton of the spine.
- **Output:** `_dirty/campaign2/cartography/*.md` — the map of the exe + a **prioritized cluster
  backlog** (§6).
- **Exit:** every prioritized cluster has a defined function-set + an owning slot in the backlog.

### Phase B — Deep comprehension (READONLY, per cluster)
- Per prioritized cluster: a `re-comprehension-orchestrator` fans out Tier-3 analysts (sub-waves of 3).
  Each worker deep-reads a function sub-cluster — **role, callers/callees, xrefs, data-flow, usage,
  utility** — and proposes **one canonical name + one neutral comment per function**.
- **Output per cluster:** `_dirty/campaign2/comprehension/<cluster>/*.md` + `names.proposed.yaml` +
  `comments.proposed.md`, each lane confidence-rated, conflicts flagged.
- **Exit:** the cluster's critical functions are understood; manifests complete; conflicts flagged.

### Phase C — Reconciliation & glossary (Tier-1, gate)
- Tier-1 merges all `names.proposed.yaml` into `_dirty/campaign2/glossary.yaml`: dedupe,
  **canonicalize reused elements** (one name per underlying symbol; struct fields; enum members),
  resolve `CONFLICT:` markers, and run the **neutrality check** (zero `sub_/loc_/_DWORD/__thiscall`,
  zero mangled names; address keys are quoted strings; no duplicate names).
- **Exit (HARD GATE):** glossary passes the neutrality check; no duplicate names; every name maps to
  exactly one symbol. *No IDB write happens before this passes.*

### Phase D — IDA annotation (WRITE, serialized)
- The `re-annotation-orchestrator` applies, **cluster by cluster, one annotator at a time**, via
  `/ida-annotate-batch`: **dry-run → review the diff → apply** renames (from the glossary) + comments
  + struct/enum types. Scripts are idempotent and re-runnable.
- **Output:** annotated IDB + `_dirty/campaign2/applied/<cluster>.md` (applied list + pull-back).
- **Exit:** each cluster's `sub_xxxx` resorbed per the glossary; re-decompiling a sample function in
  the cluster visibly shows the applied names/comments.

### Phase E — Sync-back, verify & provenance (Tier-1)
- **Pull-back** the IDB's renamed function/global symbols and **merge into `names.yaml`**
  (`functions:`/`globals:` — first population) via the `ida-naming-sync` pull path. Neutral names only.
- **Spot-verify** legibility: re-read 3–5 renamed functions per annotated cluster.
- `/clean-room-firewall-check`: no pseudo-C / mangled names landed in `names.yaml` / `journal.md`.
- Append a `journal.md` provenance entry (`/re-session-log`): date, sha prefix, clusters annotated by
  canonical name, counts (functions renamed, comments added).
- Update `ROADMAP-CAMPAGNE2.md` statuses in place; update memory facts.
- **Exit:** `names.yaml` diff is additive & neutral; firewall PASS; journal entry written.

### Phase T — Tooling (parallel with A–E)
- `agent-author` mints the 3 agents (§2); `skill-author` mints `/ida-annotate-batch` (§5.2).
- `tooling-auditor` PASS; Tier-1 updates the `.claude/` inventory line in `CLAUDE.md` if counts shift.

---

## 5 · The campaign glossary & sync-back

### 5.1 Why a campaign glossary distinct from `names.yaml`
D1 ("Direct + sync-back") deliberately decouples the **fast working set** (the campaign glossary,
dirty, the annotators' source of truth) from the **committed canon** (`names.yaml`, Tier-1-owned).
This lets annotation move at speed without per-name round-trips through a committed file, while
keeping `names.yaml` as the clean, reviewed, committed end-state. The glossary is the bridge; the
sync-back is the promotion.

```
names.proposed.yaml (per cluster)  ──merge/dedup/canonicalize──►  glossary.yaml  (Phase C gate)
        (Phase B)                                                      │
                                                                       ▼  (Phase D: apply to IDB)
                                                              annotated IDB symbols
                                                                       │
                                                                       ▼  (Phase E: pull-back)
                                                                  names.yaml  (committed canon)
```

### 5.2 `/ida-annotate-batch` (the new skill)
A clean extension of the proven `ida-rename-batch` / `ida-naming-sync` pattern, adding comments and
types. Contract:
- **Input:** a manifest slice of `glossary.yaml` (addresses → name + comment + optional type) for one
  cluster.
- **Dry-run first, always.** Emit a per-entry verdict (`apply` / `noop` / `skip-runtime` /
  `conflict`), counts, and the SHA-256 check vs `names.yaml.binary.sha256`. Apply **only** on explicit
  confirmation.
- **Idempotent.** Re-running produces `noop` for already-applied entries.
- **Never renames compiler-runtime symbols** (reuses the existing MSVC/CRT skip patterns).
- **Writes one repo file**: the dirty applied-report under `_dirty/campaign2/applied/`. Never a
  committed spec, never `names.yaml`/`journal.md`, never C#.

---

## 6 · Cluster taxonomy & priority order

Priority follows D3 (highest interop value first). Each cluster is one Phase-B block. The Phase-A
cartography refines the exact function sets; this is the starting partition.

| Prio | Cluster | What it covers (seed) | Why first |
|------|---------|------------------------|-----------|
| 1 | **network-dispatch** | recv/send path, opcode dispatch table, packet handlers | The wire is the highest-leverage interop surface |
| 2 | **crypto-session** | packet cipher, key schedule, 0/0 handshake, LZ4 | Decryption legibility unlocks all packet reading |
| 3 | **vfs-assetio** | `data.inf`/`data.vfs` open/lookup/read, the file-open chokepoint, parsers | The VFS underpins every asset (flagship §0.4 of the template) |
| 4 | **scene-machine** | 9-state scene lifecycle, boot, transitions, main loop | The spine that ties subsystems together |
| 5 | **effects-render** | XEffect family, render passes, skinning deform | Large, visually load-bearing, partly open (OQ-EFX-*) |
| 6+ | **expand** | UI/widget toolkit, sound, combat/gameplay handlers, Lua surface, terrain | Outward from the spine as budget allows |

> **Coordination note (D4):** the scene-machine / boot cluster overlaps Cycle 4's active spine.
> Campaign 2 leads naming here too — see §7.

---

## 7 · Coordination with Cycle 4

D4 makes Campaign 2 the **naming source of truth**. Both sessions share `names.yaml` / `journal.md`.
The race rule:

- **One writer of `names.yaml` at a time.** Campaign 2's sync-back (Phase E) is a Tier-1 serialized
  write; if Cycle 4 is mid-write, sequence them. This is a **maintainer-arbitrated** coordination
  point — surface it as `⚖️ PENDING MAINTAINER DECISION` in `ROADMAP-CAMPAGNE2.md` whenever both
  sessions are live.
- **IDB is shared, single, mutable.** Campaign 2's WRITE phase and any Cycle-4 IDB write must not
  overlap. Serialize across sessions (the maintainer drives one IDB).
- **Cycle 4 benefits, doesn't fight:** once Campaign 2 annotates a cluster, Cycle 4 reads the
  annotated IDB for free. Campaign 2 should annotate the spine *early* if Cycle 4 needs it.

---

## 8 · Risk register

| ID | Risk | Mitigation |
|----|------|-----------|
| C2-R1 | **Two writers hit the IDB** (corruption / lost renames) | §3.2: exactly one annotator in flight; serialize across sessions (§7) |
| C2-R2 | **A proposed name is wrong** and gets applied widely | Phase C gate + dry-run review in Phase D; idempotent re-run fixes; pull-back shows the truth |
| C2-R3 | **Pseudo-C leaks** into a comment that later gets quoted into a spec | Comments stay neutral; Phase E firewall check; comments never auto-promote |
| C2-R4 | **Reused-symbol over-rename** (a generic helper named too specifically) | Canonicalize in Phase C by *role*, not by one call site; prefer generic names for shared helpers |
| C2-R5 | **IDA/MCP unreachable or wrong endpoint** (`dbg_*` absent) | Re-register on `?ext=dbg`; fall back to bundled IDAPython; static-only is fine here |
| C2-R6 | **Scope creep** beyond the prioritized clusters | Enforce §6 priority; defer outward clusters to later waves |
| C2-R7 | **`names.yaml` write race with Cycle 4** | §7: one writer at a time; maintainer arbitrates |
| C2-R8 | **Anti-debug (XTrap)** if a lane tries the debugger | Campaign 2 is static-first; debugger optional and serialized; document static-only facts honestly |

---

## 9 · Run skeleton

> Paste into `Docs/ROADMAP-CAMPAGNE2.md` and keep statuses updated in place as waves land.

```markdown
# CAMPAIGN 2 — Make doida.exe legible (launched <YYYY-MM-DD>)
**Mandate:** "<verbatim>"
**Baseline:** <named>/<total> functions named; <unnamed> sub_xxxx. sha <prefix>.

## Phase 0 — Pre-flight        STATUS: <…>
## Phase A — Cartography       STATUS: <…>   → cluster backlog
## Phase B — Comprehension     STATUS: <…>   (per-cluster lane tables)
## Phase C — Glossary (gate)   STATUS: <…>
## Phase D — Annotation        STATUS: <…>   (per-cluster apply reports)
## Phase E — Sync-back         STATUS: <…>
## Phase T — Tooling           STATUS: <…>
### ⚖️ PENDING MAINTAINER DECISION (Cycle-4 names.yaml/IDB coordination)
```

---

*This document is the method for Campaign 2. `Docs/ROADMAP-CAMPAGNE2.md` is the record of the run.
`Docs/CAMPAIGN_TEMPLATE.md` is the shared doctrine. When this doc and disk reality disagree, disk
reality wins — then fix this doc.*
