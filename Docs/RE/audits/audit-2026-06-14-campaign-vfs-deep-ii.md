**Verdict: PASS**

# Clean-room firewall audit — CAMPAIGN VFS-DEEP-II

- **Date:** 2026-06-14
- **Auditor:** clean-room-auditor (human-trust backstop, EU 2009/24/EC Art. 6)
- **Mode:** release/full-tree audit. Firewall gate run `--mode staged`; smell scan run per-layer over the whole `.cs` tree (`obj`/`bin`/`*.g.cs` excluded by the scanner; `_dirty/` never read).

## Skill verdicts (raw)

- **clean-room-firewall-check** (`--mode staged`, 45 staged files): **exit 0** — all three invariants OK.
  - inv.1 quarantine & originals out of git: OK
  - inv.2 clean-room C# never references `_dirty/`: OK
  - inv.3 changed specs are journaled: OK (`journal.md` is in the change set; per-spec pairing independently confirmed below)
- **clean-room-audit** (per layer): **0 high** in every layer.
  - 01.Infrastructure.Shared: 7 files, 0 high / 0 medium
  - 02.Network.Layer: 72 files, 0 high / 12 medium
  - 03.Storage.Assets: 104 files, 0 high / 63 medium
  - 04.Client.Core: 113 files, 0 high / 61 medium
  - 05.Presentation: 72 files, 0 high / 11 medium

## Check 1 — decompiler artifacts in committed files: CLEAN

Direct grep for `sub_####`/`loc_`/`dword_`/`_DWORD`/`_QWORD`/`__thiscall`/`__fastcall`/`@@QAE`/`@@YA`/runtime addrs (`0x004xxxxx`) across in-scope C#. All hits are confirmed **false positives**:

| path:line | matched text | verdict | rationale |
|---|---|---|---|
| Mapping/TerrainGltfConverter.cs:75 (+ Gltf/Collision/BudScene converters) | `0x004E4942u` | false positive | ASCII `'BIN\0'` glTF GLB chunk-type magic — a file-format constant, not a runtime address. |
| Parsers/TerrainLayerParsers.cs:567,572,585,586 | `sub_chunk_count` | false positive | neutral field name (record-count), not a Hex-Rays `sub_` autoname. |
| Parsers/XeffParser.cs, Models/EffectData.cs, XeffJsonConverter.cs | `sub_effect_count`, `sub_effect[i]` | false positive | neutral field/loop names; each backed by `// spec: Docs/RE/formats/effects.md §A.2`. |

EnvironmentNode.cs (in scope): zero matches.

## Check 2 — dirty notes gitignored/untracked: CONFIRMED

- `git check-ignore -v Docs/RE/_dirty/campaign-vfs-deep-ii/` → matched `.gitignore:429 Docs/RE/_dirty/`.
- `git ls-files Docs/RE/_dirty/` → empty (nothing tracked).
- `git status --porcelain Docs/RE/_dirty/campaign-vfs-deep-ii/` → empty (untracked; dir exists on disk, holds the debugger-probe raw addresses legitimately, never enters git).

## Check 3 — journal pairing per in-scope spec: ALL PAIRED

Every in-scope spec has ≥1 exact-filename mention in `Docs/RE/journal.md`:

| spec | exact-filename mentions |
|---|---|
| formats/items_scr.md | 1 |
| formats/events_scr.md | 1 |
| formats/mud.md | 2 |
| formats/sound_tables.md | 6 |
| formats/environment_bins.md | 6 |
| formats/mi.md | 2 |
| formats/terrain.md | 6 |
| formats/terrain_layers.md | 8 |
| formats/config_tables.md | 9 |
| formats/text_tables.md | 2 |
| formats/scr.md | 2 |
| formats/bgtexture_lst.md | 2 |
| formats/xdb_tables.md | 3 |
| formats/animation.md | 10 |
| formats/authoring_sidecars.md (new) | 1 |
| formats/items_csv.md (new) | 1 |
| specs/environment.md | 5 |
| specs/asset_pipeline.md | 3 |

**No in-scope spec lacks a journal mention.**

## Check 4 — magic offsets cite their spec: SATISFIED

In-scope new/changed parsers carry dense `// spec: Docs/RE/...` citations
(ItemsScrParser 33, CitemsParser 34, SoundTableParser 37, MudSoundGridParser 11,
MobInfoPanelParser 30, EventsScrParser 32, BgtextureLstParser 19, AnimationParser 23,
ConfigTableParser 73, ItemsCsvParser 28).

MEDIUM uncited-offset hits from the scanner were corroborated in context and are NOT defects:

| path:line | scanner flag | verdict | rationale |
|---|---|---|---|
| Parsers/EnvironmentBinParsers.cs:325–342 | uncited-offset | false positive | each color-component read is covered by `// spec: ...environment_bins.md §9.2` on the same/preceding lines (323/330/337); scanner's per-line window missed the citation 2–3 lines up. |
| Mapping/TerrainGltfConverter.cs:453 | uncited-offset | false positive | `stackalloc byte[12]` is the GLB **output** header writer (well-known glTF format), not an eyeballed input offset. |
| Parsers/ParticleEmitterParser.cs:44–46 | uncited-offset | advisory | out-of-scope file; offset consts annotated CONFIRMED/UNRESOLVED — recommend adding explicit `// spec:` path when its format doc lands. |

No magic offset in changed **protocol/crypto/parser** code is eyeballed-without-provenance.

## Verdict

**PASS.** The firewall gate exited 0, zero HIGH-severity leakage smells exist in any layer, every in-scope spec is paired in the journal, and every magic offset in the in-scope parsers carries a spec citation.

This PASS asserts only the firewall invariants for this change set — quarantine stays out of git, no clean-room C# points at `_dirty/`, in-scope specs are journaled, and no decompiler artifact / uncited protocol-parser offset leaked. It is **not** a blanket correctness or copyright clearance; deeper human/spec review remains required for release.

### Recommendations (advisory only — engineer's call, no BLOCKERs)

- `03.Storage.Assets/.../ParticleEmitterParser.cs:44–46` (out of scope, untracked): add `// spec: Docs/RE/...` once the particle/fx format doc is committed, replacing the inline CONFIRMED/UNRESOLVED notes.
- Optional: nudge the in-scope offset citations onto the same line as the read where practical, so the heuristic scanner stops flagging them.
