---
name: opcode-catalog
description: Use to maintain the authoritative Docs/RE/opcodes.md opcode catalog (no IDA addresses). Add/curate entries (opcode + name + direction + size + packet-yaml link + status), schema-validate the table, detect duplicate opcode ids, and flag opcodes that appear in captures but are missing from the catalog. Clean-side curation only — never store addresses or decompiler output.
allowed-tools: Read Write Bash(python *)
model: sonnet
effort: high
---

# opcode-catalog

Curate `Docs/RE/opcodes.md` — the clean-room source of truth for the *Martial Heroes* wire
protocol's message opcodes. This catalog carries **only promoted, neutral facts**: address-
level discovery stays in `_dirty/opcodes.raw.md` (gitignored) and never crosses into this
file. The catalog is consumed by the `network-protocol-engineer` and the `packet-codegen`
skill.

## The catalog schema (must not drift)

`Docs/RE/opcodes.md` holds one Markdown table. Every data row has exactly these columns:

| Column | Rule |
|---|---|
| `Opcode` | Hex, lowercase `0x` form, e.g. `0x42`. **No IDA addresses, ever.** Unique. |
| `Name` | Canonical message name. Convention: `Cmsg*`/`Smsg*` (client→/server→) e.g. `SmsgMovePlayer`. Must agree with `Docs/RE/names.yaml` `opcodes:`. |
| `Direction` | `C2S` or `S2C`, from the **client's** point of view. |
| `Size (bytes)` | Fixed integer (e.g. `12`) or `var` for variable-length payloads. |
| `Packet spec` | Link to the field spec, e.g. `packets/move.yaml`, or `—` if none yet. |
| `Status` | `draft` · `observed` · `confirmed` · `implemented` (see legend in the file). |
| `Notes` | Short neutral prose. No pseudo-code, no addresses. |

Status legend (kept in the file): `draft` (hypothesized) · `observed` (seen in a capture) ·
`confirmed` (cross-checked against the binary's dispatch table) · `implemented` (C# struct +
handler exist).

## Workflow

1. **Read the current catalog.** Open `Docs/RE/opcodes.md`. Preserve its header prose, the
   column order, and the status legend exactly.

2. **Add or update an entry.** Insert a row keeping the table sorted by opcode ascending.
   - New opcode just seen in a capture but not yet specced: `Status = observed`, `Packet spec
     = —`, a Note like "S2C, ~28B, seen in stream_3 around movement".
   - Opcode with a written field spec: link `packets/<name>.yaml`, bump `Status` to `draft`
     or higher.
   - Opcode confirmed against the binary's dispatch table (by an analyst, in `_dirty/`):
     `Status = confirmed` — but **copy only the neutral fact** (it exists, its size); never
     the address.
   - Opcode whose C# struct + handler exist in `Network.Protocol`: `Status = implemented`.

3. **Validate before saving / committing:**

   ```powershell
   python ${CLAUDE_SKILL_DIR}/scripts/validate_opcodes.py Docs/RE/opcodes.md
   ```

   The validator:
   - parses the table and checks every row has all 7 columns;
   - normalizes and validates each opcode is a unique hex literal — **fails on duplicate ids**;
   - checks `Direction in {C2S,S2C}`, `Status in {draft,observed,confirmed,implemented}`,
     and that `Size` is an int or `var`;
   - **scans for address leakage** — any `sub_`, `loc_`, `0x004…`-style absolute address, or
     `.text:` token in the file is a hard error (this file must never carry addresses);
   - confirms each non-`—` `Packet spec` link points to a path under `packets/`.

4. **Cross-check against captures (optional but recommended).** If you have an opcode
   inventory derived from `pcap-extract`/`packet-diff`, pass it so the validator flags opcodes
   **seen in captures but missing from the catalog**:

   ```powershell
   python ${CLAUDE_SKILL_DIR}/scripts/validate_opcodes.py Docs/RE/opcodes.md --seen 0x01,0x02,0x42
   ```

   or read the ids from a file (one hex opcode per line):

   ```powershell
   python ${CLAUDE_SKILL_DIR}/scripts/validate_opcodes.py Docs/RE/opcodes.md --seen-file _dirty/captures/seen_opcodes.txt
   ```

   Missing-from-catalog opcodes are reported as warnings to add as `observed` rows.

5. **Keep names.yaml in sync.** When you add/rename an opcode, mirror it in
   `Docs/RE/names.yaml` under `opcodes:` (same id, name, direction). The catalog is the
   human-facing table; `names.yaml` is the machine glossary the `ida-naming-sync` skill uses.

6. **Journal it.** Any committed change to `opcodes.md` gets a one-line entry in
   `Docs/RE/journal.md` (which specs changed, by whom, no pseudo-code).

## Decision points

- **major/minor vs flat id?** This protocol's opcodes are `(major<<16)|minor` — a message is a
  `(major, minor)` pair (e.g. melee `2/52`, chat `2/7`, storage `2/142`, buff `4/102`). Record
  the catalog id in whatever single-hex convention the file already uses, but keep the pair
  legible in `Name`/`Notes` so it round-trips to the wire frame `[u16 major][u16 minor]`.
- **What status?** Seen on the wire only → `observed`. A `packets/*.yaml` exists → `draft`+.
  An analyst confirmed it against the binary's dispatch table (neutral fact only) → `confirmed`.
  A C# struct + handler live in `Network.Protocol` (incl. the `OnUnhandled` fallback set
  0/0, 3/1, 3/7, 3/4, 3/6, 3/23) → `implemented`.
- **Validator fails on an address token?** That means dirty material leaked in — strip it; the
  catalog never carries `sub_`/`loc_`/absolute addresses. Promote only *that it exists* and its
  size, never *where* it lives.

Verify / Done when: `validate_opcodes.py` passes (all 7 columns, unique ids, valid
direction/status/size, **zero address tokens**, every `Packet spec` link resolves under
`packets/`); any capture-`--seen` warnings are triaged; `names.yaml` `opcodes:` mirrors the row;
the change is journaled.

## Pitfalls (anti-patterns)

- **Never** store an address, `sub_`/`loc_`, or any decompiler output — the validator fails the
  file and the firewall is breached.
- **Never** read `_dirty/` or call IDA — the catalog carries only already-promoted neutral facts.
- **Never** duplicate an opcode id — duplicates are a hard validation failure.
- Don't reformat the file's prose, legend, or column order — edit table rows only.

North star: serves **N2** — a complete, validated opcode table is the index of every message the
re-implemented client must speak byte-for-byte like the original.

## Hard rules

- **No addresses. Ever.** This file documents *what* a message is, never *where* it lives in
  the binary. Address discovery stays in gitignored `_dirty/opcodes.raw.md`. The validator
  enforces this and will fail the file if it finds an address-shaped token.
- **No decompiler output** anywhere in the catalog — neutral prose only.
- **Clean-side only:** never read anything under `_dirty/` and never call IDA; the catalog carries only neutral facts already promoted across the firewall.
- One opcode id appears at most once. Duplicate ids are a hard validation failure.
- Do not reformat the file's prose, legend, or column order — only edit table rows.
