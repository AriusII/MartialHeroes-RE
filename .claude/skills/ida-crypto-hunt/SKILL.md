---
name: ida-crypto-hunt
description: Use to recover the packet cipher and key schedule of the legacy Martial Heroes client (Main.exe) so Network.Crypto can interoperate with the original wire format. Fuses crypto-shaped bit-op loop detection, xrefs walked out from the socket-recv/decrypt path, and constant-table (S-box/key-material) extraction into one crypto-focused report under Docs/RE/_dirty/crypto/. The deliverable that crosses the firewall is a NEUTRAL algorithm description in words and math — never transcribed code.
allowed-tools: 'Read Write'
model: sonnet
effort: high
---

# ida-crypto-hunt — recover the packet cipher

The Martial Heroes protocol encrypts packet bodies. `Network.Crypto` must reproduce that
transform in-place on `Span<byte>` to talk to the original wire format (the legal core of
the interoperability exception). This skill finds the cipher in the legacy client and
captures enough to describe it precisely.

It combines three signals into one report:
1. **Recv-path anchoring** — walk out from socket reads (`recv`/`WSARecv`) and decrypt-ish
   names to the function that first transforms the received bytes.
2. **Bit-op loop detection** — the cipher's inner loop (XOR / rotate / shift / add per byte
   or per block).
3. **Constant-table extraction** — S-boxes, round constants, or an embedded key.

## What crosses the firewall (read this first)

The committed deliverable for `Network.Crypto` is a **neutral algorithm description**: prose
+ math describing the transform (e.g. "keystream byte k_i = S[(S[a]+S[b]) mod 256]; cipher
byte = plain XOR k_i; key schedule seeds S from a 16-byte session key delivered in opcode
0x02"). It goes into `Docs/RE/specs/crypto.md` by the **spec-author**, never from this skill.

- This skill writes ONLY to `Docs/RE/_dirty/crypto/` (gitignored).
- NEVER transcribe decompiled C/C++ into a committed file or into C#.
- Interoperability-necessary constants (an S-box, a fixed key, magic IVs) may be promoted
  ONLY through a reviewed spec, with a `journal.md` provenance entry — and even then as data
  tables justified by interoperability, not copied code.

## Preconditions

- IDA Pro 9.3 open on the legacy client, auto-analysis finished.
- IDA MCP server at `http://127.0.0.1:13337/mcp`, registered as `ida` (`mcp__ida__*`).
  If absent: `claude mcp add --transport http ida http://127.0.0.1:13337/mcp`.

## Steps

1. **Check connectivity.** List `mcp__ida__*` tools; if none resolve, report the
   `claude mcp add` hint and stop.

2. **Run the bundled tracer.** Read `${CLAUDE_SKILL_DIR}/scripts/crypto_trace.py`, then paste
   its full source into the IDA script-exec tool (name varies:
   `mcp__ida__execute_script` / `mcp__ida__run_python` / `mcp__ida__eval`). It:
   - locates recv/decrypt anchor functions by import + string heuristics;
   - from those anchors, walks callees a few levels and flags functions containing
     cipher-shaped loops (XOR/ROL/ROR/shift, per-byte stride);
   - extracts 256-byte / 256-dword constant tables near those functions (S-box / round
     constants / CRC) and flags 0..255 permutations;
   - emits a ranked Markdown report (anchor -> candidate cipher func -> loop fingerprint ->
     associated constant tables).

3. **Capture output** to `Docs/RE/_dirty/crypto/crypto_trace.md` (the script writes it if it
   can; otherwise copy the printed Markdown and Write it there yourself).

4. **Confirm the cipher function.** The top candidate is the function that is (a) reachable
   from the recv path, (b) has the tightest bit-op loop, and (c) references a constant table.
   If two functions look symmetric, you've likely found encrypt + decrypt — note both.

5. **Characterize, in neutral terms.** From behavior (not pasted code), write down for the
   dirty notes: operation per byte/block (XOR? add-mod-256? rotate-by-key?), block vs.
   stream, state size, how the key is derived (key schedule), where the key enters (which
   packet/opcode or handshake), endianness, and whether encrypt and decrypt are the same
   routine. If you need callers of the key-setter or what touches the key global, use the
   **ida-script-runner** snippets (`callers_of.py`, `touches_global.py`).

6. **Optional: behavioral verification.** If a Wireshark capture's first encrypted bytes are
   known, you can sanity-check the recovered algorithm against them out-of-band — but never
   put capture bytes or addresses in a committed file.

7. **Hand off to the spec-author.** State: "Crypto recon in Docs/RE/_dirty/crypto/; the
   neutral algorithm description for Network.Crypto must be authored into
   Docs/RE/specs/crypto.md by the spec-author, with a journal.md entry. Constants promote
   only via that reviewed spec." Do not author the clean spec from here.

## Decision points

- **If two symmetric routines appear**, you've likely found encrypt + decrypt — note both and which
  side the recv path uses. Expect a rolling **XOR/ROL** shape operating in-place per byte/block.
- **If the static algorithm is uncertain** (key schedule, where the session key enters), the decisive
  evidence is dynamic: hand off to `ida-debugger-drive` — in the maintainer's live F9 session,
  breakpoint just before and after the cipher routine and `dbg_read` the buffer (it reads through
  `PAGE_NOACCESS`) to capture the same bytes plain and encrypted. That confirms the transform and the
  key without ever transcribing code. Static forms the hypothesis; the debugger confirms it.
- **If a 256-entry table is a 0..255 permutation**, flag it as a likely S-box; it promotes only via a
  reviewed spec justified by interoperability necessity.

## Verify / Done when

- The cipher function is identified by all three signals (recv-reachable, tight bit-op loop, constant table).
- The dirty notes characterize: per-byte/block op, stream vs block, state size, key schedule, key entry point, endianness, encrypt==decrypt?.
- Everything stays in `_dirty/crypto/`; the deliverable that crosses is a neutral words+math description.

## Pitfalls (never)

- Never transcribe Hex-Rays pseudo-code anywhere — describe the transform in words and math only.
- Never put capture bytes, keys, or addresses in a committed file.
- Never promote an S-box/key as copied code — only as interoperability-justified data via a reviewed spec.

*North star N1: recovers the wire cipher Network.Crypto must reproduce byte-exact — the static shape a live debugger read confirms against ground truth.*

## Hard rules

- Output goes ONLY to `Docs/RE/_dirty/crypto/`. Never touch `Docs/RE/specs/`,
  `Docs/RE/opcodes.md`, `journal.md`, or C# from this skill.
- Never paste Hex-Rays pseudo-code anywhere. Describe the transform in words and math.
- Constants/S-boxes/keys cross the firewall only through a reviewed spec justified by
  interoperability necessity, never as a verbatim code copy.
