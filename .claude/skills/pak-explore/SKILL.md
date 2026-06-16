---
name: pak-explore
description: Use to inspect a Martial Heroes .pak archive index from the documented format spec (Docs/RE/formats/pak.md); lists entry name/offset/size only and never extracts or prints copyrighted payload bytes.
allowed-tools: Read Write Bash(python *)
model: sonnet
effort: medium
---

# pak-explore

Read-only inspection of a legacy *Martial Heroes* (`.pak`) archive's **directory/index** so an
engineer can sanity-check the `Docs/RE/formats/pak.md` spec, plan `Assets.Vfs` mounting, and count
or locate logical files — **without ever reading, copying, or emitting the compressed/raw asset
bytes** the archive contains.

A `.pak` is a user-supplied original game file. It is gitignored (`*.pak`) and copyright-tainted.
This skill touches only the small index region described by the committed format spec; the payload
is off-limits.

**Ground-truth doctrine.** The archive's real layout is the truth — proved in the original's loader
routines inside `doida.exe` and/or witnessed directly in the maintainer's own sample bytes, then
captured in `Docs/RE/formats/pak.md`. That committed spec is the **derived truth** this skill reads
from; it lists index metadata (name/offset/size) only and **never** emits copyrighted payload bytes.

## Hard rules (non-negotiable)

1. **NEVER extract, decompress, decode, hexdump, or print payload bytes** from a `.pak`. The only
   data that may leave this skill is, **per directory entry**: the logical name (a path string),
   the byte offset of its payload, and the payload's byte length (plus optional flags/CRC that the
   spec marks as index-only metadata). Sizes and offsets are numbers, not content.
2. The bundled `pak_index.py` is a **listing tool only** and has no extract mode. Do not write,
   suggest, or run any code that seeks to an entry's `offset` and reads `size` bytes of payload. If
   the user asks to "extract", "unpack", "dump the files", or "get the bytes out", **REFUSE** and
   explain that extraction of copyrighted payload is outside this project's clean-room/preservation
   scope; offer the index listing instead.
3. **Read-only on disk.** Open the archive read-only. Never modify, truncate, or rewrite a `.pak`.
4. **Output goes to stdout or a gitignored scratch file only.** Never write a listing into a
   committed/tracked path. The repo gitignores `/.work/`, `*.tsv`, and `Docs/RE/_dirty/`; write any
   saved listing under `_dirty/scratch/` (e.g. `Docs/RE/_dirty/scratch/pak-index.txt`) or `.work/`.
   Entry names alone are not copyrighted, but keep generated artifacts local by default.
5. **Spec-driven, not guess-driven.** The exact header magic, endianness, and entry record layout
   come from `Docs/RE/formats/pak.md`. Pass the layout to the script via flags (see below). Do not
   hardcode reverse-engineered offsets here without citing that spec.

## Inputs

- A path to a `.pak` archive (user-supplied; lives outside the repo or under `/LegacyClient/`).
- The `Docs/RE/formats/pak.md` format spec, which defines the index layout this skill reads.

## Steps

1. **Confirm intent is inspection, not extraction.** If the request is to unpack/extract payload,
   stop and apply Hard Rule #2.

2. **Read the spec.** Read `Docs/RE/formats/pak.md` and extract the index parameters:
   - file magic / signature bytes and expected version,
   - endianness (little vs big),
   - where the directory lives (header-after-magic, or a footer index at end-of-file),
   - the per-entry record: name encoding (fixed-length char field vs length-prefixed vs offset into
     a name table) and the field order/sizes for `name`, `offset`, `size` (and any flags/CRC).

   If `Docs/RE/formats/pak.md` does **not exist yet**, tell the user the spec is missing and that
   the format must be documented first (use the `asset-format-doc` skill to seed
   `Docs/RE/formats/pak.md`). Do not invent a layout. You may still run the script in its
   self-describing `--probe` mode (below) to report the file's size and first magic bytes only, to
   help bootstrap the spec — that reads a handful of header bytes, never payload.

3. **List the index.** Run the bundled script, mapping the spec's parameters onto its flags:

   ```bash
   python "${CLAUDE_SKILL_DIR}/scripts/pak_index.py" \
     --pak "<path-to.pak>" \
     --magic "<hex or ascii signature from spec>" \
     --endian <little|big> \
     --index <header|footer> \
     --layout "<field=size,... per spec>"
   ```

   Run `python "${CLAUDE_SKILL_DIR}/scripts/pak_index.py" --help` to see the exact flag grammar and
   the built-in layout presets. The script validates the magic, walks the directory, and prints one
   line per entry: `index  offset  size  name`. It refuses to read beyond the index region.

4. **Optionally save the listing** to a gitignored scratch file (Hard Rule #4) for an engineer to
   reference while wiring `Assets.Vfs`:

   ```bash
   python "${CLAUDE_SKILL_DIR}/scripts/pak_index.py" --pak "<...>" ... \
     > "Docs/RE/_dirty/scratch/pak-index.txt"
   ```

5. **Summarize for the engineer**: entry count, total payload bytes covered, any names that look
   like directory roots (useful for `Assets.Vfs` mount points), and whether offsets are monotonic
   and within the file size (a cheap integrity check that needs no payload reads).

## Decision points

- **`pak.md` missing?** Don't invent a layout — run `--probe` (size + first magic bytes only) to
  bootstrap, then hand to `asset-format-doc` to seed `Docs/RE/formats/pak.md` first.
- **Offsets non-monotonic or out of range?** That's a layout/endianness mismatch with the spec,
  not a corrupt file — recheck `--endian` and the entry record sizes against `pak.md` before
  trusting the listing.
- **User wants the bytes out?** Any "extract / unpack / dump / get the bytes" request → REFUSE
  (Hard Rule #2) and offer the index listing. Existence + name/offset/size only — never payload.
- **Tracing an asset, not auditing the archive?** If the goal is "where does mob/skin/terrain id
  X resolve on disk", that's the recovered-chain job — use `/asset-chain-trace`, not this skill.

Verify / Done when: magic validated against `pak.md`; one `index offset size name` line per
entry; offsets are monotonic and within file size (the integrity check); entry count + total
covered bytes reported; **zero payload bytes** were read; any saved listing lives under
`_dirty/scratch/` or `.work/`.

## Pitfalls (anti-patterns)

- **Never** seek to an entry `offset` and read `size` payload bytes — index region only.
- **Never** hexdump, decompress, or decode entry content — this is a listing tool with no
  extract mode.
- **Never** write a listing into a committed/tracked path, and never modify the `.pak`.
- Don't hardcode RE'd offsets here — they come from `pak.md` via flags, cited.

North star: serves **N2** — knowing the archive index (without ever copying payload) is the
first step to faithfully reproducing the original asset set.

## What this skill deliberately does NOT do

- It does not open or stream payload regions, so it cannot and will not extract assets.
- It does not write to any committed spec file (that is the `asset-format-doc` skill's job).
- It does not call IDA or read `_dirty/` decompiler output; it reads only the archive's index
  bytes and the neutral committed spec.
