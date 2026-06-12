---
name: re-promote
description: Use to cross the clean-room firewall — take a raw finding sitting in Docs/RE/_dirty/ and REWRITE it (never copy) into neutral prose inside the right committed spec (formats/ structs/ specs/ opcodes.md / packets/), strip every Hex-Rays artifact, add the // spec citation note, and journal it. The deliberate dirty→clean promotion step the whole legal model depends on.
allowed-tools: Read Write Edit Grep Glob
model: opus
---

# re-promote — the dirty→clean firewall bridge

Promotion is the single most legally load-bearing act in this project. The EU Software Directive
2009/24/EC Art. 6 exception holds only because we **rewrite** what was learned from the decompiler
into neutral, human-authored descriptions — we never transcribe the binary. This skill performs one
such promotion, carefully and in the spec-author role.

```
Docs/RE/_dirty/<finding>   ──►   [you rewrite in neutral prose]   ──►   Docs/RE/{formats|structs|specs|opcodes.md|packets}/<spec>
   (tainted, gitignored)          NO sub_/_DWORD/__thiscall/pseudo-C        (committed, neutral, citable)
```

You are acting as a **spec-author**: you are *allowed* to read `_dirty/` here (this is the one skill
that does), but the output that crosses the firewall must contain **zero** decompiler-derived
material. If you find yourself tempted to paste, stop — describe the behavior instead.

## What may and may not cross

| Allowed across (neutral) | NEVER crosses (tainted) |
|---|---|
| Field offset/size/type tables in your own words | Hex-Rays pseudo-C, even "cleaned up" |
| Byte-layout diagrams, math, algorithm prose | `sub_4A1230`, `loc_…`, `dword_…`, `off_…` autonames |
| Opcode → name → direction → size catalog rows | `_DWORD`, `_BYTE`, `__thiscall`, `__fastcall`, `LODWORD`, mangled `?x@@…` |
| Canonical names from `names.yaml` | raw IDA addresses (`0x004A1230`) |
| "This loop XORs each byte with a rolling key" | a transcribed loop body / control flow |

## Target spec, by finding kind

| Finding is about… | Promote into |
|---|---|
| An asset/file binary format (mesh, terrain, anim, texture, `.pak`, a CP949 text table) | `Docs/RE/formats/<ext>.md` |
| A struct / object / vtable field layout | `Docs/RE/structs/<name>.md` (offset/size/type table) |
| A crypto routine, framing rule, or other algorithm | `Docs/RE/specs/<name>.md` |
| An opcode id ↔ name ↔ direction ↔ size | `Docs/RE/opcodes.md` (no addresses) — prefer the `opcode-catalog` skill for curation |
| A packet's wire field layout | `Docs/RE/packets/<name>.yaml` — prefer the `packet-diff` / `packet-codegen` skills |

## Steps

1. **Locate the dirty finding.** Identify the exact file under `Docs/RE/_dirty/` to promote (e.g.
   `_dirty/formats/<x>.md`, `_dirty/structs/<y>.md`, `_dirty/crypto/<z>.md`, a `_dirty/queries/*`).
   Read it. This is the only sanctioned read of `_dirty/` — you are the spec-author, not an engineer.

2. **Triage the target.** Pick the committed destination from the table above. If a spec for that
   subject already exists, you will *extend/refine* it; if not, you will create it. Confirm the
   committed spec tree exists (run `re-workspace-init` if `Docs/RE/formats|structs|specs` is missing).

3. **Rewrite — never copy.** Author the neutral description from scratch:
   - Describe *what the format/algorithm does and how its bytes are laid out*, not how the
     decompiler rendered the code.
   - Use canonical names from `Docs/RE/names.yaml`. If the dirty note still carries an autoname or
     an address, resolve it first (and remind the maintainer to record the mapping in `names.yaml`).
   - Mark each fact's confidence the way the existing specs do (e.g. `CONFIRMED` vs `UNVERIFIED`),
     so engineers know what is load-bearing.
   - Express offsets as a tidy table (`offset | size | type | field | notes`) — the form
     `packet-codegen` and the engineers consume.

4. **Self-scrub before writing.** Re-read your draft and reject any of: a hex address (`0x004…`), a
   Hex-Rays pseudo-type (`_DWORD`/`__thiscall`/…), an autoname (`sub_`/`loc_`/`dword_`/…), a mangled
   symbol (`?x@@…`), or anything that reads like transcribed control flow. If the only way to make a
   point is to paste code, the point stays in `_dirty/` — it does not belong in a committed spec.

5. **Add the citation breadcrumb.** State the spec's own path so downstream C# can cite it. Every
   magic constant the engineer later writes must carry `// spec: Docs/RE/<this file>` — make the
   spec path obvious and stable so that citation is easy and correct.

6. **Write the committed spec**, matching the house style of the neighbouring `Docs/RE/` files
   (banner/intro, layout table, confidence tags, prose explanation). Do **not** edit anything under
   `_dirty/` — the dirty note is left intact as the (gitignored) provenance source.

7. **Journal it.** A committed spec change MUST be paired with a `journal.md` entry (this pairing is
   exactly what `clean-room-firewall-check` enforces in CI). Use the `re-session-log` skill to append
   one entry naming the spec path you produced — by canonical name only, no addresses, no pseudo-code.

8. **Report**: the dirty source you promoted, the committed spec path you wrote, the confidence of
   the key facts, and a reminder that the journal entry is itself a committed change.

## Hard rules

- **Rewrite, never copy.** Promotion that transcribes is a firewall breach and voids the legal basis.
- The committed output must contain **zero** Hex-Rays artifacts, addresses, or pseudo-code. Self-scrub
  (step 4) before writing; when in doubt, leave it out.
- Write only under the committed `Docs/RE/{formats|structs|specs|opcodes.md|packets}` tree. Never
  modify, delete, or "clean up" the `_dirty/` source — it is the provenance record.
- Do not edit `names.yaml` or `journal.md` directly as a side effect here beyond the journal append
  (and prefer the dedicated skills `ida-naming-sync` / `re-session-log` for those). Glossary and
  journal are orchestrator-owned discipline.
- One finding per promotion. Resist bundling unrelated facts into one spec — it muddies the audit
  trail.
