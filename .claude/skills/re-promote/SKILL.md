---
name: re-promote
description: Use to cross the clean-room firewall — take a raw finding sitting in Docs/RE/_dirty/ and REWRITE it (never copy) into neutral prose inside the right committed spec (formats/ structs/ specs/ opcodes.md / packets/), strip every Hex-Rays artifact, add the // spec citation note, and journal it. The deliberate dirty→clean promotion step the whole legal model depends on. Also includes a WORKSPACE-INIT mode that idempotently materializes the Docs/RE clean-room knowledge base (committed spec tree + the gitignored _dirty/ quarantine) when it is missing — never touching files that already exist.
allowed-tools: Read Write Edit Grep Glob Bash(mkdir *)
model: opus
effort: high
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

This skill has **two modes**: **Mode A — PROMOTE** (the dirty→clean rewrite, the primary act) and
**Mode B — WORKSPACE-INIT** (materialize the empty `Docs/RE/` home when it is missing). Mode B never
authors a finding; it only builds the lawful home Mode A fills.

**Ground-truth doctrine.** IDA / `doida.exe` is the single absolute truth; the committed spec you
write here is the **derived truth** and the *only* thing engineers downstream read. So the spec must
faithfully reflect what the binary proved — no embellishment, no guesses dressed as facts. If the
dirty finding ever conflicts with a fact already in the committed spec, **the binary wins**: correct
the spec to match the binary and journal the correction (never bend the binary's truth to fit a stale
spec). A promotion that diverges from the binary poisons every engineer who trusts the spec.

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
| An opcode id ↔ name ↔ direction ↔ size | `Docs/RE/opcodes.md` (no addresses) — prefer the `packet-codegen` opcode-catalog mode for curation |
| A packet's wire field layout | `Docs/RE/packets/<name>.yaml` — prefer the `pcap-extract` field-diff / `packet-codegen` skills |

## Mode A — PROMOTE (steps)

1. **Locate the dirty finding.** Identify the exact file under `Docs/RE/_dirty/` to promote (e.g.
   `_dirty/formats/<x>.md`, `_dirty/structs/<y>.md`, `_dirty/crypto/<z>.md`, a `_dirty/queries/*`).
   Read it. This is the only sanctioned read of `_dirty/` — you are the spec-author, not an engineer.

2. **Triage the target.** Pick the committed destination from the table above. If a spec for that
   subject already exists, you will *extend/refine* it; if not, you will create it. Confirm the
   committed spec tree exists (run Mode B below if `Docs/RE/formats|structs|specs` is missing).

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
   exactly what `clean-room-check`'s firewall-gate mode enforces in CI). Use the `preservation`
   session-log mode to append one entry naming the spec path you produced — by canonical name only, no
   addresses, no pseudo-code.

8. **Report**: the dirty source you promoted, the committed spec path you wrote, the confidence of
   the key facts, and a reminder that the journal entry is itself a committed change.

## Mode B — WORKSPACE-INIT (materialize the Docs/RE tree)

When `Docs/RE/` is missing its canonical structure (fresh clone, deleted folder, specs have no home),
this mode idempotently builds the clean-room knowledge base — the committed spec tree **plus** the
gitignored `_dirty/` quarantine. **It is purely additive:** it creates missing directories and seeds
missing canonical files, and MUST NEVER overwrite, truncate, or reformat a file that already exists
(those files may hold real RE findings). In this repo the tree usually already exists; the correct
outcome is then "everything present, nothing changed."

### Canonical layout
```
Docs/RE/
  README.md   names.yaml   journal.md   opcodes.md      committed — doctrine / glossary / audit trail / opcode catalog
  packets/  formats/  structs/  specs/  audits/         committed — the spec trees
  _dirty/  (GITIGNORED quarantine)  recon/ functions/ structs/ crypto/ queries/
```

### Steps (Mode B)

1. **Locate the repo root** (confirm `PRESERVATION_AND_ARCHITECTURE.md` + `.gitignore` sit there); if
   you cannot find them, ask the user — do not guess.
2. **Verify the firewall is gitignored before creating it.** Read `.gitignore` and confirm a
   `Docs/RE/_dirty/` rule. If missing, **STOP** and warn the user (creating the quarantine without that
   ignore risks committing tainted material) — do not add the rule silently.
3. **Create directories idempotently** with `mkdir -p` (no-op when present): committed `Docs/RE`,
   `packets`, `formats`, `structs`, `specs`, `audits`; quarantine `_dirty` + `recon`/`functions`/
   `structs`/`crypto`/`queries`.
4. **Seed missing canonical files only.** Read each first; if it exists (even empty-with-content), SKIP
   and report "kept"; only Write when absent — never diff or "improve" an existing file. Seed:
   `README.md` (firewall doctrine — the dirty/clean split, `_dirty/` gitignored, promotion is a
   deliberate *rewrite*, the five hard rules), `names.yaml` (glossary skeleton: empty `binary:` with
   blank sha256 and `functions: {}`/`globals: {}`/`crypto: {}`/`opcodes: {}`, address keys as quoted
   strings), `journal.md` (append-only Art. 6 audit-trail header + entry template + `<!-- entries below
   -->` marker), `opcodes.md` (catalog header — NO addresses, client-POV `C2S`/`S2C`, the 7-column
   table + one italic placeholder row + the status legend), `_dirty/README.md` (quarantine notice), and
   a `.gitkeep` in each empty committed subfolder (`packets`/`formats`/`structs`/`specs`/`audits`) —
   **never** a `.gitkeep` under `_dirty/`.
5. **Report a gap summary**: one line per path (`created` / `kept` / `skipped`), a created-vs-kept
   count, and the reminder that any new committed spec needs a `journal.md` entry (`preservation`
   session-log mode) and will be audited by `clean-room-check`.

**Mode B pitfalls:** never overwrite/truncate/reformat an existing file; never write under `_dirty/`
except its `README.md`; never create the quarantine when its `.gitignore` rule is absent.

## Decision points

- **If a fact still rests only on a static hypothesis**, tag it `UNVERIFIED` — do not promote a
  guessed offset/algorithm as `CONFIRMED`. The debugger-confirmed half of N1 is what earns
  `CONFIRMED`; static-only stays load-bearing-but-flagged.
- **If the dirty note still carries an autoname or address**, resolve it to a canonical
  `names.yaml` name *before* writing — never let `sub_`/`0x004…` leak into the draft.
- **If the finding is an opcode row or a packet wire layout**, prefer the dedicated `packet-codegen`
  (opcode-catalog + struct codegen modes) / `pcap-extract` (field-diff) skills (schema-validated) over
  hand-writing here, and validate that a packet's `size:` equals the sum of its field widths.
- **If the draft can only make its point by pasting code**, the point stays in `_dirty/` — it
  does not belong in a committed spec.
- **If no committed spec tree exists yet**, run Mode B (workspace-init) first.

## Verify / Done when

- [ ] The committed draft contains **zero** Hex-Rays artifacts: no `sub_`/`loc_`/`dword_`/`off_`,
      no `_DWORD`/`__thiscall`/`LODWORD`, no mangled `?x@@…`, no `0x004…` address.
- [ ] Every name is canonical (resolves in `names.yaml`); offsets are a tidy
      `offset | size | type | field | notes` table; each fact carries a confidence tag.
- [ ] The spec path is stable and obvious so downstream C# can cite `// spec: Docs/RE/<file>`.
- [ ] A paired `journal.md` entry names the spec path (the `clean-room-check` firewall-gate pairing).
- [ ] The `_dirty/` source is left intact, unmodified, as the provenance record.

## Pitfalls (anti-patterns)

- **Never** copy/transcribe — even "cleaned-up" pseudo-C is a firewall breach that voids Art. 6.
- **Never** bundle unrelated findings into one promotion; one finding per spec keeps the audit clean.
- **Never** edit, delete, or "tidy" the `_dirty/` source — it is the gitignored provenance.
- **Never** skip the journal pairing; an unjournaled spec change fails the firewall check.

> North star: this skill IS the **N1→N2 bridge** — it converts dirty IDA findings into the
> neutral committed specs that the faithful 1:1 re-implementation reads. Rewrite, never copy.

## Hard rules

- **Rewrite, never copy.** Promotion that transcribes is a firewall breach and voids the legal basis.
- The committed output must contain **zero** Hex-Rays artifacts, addresses, or pseudo-code. Self-scrub
  (step 4) before writing; when in doubt, leave it out.
- Write only under the committed `Docs/RE/{formats|structs|specs|opcodes.md|packets}` tree. Never
  modify, delete, or "clean up" the `_dirty/` source — it is the provenance record.
- Do not edit `names.yaml` or `journal.md` directly as a side effect here beyond the journal append
  (and prefer the dedicated `ida-annotate` names-sync mode / `preservation` session-log mode for those).
  Glossary and journal are orchestrator-owned discipline.
- One finding per promotion. Resist bundling unrelated facts into one spec — it muddies the audit
  trail.
