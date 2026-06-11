# Clean-Room Audit — Real-Asset RE Wave (Phase 1)

**Verdict: PASS**

- Date: 2026-06-12
- Auditor: clean-room-auditor (read-only; no source/spec/fixture modified)
- Scope: the "RE assets réels" wave — 13 asset specs + journal + names.yaml, all
  authored/promoted against the real client VFS with samples held in the gitignored
  quarantine `Docs/RE/_dirty/samples/`.

## Mode and change set

Release/full-repo audit. Nothing is staged; the wave files are working-tree
modifications (8) and untracked new files (6). Audited the working-tree text of:

- Modified: `Docs/RE/formats/animation.md`, `config_tables.md`, `mesh.md`, `pak.md`
  (promoted CONFIRMED), `sound_tables.md`, `terrain.md`, `texture.md`,
  `Docs/RE/journal.md`, `Docs/RE/names.yaml`.
- New (untracked): `Docs/RE/formats/effects.md`, `misc_data.md`, `npc_spawns.md`,
  `shaders.md`, `terrain_layers.md`, `terrain_scene.md`.

(13 specs total: the 12 the requester listed plus `pak.md`.)

## Skill verdicts (raw)

- `clean-room-firewall-check --mode tracked` → exit **0** (gate green). All three
  invariants OK over 354 tracked files: quarantine/originals out of git; no C#
  `_dirty/` ref; changed specs journaled (journal.md is in the same change set,
  and was additionally confirmed to name every spec — see Invariant 3 below).
- `clean-room-audit` (root `Docs/RE`) → **0 high, 0 medium** — but note the scanner
  only walks `**/*.cs`; this wave touches zero `.cs` files, so the C# scanner is
  vacuous here. The substantive leak check was performed manually by applying the
  HIGH/MEDIUM heuristic patterns to the spec Markdown/YAML (results below).

## Findings — manual heuristic scan of the spec text

| Path:line | Smell pattern checked | Severity | Result | Rationale |
|---|---|---|---|---|
| (all specs) | Hex-Rays autonames `sub_/loc_/dword_/off_/byte_/unk_` | HIGH | none | Grep over `**/*.{md,yaml}` returned no matches. |
| (all specs) | MSVC pseudo-types / callconv `_DWORD/__int64/__thiscall` | HIGH | none | No matches. |
| (all specs) | Mangled MSVC symbols `?x@Cls@@…` / `@@QAE` | HIGH | none | No matches. |
| (all specs) | Hex-Rays locals/args `v1/v2/a1` as identifiers | MEDIUM | none | No matches in spec bodies. |
| `journal.md:67`, `names.yaml:13` | imagebase `0x400000` | n/a | OK (not a leak) | Both are **provenance metadata** (binary record / glossary header), the designed home for the imagebase. Not embedded in any format-spec body. |
| `shaders.md` v0/v1/v2, c0–c10, t0 | looks-like Hex-Rays locals | — | FALSE POSITIVE | These are **D3D9 shader-assembly registers** (vertex input / constant / texture), explicitly documented as standard case-insensitive D3D9 notation. Format facts, not decompiler locals. |
| `effects.md`, `sound_tables.md`, `names.yaml` `0x46464558` ('XEFF') | magic literal | — | OK | Documented file-validity sentinel; format fact. |
| `texture.md`, `names.yaml` `0x20534444` ('DDS ') | magic literal | — | OK | Standard DDS container magic; format fact. |
| `config_tables.md` `0x7FFFFFFF` | magic literal | — | OK | INT32_MAX sentinel records; format fact. |
| `config_tables.md`, `npc_spawns.md` `field_NN` (14) | could be IDA struct autoname | — | FALSE POSITIVE | Neutral **offset-derived member names** (`field_04` = member at +4) each carrying "semantic UNVERIFIED" notes. This is the scrub the engineer applied (per journal: `v3/v5/v6 -> field_NN`). Not `sub_`/address-bearing IDA names. |
| `pak.md:99-109` "implementation sketch" | transcribed pseudo-code | — | REVIEWED / CLEARED | Plain-language algorithm (`BinarySearch`, `SeekAbsolute`, `Allocate`); no decompiler artifacts (`sub_`, `_DWORD`, `__thiscall`, `vN`). A clean-room behavioural description, not pasted Hex-Rays output. |
| `formats/*` `UPPER_SNAKE_CASE` tokens | analyst symbol names | — | FALSE POSITIVE | Config keys (`OPTION_*`), terrain grammar keywords (`MAX_HEIGHTFILED`, `EXTRA_TERRAIN`), and project-named constants (`XEFF_*`) — data/format identifiers, not function symbols. The previously-noted `EditorTool_*` / `TextureListTxt_*` analyst names are absent. |
| "IDA" / "decompiler" mentions | methodological leak | — | OK | All occurrences are firewall disclaimers ("NO decompiler pseudo-code"), README/glossary methodology, or required journal provenance (`tool: IDA Pro 9.3 via MCP`). No standalone editorializing inside spec bodies. |

## Hard invariants (firewall gate, restated)

1. **Quarantine & originals out of git** — PASS. `git ls-files Docs/RE/_dirty/` = 0
   entries; `_dirty/` is gitignored at `.gitignore:429` (covers `samples/`,
   `assets/`, and any stray file beneath). No tracked/staged `*.pak/.pcapng/.tsv/
   .exe/.dll`. No `.bud/.ted/.mot/.scr/.dds/.png/.csv/.vfs/.inf` sample is tracked
   or staged (all live only under the ignored `_dirty/`).
2. **No C# references `_dirty/`** — PASS (vacuous; no `.cs` in the change set).
3. **Changed specs journaled** — PASS. The consolidated 2026-06-11 journal entry
   names all 13 specs by filename (mention counts: terrain 2, terrain_scene 1,
   terrain_layers 1, animation 2, mesh 2, config_tables 2, texture 2, sound_tables
   2, effects 1, shaders 1, npc_spawns 1, misc_data 1, pak 3), with provenance
   (binary `doida.exe @ 63fcaf8e`, the real client source, the `_dirty/samples/`
   quarantine, dirty->clean rewrite chain). Art. 6 trail intact per spec.

## Note on the binary-extension gitignore (defense-in-depth observation, not a violation)

`.bud/.ted/.mot/.scr/.dds/.png/.csv/.vfs/.inf` are **not** individually ignored by
extension; they are protected solely by living under the ignored `Docs/RE/_dirty/`.
The firewall holds for this change set (none are tracked). Recommendation (optional,
no action required for this commit): add these sample extensions to `.gitignore` so a
future stray extraction outside `_dirty/` cannot be staged by accident.

## What a PASS asserts here

The firewall gate is green and the manual heuristic scan over the spec text found no
confirmed HIGH leak, no transcribed Hex-Rays pseudo-code, no decompiler identifiers
or imagebase addresses in any spec body, and no copyrighted sample tracked/staged.
The journal backs every changed spec. **This PASS asserts only these invariants for
this change set; it is not blanket innocence.** Per the hard rules, no file under
`_dirty/` was opened to "verify" any hit — the audit worked on committed/working-tree
text and git paths only.
