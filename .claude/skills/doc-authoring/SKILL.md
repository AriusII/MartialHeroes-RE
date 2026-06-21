---
name: doc-authoring
description: Use when writing or refining Martial Heroes project documentation — ROADMAP/PLAN/READMEs/session logs/spec prose — in clean-room-neutral language; covers the broad doc corpus beyond preservation's README/CONTRIBUTING/journal scope. Triggers on "update the ROADMAP", "write a doc for X", "refine PLAN.md / CAMPAIGN_TEMPLATE", "polish this spec's prose", "document the cycle", "fix the docs". Author/refine `Docs/ROADMAP.md`, `Docs/PLAN.md`, `Docs/CAMPAIGN_TEMPLATE.md`, the many READMEs, the `CLAUDE.md`/`KIT.md` inventories, cycle/session write-ups, and the *readability* of committed `Docs/RE/` specs — always firewall-neutral (no decompiler output, autonames, addresses, or copyrighted bytes; cite the spec, not the binary). Documentation describes what IDA/specs/code already prove; it never asserts a fact without an IDA/spec basis.
allowed-tools: Read, Write, Edit, Grep, Glob
model: sonnet
effort: high
---

# doc-authoring — author/refine the broad committed doc corpus

The map, not the territory. This skill writes the **wide documentation layer** that keeps the project
legible: the live run record, the planning charter, the campaign method, the dozens of READMEs, the
`CLAUDE.md`/`KIT.md` inventories, per-cycle write-ups, and the *prose* of the committed specs. Every word
is **firewall-neutral**: documentation describes what IDA / the `Docs/RE/` specs / the C# code **already
prove** — it never asserts a binary fact without a spec basis, and never leaks a decompiler artifact.

## Ground-truth stance (which tier each doc derives from)
- **`Docs/RE/` specs** are the derived truth (tier 2). When polishing spec prose, you improve *readability
  only* — never change a recovered value, offset, opcode, or `verification:` banner. A value is wrong only
  if IDA disproved it; that is the RE domain's call, not a doc edit.
- **`Docs/ROADMAP.md` / `journal.md` / git** are the record of what was done — trust them to state status;
  do not re-derive RE to "confirm" a doc claim. If a doc and the on-disk reality disagree, **disk wins**
  (real project names like `Network.Transport.Pipelines`, not the blueprint's stale "Pipe").
- **Pixels:** the official captures are the visual oracle; a doc about rendering says "oracle > spec".

## Firewall placement — clean/neutral, NO IDA
This is committed, public-facing prose. Same firewall as every committed file:
- **NEVER** paste Hex-Rays pseudo-C, `sub_xxxx`/`loc_xxxx` autonames, `_DWORD`/`__thiscall`/mangled names,
  raw addresses (`0x004…`), or any asset/binary bytes into a doc.
- Refer to behavior by **canonical name** (resolve via `Docs/RE/names.yaml` — read it, never edit it) and
  **cite the spec path** (`see Docs/RE/formats/terrain.md`), never the binary offset.
- If the only way to make a point is to paste code, the point belongs in `_dirty/` (gitignored), not here.

## Distinct from the `preservation` skill (no overlap)
`preservation` owns a **narrow, fixed** set: the repo-root `README.md` + `CONTRIBUTING.md` (Mode A) and
the **append-only** `Docs/RE/journal.md` provenance entry (Mode B). **This skill owns everything else** —
`ROADMAP`, `PLAN`, `CAMPAIGN_TEMPLATE`, sub-tree READMEs, `Docs/` guides, the `CLAUDE.md`/`KIT.md`
inventories, cycle reports, and spec-prose polish. If the task is the root README/CONTRIBUTING or a
journal entry → use `preservation`. Anything else doc-shaped → here. Never duplicate journal logic; never
overwrite the root README from this skill.

## Procedure
1. **Locate the doc + its source of truth.** `Glob`/`Grep` to find the exact file (don't invent a path).
   Identify what authoritative source backs each claim:
   | Doc | Authoritative source |
   |---|---|
   | `Docs/ROADMAP.md` (live run record) | git log + `Docs/RE/journal.md` + the actual cycle work |
   | `Docs/PLAN.md` (method/charter) | `Docs/CAMPAIGN_TEMPLATE.md` + the maintainer's mandate |
   | `Docs/CAMPAIGN_TEMPLATE.md` | the command-tier / concurrency doctrine in `KIT.md` §2 + `CLAUDE.md` |
   | a layer/tool README | the on-disk project (`*.csproj`, real namespaces, the layer DAG) |
   | `Docs/RE/**` spec prose | the spec's own recovered values (polish wording, NOT values) |
   | `CLAUDE.md` / `KIT.md` inventories | `ls .claude/agents/` + `.claude/skills/` + `KIT.md` rosters |
2. **Read before writing.** Read the target if it exists and **refresh in place** — preserve
   maintainer-added banners, badges, links, `verification:` lines, and existing structure. Never
   blind-overwrite a living doc; prefer `Edit` over `Write` for surgical changes.
3. **Draft neutral prose.** Mission-true, concise, professional. State status as the record shows it
   ("CYCLE 9 Phase 3 done") — never a stale "greenfield" snapshot, never aspirational claims dressed as
   done. Cite specs for any recovered fact (`see Docs/RE/specs/skinning.md`).
4. **Cross-check vs specs/code/disk.** Every project/folder name matches on-disk reality; every recovered
   value you quote matches its spec verbatim; every cited path resolves (`Grep` it). Blueprint vs disk
   disagreement → disk wins.
5. **Self-scrub the firewall.** Re-read your diff hunting for: hex addresses, `sub_`/`loc_`/`dword_`
   autonames, `_DWORD`/`__thiscall`/mangled identifiers, pasted pseudo-C, copyrighted strings/bytes,
   affiliation claims. Any hit ⇒ rewrite to a canonical name + spec citation, or delete.
6. **Validate + report.** Run the checklist below; report the path(s) written and a one-line summary.

## Validation checklist (Done when)
- [ ] Right file, refreshed in place; maintainer banners/`verification:` lines preserved.
- [ ] Every recovered fact cites a `Docs/RE/` spec; no value changed (prose-only on specs).
- [ ] Every name matches disk reality; every cited path resolves.
- [ ] Status reflects git/journal reality — no stale or aspirational snapshot.
- [ ] **Firewall clean:** no address, autoname, pseudo-C, copyrighted byte, or affiliation claim.
- [ ] Did NOT touch the root `README.md`/`CONTRIBUTING.md` (→ `preservation`).
- [ ] Did NOT edit `Docs/RE/journal.md` or `Docs/RE/names.yaml` (orchestrator-owned — read-only here).

## Docs/ layout map (where things live)
- `Docs/ROADMAP.md` — live run record (what's done / in flight / where to resume).
- `Docs/PLAN.md` — active campaign method + charter.
- `Docs/CAMPAIGN_TEMPLATE.md` — the command-tier + concurrency doctrine all campaigns specialize.
- `Docs/RE/README.md` — the clean-room firewall doctrine (summarize accurately, never weaken).
- `Docs/RE/{formats,packets,structs,specs}/`, `Docs/RE/opcodes.md` — the committed neutral specs
  (polish prose; never alter recovered values or `verification:` banners).
- `Docs/RE/journal.md`, `Docs/RE/names.yaml` — **ORCHESTRATOR-OWNED. Read for cross-reference; NEVER edit
  from this skill.** Journal entries go through `preservation` Mode B; glossary changes go through the RE
  orchestrator.
- `Docs/RE/_dirty/` — gitignored, tainted. **Never read it into a committed doc.**
- Sub-tree READMEs (per layer / `Tools/` / `clientdata/`) + `CLAUDE.md` / `KIT.md` inventories.

## Anti-patterns (never)
- Never assert a binary fact without a spec citation — docs describe proven truth, they don't establish it.
- Never paste an address, autoname, pseudo-C, or copyrighted byte into a committed doc (firewall breach).
- Never edit `Docs/RE/journal.md` or `Docs/RE/names.yaml`, or overwrite the root README/CONTRIBUTING
  (those are `preservation`/orchestrator territory).
- Never change a recovered value or `verification:` banner while "polishing" a spec — wording only.
- Never use a stale blueprint name over the real on-disk one, or a stale status snapshot.
- Never blind-overwrite a living doc — refresh in place, preserve maintainer additions.

> North star (N1+N2): keeps the project's map honest — the run record, the plan, the READMEs, and the spec
> prose all stay accurate, firewall-clean, and trustworthy, so the clean-room reverse and the 1:1 port both
> run on documentation people can rely on.

## Hard rules
- Committed, public prose: zero addresses / pseudo-C / autonames / copyrighted bytes; cite specs not the binary.
- Out of scope: root README/CONTRIBUTING + journal entries (→ `preservation`); `journal.md`/`names.yaml`
  (orchestrator-owned, read-only); recovered spec values (RE domain owns those).
- Disk reality wins over blueprint naming; status mirrors git + `journal.md`, never an invented snapshot.
