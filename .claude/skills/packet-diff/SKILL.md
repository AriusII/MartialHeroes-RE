---
name: packet-diff
description: Use to isolate which bytes encode a field by diffing two (or more) captured packets of the same opcode. Aligns the messages, diffs them byte-column by byte-column, highlights the offsets that vary, and emits an offset->hypothesis table you paste into the Notes of a Docs/RE/packets/*.yaml spec. Pure observation of capture bytes — clean-room-safe, no IDA, no decompiler.
allowed-tools: Bash(tshark *) Read Write
model: sonnet
effort: high
---

# packet-diff

Differential analysis of captured *Martial Heroes* packets: given two or more messages that
share an opcode but differ in one observable game variable (player position, HP, item id…),
diff them column-by-column to localize which byte offsets carry that variable. This is the
clean-room way to derive packet layouts from the **capture oracle** without ever opening the
decompiler.

## When to use

- You have `.tsv` extracts from `pcap-extract` (or raw hex payloads pasted by the user) and
  you want to know *where in the packet* a field lives.
- You are about to write or refine a `Docs/RE/packets/*.yaml` spec and need offset evidence.
- Direction tag (`C2S`/`S2C`) and opcode are already known or hypothesized.

This skill **localizes** offsets; it does not decide endianness or semantics on its own — it
hands you a ranked, evidence-backed hypothesis table to fill in.

## Inputs

Provide the bytes one of three ways:

1. `--hex "aa01..." --hex "aa02..."` — two or more raw payload hex strings on the CLI.
2. `--tsv stream_3.tsv --col data.data` — pull the `data.data` column from a `pcap-extract`
   `.tsv`; combine with `--opcode 42` and `--opcode-offset 0` to keep only rows whose first
   byte(s) match the opcode, and `--limit N` to cap how many are compared.
3. A mix — the script concatenates all sources.

All inputs are gitignored capture-derived material; nothing here is committed except the YAML
note you paste the result into.

## Workflow

1. **Gather aligned samples.** Pick 2+ packets of the *same opcode and direction* where you
   know one game variable changed between them (ideally only one). The more the samples agree
   on everything except the target field, the sharper the diff.

2. **Run the diff:**

   ```powershell
   python ${CLAUDE_SKILL_DIR}/scripts/byte_diff.py --hex "<msgA hex>" --hex "<msgB hex>"
   ```

   or from a capture extract:

   ```powershell
   python ${CLAUDE_SKILL_DIR}/scripts/byte_diff.py --tsv "_dirty/captures/stream_3.tsv" --opcode 0x42 --limit 8
   ```

   The script:
   - Aligns messages from offset 0 (left-aligned; mismatched lengths are compared up to the
     shortest, with trailing bytes flagged as `len-variant`).
   - Prints a per-offset column view: offset, the byte value from each sample (hex), and a
     marker — `=` constant across all samples, `*` variant, `.` only-present-in-some.
   - Groups contiguous variant offsets into candidate **fields** and guesses width
     (1/2/4 bytes) and whether the values look like a monotonic counter, a small enum, or a
     coordinate-like value.

3. **Read the offset → hypothesis table.** The script ends with a ready-to-paste table:

   ```
   offset  width  bytes(per sample)        hypothesis
   0x00    1      42 42 42                  opcode (constant) = 0x42
   0x01    2      0100 0200 0300            le u16, monotonic -> sequence/counter
   0x06    4      <varies>                  candidate coordinate (4B) -- confirm endianness
   ```

4. **Promote into the spec.** Open or create `Docs/RE/packets/<name>.yaml`, set/confirm
   fields at the localized offsets, and paste the hypothesis table under the spec's `notes:`
   so the evidence travels with the spec. Mark uncertain fields `status: draft`. If you
   created/changed a committed spec, add a one-line entry to `Docs/RE/journal.md`.

## Decision points

- **Variant block looks like a coordinate?** A 4-byte run that changes smoothly with player
  movement is almost always an `f32` (little-endian) — confirm by re-diffing two positions a
  known delta apart. A 2-byte run that ticks `+1` per packet is a sequence/counter `u16`.
- **Where does the payload start?** The first bytes are the opcode pair, not field data — the
  wire frame is `[u32 size][u16 major][u16 minor]` and opcodes combine as `(major<<16)|minor`.
  Align the diff *after* the header if you are diffing whole frames, or feed already-stripped
  payloads.
- **Nothing varies / everything varies?** All-`=` means your samples didn't actually change the
  target variable — vary it harder. All-`*` past the header usually means you crossed opcodes or
  directions — re-filter (`--opcode`, fixed direction) before trusting the table.
- **Ragged lengths?** A trailing `.` region is a variable-length tail (string/array) → record it
  `size: var`, not a fixed field.

Verify / Done when: the diff fixes *one* opcode + direction; the varying offsets line up with
the single game variable you changed; the hypothesis table names width + a candidate type per
field; uncertain rows are marked `status: draft`; the table is pasted under the spec's `notes:`
and (if a committed spec changed) a `journal.md` line is added.

## Pitfalls (anti-patterns)

- **Never** diff across different opcodes or directions — that yields noise, not fields.
- **Never** assert endianness/semantics the diff alone can't support — they stay labeled
  hypotheses for confirmation against more captures.
- **Never** read `_dirty/` decompiler notes or call IDA to "explain" a byte — this is the
  capture-only lane.
- Don't forget the 8-byte frame header offset when diffing whole frames vs stripped payloads.

North star: serves **N2** — localizing real wire offsets is how the re-implemented packet
structs reach byte-exact parity with the original.

## Reading the markers

- `=` the byte is **identical** in every sample → structural/constant (opcode, flags, padding,
  or a field that simply didn't change between your samples — vary it more to be sure).
- `*` the byte **differs** → part of a field that tracks the variable you changed.
- `.` the byte exists in some samples but not all → length-variant region (variable-length
  tail, string, or trailing array). Treat as `size: var`.

## Hard rules

- **Observation only.** This skill never calls IDA, never reads `_dirty/` decompiler notes,
  and never asserts a byte meaning the diff cannot support. Width/endianness guesses are
  labeled hypotheses for a human to confirm against more captures.
- Inputs (`.tsv`, raw hex) are capture-derived and gitignored. The **only** committed artifact
  is the hypothesis you transcribe into `Docs/RE/packets/*.yaml` (+ a journal line).
- Diff at least samples where the *same* opcode/direction is fixed; diffing across opcodes
  produces noise, not fields.
