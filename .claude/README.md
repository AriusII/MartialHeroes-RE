# `.claude/` — Martial Heroes Claude Code kit

The committed Claude Code tooling for this clean-room RE + Godot revival project. **31 agents, 34
skills, 12 hook files** (11 advisory hooks + `_hooklib`), rationalized into five domain orchestrators
(Planning · Reverse Engineering · C# Porting [00→04 + Tools] · Godot [05] · Documentation & Tooling).
Start with the two design docs, then the directories.

## Read these first
- **[`KIT.md`](KIT.md)** — the authoritative kit design: the **5 domain orchestrators** (§2), the per-role
  **`model` + `effort`** policy (§1), the 26-worker roster (§3), the **agent↔skill linking fabric**
  (§4 — `skills:` preload + knowledge-skill `paths:`), and the skill/hook plans (§5/§6/§7). Read it
  before authoring or refining anything here.
- **[`../CLAUDE.md`](../CLAUDE.md)** "Tooling Map" — the representative inventory + the orchestration
  doctrine (Tier-1 session → Tier-2 orchestrator → Tier-3 worker).

## Layout
| Dir / file | What it is | Who maintains it |
|---|---|---|
| `agents/*.md` | Subagent definitions (`@name`), grouped by domain. Every one declares explicit `model:`+`effort:`; orchestrators carry a linked roster. | `@kit-author` |
| `skills/<name>/SKILL.md` | `/command` action skills + bundled `scripts/`, and the 4 auto-loading **knowledge** skills (`user-invocable: false`). | `@kit-author` |
| `hooks/*.py` | Lifecycle hooks — **advisory-only & fail-open**, std-lib + `_hooklib` only; they warn / inject context, never block. | `@kit-author` |
| `hooks/_hooklib.py` | Shared hook helpers (`import _hooklib as h`). | `@kit-author` |
| `settings.json` | Hook wiring + enabled MCP servers + permissions. **Orchestrator-owned** — authors never edit it. | main session |
| `settings.local.json`, `hooks/state/` | Machine-local (gitignored). | — |

## Self-consistency guards
One advisory meta-hook keeps the kit coherent as it grows: `kit_guard` (agent frontmatter +
`skills:`/`Agent()` references resolve; SKILL.md uses `allowed-tools` not `tools` + bundled scripts
exist; no hook can *block* — the advisory-only rule; settings wiring has no orphans). The
`@tooling-auditor` agent is the full read-only gate; run it after any `.claude/` change.

## Firewall (non-negotiable)
This kit serves a clean-room RE project (EU Directive 2009/24/EC Art. 6). Dirty-room agents/skills write
ONLY to `Docs/RE/_dirty/` (gitignored); spec-authors **rewrite** findings into committed neutral specs;
clean-room engineers read only those specs. No committed file (this kit included) contains Hex-Rays
pseudo-C, decompiler autonames, raw addresses, or copyrighted data. See `KIT.md` §0 and the
`ida-pro-re` knowledge skill.
