# Canonical tshark invocation (version-controlled)

This file is the **single source of truth** for how *Martial Heroes* `.pcapng` captures are
turned into `.tsv`. `.gitignore` and `CLAUDE.md` point here; `scripts/extract_streams.py`
implements exactly this command. If you change one, change both in the same commit.

The capture is a protocol **oracle**, not copyrighted code — observing wire bytes is
clean-room-safe. The `.pcapng` input and every `.tsv` output are gitignored; **only this doc
is committed.**

## The command

Extract every TCP segment that carries payload, one row per segment, tab-separated, no header
(the wrapper adds the header and the derived `dir` column):

```bash
tshark -r "<capture>.pcapng" \
  -Y "tcp.len > 0" \
  -T fields \
  -E separator=/t \
  -E occurrence=f \
  -e frame.time_epoch \
  -e tcp.stream \
  -e ip.src \
  -e tcp.srcport \
  -e tcp.len \
  -e data.data
```

### Flag rationale

| Flag | Why |
|---|---|
| `-r <file>` | Read from the capture file (offline; never live-capture in this project). |
| `-Y "tcp.len > 0"` | Display filter: keep only segments with payload. Drops pure ACKs/handshake. |
| `-T fields` | Emit selected fields instead of the packet summary. |
| `-E separator=/t` | Tab-separate columns. `/t` is tshark's literal escape for a tab. |
| `-E occurrence=f` | First occurrence only — guards against a field appearing twice per packet. |
| `-e frame.time_epoch` | Absolute timestamp; orders segments within a stream. |
| `-e tcp.stream` | Per-connection index tshark assigns; the split key. |
| `-e ip.src` | Source IP — discriminator for the derived `C2S`/`S2C` tag. |
| `-e tcp.srcport` | Source port — the other direction discriminator. |
| `-e tcp.len` | Payload length in bytes. |
| `-e data.data` | Raw TCP payload as a lowercase, colon-free hex string. |

### Notes and pitfalls

- **`data.data` requires the payload to be undissected.** If Wireshark recognizes the port as
  a known protocol it dissects the payload and `data.data` is empty. The game uses a custom
  protocol on a non-standard port, so this is normally fine; if `data.data` comes back empty,
  the user must tell Wireshark to treat that port as Data (Decode As… → Transport → none) or
  pass `-d "tcp.port==<port>,data"` to force the Data dissector. Document the chosen port in
  `Docs/RE/specs/` once known.
- **TCP is a byte stream, not a message stream.** One row = one TCP segment, which may hold a
  partial game message or several concatenated ones. Re-framing into game messages (length-
  prefix / opcode boundaries) is the job of `Network.Transport.Pipelines` and the analyst, not
  this extraction.
- **Reassembly.** Default Wireshark TCP reassembly does not change `tcp.len`/`data.data` per
  segment here; we deliberately keep raw per-segment bytes so framing behavior stays
  observable. Do **not** add `-o tcp.desegment_tcp_streams:TRUE` to the canonical command.
- **Single stream.** To dump one connection, append `and tcp.stream == <N>` inside `-Y`:
  `-Y "tcp.len > 0 and tcp.stream == 3"`. The wrapper does this via `--stream <N>`.

## Direction (`dir`) derivation — added by the wrapper, not by tshark

tshark cannot emit `C2S`/`S2C` directly. The wrapper derives it from the server endpoint the
user identifies, matching `Docs/RE/opcodes.md`'s client-POV convention:

- Given `--server-port P`: row is `S2C` iff `tcp.srcport == P`, else `C2S`.
- Given `--server-ip A`: row is `S2C` iff `ip.src == A`, else `C2S`.
- Given neither: `dir = ?` and the wrapper prints the distinct `(ip.src, tcp.srcport)` pairs
  so the user can spot the server (the endpoint every stream connects *to*) and re-run.

## Reproducibility contract

Two people running this exact command on the same `.pcapng` MUST get byte-identical `.tsv`
rows (modulo the wrapper's header + `dir` column). If a future tshark version changes field
formatting, pin the version in `Docs/RE/journal.md` and update this doc.
