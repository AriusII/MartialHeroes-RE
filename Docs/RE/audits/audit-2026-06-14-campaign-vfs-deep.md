**Verdict: PASS**

# Clean-room firewall audit — CAMPAIGN VFS-DEEP, Phase 5 gate

- **Date:** 2026-06-14
- **Branch:** campaign3
- **Auditor role:** clean-room auditor (human-trust backstop, EU 2009/24/EC Art. 6)
- **Scope note:** A PASS asserts ONLY the three firewall invariants + absence of the
  enumerated leakage smells held for this change set. It is not blanket innocence and
  not a substitute for human review.

## Change set audited

- **Mode:** staged (`git diff --cached --name-only --diff-filter=ACMR`), 43 staged files.
  The staged set is broader than the campaign brief (it also carries CAMPAIGN 5 wiring +
  some Godot/tooling edits); the whole staged set was audited, with focus on the
  VFS-DEEP specs and parsers named in the brief.
- **Skill 1 — clean-room-firewall-check (`--mode staged`):** exit code **0** (all three
  invariants green).
- **Skill 2 — clean-room-audit (smell scan):** **0 high, 0 medium** across every
  campaign new/changed `.cs` file. (Whole-tree run reports 0 high / 723 medium, but ALL
  mediums are in `.claude/skills/**` analysis scratch tools or are known scanner
  false-positives — see cleared list — none in the five numbered layers' campaign files.)
  `obj/`, `bin/`, `*.g.cs`, `*.Designer.cs`, `_dirty/` excluded.

## Findings table

| path:line | smell / violation | severity | true/false | rationale |
|---|---|---|---|---|
| — | Hex-Rays autonames (`sub_`/`loc_`/`dword_`/`off_`/`unk_`) in specs or C# | HIGH | none | Grep over `Docs/RE/**/*.md` + campaign `.cs`: no autoname matches. |
| — | MSVC/Hex-Rays pseudo-types (`_DWORD`/`__thiscall`/mangled `@@`) | HIGH | none | No matches in any committed spec or C#. |
| — | IDA addresses (`0x004xxxxx`) leaked into committed files | HIGH | none | No image-range VA matches in specs or C#. |
| — | Committed file under or citing `Docs/RE/_dirty/**` | BLOCKER | none | Invariant 1+2 green; no `.cs` under numbered layers references `_dirty/`; all spec `_dirty/` hits are README/journal process prose, not citations. |
| — | Uncited magic byte-offset in new C# (protocol/asset parser) | MEDIUM/BLOCKER | none | Every offset read in the new/corrected parsers carries an adjacent `// spec: Docs/RE/...` citation — smell scan returned 0 offset hits for campaign files. |
| — | Changed/new spec missing a journal mention (invariant 3) | BLOCKER | none | journal.md:947–961 "CAMPAIGN VFS-DEEP" block names all 8 new + all 8 changed specs explicitly. |

No HIGH/BLOCKER findings. Nothing requires remediation.

## False positives explicitly cleared

- `sub_effect_count` (effects.md:86/99/101/117/369/388/396/408; XeffParser) — legitimate
  recovered field name (count of sub-effect blocks), not a `sub_4A1230` autoname. Pre-cleared
  by the brief.
- `sub_chunk_count` (terrain_layers.md:291/334), `unk_dist` (terrain_layers.md:363/890),
  `_unk_c/d/e/f_` (effects.md:790–796) — neutral recovered/placeholder field labels in spec
  tables, not decompiler identifiers.
- `_dirty/` strings throughout `Docs/RE/journal.md` and `Docs/RE/README.md` — these DESCRIBE
  the firewall (where tainted material is quarantined); they are not C# citations and are not
  in the new campaign spec files. Invariant 2 (C# → `_dirty/`) returned zero matches.
- Whole-tree MEDIUM smells (DXT block locals `a0`/`a1`, triangle verts `v1`/`v2`/`v3`,
  CSV/array indices `f[10]`, `inv[12]`, `rec[24]`, `Slice(84,4)` DDS header reads) live in
  `.claude/skills/vfs-inspect/scripts/**` scratch tools and `Assets.Mapping` DXT/PNG/glTF
  codecs — outside the campaign change set and/or benign codec constants, not eyeballed wire
  offsets. Not graded for this gate.

## What was checked and held

- Invariant 1 — quarantine & copyrighted originals stay out of git: GREEN (no staged
  `_dirty/`/`*.pak`/`*.exe`/`*.dll`/`*.pcapng`/`*.tsv`).
- Invariant 2 — clean-room C# never points at `_dirty/`: GREEN (zero references in numbered layers).
- Invariant 3 — spec changes journaled: GREEN (VFS-DEEP block journal.md:947 covers every
  touched spec; journal.md itself is in the same change set).
- Smell scan — 0 high across the tree; 0 high/0 medium in every campaign parser; every new
  asset-offset read carries a `// spec:` citation (verified directly in CitemsParser,
  MudSoundGridParser, RegionBinParser, XeffParser).

The firewall held for this change set. The EU Art. 6 clean-room posture is intact for
CAMPAIGN VFS-DEEP. This green result asserts only these invariants — not absolution.
