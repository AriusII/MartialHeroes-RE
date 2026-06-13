---
name: preservation-readme
description: Use to generate or refresh the repository README.md and CONTRIBUTING.md from the authoritative PRESERVATION_AND_ARCHITECTURE.md. Covers the EU Art.6 legal basis, bring-your-own-assets policy, the dirty/clean clean-room firewall, the no-commit rules for .pak/.exe/.pcapng/.tsv, and the contribution flow. Loving-fan-project tone, professional and accurate.
allowed-tools: Read Write
model: sonnet
effort: medium
---

# preservation-readme

Author or refresh the project's public-facing `README.md` and `CONTRIBUTING.md` so newcomers
understand what Martial Heroes (the revival of the dead 2004–2008 MMORPG *D.O. Online* / *Martial
Heroes*) is, why it is legal, and how to participate without breaching the clean-room firewall.

The **authoritative source** for all legal and architectural claims is
`PRESERVATION_AND_ARCHITECTURE.md`. Never invent legal wording — quote and condense from there (and
from `CLAUDE.md` for the operational rules). The tone is a loving preservation/fan project:
warm and mission-driven, but professional and legally precise. No hype, no false claims of
affiliation with the original rights holders.

## Steps

1. **Read the sources** before writing anything:
   - `PRESERVATION_AND_ARCHITECTURE.md` — legal framework (Art. 6), philosophy, architecture.
   - `CLAUDE.md` — current state, operational rules, the real project/folder names.
   - `Docs/RE/README.md` — the firewall doctrine, to summarize accurately.
   - `.gitignore` — to state the exact no-commit patterns truthfully.
   If a `README.md` / `CONTRIBUTING.md` already exists, Read it and preserve any project-specific
   content the maintainer added (badges, links, credits) — refresh, don't blindly overwrite.

2. **Write `README.md`** at the repo root with these sections:
   - **Title + one-line mission** — "A clean-room, fan-driven revival of the lost MMORPG *Martial
     Heroes* (*D.O. Online*), shut down in December 2008." State plainly it is non-commercial and
     unaffiliated with the original rights holders.
   - **What this is / What this is NOT** — IS: documentation of protocol + asset formats and a fresh
     .NET 10 / C# 14 + Godot 4.6 reimplementation. IS NOT: a redistribution of any original game
     file, binary, or server; not endorsed by the rights holders.
   - **Legal basis** — condense the EU Software Directive 2009/24/EC, Art. 6 (decompilation for
     interoperability) argument and the "zero market subversion / defunct since 2008" reasoning from
     `PRESERVATION_AND_ARCHITECTURE.md`. Keep it factual; link to the full doc.
   - **Bring your own assets** — the project ships NO copyrighted content. To use the extraction and
     protocol pipelines, a user must supply their own legally-obtained legacy client (`Main.exe`),
     archives (`.pak`), and any captures. Mention the recommended `/LegacyClient/` drop-zone.
   - **The clean-room firewall** — summarize the dirty/clean split: `Docs/RE/_dirty/` (gitignored)
     holds raw decompiler output; only neutral specs are committed; engineers implement from specs,
     never from pseudo-code. Point to `Docs/RE/README.md`.
   - **Architecture at a glance** — the five layers (`01.Infrastructure.Shared` …
     `05.Presentation`) and the zero-allocation socket→pipeline→crypto→protocol→app→domain→Godot
     data path. Use the REAL names: `Network.Transport.Pipelines` (not "Pipe").
   - **Build & run** — `dotnet build MartialHeroes.slnx`, `dotnet test MartialHeroes.slnx`
     (xUnit; needs a .NET 10 SDK), and that the Godot client opens via the Godot 4.6 editor.
   - **Status** — greenfield skeleton (June 2026): 12 class libs, references being wired per the
     dependency graph.
   - **License & credits** — link to the license; credit the original creators respectfully;
     reiterate non-affiliation.

3. **Write `CONTRIBUTING.md`** at the repo root covering:
   - **Golden rule** — never commit copyrighted originals. Restate the gitignored patterns exactly:
     `*.pak`, `*.pcapng`, `*.tsv`, `Main.exe` / `*.exe` / `*.dll`, and `Docs/RE/_dirty/`. Tell
     contributors to verify `git status` before staging, because some patterns (legacy binaries) are
     easy to add accidentally.
   - **Two kinds of contributor** — *RE analysts* (work in `_dirty/`, then hand neutral findings to
     spec-authors; must log sessions in `Docs/RE/journal.md`) and *engineers* (clean-room: read only
     `Docs/RE/` specs + the C# tree, never IDA, never `_dirty/`; cite every magic offset with
     `// spec: Docs/RE/...`).
   - **Clean-room firewall for contributors** — the same dirty/clean rules; link `Docs/RE/README.md`.
     Mention the `clean-room-audit` and `clean-room-firewall-check` checks that guard merges.
   - **Dependency graph discipline** — lower layers never reference higher ones; hot paths
     (Network.*, Assets.*) stay zero-allocation; core below layer 05 stays engine-free (no
     `using Godot;`).
   - **Workflow** — branch, implement one project at a time, add xUnit tests, run
     `dotnet build` / `dotnet test`, keep prose in English, open a PR. Note the
     `Co-Authored-By` / generated-with-Claude-Code conventions if relevant.
   - **Conduct** — a short, kind code-of-conduct note fitting a preservation community.

4. **Verify accuracy.** Every legal sentence must be traceable to `PRESERVATION_AND_ARCHITECTURE.md`;
   every no-commit pattern must match `.gitignore`; every project/folder name must match the real
   on-disk names. Report both written paths to the user.

## Decision heuristics

- **If a README/CONTRIBUTING already exists** → refresh in place, preserving maintainer-added badges,
  links, and credits; never blind-overwrite.
- **If the blueprint and disk disagree on a name** → disk reality wins (e.g. write
  `Network.Transport.Pipelines`, not the stale "Pipe"); the `Status` section reflects the *current* state
  (full client built, Godot rendering), not the old "greenfield skeleton" line.
- **If tempted to phrase a legal claim** → quote/condense from `PRESERVATION_AND_ARCHITECTURE.md` only;
  if the wording isn't traceable there, omit it rather than freelance.

## Verify / Done when

- `README.md` and `CONTRIBUTING.md` both written at the repo root, with all required sections present.
- Every legal sentence traces to `PRESERVATION_AND_ARCHITECTURE.md`; every no-commit pattern matches
  `.gitignore` verbatim; every project/folder name matches on-disk reality.
- No pseudo-code, addresses, asset/binary contents, or affiliation claims leaked into the public docs.
- Both written paths reported to the user.

## Pitfalls

- Never freelance legal language — unsourced legal wording is a liability; source it from the blueprint
  or drop it.
- Never imply endorsement by or affiliation with the original rights holders.
- Never use stale blueprint names over real on-disk ones, or a stale `Status` snapshot.
- These are public, committed docs: never paste a decompiler identifier, an address, or any asset/binary
  bytes into them.

> North star N1: the public docs make the EU Art. 6 clean-room basis and the bring-your-own-assets
> firewall legible to newcomers, so contributors stay inside the legal lines while advancing the N2 revival.

## Hard rules

- Source all legal claims from `PRESERVATION_AND_ARCHITECTURE.md` — do not freelance legal language.
- Never imply endorsement by or affiliation with the original rights holders.
- Use the REAL project names (e.g. `Network.Transport.Pipelines`), not the stale blueprint names.
- These are public docs: include no addresses, no pseudo-code, no asset/binary contents.
