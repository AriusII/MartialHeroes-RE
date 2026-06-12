**Verdict: PASS**

# Clean-room audit — render-and-UI campaign (pre-commit)

- **Date:** 2026-06-12
- **Auditor:** clean-room-auditor (Claude Code)
- **Scope:** pre-commit, **staged set** (`git diff --cached --name-only --diff-filter=ACMR`), 48 paths.
- **Caveat on scope:** Several files the commit message lists (`CLAUDE.md`, `.gitignore`,
  `Docs/RE/journal.md`, `Docs/RE/names.yaml`, `client_dir.cfg`, `project.godot`, and the
  modified `World/*.cs` / `Helpers/WorldCoordinates.cs` / `Autoload/ClientContext.cs` /
  `Dev/ClientPathResolver.cs`, plus the touched specs `animation.md`, `mesh.md`,
  `misc_data.md`, `terrain_scene.md`, `texture.md`, `camera_movement.md`,
  `client_runtime.md`) are **modified in the working tree but NOT staged** (`M`, not `AM`).
  This audit is authoritative for the staged index only. Those unstaged edits — including
  `journal.md` and `names.yaml` — must be `git add`ed before the commit, and a re-audit is
  recommended once they are staged.

## Raw skill verdicts

- **clean-room-firewall-check** `--mode staged`: **exit 0** — all three invariants green
  (quarantine/originals out of git; no `_dirty/` ref in layer C#; changed specs journaled).
- **clean-room-audit** (leak smell scan): **0 high, 0 medium** in the staged change set.
  (Directory scans surfaced medium hits only in *unchanged/unstaged* files —
  `ItemsCsvParser.cs`, `TerrainLayerParsers.cs`, `RealWorldRenderer.cs` — out of scope here.)
- **Defensive cross-check:** HIGH-pattern grep run directly over **staged blob content**
  (`git show :path`, i.e. the index, not disk) across all staged `.cs/.md/.yaml/.tscn`:
  **0 files with hits**. No raw VA-address literals (`0x004xxxxx`) in any staged `.cs`.

## Hard path invariants (manually re-confirmed)

- No `_dirty/` path tracked anywhere in the repo, none staged.
- No copyrighted originals staged (`*.pak/.vfs/.inf/.exe/.dll/.pcapng/.tsv/.mot/.skn/.bnd/.bud/.ted/.sod/.arr/.xeff/.png`).
- `clientdata/data.inf` and `clientdata/data/data.vfs` are gitignored (`!!`), **not staged**.
  Only the intentional meta files `clientdata/.gitignore` and `clientdata/README.md` are staged. ALLOWED.
- `mh_screens/` (ignored) and `screenshot_probe.gd` are **not staged**. Confirmed.

## Findings table

| path:line | smell / check | severity | verdict | rationale |
|---|---|---|---|---|
| EnvironmentBinParsers.cs:320-323,327-330,334-337 | uncited-offset (scanner) | medium | **FALSE POSITIVE** | Each `slot+0xNN` block is preceded by `// spec: Docs/RE/formats/environment_bins.md §9.2 — color_X RGBA @ slot+0xNN: CONFIRMED`. The scanner's 3-line lookback clipped the citation at block edges; the spec section exists (§9.2 offset table `+0x00/+0x10/+0x20`). Provenance present. |
| SkinningMath.cs:177 | uncited-offset (scanner) | medium | **FALSE POSITIVE** | `idToIndex = new int[256]` is the cardinality of a byte (256 lookup slots), not a file byte-offset. Method is cited to `specs/skinning.md §3.2` + `formats/mesh.md` in its doc comment. |
| ui_manifests.md (staged blob) | residual decompiler token | — | **CLEAN** | The journal (2026-06-12 follow-up, lines 312-314) records one leaked `sub_` token caught and rewritten by the orchestrator; staged blob grep confirms it is gone. |

No HIGH-severity findings. No true-positive leaks. No uncited protocol/crypto/parser offset.

## Journal provenance (invariant 3, detail)

5 changed specs are staged: `environment_bins.md`, `ui_manifests.md`, `environment.md`,
`skinning.md`, `ui_system.md`. `journal.md` is *not* in the staged set, so the gate satisfied
invariant 3 by basename presence in the committed journal text — confirmed: all 5 are named in
the 2026-06-12 "render-and-UI campaign" entries (journal lines 264-313). 4 dated 2026-06-12
entries present. **Recommendation:** stage the working-tree `journal.md` (and `names.yaml`) with
this commit so the provenance edits land atomically with the specs they describe.

## What a PASS asserts (and does not)

This PASS asserts only that, **for the 48 staged paths**: (1) no decompiler-derived identifier,
MSVC pseudo-type, or mangled symbol is present; (2) every byte-offset in staged parser/skinning
C# carries a `// spec: Docs/RE/...` citation backed by a real spec section; (3) no `_dirty/` path
or copyrighted original is staged, and no layer C# references `_dirty/`; (4) all changed specs are
journaled. It is **not** blanket innocence, and it does **not** cover the unstaged working-tree
files listed in the caveat — re-run this gate after those are staged.
