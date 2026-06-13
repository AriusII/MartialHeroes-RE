# `.claude/` — Martial Heroes Claude Code kit

The committed Claude Code tooling for this clean-room RE + Godot revival project. **53 agents, 53
skills, 27 hooks.** Start with the two design docs, then the directories.

## Read these first
- **[`KIT.md`](KIT.md)** — the authoritative kit design: the **orchestrator fleet** (§2), the per-role
  **`model` + `effort`** policy (§1), the new worker agents (§3), the **agent↔skill linking fabric**
  (§4 — `skills:` preload + knowledge-skill `paths:`), and the skill/hook plans (§5/§6/§7). Read it
  before authoring or refining anything here.
- **[`../CLAUDE.md`](../CLAUDE.md)** "Tooling Map" — the representative inventory + the orchestration
  doctrine (Tier-1 session → Tier-2 orchestrator → Tier-3 worker).

## Layout
| Dir / file | What it is | Who maintains it |
|---|---|---|
| `agents/*.md` | Subagent definitions (`@name`). Every one declares explicit `model:`+`effort:`; orchestrators carry a linked roster. | `@agent-author` |
| `skills/<name>/SKILL.md` | `/command` action skills + bundled `scripts/`, and the 4 auto-loading **knowledge** skills (`user-invocable: false`). | `@skill-author` |
| `hooks/*.py` | Lifecycle hooks — **advisory-only & fail-open**, std-lib + `_hooklib` only; they warn / inject context, never block. | `@hook-author` |
| `hooks/_hooklib.py` | Shared hook helpers (`import _hooklib as h`). | `@hook-author` |
| `settings.json` | Hook wiring + enabled MCP servers + permissions. **Orchestrator-owned** — authors never edit it. | main session |
| `settings.local.json`, `hooks/state/` | Machine-local (gitignored). | — |

## Self-consistency guards
Four advisory meta-hooks keep the kit coherent as it grows: `agent_md_guard` (agent frontmatter +
`skills:`/`Agent()` references resolve), `skill_md_guard` (`allowed-tools` not `tools`, bundled scripts
exist), `hook_advisory_guard` (flags any hook that could *block* — the advisory-only rule), and
`settings_wiring_nudge` (every wired hook exists; no orphans). The `@tooling-auditor` agent is the
full read-only gate; run it after any `.claude/` change.

## Firewall (non-negotiable)
This kit serves a clean-room RE project (EU Directive 2009/24/EC Art. 6). Dirty-room agents/skills write
ONLY to `Docs/RE/_dirty/` (gitignored); spec-authors **rewrite** findings into committed neutral specs;
clean-room engineers read only those specs. No committed file (this kit included) contains Hex-Rays
pseudo-C, decompiler autonames, raw addresses, or copyrighted data. See `KIT.md` §0 and the
`ida-pro-re` knowledge skill.
