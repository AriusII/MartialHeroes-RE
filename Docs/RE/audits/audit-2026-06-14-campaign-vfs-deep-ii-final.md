**Verdict: PASS**

# Clean-room firewall FINAL re-audit — CAMPAIGN VFS-DEEP-II (Tier-1 closing artifacts)

- **Date:** 2026-06-14
- **Auditor:** clean-room-auditor (human-trust backstop, EU Software Directive 2009/24/EC Art. 6)
- **Scope:** the four Tier-1 finalization edits made after the first PASS, plus a global firewall re-confirmation.
- **Mode:** working-tree / release audit (the four files are modified-but-unstaged; staged set was empty, so the gate was run `--mode staged` AND `--mode tracked`, and the four files were inspected by content directly).

## Skill verdicts (raw)

- **clean-room-firewall-check** — `--mode staged`: exit **0** (0 staged files); `--mode tracked`: exit **0** (990 tracked files). All three invariants OK in both modes.
- **clean-room-audit (leak_scan)** — EffectRenderer.cs: **0 high, 0 medium**. No leakage smells.

## Files audited

1. `Docs/RE/journal.md` — new "CAMPAIGN VFS-DEEP-II … Tier-1 consolidation" entry (lines 1244-1260).
2. `Docs/RE/names.yaml` — "Campaign VFS-DEEP-II" block of 9 new function entries (lines 25-33).
3. `Docs/RE/debugger_probe_plan.md` — NEW committed file (50 lines).
4. `05.Presentation/MartialHeroes.Client.Godot/World/EffectRenderer.cs` — CS8601 fix at line 556.

## Findings

| path:line | check | severity | true/false positive | rationale |
|---|---|---|---|---|
| journal.md:1244-1260 | runtime addr in new entry | n/a | clean | New entry references LOCATED functions by NAME only (e.g. `ItemsScr_LoadRecord`, `Ted_LoadCellTerrainBlob`, `Lighting_ApplyBrightnessAmbient`, line 1258). No `0x4xxxxx`/`0x5xxxxx` runtime address. Hex tokens present are LEGITIMATE file-format offsets (`0x224`, `@0x220`, `@0x0E4`, `@0x64`) and data constants. |
| journal.md:1217 | `0x3F800000` | n/a | false positive | IEEE-754 bit pattern for `1.0f` (data value documented in-text), not a runtime address. Pre-existing CAMPAIGN-5B context line, outside the new entry. |
| journal.md:67/258/441/676 | `0x400000` | n/a | false positive | Documented imagebase / data constants in pre-existing entries; not function runtime addresses. |
| names.yaml | YAML validity / duplicate keys | n/a | clean | Parses as valid YAML; 2522 address keys, **zero duplicates**; 2-space indent uniform (all 2522 keys at indent 2). The two flagged pre-existing keys `0x45af14` (line 97) and `0x45993c` (line 689) each occur EXACTLY ONCE — not duplicated by the new block. |
| names.yaml:25-33 | 9 new entries well-formed | n/a | clean | Exactly 9 entries, all 9 addresses unique, each occurring once file-wide; each `{ name, note }` well-formed. Addresses here are EXPECTED (names.yaml is the whitelisted address→symbol glossary by design). |
| debugger_probe_plan.md | runtime addr | n/a | clean | NEW committed file references function NAMES (`ItemsScr_LoadRecord`, `Lighting_ApplyBrightnessAmbient`, `Renderer_SetDeviceAmbient`), spec sections, and the `_dirty/campaign-vfs-deep-ii/ida/` pointer only. No `0x4xxxxx`/`0x5xxxxx` runtime address. Hex tokens are format offsets (`0x224`, `0xFFFFFFFF`). It is a Docs file (not a `.cs` under a numbered layer), so firewall invariant 2 does not apply to its `_dirty/` pointer. Journaled at journal.md:1260. |
| EffectRenderer.cs:556 | CS8601 fix / decompiler artifact | n/a | false positive | `textures[i] = LoadSubEffectTextures(se) ?? System.Array.Empty<ImageTexture?>();` — a nullable-coalesce with an explanatory comment + nearby `// spec:` (line 561). No autoname/pseudo-type/mangled symbol. |

## Global firewall re-confirmation

- `Docs/RE/_dirty/campaign-vfs-deep-ii/` is **untracked** (`git ls-files` empty) and **gitignored** (covered by `.gitignore:429` → `Docs/RE/_dirty/`). Raw debugger-probe addresses are legitimately confined there and were NEVER opened by this audit.
- No tracked path contains `_dirty/`; no copyrighted original staged; no `.cs` cites a `_dirty/` path.
- All changed committed specs are journaled (`names.yaml` synced in this entry; `debugger_probe_plan.md` referenced at journal.md:1260).

## Verdict

**PASS.** The firewall held for this change set across both gate modes and the targeted content checks. The closing gate is GREEN: the campaign may be reported complete.

This PASS asserts only the audited invariants — quarantine stays out of git, clean-room C# never points at `_dirty/`, spec changes are journaled, no runtime addresses leaked into the journal/probe-plan, names.yaml is valid and duplicate-free, and the EffectRenderer fix carries no decompiler artifacts. It is not blanket absolution; deeper human review remains the release backstop.
