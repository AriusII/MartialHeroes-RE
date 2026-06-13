---
name: pcap-extract
description: Use to turn a .pcapng Martial Heroes capture into per-TCP-stream .tsv extracts (the protocol oracle for Network.Protocol / Network.Crypto work). Splits a capture by tcp.stream and emits one tab-separated row per segment (frame.time_epoch, tcp.stream, ip.src, tcp.srcport, tcp.len, data.data, plus a C2S/S2C direction tag). This skill is the single version-controlled home of the canonical tshark invocation.
allowed-tools: Bash(tshark *) Read Write
model: sonnet
effort: medium
---

# pcap-extract

Turn a raw Wireshark capture of a live (or replayed) *Martial Heroes* session into
regenerable, per-stream `.tsv` extracts that downstream clean-room skills (`packet-diff`,
`opcode-catalog`, `packet-codegen`) consume. The capture is an **oracle, not copyrighted
code** — observing bytes on the wire is clean-room-safe, so this skill has no `_dirty/`
involvement.

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
  `stream_<N>.tsv`. Steer downstream consumers (`packet-diff`, `opcode-catalog`) to that
  stream for combat/movement, the short one for the lobby/PIN handshake.
- **Direction can't be tagged?** If both `--server-port` and `--server-ip` are absent and the
  printed `(ip.src, tcp.srcport)` pairs are ambiguous, the server side is the endpoint that
  *replies first* after the client's SYN and carries the bulk of S2C broadcast traffic — name
  it and re-run rather than committing `?` rows.
- **Reassembly:** the 8-byte frame `[u32 size][u16 major][u16 minor]` can span TCP segments,
  so a single `data.data` row is *not* guaranteed to be one packet. Leave reframing to the
  consumer; this skill emits per-segment payload, never re-split frames.

Verify / Done when: `streams_all.tsv` + one `stream_<N>.tsv` per stream exist under the
gitignored out-dir; each row has all 7 columns; `dir` is `C2S`/`S2C` (not `?`) for the stream
you care about; pure-ACK rows (`tcp.len == 0`) are dropped; nothing was staged.

## Pitfalls (anti-patterns)

- **Never** stage the `.pcapng` or any `.tsv` — only `references/tshark.md` is committed.
- **Never** edit the tshark fields in `scripts/extract_streams.py` without also editing
  `references/tshark.md` — the two must never drift.
- **Never** interpret a byte's meaning here — this skill only transports observed bytes;
  semantics belong to `packet-diff`/`opcode-catalog`.
- Don't treat one `data.data` row as one packet — the wire frame can straddle segments.

North star: serves **N2** — the capture is the ground-truth oracle for byte-exact wire parity
with the original client.

## Hard rules

- The `.pcapng` and every `.tsv` are **gitignored and never committed**. Only
  `references/tshark.md` enters git.
- This skill never reads or writes C#, never touches IDA, and never invents byte meanings —
  it only transports observed wire bytes into `.tsv`. Interpretation belongs to `packet-diff`
  and `opcode-catalog`.
- The tshark command lives in **one** place: `references/tshark.md`. If you must change
  fields or the split, edit that doc and `scripts/extract_streams.py` together so they never
  drift.
