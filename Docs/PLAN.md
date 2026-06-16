# Docs/PLAN.md — Active Campaign Charter & Method

> **What this file is.** The *charter* of the currently-active reverse-engineering campaign: its
> mandate, north stars, scope, and method. The **method** is `Docs/CAMPAIGN_TEMPLATE.md` (the reusable
> W▸P▸E▸T▸R▸C pipeline + 3-tier orchestration + clean-room firewall). The **live run record** — every
> phase status, dated, updated in place — is `Docs/ROADMAP.md`. When this charter and disk reality
> disagree, disk reality wins, then fix the charter.

---

## ACTIVE: CAMPAIGN 10 — Total Client Comprehension & Doc Re-Verification (`doida.exe`)

**Launched:** 2026-06-16 · **Branch:** campaign3 · **IDB anchor:** `doida.exe` SHA `263bd994…`.

### Mandate (maintainer, verbatim intent)
Deploy a large fleet of agents to **deep-analyze the entire `doida.exe` client** and understand the
whole client: how it is **constructed and boots**, what it does, how its **functions / modules /
scopes** are organized, how every **scene (= "window")** is built, with **ultra-precise attention to
UI/UX (GUI) window construction**, and a deep refinement of the **`data.vfs` asset pipeline**. The
current docs are **not assumed correct** — IDA Pro 9.3 (via the MCP) is the single source of truth.
The deliverable is to **re-verify and rewrite the entire `Docs/RE/` knowledge base to 100% certainty**,
then **align the C#/.NET core and the Godot client** to the corrected specs. *Leave nothing to chance;
be extremely meticulous and exhaustive.*

### Decisions
- **Scope:** RE + docs + re-implementation (full W▸P▸E▸T▸R▸C, incl. Engineering + Review).
- **Breadth:** systematic re-verification of **every** RE doc (~37 specs + 32 formats + 10 structs +
  ~80 packet YAMLs) in this campaign.
- **Evidence:** **static IDA + VFS observation only** (no live debugger / capture this campaign). Facts
  needing runtime proof ship **explicitly flagged** `capture/debugger-pending`, never silently claimed.

### North stars
- **N1** — total clean-room RE of the entire `doida.exe`; leave no construction element un-mapped.
- **N2** — faithful 1:1 re-creation (.NET 10 / C# 14 + Godot 4.6.3) aligned to the corrected specs.

### Out of scope (deferred)
- The game **server** (keep core engine-free for a future `Server.Console`).
- Live debugger / packet-capture confirmation.
- Blanket-naming the ~19k unnamed functions (annotate the campaign's subsystems, not STL/leaf/thunk noise).

### The "100% sure" gate — verification banner
Every campaign-touched `Docs/RE/**` doc carries a machine-checkable front-matter banner. A doc may only
claim `confirmed`/`sample-verified` when a Phase-W lane re-derived it from the `263bd994` IDB (and, for
formats, from a real VFS sample). Single static inferences stay `static-hypothesis`; runtime-dependent
facts stay `capture/debugger-pending`. The Phase-R gate fails any touched doc lacking the banner.

```yaml
---
verification: confirmed            # confirmed | sample-verified | static-hypothesis | capture/debugger-pending
ida_reverified: 2026-06-16
ida_anchor: 263bd994
evidence: [static-ida, vfs-sample]
conflicts: none
---
```

### Research partition — 7 domain blocks (cover the whole corpus)
| Block | Theme | Priority |
|---|---|---|
| **A** | Boot & Runtime Construction (entry → init tiers → `WinMain while(1) switch(GameState 0..8)` → loop → singletons/scopes) | ★ |
| **B** | Scene/Window State Machine & UI Framework (Diamond `GU*` toolkit; every window `construct()` element-by-element) | ★ |
| **C** | VFS / Asset I/O & Resource Pipeline (`data.inf` index, mount, lookup, read/decompress, cache; request→object) | ★ |
| **D** | Asset Format Corpus — two-witness (every `formats/*.md` vs the IDA loader AND a VFS sample) | ★ |
| **E** | Network / Protocol / Crypto (dispatch table, packet layouts, cipher/handshake; entity structs) | |
| **F** | Gameplay Systems (combat, skills, inventory, quests, npc, chat/social, minimap, camera/movement, Lua) | |
| **G** | Rendering / Effects / Terrain / Skinning / Environment / Sound | |

### Phase pipeline (gated; full detail per phase in `Docs/CAMPAIGN_TEMPLATE.md`)
0. **Mandate & Pre-flight** — this charter + ROADMAP + baseline + scaffold.
1. **W (Giga-research)** — blocks A→G, massively-parallel static-IDA + VFS lanes → `_dirty/campaign10/**`.
2. **P (Promotion)** — rewrite every touched spec to 100% + verification banner; master synthesis
   `specs/client_architecture.md`; Tier-1 reconcile (`opcodes.md`, `names.yaml`, `journal.md`).
3. **D (IDB annotation)** — `re-annotation-orchestrator` applies the campaign glossary (names+comments+types).
4. **E (Engineering)** — align C#/.NET + Godot to changed specs (staged, one engineer per project).
5. **T (Tooling)** — fold scanners into `vfs-inspect`; register the missing orchestrator agent-types.
6. **R (Review + Gates)** — 4 reviewers + fix wave + hard gates (build 0/0, tests green, firewall PASS,
   banner audit). **C (Consolidation)** — ROADMAP/journal/names/memory; commit **only on request**.

### Run order
A → B → C → D (the ★ priority, each a W→P→(D) sub-cycle) → E → F → G → global E/R/C close-out.
**Multi-session:** `Docs/ROADMAP.md` tracks block/phase status in place so any session resumes cleanly.

---

*Method = `Docs/CAMPAIGN_TEMPLATE.md`. Record = `Docs/ROADMAP.md`. Ground truth for layers/conventions
/legal basis = `CLAUDE.md` + `PRESERVATION_AND_ARCHITECTURE.md`.*
