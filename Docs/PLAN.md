# Docs/PLAN.md — Active Campaign Charter & Method

> **What this file is.** The *charter* of the currently-active campaign: its mandate, north stars,
> scope, and method. The **method** is `Docs/CAMPAIGN_TEMPLATE.md` (the reusable W▸P▸E▸T▸R▸C pipeline +
> 3-tier orchestration + clean-room firewall). The **live run record** — every phase status, dated,
> updated in place — is `Docs/ROADMAP.md`. When this charter and disk reality disagree, disk reality
> wins, then fix the charter.

---

## ACTIVE: CAMPAIGN 12 — C#/Godot Fidelity Completion ("everything possible")

**Launched:** 2026-06-16 · **Branch:** `campaign12` (off `master`, after the campaign3→master merge
PR #1) · **IDB anchor:** `doida.exe` SHA `263bd994…` (IDA MCP UP — queryable for confirmation).

### Mandate (maintainer, verbatim intent)
Continue the CAMPAIGN-11 direction and **finish everything still possible on the C# (core projects 01–04)
AND Godot (05)**. Ground every decision on **what the official game client actually does**, corroborated
by the **IDA comprehension (the source of truth)** and the **C10-re-verified `Docs/RE/` specs** — and
**query IDA directly when a behaviour is in doubt**. Concretely: (1) **delete** useless / wrong elements
that should not be there; (2) **improve, correct, optimise** the code so it is the cleanest and **TRUEST
possible vs IDA / the spec**; (3) **deploy a large agent fleet** — use every agent and skill needed;
(4) **adjust the PLAN / ROADMAP / campaign** to set this new direction, without conflicting with what is
already done. *Make the C# the cleanest, most excellent, most optimised and functional possible.*

### Why this campaign exists (relationship to C10 / C11)
- **C10** re-verified the entire `Docs/RE/` knowledge base to 100% against the live IDB → **the specs are
  the truth**.
- **C11** ran a broad audit→fix→reconcile→gate over all C# (core + Godot): 170 findings → 114 fixes, arch
  DAG accepted+documented, **1944 tests green**. But it deliberately left a tail of **non-blocking
  follow-ups** and was **core-weighted** — the Godot layer got the lighter pass, and the front-end visual
  fidelity was only **headless-smoke** verified, never **screenshot-confirmed**.
- **C12** is the **completion pass**: close the C11 follow-ups, give Godot 05 the deep per-scene fidelity
  treatment C11 gave the core, and **prove fidelity with the screenshot oracle** (the campaign-9 lesson:
  *"spec-faithful alone diverged from the real client; screenshots-as-oracle is what grounds fidelity"*).

### North stars
- **N1** — total clean-room RE of `doida.exe` (DONE through C10; specs are the truth; IDA stays queryable
  for confirmation only — no re-RE of settled facts).
- **N2 (the active driver)** — the faithful **1:1 re-creation** (.NET 10 / C# 14 core + Godot 4.6.3-mono)
  must match the re-verified specs **and the official client's observable behaviour** exactly, be
  clean-room-pure, zero-alloc on hot paths, idiomatic C#14/.NET10, and carry **no cruft**.

### Scope — 5 lane groups (the worklist)
| Group | Theme | Seeds (the named follow-ups + the deep pass) |
|---|---|---|
| **V — Visual fidelity (screenshot oracle)** ★ | windowed Godot capture of every front-end scene; verify vs spec + known-good finals; fix divergences/regressions | Login / PIN / ServerList / CharSelect / Opening render; effect diffuse tint; Opening alpha fade; lobby re-model visual effect |
| **F — Deep C#/Godot fidelity** ★ | fresh per-scene / per-system confront-to-spec (Godot-weighted, where C11 was lighter); query IDA when ambiguous; optimise hot paths | scene construction order, GU* binding, asset src-rects/fonts, HUD chrome, world systems, skinning/anim, sound |
| **C — Cleanup / cruft** | delete dead-but-valid chrome + any remaining cruft C11 missed | GameHud dead "Unknown" zone pill; stale `CLAUDE.md` skeleton `g{IdB}.bnd` claim vs `skinning.md §8(e)` |
| **W — Wire/data paths** | replace DEV-seed-only data with real wire→view adapters | `ServerSelectScreen` lobby wire→view adapter; audit other DEV-seeded screens |
| **R — RE legibility (parallel, IDB-only)** | make the IDB legible to support IDA confirmation queries | `names.yaml` sync of the C10-deferred IDB names + 6 ctor collisions via `ida-naming-sync` (touches no C#) |

★ = deepest lane counts / first passes.

### Decisions (carried)
- **Evidence:** the official client behaviour + the C10-verified specs are primary; **IDA is queried for
  confirmation** (static; no live debugger this campaign — debugger-pending facts stay pending, e.g. the
  `3/14`-vs-`4/1` spawn ordering).
- **VFS** stays memory-mapped (documented zero-copy port choice; the original used `ReadFile`+critical
  section). Not re-architected.
- **Arch DAG** = accept+document (the C11 Phase-3b decision holds); the by-design downward edges stay.
- **Commit** only on explicit request, **targeted paths**, on the `campaign12` branch — never `_dirty/`,
  `.godot/`, or any original asset/binary.

### Out of scope (deferred)
- The game **server** (keep core engine-free for a future `Server.Console`).
- Live **debugger / packet-capture** confirmation (flagged-pending facts stay pending).
- Re-RE of already-verified specs (read them; do not re-derive).
- Blanket-naming the ~19k unnamed IDB functions.

### Phase pipeline (gated; method detail in `Docs/CAMPAIGN_TEMPLATE.md`)
0. **Charter & Pre-flight** — this charter + ROADMAP + branch `campaign12` + baseline gate.
1. **Discovery audit (W)** — massively-parallel read-only fan-out (the `Workflow` tool) over the 5 lane
   groups → one structured, owner-grouped worklist; locate each scene's spec + known-good final.
2. **Fix waves (E)** — one writer per area, disjoint files, no concurrent builds (Tier-1 gate is
   authoritative). Every magic constant cites its spec; clean-room firewall holds.
3. **Screenshot fidelity loop (R-visual)** — Tier-1 serialized Godot windowed runs; capture → compare to
   spec + finals → fix → re-capture. Restore `client_dir.cfg` byte-exact after each run.
4. **RE legibility (T)** — `names.yaml` sync (parallel; IDB-only).
5. **Hard gates + Consolidate (R + C)** — nuke + `--no-incremental` build 0/0, all suites green, firewall
   PASS, DAG PASS, headless boot clean, screenshot evidence; ROADMAP/journal/memory; commit on request.

### Run order
P0 → P1 (discovery) → P2 (fix) ⟂ P4 (names.yaml, parallel) → P3 (screenshot loop, after fixes land) →
P5 (gates + commit). **Multi-session:** `Docs/ROADMAP.md` tracks phase/lane status in place so any session
resumes cleanly.

---

*Method = `Docs/CAMPAIGN_TEMPLATE.md`. Record = `Docs/ROADMAP.md`. Ground truth for layers/conventions
/legal basis = `CLAUDE.md` + `PRESERVATION_AND_ARCHITECTURE.md`. Prior campaigns (incl. C10 charter) live
in git history + `Docs/RE/journal.md`.*
