---
name: pcap-extract
description: Use to work the Martial Heroes capture oracle — two modes. EXTRACT turns a .pcapng capture into per-TCP-stream .tsv extracts (splits by tcp.stream, one tab-separated row per segment with frame.time_epoch, tcp.stream, ip.src, tcp.srcport, tcp.len, data.data + a C2S/S2C direction tag) and is the single version-controlled home of the canonical tshark invocation. FIELD-DIFF isolates which bytes encode a field by diffing two or more captured packets of the same opcode — aligns them, diffs byte-column by byte-column, highlights the varying offsets, and emits an offset->hypothesis table to paste into a Docs/RE/packets/*.yaml spec's notes. Pure observation of capture bytes — clean-room-safe, never reads _dirty/, never calls IDA.
allowed-tools: Bash(tshark *) Bash(python *) Read Write
model: sonnet
effort: high
---

# pcap-extract

Work the *Martial Heroes* capture **oracle** — **two modes** that share one stance (the capture is an
oracle, **not** copyrighted code: observing bytes on the wire is clean-room-safe, so there is no
`_dirty/` involvement and IDA is never called):

- **Mode A — EXTRACT:** turn a raw `.pcapng` into regenerable, per-stream `.tsv` extracts.
- **Mode B — FIELD-DIFF:** diff 2+ captured packets of the same opcode to localize which byte offsets
  carry a field.

**Ground-truth doctrine.** For the wire protocol, the original's framing/parsing/dispatch code inside
`doida.exe` is the absolute truth for layout/opcodes; this capture is the **corroborating oracle** for
what actually went over the socket — together they ground a faithful spec. These modes only
*transport* and *diff* observed bytes; they never interpret semantics as final, never read `_dirty/`,
and never call IDA. Width/endianness guesses stay labeled hypotheses for a spec-author to confirm.

# Mode A — EXTRACT (.pcapng → per-stream .tsv)

Turn a raw Wireshark capture of a live (or replayed) session into regenerable, per-stream `.tsv`
extracts that downstream clean-room work (Mode B, `packet-codegen`) consumes.

## Why this skill exists

`CLAUDE.md` and `.gitignore` both reference "the `tshark` extraction command" without
defining it — `.gitignore` line 413 literally says *"see the pcap-extract skill"*. This
skill **is** that definition. `references/tshark.md` version-controls the exact invocation
so every extract in the project is byte-reproducible from the same capture.

## Inputs and outputs (all gitignored)

- **Input:** a `.pcapng` the maintainer supplies locally (e.g. the ~204 MB "Vasselix"
  combat capture). `*.pcapng` is gitignored — never stage it, never copy it into the repo.
- **Output:** `.tsv` files under a local, gitignored work dir. `*.tsv` is gitignored and is
  *derived*, never source. Default output dir: `_dirty/captures/` (already gitignored), or
  any path the user names. **Only `references/tshark.md` (the command doc) is committed.**

The `.tsv` columns, in order, are:

| Column | tshark field | Meaning |
|---|---|---|
| `frame.time_epoch` | `frame.time_epoch` | Absolute capture timestamp (orders segments). |
| `tcp.stream` | `tcp.stream` | tshark's per-connection stream index. |
| `ip.src` | `ip.src` | Source IP (used to derive direction). |
| `tcp.srcport` | `tcp.srcport` | Source TCP port. |
| `tcp.len` | `tcp.len` | TCP payload length in bytes (0 = pure ACK, dropped). |
| `data.data` | `data.data` | TCP payload as a lowercase hex string, colon-free. |
| `dir` | *(derived)* | `C2S` or `S2C` — added by this skill, see step 4. |

## Workflow

1. **Confirm tshark is available.** Run `tshark --version`. If it is missing, stop and tell
   the user to install Wireshark/tshark and put it on `PATH`; do not attempt a workaround.

2. **Locate the capture.** Ask the user for the absolute path to the `.pcapng` if they did
   not give one. Never search the repo for it — it lives outside the tree by policy.

3. **Run the canonical extraction.** Use the wrapper, which encodes the exact, version-
   controlled tshark command from `references/tshark.md`:

   ```powershell
   python ${CLAUDE_SKILL_DIR}/scripts/extract_streams.py --in "<capture>.pcapng" --out-dir "_dirty/captures"
   ```

   The wrapper runs tshark once, then fans the rows out into one `streams_all.tsv` plus one
   `stream_<N>.tsv` per `tcp.stream`. Pass `--server-port <p>` and/or `--server-ip <ip>` to
   tag direction (see step 4); pass `--stream <N>` to extract a single stream.

4. **Direction tagging (C2S vs S2C).** Direction is from the **client's** point of view and
   must match `Docs/RE/opcodes.md` (`C2S` = client→server, `S2C` = server→client). The
   wrapper assigns it from whichever discriminator the user supplies:
   - `--server-port 9001` → rows whose `tcp.srcport == 9001` are `S2C`, else `C2S`.
   - `--server-ip 10.0.0.5` → rows whose `ip.src == 10.0.0.5` are `S2C`, else `C2S`.
   - If neither is given, `dir` is left as `?` and the skill prints the distinct
     `(ip.src, tcp.srcport)` pairs so the user can identify the server endpoint and re-run.

5. **Report, do not commit.** Print the output dir, the per-stream row counts, and the
   server endpoint used. Remind the user the `.tsv` files are gitignored and regenerable —
   nothing here gets staged. If you changed the canonical command, update
   `references/tshark.md` **and** say so explicitly.

## Decision points

- **Many tiny streams vs one fat stream?** The login flow is its own short-lived stream;
  the world/game stream is the long high-volume one — usually the highest-row-count
  `stream_<N>.tsv`. Steer Mode B (and `packet-codegen`) to that stream for combat/movement, the
  short one for the lobby/PIN handshake.
- **Direction can't be tagged?** If both `--server-port` and `--server-ip` are absent and the
  printed `(ip.src, tcp.srcport)` pairs are ambiguous, the server side is the endpoint that
  *replies first* after the client's SYN and carries the bulk of S2C broadcast traffic — name
  it and re-run rather than committing `?` rows.
- **Reassembly:** the 8-byte frame `[u32 size][u16 major][u16 minor]` can span TCP segments,
  so a single `data.data` row is *not* guaranteed to be one packet. Leave reframing to the
  consumer; this skill emits per-segment payload, never re-split frames.

Verify / Done when (Mode A): `streams_all.tsv` + one `stream_<N>.tsv` per stream exist under the
gitignored out-dir; each row has all 7 columns; `dir` is `C2S`/`S2C` (not `?`) for the stream
you care about; pure-ACK rows (`tcp.len == 0`) are dropped; nothing was staged.

# Mode B — FIELD-DIFF (localize a field's byte offsets)

Differential analysis of captured packets: given 2+ messages that share an opcode but differ in one
observable game variable (player position, HP, item id…), diff them column-by-column to localize which
byte offsets carry that variable — the clean-room way to derive packet layouts from the capture oracle
without ever opening the decompiler. It **localizes** offsets; it does not decide endianness/semantics
on its own — it hands you a ranked, evidence-backed hypothesis table for a spec-author to confirm.

**Use it when** you have `.tsv` extracts from Mode A (or raw hex pasted by the user) and want to know
*where in the packet* a field lives, you are about to write/refine a `Docs/RE/packets/*.yaml` spec, and
the direction tag (`C2S`/`S2C`) + opcode are known or hypothesized.

**Inputs** (one of three ways): `--hex "aa01..." --hex "aa02..."` (raw payload hex on the CLI);
`--tsv stream_3.tsv --col data.data` (pull the `data.data` column from a Mode A `.tsv`, combine with
`--opcode 42 --opcode-offset 0` to keep only matching rows and `--limit N` to cap); or a mix (sources
concatenate). All inputs are gitignored capture-derived material.

1. **Gather aligned samples.** Pick 2+ packets of the *same opcode and direction* where you know one
   game variable changed (ideally only one). The more they agree on everything except the target field,
   the sharper the diff.
2. **Run the diff:**
   ```powershell
   python ${CLAUDE_SKILL_DIR}/scripts/byte_diff.py --hex "<msgA hex>" --hex "<msgB hex>"
   python ${CLAUDE_SKILL_DIR}/scripts/byte_diff.py --tsv "_dirty/captures/stream_3.tsv" --opcode 0x42 --limit 8
   ```
   The script aligns messages from offset 0 (mismatched lengths compared up to the shortest, trailing
   bytes flagged `len-variant`); prints a per-offset column view (offset, each sample's byte in hex, and
   a marker — `=` constant, `*` variant, `.` only-present-in-some); and groups contiguous variant
   offsets into candidate **fields**, guessing width (1/2/4) and whether values look like a monotonic
   counter, a small enum, or a coordinate.
3. **Read the offset → hypothesis table** the script ends with:
   ```
   offset  width  bytes(per sample)        hypothesis
   0x00    1      42 42 42                  opcode (constant) = 0x42
   0x01    2      0100 0200 0300            le u16, monotonic -> sequence/counter
   0x06    4      <varies>                  candidate coordinate (4B) -- confirm endianness
   ```
4. **Promote into the spec.** Open/create `Docs/RE/packets/<name>.yaml`, set/confirm fields at the
   localized offsets, and paste the hypothesis table under the spec's `notes:` so the evidence travels
   with the spec. Mark uncertain fields `status: draft`. If you created/changed a committed spec, add a
   one-line entry to `Docs/RE/journal.md` (via the `preservation` session-log mode).

**Mode B decision points:** a 4-byte run that changes smoothly with movement is almost always an `f32`
(little-endian) — confirm by re-diffing two positions a known delta apart; a 2-byte run ticking `+1`
per packet is a sequence/counter `u16`. The first bytes are the opcode pair, not field data — the wire
frame is `[u32 size][u16 major][u16 minor]`, opcodes combine as `(major<<16)|minor`; align the diff
*after* the header (or feed already-stripped payloads). All-`=` means the samples didn't actually
change the target variable — vary it harder; all-`*` past the header usually means you crossed
opcodes/directions — re-filter. A trailing `.` region is a variable-length tail → record it `size: var`.

**Reading the markers:** `=` identical in every sample → structural/constant (opcode, flags, padding,
or a field that just didn't change — vary it more); `*` differs → part of a field tracking the variable
you changed; `.` present in some samples but not all → length-variant region (variable-length tail,
string, trailing array) → treat as `size: var`.

Verify / Done when (Mode B): the diff fixes *one* opcode + direction; the varying offsets line up with
the single game variable you changed; the hypothesis table names width + a candidate type per field;
uncertain rows are marked `status: draft`; the table is pasted under the spec's `notes:` and (if a
committed spec changed) a `journal.md` line is added.

## Pitfalls (anti-patterns)

- **Never** stage the `.pcapng` or any `.tsv` — only `references/tshark.md` is committed.
- **Never** edit the tshark fields in `scripts/extract_streams.py` without also editing
  `references/tshark.md` — the two must never drift.
- **Never** interpret a byte's meaning as final — both modes only observe; width/endianness/semantics
  stay labeled hypotheses for a spec-author to confirm against more captures.
- **Never** diff across different opcodes or directions (Mode B) — that yields noise, not fields.
- **Never** read `_dirty/` decompiler notes or call IDA to "explain" a byte — this is the capture-only lane.
- Don't treat one `data.data` row as one packet — the 8-byte wire frame can straddle TCP segments.

North star: serves **N2** — the capture is the ground-truth oracle for byte-exact wire parity
with the original client.

## Hard rules

- The `.pcapng` and every `.tsv` are **gitignored and never committed**. Only
  `references/tshark.md` enters git.
- This skill never reads or writes C#, never touches IDA, and never invents byte meanings —
  Mode A transports observed wire bytes into `.tsv`; Mode B localizes field offsets and emits
  labeled hypotheses. The only committed artifact Mode B produces is the hypothesis you transcribe
  into `Docs/RE/packets/*.yaml` (+ a journal line).
- The tshark command lives in **one** place: `references/tshark.md`. If you must change
  fields or the split, edit that doc and `scripts/extract_streams.py` together so they never
  drift.
