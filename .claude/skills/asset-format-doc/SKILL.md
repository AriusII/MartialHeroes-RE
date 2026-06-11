---
name: asset-format-doc
description: Use to start documenting a new binary asset format from the legacy Martial Heroes client (mesh, terrain, anim, texture, etc.); scaffolds a neutral Docs/RE/formats/<ext>.md spec and produces an annotated hexdump of a sample to seed it, without ever committing sample bytes.
allowed-tools: Read Write Bash(python *)
---

# asset-format-doc

Bootstrap a **clean-room format spec** for a not-yet-documented binary asset extracted from the
legacy *Martial Heroes* client (e.g. `.msh` mesh, `.ter` terrain, `.ani` animation, `.tex`
texture). The output is a committed, neutral-prose spec at `Docs/RE/formats/<ext>.md` that an
engineer can implement `Assets.Parsers` from — plus a throwaway annotated hexdump that lives only
in the gitignored quarantine while you reason about the layout.

This is the *seed* step of the firewall flow described in `Docs/RE/README.md`:
raw sample/decompiler insight (dirty) -> neutral spec in `formats/` (committed) -> engineer
implements fresh. This skill writes the neutral spec and helps you read the byte layout; it does
**not** read IDA output and does **not** write any C#.

## Hard rules (non-negotiable)

1. **Never commit sample bytes.** The annotated hexdump derived from a real asset is
   copyright-tainted and stays in the gitignored quarantine `Docs/RE/_dirty/` (or `/.work/`). The
   committed `Docs/RE/formats/<ext>.md` contains only **neutral descriptions** — field names,
   offsets, sizes, types, magic values, enumerations, and prose. It must NOT contain pasted hex
   rows, byte tables copied from a specific file, or any reproduction of asset content.
2. **Describe layout, don't transcribe payload.** Documenting "bytes 0..3 are ASCII magic `MSH0`;
   bytes 4..5 are a u16 version" is allowed and is the whole point. Pasting a 256-byte hex blob of
   a real model into the committed spec is not.
3. **No decompiler pseudo-code, no IDA.** This skill never calls `mcp__ida__*` and never reads
   `_dirty/` decompiler dumps. Layout knowledge here comes from staring at the binary's structure
   (via the bundled hexdump tool) and from neutral notes a spec-author already promoted.
4. **One spec per extension; don't clobber.** The committed spec is `Docs/RE/formats/<ext>.md`
   where `<ext>` is the lowercase extension without the dot (e.g. `msh`). If it already exists,
   read it and extend it — never overwrite a populated spec.
5. **Every committed spec change needs a `journal.md` entry** (per `Docs/RE/README.md` rule 3).
   Remind the user to append a provenance line after the spec is filled in.

## Inputs

- The target extension (e.g. `msh`, `ter`, `ani`, `tex`).
- A path to one (or a few) **sample** files of that format — user-supplied, gitignored originals,
  typically pulled from a `.pak` (see the `pak-explore` skill) into `/LegacyClient/` or a scratch
  dir. Samples are never committed.

## Steps

1. **Produce an annotated hexdump of a sample** into the gitignored quarantine. The bundled tool
   prints `offset | hex | ASCII | guessed-field` rows and a heuristic header summary (probable
   magic, candidate u16/u32 fields, embedded strings, repeated record stride):

   ```bash
   python "${CLAUDE_SKILL_DIR}/scripts/hexdump_annotate.py" \
     --file "<path-to-sample.msh>" \
     --length 512 \
     > "Docs/RE/_dirty/scratch/msh.hexdump.txt"
   ```

   Run `python "${CLAUDE_SKILL_DIR}/scripts/hexdump_annotate.py" --help` for options
   (`--offset` to start deeper, `--width` bytes per row, `--guess`/`--no-guess` for field heuristics,
   `--endian`). **The dump output is dirty** — keep it under `_dirty/` or `/.work/`, never in
   `formats/`.

2. **Scaffold the committed spec** if it does not exist yet. Create `Docs/RE/formats/<ext>.md`
   using the template below. Fill the magic/version from what the hexdump revealed; leave the rest
   as clearly-marked TODO/unknown so the engineer knows what is verified vs. guessed.

3. **Reason from the hexdump into neutral prose.** Translate observed structure into the spec's
   tables (header fields, then record layout). Mark each field's confidence. Move anything you are
   unsure about into "Known unknowns". **Do not paste hexdump rows into the spec** (Hard Rule #1).

4. **Cross-check with a second sample** if available — re-run the tool on another file and confirm
   the magic/version are stable and the record stride matches. Note variance in the spec.

5. **Hand off.** Tell the user to (a) append a `journal.md` provenance entry, and (b) that an
   `Assets.Parsers` engineer can now implement from `Docs/RE/formats/<ext>.md`, citing offsets with
   `// spec: Docs/RE/formats/<ext>.md`.

## Committed spec template (write to `Docs/RE/formats/<ext>.md`)

```markdown
# Format: .<ext>  (<one-line role, e.g. "static mesh geometry">)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by `Assets.Parsers`. Every offset an engineer cites must reference this file.

## Identification
- **Extension:** `.<ext>`
- **Found in:** `.pak` archive (logical path pattern: TODO, e.g. `models/*.<ext>`)
- **Magic / signature:** TODO (e.g. ASCII `MSH0` = `4D 53 48 30`) — confidence: TODO
- **Version field:** TODO (offset, size, observed values) — confidence: TODO
- **Endianness:** TODO (little / big)

## Header layout
| Offset | Size | Type   | Field        | Notes / observed values        | Confidence |
|-------:|-----:|--------|--------------|--------------------------------|------------|
| 0x00   | 4    | char[] | magic        | TODO                           | TODO       |
| 0x04   | 2    | u16    | version      | TODO                           | TODO       |
| ...    | ...  | ...    | TODO         | TODO                           | TODO       |

## Record / body layout
<Describe the repeating record(s) or sections after the header: stride, count source, field order.>

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| ...    | ...  | ...  | TODO  | TODO  | TODO       |

- **Record count source:** TODO (a header field? derived from file size / stride?)
- **Record stride:** TODO bytes

## Enumerations / flags
<Any enum or bitflag fields and their meanings, once known.>

## Known unknowns
- TODO: list every field/region not yet understood, so the engineer doesn't guess.

## Cross-references
- Related formats: TODO
- Glossary: see `Docs/RE/names.yaml`
- Provenance: see `Docs/RE/journal.md` (add an entry for this spec)
```

## What this skill deliberately does NOT do

- It does not write asset bytes into any committed file.
- It does not implement a parser (that's an `Assets.Parsers` engineer's job, from this spec).
- It does not call IDA or read `_dirty/` decompiler output; it reads only the sample's own bytes
  (kept dirty) and emits neutral prose.
