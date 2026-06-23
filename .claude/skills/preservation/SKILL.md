---
name: preservation
description: Use to AUTHOR the project's preservation & provenance documentation — one skill, two modes. PROJECT-DOCS generates or refreshes the public README.md and CONTRIBUTING.md from the authoritative PRESERVATION_AND_ARCHITECTURE.md (EU Art.6 legal basis, bring-your-own-assets policy, the dirty/clean clean-room firewall, the no-commit rules for .pak/.exe/.pcapng/.tsv, the contribution flow) in a loving-fan-project tone that stays professional and legally precise. SESSION-LOG appends one structured provenance entry to Docs/RE/journal.md after an IDA RE session (date, analyst, binary sha256 prefix, the functions/opcodes/structs touched by canonical name only, the committed specs produced) — the append-only Art.6 audit trail. Both are documentation-only: canonical names, no addresses, no pseudo-code, no asset/binary bytes.
allowed-tools: Read Write
model: sonnet
effort: medium
---

# preservation — author the project's preservation & provenance docs

One skill, two documentation jobs that together keep the project's legal story legible and its
clean-room provenance auditable. Both are **documentation-only** and obey the same firewall: canonical
names only, never an address / a Hex-Rays pseudo-type / a decompiler autoname / a code snippet / any
asset or binary bytes.

| Mode | Produces | Authoritative source | Discipline |
|---|---|---|---|
| **PROJECT-DOCS** | `README.md`, `CONTRIBUTING.md` (repo root) | `PRESERVATION_AND_ARCHITECTURE.md` | quote/condense legal wording — never freelance |
| **SESSION-LOG** | one appended entry in `Docs/RE/journal.md` | the RE session + `Docs/RE/names.yaml` | **append-only**; neutral prose only |

## Mode A — PROJECT-DOCS (README + CONTRIBUTING)

Author or refresh the public-facing docs so newcomers understand what Martial Heroes (the revival of
the dead 2004–2008 MMORPG *D.O. Online* / *Martial Heroes*) is, why it is legal, and how to participate
without breaching the clean-room firewall. The **authoritative source** for every legal/architectural
claim is `PRESERVATION_AND_ARCHITECTURE.md` — never invent legal wording; quote and condense from
there (and `CLAUDE.md` for operational rules). Tone: a loving preservation/fan project — warm and
mission-driven, but professional and legally precise; no hype, no claim of affiliation with the
original rights holders.

1. **Read the sources first:** `PRESERVATION_AND_ARCHITECTURE.md` (legal framework Art. 6, philosophy,
   architecture); `CLAUDE.md` (current state, operational rules, real project/folder names);
   `Docs/RE/README.md` (firewall doctrine, to summarize accurately); `.gitignore` (the exact
   no-commit patterns). If `README.md`/`CONTRIBUTING.md` already exist, Read and **refresh in place**,
   preserving maintainer-added badges/links/credits — never blind-overwrite.
2. **Write `README.md`** with: title + one-line mission (clean-room, non-commercial, unaffiliated);
   What this IS / IS NOT (documentation of protocol + asset formats and a fresh .NET 10 / C# 14 +
   Godot 4.6 reimplementation; NOT a redistribution of any original game file/binary/server, not
   endorsed); legal basis (condense EU Software Directive 2009/24/EC Art. 6 + the "zero market
   subversion / defunct since 2008" reasoning, link the full doc); bring-your-own-assets (ships NO
   copyrighted content; a user supplies their own legally-obtained client `Main.exe`/`doida.exe`,
   `.pak`/VFS archives, captures; the recommended drop-zone); the clean-room firewall (the
   `Docs/RE/_dirty/` gitignored dirty room vs. committed neutral specs; engineers implement from specs,
   never pseudo-code); architecture at a glance (the five layers + the zero-alloc
   socket→pipeline→crypto→protocol→app→domain→Godot path, REAL names like
   `Network.Transport.Pipelines` not "Pipe"); build & run (`dotnet build`/`dotnet test
   MartialHeroes.slnx`; the Godot 4.6 editor); status (reflect the *current* state — full client built
   + Godot rendering — not a stale "greenfield" snapshot); license & credits (respectful credit to the
   original creators, reiterate non-affiliation).
3. **Write `CONTRIBUTING.md`** with: the golden rule (never commit copyrighted originals — restate the
   gitignored patterns exactly: `*.pak`, `*.vfs`, `*.pcapng`, `*.tsv`, `Main.exe`/`*.exe`/`*.dll`,
   `Docs/RE/_dirty/`; verify `git status` before staging); the two contributor kinds (*RE analysts*
   work in `_dirty/`, hand neutral findings to spec-authors, log sessions in `Docs/RE/journal.md`;
   *engineers* read only `Docs/RE/` specs + the C# tree, never IDA / `_dirty/`, cite every magic offset
   with `// spec: Docs/RE/...`); the clean-room firewall for contributors (link `Docs/RE/README.md`;
   mention the `clean-room-check` audits that guard merges); dependency-graph discipline (lower layers
   never reference higher; hot paths stay zero-allocation; core below layer 05 stays engine-free);
   workflow (branch, implement one project at a time, add xUnit tests, run `dotnet build`/`dotnet
   test`, English prose, open a PR; the `Co-Authored-By` convention); a short kind code-of-conduct.
4. **Verify accuracy.** Every legal sentence traces to `PRESERVATION_AND_ARCHITECTURE.md`; every
   no-commit pattern matches `.gitignore` verbatim; every project/folder name matches on-disk reality.
   Report both written paths.

**Heuristics:** blueprint vs. disk disagree on a name → disk reality wins (`Network.Transport.Pipelines`,
not the stale "Pipe"); a legal claim not traceable to the blueprint → omit it rather than freelance.

## Mode B — SESSION-LOG (append a provenance entry to the journal)

Append exactly one structured entry to `Docs/RE/journal.md` documenting an RE session. The journal is
the **legal audit trail**: the Art. 6 exception holds only while decompilation is "performed
exclusively to achieve interoperability," and the journal is the contemporaneous record proving each
session mapped protocol/asset *structure* for interoperability and produced neutral specs — not a copy
of the code. It records that a session settled its facts in the one ground truth (`doida.exe` in IDA,
static hypothesis confirmed against the live `?ext=dbg` debugger where noted) and rewrote them into the
named committed specs. **Append-only** — never edit, reorder, delete, or reformat existing entries.

### Entry schema (every entry MUST carry)

- **Date** — ISO `YYYY-MM-DD` of the session.
- **Analyst** — name/handle.
- **Binary** — `doida.exe` (or `Main.exe`) `@ <sha256 prefix>` (first ~8 hex of the pinned build's
  SHA-256, matching `binary.sha256` in `names.yaml`). Unknown ⇒ `@ (unhashed)` and flag it — never invent digits.
- **Tool** — e.g. `IDA Pro 9.3 via MCP (mcp__ida__*)`.
- **Analyzed** — functions / opcodes / structs touched, **by canonical name only** (`RecvPacketDispatch`,
  `SmsgMovePlayer (0x42)`, `DecryptInPlace`). Never raw addresses, never `sub_…` autonames — translate
  via `names.yaml`.
- **Specs produced/updated** — committed paths under `Docs/RE/` (`packets/move.yaml`, `opcodes.md`,
  `specs/crypto.md`). No committed spec ⇒ say so explicitly ("specs: none").
- **Notes** — plain-language summary of behavior learned. **No pseudo-code, no decompiler output, no addresses.**

### Steps

1. **Read `Docs/RE/journal.md`.** If absent, run `re-promote`'s workspace-init mode first — do not
   create the journal from scratch here (its header is canonical). Confirm it ends with the
   `<!-- entries below -->` marker and match the prior-entry format.
2. **Gather the schema facts** from the session. Cross-check every name against `Docs/RE/names.yaml`:
   if about to write an address or a `sub_`/`loc_`/`dword_` autoname, STOP — resolve to the canonical
   name first (and remind the analyst to record the mapping in `names.yaml`, itself a committed spec change).
3. **Scrub for taint.** Reject any note containing a hex address (`0x004…`), a Hex-Rays pseudo-type
   (`_DWORD`, `__thiscall`, …), a decompiler autoname, or a code/control-flow transcription. If the
   only way to make a point is to paste code, the point belongs in `_dirty/` (gitignored), not here.
4. **Append the entry** at the end of the file (after the last entry), preserving every existing byte:
   ```
   ## YYYY-MM-DD — <analyst>
   - binary: doida.exe @ <sha256-prefix>
   - tool: IDA Pro 9.3 via MCP (mcp__ida__*)
   - analyzed: <canonical names — functions / opcodes / structs>
   - specs produced/updated: <committed paths under Docs/RE/, or "none">
   - notes: <plain-language summary; no pseudo-code, no addresses>
   ```
5. **Cross-check the firewall pairing.** Every committed spec change should be paired with a journal
   mention of the spec path — exactly what `clean-room-check`'s firewall-gate mode enforces in CI. If
   this session changed a spec you cannot name here, fix that before committing. Report the appended
   entry and remind the user the journal change is itself committed.

**Heuristics:** a debugger-confirmed fact may note "confirmed via live session" in plain language, but
still no addresses — the journal records *what* was learned, not *how*.

## Verify / Done when

- **Mode A:** `README.md` + `CONTRIBUTING.md` written at the repo root with all required sections;
  every legal sentence traces to `PRESERVATION_AND_ARCHITECTURE.md`; every no-commit pattern matches
  `.gitignore` verbatim; every name matches on-disk reality; both paths reported.
- **Mode B:** exactly one new entry appended at the bottom, every prior byte preserved; all names
  canonical (resolve in `names.yaml`); no address / autoname / pseudo-code anywhere; every committed
  spec the session touched is named.
- Both: no decompiler identifier, address, asset/binary bytes, or affiliation claim leaked into a
  committed doc.

## Pitfalls (never)

- Never freelance legal language — unsourced legal wording is a liability; source it from the blueprint or drop it.
- Never imply endorsement by or affiliation with the original rights holders.
- Never use stale blueprint names over real on-disk ones, or a stale `Status` snapshot.
- Never edit, reorder, or reformat existing journal entries — append-only, always.
- Never record a raw address or autoname (resolve to canonical first), and never paste a code snippet
  or control-flow transcription into a note or a public doc.

> North star N1: the public docs make the EU Art. 6 clean-room basis + bring-your-own-assets firewall
> legible to newcomers, and the journal is the contemporaneous Art. 6 audit trail proving each session
> mapped structure for interoperability and produced neutral specs — both keeping the N2 revival lawful.

## Hard rules

- Source all legal claims from `PRESERVATION_AND_ARCHITECTURE.md`; use the REAL project names
  (`Network.Transport.Pipelines`); these are public/committed docs — no addresses, no pseudo-code, no
  asset/binary contents.
- The journal is append-only and canonical-names-only; zero pseudo-code/decompiler output (that content
  lives only under `_dirty/`).
