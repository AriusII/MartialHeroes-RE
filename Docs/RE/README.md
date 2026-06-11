# Docs/RE — Reverse-Engineering Knowledge Base & Clean-Room Firewall

This directory is the **single source of truth** for everything learned by reverse-engineering
the legacy *Martial Heroes* client, and the mechanism that keeps the project legally clean.

## The clean-room firewall

The project's legal basis (EU Software Directive 2009/24/EC, Art. 6 — decompilation for
interoperability) depends on a strict separation between two activities:

- **Dirty room** — looking at IDA / Hex-Rays output. All raw, copyright-tainted material lives
  under [`_dirty/`](./_dirty/), which is **gitignored and never committed or shared**.
- **Clean room** — writing the new C# client. It may read **only** the neutral, human-authored
  specs in this directory (everything *except* `_dirty/`) and never sees decompiler output.

Promotion across the firewall is deliberate: an analyst documents behavior in `_dirty/` in plain
language, a **spec-author** rewrites it (never copies it) into the committed spec files below, and
only then may an **engineer** implement from that spec.

```
RAW DECOMPILER OUTPUT ──► _dirty/ (gitignored) ──► [spec-author rewrites] ──► specs/ packets/ formats/ opcodes.md ──► [engineer implements] ──► C#
        (tainted)                                        NEUTRAL PROSE / TABLES (committed)                            (fresh, clean-room)
```

## Layout

| Path | Status | Contents |
|---|---|---|
| `README.md` | committed | This file. |
| `names.yaml` | committed | Project glossary: original symbol/address → canonical name. A glossary, not pseudo-code. |
| `journal.md` | committed | Append-only provenance trail backing the Art. 6 "documented for interoperability" claim. |
| `opcodes.md` | committed | Authoritative opcode catalog — **no addresses**, direction + size + packet link only. |
| `packets/*.yaml` | committed | Packet field specs consumed by the `packet-codegen` skill. |
| `formats/*.md` | committed | Asset format specs (`.pak`, mesh, anim, texture). |
| `structs/*.md` | committed | Promoted, neutral struct/vtable layout sketches (offset/size/type tables). |
| `specs/*.md` | committed | Other promoted specs (e.g. `crypto.md`). |
| `audits/*.md` | committed | Clean-room auditor verdicts. |
| `_dirty/` | **gitignored** | Quarantine for raw IDA output. Never committed, never read by clean-room agents. |

## Rules

1. **Never** paste Hex-Rays / decompiler pseudo-code into any committed file or into C#.
2. RE analyst agents write **only** under `_dirty/`. Engineer agents are forbidden to read any
   path containing `_dirty/`.
3. Every change to a committed spec must have a matching entry in `journal.md`.
4. Magic offsets/constants in C# must cite their source spec (e.g. `// spec: Docs/RE/packets/move.yaml`).
5. User-supplied originals (`.pak`, `Main.exe`, captures) are never committed — see the repo `.gitignore`.
