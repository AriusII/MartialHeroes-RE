---
name: re-workspace-init
description: Use when Docs/RE is missing its canonical structure (e.g. fresh clone, deleted folder, or a teammate reports specs have no home). Idempotently materializes the clean-room knowledge base — committed spec tree plus the gitignored _dirty/ quarantine — without ever touching files that already exist.
allowed-tools: Read Write Bash(mkdir *)
model: sonnet
effort: medium
---

# re-workspace-init

Materialize the `Docs/RE/` clean-room knowledge base for the Martial Heroes preservation
project. The tree is the legal backbone of the project (EU Software Directive 2009/24/EC, Art. 6):
a committed half holding only neutral, human-authored specs, and a gitignored `_dirty/` half
quarantining raw decompiler output.

The committed half is the project's **derived truth** — the rewritten record of what IDA proved about
`doida.exe`, and the *only* thing implementation reads; the `_dirty/` half holds what the binary said
before it was rewritten across the firewall. This skill only builds the empty home for both; it never
authors a finding. Promotion from one to the other is `re-promote`'s deliberate, journaled act.

**This skill is purely additive.** It creates missing directories and seeds missing canonical
files. It MUST NEVER overwrite, truncate, or reformat a file that already exists — those files may
contain real RE findings. In this repo the tree usually already exists; the correct outcome is
then "everything present, nothing changed."

## Canonical layout

```
Docs/RE/
  README.md            committed — firewall doctrine
  names.yaml           committed — glossary (original symbol/address -> canonical name)
  journal.md           committed — append-only provenance trail
  opcodes.md           committed — opcode catalog (NO addresses)
  packets/             committed — packets/*.yaml field specs
  formats/             committed — formats/*.md asset-format specs
  structs/             committed — structs/*.md neutral layout sketches
  specs/               committed — specs/*.md misc promoted specs (e.g. crypto.md)
  audits/              committed — audits/*.md auditor verdicts
  _dirty/              GITIGNORED — quarantine for raw IDA output
    recon/
    functions/
    structs/
    crypto/
    queries/
```

## Steps

1. **Locate the repo root.** All paths below are relative to it. Confirm `PRESERVATION_AND_ARCHITECTURE.md`
   and `.gitignore` sit there. Do not run if you cannot find them — ask the user for the root.

2. **Verify the firewall is gitignored before creating it.** Read `.gitignore` and confirm it
   contains a `Docs/RE/_dirty/` rule. If it is missing, STOP and warn the user: creating the
   quarantine without that ignore rule risks committing tainted material. Do not add the rule
   silently — flag it and let the user confirm.

3. **Create directories idempotently.** Use `mkdir -p` (no-op when the directory exists) for every
   folder in the layout above:
   - committed: `Docs/RE`, `Docs/RE/packets`, `Docs/RE/formats`, `Docs/RE/structs`,
     `Docs/RE/specs`, `Docs/RE/audits`
   - quarantine: `Docs/RE/_dirty`, `Docs/RE/_dirty/recon`, `Docs/RE/_dirty/functions`,
     `Docs/RE/_dirty/structs`, `Docs/RE/_dirty/crypto`, `Docs/RE/_dirty/queries`

4. **Seed missing canonical files only.** For each file below, first Read it. If the Read succeeds
   (file exists, even if empty-with-content), SKIP it and report "kept". Only Write when the Read
   reports the file does not exist. Never diff or "improve" an existing file here.

   Seed content (use verbatim when the file is absent):

   - `Docs/RE/README.md` — firewall doctrine. State: the dirty/clean split, that `_dirty/` is
     gitignored, that promotion is a deliberate *rewrite* (analyst -> spec-author -> engineer),
     and the five hard rules (no pseudo-code in committed files; analysts write only under
     `_dirty/`; every spec change needs a `journal.md` entry; magic offsets in C# cite a spec;
     user originals are never committed).
   - `Docs/RE/names.yaml` — glossary skeleton with empty `binary:` (name `Main.exe`, blank
     sha256), and empty `functions: {}`, `globals: {}`, `crypto: {}`, `opcodes: {}` maps with a
     commented example per map. Address keys are quoted strings so YAML never coerces them.
   - `Docs/RE/journal.md` — header explaining it is the append-only Art. 6 audit trail, the entry
     template (date, analyst, binary sha256 prefix, analyzed-by-canonical-name, specs produced,
     plain-language notes), and an `<!-- entries below -->` marker. No pseudo-code, ever.
   - `Docs/RE/opcodes.md` — opcode catalog header: NO addresses, direction is from the client's
     POV (`C2S`/`S2C`), each opcode links to a `packets/` spec; a Markdown table with the columns
     `Opcode | Name | Direction | Size (bytes) | Packet spec | Status | Notes` and one italic
     placeholder row; a status legend (`draft`/`observed`/`confirmed`/`implemented`).
   - `Docs/RE/_dirty/README.md` — quarantine notice: gitignored, only RE-analyst agents write here,
     engineers forbidden to read it, nothing here is ever committed; list the suggested subfolders.
   - `.gitkeep` in each committed subfolder (`packets`, `formats`, `structs`, `specs`, `audits`)
     so empty directories survive a clean checkout. Do NOT place `.gitkeep` under `_dirty/`.

5. **Report a gap summary.** Print one line per path: `created` / `kept` / `skipped (exists)`.
   End with a count of created vs. kept, and remind the user that any new committed spec must be
   accompanied by a `journal.md` entry (`re-session-log` skill) and will be audited by
   `clean-room-firewall-check`.

## Decision points

- **If `.gitignore` lacks the `Docs/RE/_dirty/` rule**, STOP and surface the gap — do not create
  the quarantine, and do not add the ignore rule silently; let the user confirm.
- **If a canonical file already exists** (even empty-with-content), SKIP it and report "kept" —
  an existing file is a signal that real RE work lives there.
- **If you cannot locate the repo root** (no `PRESERVATION_AND_ARCHITECTURE.md` / `.gitignore`),
  do not guess — ask the user for the root.

## Verify / Done when

- [ ] Every committed folder + `_dirty/` subfolder in the canonical layout exists.
- [ ] Missing canonical files were seeded; existing ones were untouched (reported "kept").
- [ ] `.gitkeep` sits in each empty committed subfolder; none under `_dirty/`.
- [ ] The gap summary lists `created`/`kept`/`skipped` per path with a created-vs-kept count.

## Pitfalls (anti-patterns)

- **Never** overwrite, truncate, or reformat a file that already exists.
- **Never** write anything under `_dirty/` except its `README.md`.
- **Never** create the quarantine when its `.gitignore` rule is absent.

> North star: serves **N1** — it materializes the dirty/clean firewall tree that gives every
> later clean-room session a lawful home for findings and neutral specs.

## Hard rules

- Additive only. A file that exists is never overwritten — treat an unexpected existing file as a
  signal that real work lives there.
- Never create or write anything under `_dirty/` except its `README.md` (and never commit it).
- If `.gitignore` lacks the `Docs/RE/_dirty/` rule, do not create the quarantine — surface the gap.
