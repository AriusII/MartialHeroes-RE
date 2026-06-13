---
name: re-session-log
description: Use to append a provenance entry to Docs/RE/journal.md after an IDA reverse-engineering session. Records date, analyst, binary sha256 prefix, the functions/opcodes/structs touched (by canonical name only), and the committed spec files produced — the append-only audit trail backing the EU Art.6 interoperability claim. Append-only; never rewrites history; never records pseudo-code.
allowed-tools: Read Write
model: sonnet
effort: medium
---

# re-session-log

Append one structured entry to `Docs/RE/journal.md` documenting an RE session. This journal is not
bookkeeping for its own sake — it is the **legal audit trail** for the project. The EU Software
Directive 2009/24/EC, Art. 6 exception only holds while decompilation is "performed exclusively to
achieve interoperability." The journal is the contemporaneous record proving each session mapped
protocol/asset structure for interoperability and produced neutral specs — not a copy of the code.

It is **append-only**. Never edit, reorder, or delete existing entries; never reformat the file.
You add exactly one new entry at the bottom.

## Entry schema

Every entry MUST carry:

- **Date** — ISO `YYYY-MM-DD` of the session.
- **Analyst** — who ran the session (name/handle).
- **Binary** — `Main.exe @ <sha256 prefix>` (first ~8 hex of the pinned build's SHA-256, matching
  `binary.sha256` in `names.yaml`). If the hash is unknown, write `@ (unhashed)` and flag it.
- **Tool** — e.g. `IDA Pro 9.3 via MCP (mcp__ida__*)`.
- **Analyzed** — functions / opcodes / structs touched, **by canonical name only** (e.g.
  `RecvPacketDispatch`, `SmsgMovePlayer (0x42)`, `DecryptInPlace`). Never raw IDA addresses,
  never `sub_…` autonames — translate via `names.yaml`.
- **Specs produced/updated** — committed paths under `Docs/RE/` (e.g. `packets/move.yaml`,
  `opcodes.md`, `specs/crypto.md`). If a session produced no committed spec, say so explicitly.
- **Notes** — plain-language summary of behavior learned. **No pseudo-code, no decompiler output,
  no addresses.**

## Steps

1. **Read `Docs/RE/journal.md`.** If it does not exist, run `re-workspace-init` first — do not
   create the file from scratch here. Confirm it ends with the `<!-- entries below -->` marker and
   note the format of any prior entries so the new one matches.

2. **Gather the facts** for the schema above from the session. Cross-check every name against
   `Docs/RE/names.yaml`: if you are about to write an address or a `sub_`/`loc_`/`dword_` autoname,
   STOP — resolve it to its canonical name first (and remind the analyst to record the mapping in
   `names.yaml`, which is itself a committed spec change).

3. **Scrub for taint.** Reject any candidate note containing: a hex address (`0x004…`), a Hex-Rays
   pseudo-type (`_DWORD`, `__thiscall`, …), a decompiler autoname, or anything resembling a code
   snippet / control-flow transcription. The journal describes *what the code does*, never *how the
   decompiler rendered it*. If the only way to make a point is to paste code, the point belongs in
   `_dirty/` (gitignored), not here.

4. **Append the entry** to the end of the file, after the last existing entry, using this template:

   ```
   ## YYYY-MM-DD — <analyst>
   - binary: Main.exe @ <sha256-prefix>
   - tool: IDA Pro 9.3 via MCP (mcp__ida__*)
   - analyzed: <canonical names — functions / opcodes / structs>
   - specs produced/updated: <committed paths under Docs/RE/, or "none">
   - notes: <plain-language summary; no pseudo-code, no addresses>
   ```

   Preserve every byte already in the file. Do not touch the header or earlier entries.

5. **Cross-check the firewall pairing.** Every committed spec change should be paired with a journal
   mention of the spec path — that pairing is exactly what `clean-room-firewall-check` enforces in
   CI. If this session changed a spec but you cannot name it here, fix that before committing.
   Report the appended entry back to the user and remind them the journal change is itself committed.

## Decision points

- **If `journal.md` is absent**, run `re-workspace-init` first — never create the journal from
  scratch here (its header is canonical).
- **If a fact was debugger-confirmed vs static-only**, you may note "confirmed via live session"
  in plain language, but still no addresses — the journal records *what was learned*, not *how*.
- **If the binary hash is unknown**, write `@ (unhashed)` and flag it — do not invent digits.
- **If the session produced no committed spec**, say so explicitly ("specs: none") rather than
  omitting the field.

## Verify / Done when

- [ ] Exactly one new entry appended at the bottom; every prior byte preserved.
- [ ] All names are canonical (resolve in `names.yaml`); no address, no `sub_`/`dword_` autoname.
- [ ] No pseudo-code / decompiler output anywhere in the note.
- [ ] Every committed spec the session touched is named in `specs produced/updated`.

## Pitfalls (anti-patterns)

- **Never** edit, reorder, or reformat existing entries — append-only, always.
- **Never** record a raw address or autoname; resolve to the canonical name first.
- **Never** paste a code snippet or control-flow transcription into a note.

> North star: serves **N1** — the journal is the contemporaneous Art. 6 audit trail proving each
> clean-room session mapped structure for interoperability and produced neutral specs.

## Hard rules

- Append-only. Never modify or delete prior entries; never rewrite the file.
- Canonical names only — no addresses, no `sub_`/`dword_` autonames, ever.
- Zero pseudo-code / decompiler output in the journal. That content lives only under `_dirty/`.
