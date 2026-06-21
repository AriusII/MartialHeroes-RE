---
name: re-handoff
description: Use to gate the IDA->C# crossing — STAMP a recovered Docs/RE spec with its readiness + per-fact confidence banner, or CHECK that a committed spec is implementation-ready (every opcode/field/offset/constant resolved, every load-bearing fact debugger- or capture-confirmed, no open CONFLICT/INCOMPLETE) before csharp-port-orchestrator builds from it.
allowed-tools: Read Write Edit Grep Glob
model: opus
effort: high
---

# re-handoff — gate G4, the IDA->C# crossing

This skill stands at **gate G4**, the last gate before C# porting consumes a spec. The RE pipeline is a
gate chain — **G0 brainstorm → G1 recover (static, `_dirty/`) → G2 confirm (the `?ext=dbg` debugger via
`re-validator`) → G3 promote (`spec-author` rewrites the committed spec) → G4 readiness (here) →** then
`csharp-port-orchestrator` builds. G4 answers one question: *is this committed spec implementation-ready,
or does a load-bearing fact still rest on an unconfirmed static hypothesis?*

**Ground-truth doctrine.** IDA / `doida.exe` is the single absolute truth; the committed `Docs/RE/` spec
this skill reads is the **derived** truth engineers consume; the IDA-MCP toolbox is only the map for
querying the binary — **none outranks the binary**. So a spec is only "ready" when its load-bearing facts
were *confirmed against the binary* (debugger or capture), not merely hypothesized from static reads. This
skill is **clean-room**: it carries **NO IDA** and **never opens `_dirty/`** — it works on the committed
spec text alone.

## Confidence ladder (the grade every fact carries)

```
static-hypothesis   recovered from static IDA only (G1) — load-bearing? then NOT ready
debugger-confirmed  proven against the live ?ext=dbg session (G2) — ready
capture-confirmed   proven against the pcap oracle / a screenshot oracle (G2) — ready
spec-promoted       rewritten into the committed neutral spec (G3) — necessary, not sufficient
implementation-ready  every load-bearing fact is confirmed + the spec is complete (G4) — C# may build
```

The load-bearing line: **a static-only fact that the C# implementation depends on is NOT
implementation-ready.** It must reach `debugger-confirmed` or `capture-confirmed` (gate G2, via
`re-validator`) first. Non-load-bearing colour (a descriptive aside, a "likely unused" note) may stay
`static-hypothesis` without blocking the gate — flag it, don't fail on it.

## Two modes

| Mode | Role | Side | Acts on |
|---|---|---|---|
| **STAMP** | `spec-author`, on promotion (G3→G4) | clean bridge | writes a `readiness:` banner + per-fact confidence tags into the committed spec |
| **CHECK** | `csharp-port`, before building (G4 gate) | clean engineer | reads the committed spec ONLY; emits READY / NOT-READY + the gaps |

## Mode A — STAMP (spec-author, on promotion)

Right after a promotion (`re-promote` Mode A), stamp the spec so downstream knows exactly what it can
trust. Never invent a confirmation — only record one that actually happened (the `re-validator` confirmed
it, or a capture proved it).

1. **Pin the IDB SHA.** Read the `binary:` sha256 in `Docs/RE/names.yaml`; the readiness banner is pinned
   to it (addresses don't transfer across builds — a spec confirmed against a stale IDB is stale).
2. **Grade each fact.** Walk every load-bearing fact in the spec (each opcode, field, offset, size,
   constant, formula) and assign a rung: `debugger-confirmed`, `capture-confirmed`, or `static-hypothesis`.
   Mark which the `re-validator` confirmed and how (debugger reg/mem read, breakpoint, binary-diff).
3. **Write the banner.** At the top of the spec, add/refresh a `readiness:` block matching the house style
   of the `verification:` banners already in `Docs/RE/`:
   ```
   readiness: <READY | NOT-READY>   idb: <sha256-prefix>   gate: G4
   confirmed: <count debugger/capture>   static-only: <count>   open: <CONFLICT/INCOMPLETE count>
   ```
4. **Annotate per fact.** Tag each load-bearing row inline (e.g. a trailing `# debugger-confirmed` /
   `# static-only` on a packet field, a `(static-hypothesis)` on a struct offset) so the CHECK side and
   the engineer can see exactly which facts are settled.
5. **Set the verdict.** `readiness: READY` only if **every** load-bearing fact is confirmed and the spec is
   complete (no open `CONFLICT:`/`INCOMPLETE:`). Otherwise `NOT-READY` and name what's missing.
6. **Report**: the spec path, the readiness verdict, the confirmed/static-only counts, and a reminder that
   the readiness banner is a committed change (pair it with a `journal.md` entry via `preservation`).

## Mode B — CHECK (csharp-port, before building)

Before `csharp-port-orchestrator` writes a line of C# against a spec, audit it. **Read ONLY the committed
spec** — never `_dirty/`, never IDA. Reading `_dirty/` here would itself cross the firewall.

### The readiness checklist (all must hold for READY)

1. **Completeness** — every element the C# needs is present and resolved: each opcode has id+name+
   direction, each field has offset+size+type, each struct row has offset+size, each constant has a value.
   No `TBD`, no `?`, no empty cell on a load-bearing row.
2. **Confirmation** — every **load-bearing** fact is `debugger-confirmed` or `capture-confirmed`, not
   `static-hypothesis`. A static-only load-bearing fact ⇒ NOT-READY (route to G2).
3. **Packet arithmetic** — for a packet YAML, the declared `size:` equals the sum of its field widths
   (header + body). A mismatch is a hard NOT-READY (the layout is wrong or incomplete).
4. **No open markers** — no `CONFLICT:` (binary vs spec dispute), no `INCOMPLETE:`/`TODO:`/`UNVERIFIED`
   on a load-bearing fact.
5. **Neutrality** — zero decompiler artifacts (`sub_`/`loc_`/`dword_`/`_DWORD`/`__thiscall`/mangled
   `?x@@`/a raw `0x004…` address). A leak here means the spec was transcribed, not rewritten — fail and
   route back to `spec-author`.
6. **IDB freshness** — the `readiness:`/`verification:` banner's IDB SHA matches `names.yaml`'s current
   `binary:` sha256. A spec pinned to a superseded IDB is NOT-READY until re-confirmed.

### Steps

1. **Scope.** The one committed spec (or a small set) the porting wave will build from — under
   `Docs/RE/{packets|formats|structs|specs}/` or `opcodes.md`. Confirm it is committed, not `_dirty/`.
2. **Audit** against the six checks above. Use Grep to sweep for artifacts/open markers; sum the field
   widths for the packet-arithmetic check; compare the banner SHA to `names.yaml`.
3. **Verdict.** Emit **READY** (every check holds) or **NOT-READY** + the precise gaps as a list:
   `<element> — <which check failed> — <route>`. Route each gap: a static-only load-bearing fact or a
   `CONFLICT:` → **G2 / `re-validator`** (debugger confirmation); a missing field/offset → **G1 recovery**
   (`re-orchestrator`); an artifact leak → **G3 / `spec-author`** rewrite; a stale IDB → re-confirm.
4. **Report.** The spec path, READY/NOT-READY, the gap list with routes, and the confirmed/static-only
   tally. Recommend only — never edit the spec or the C# to make the gate pass.

### Heuristics

- A field has an offset+size+type **and** a `debugger-confirmed` tag → ready; an offset with only a
  `static-hypothesis` tag → NOT-READY, route to `re-validator` (the debugger reads it live).
- `size:` ≠ Σ field widths → NOT-READY, no exceptions — the wire layout cannot be byte-exact.
- A descriptive aside graded `static-hypothesis` but **not** consumed by the C# (e.g. "this branch is
  likely the dead idle col15") → note it, don't fail the gate; only *load-bearing* statics block.
- The spec carries no `readiness:` banner at all → treat as NOT-READY and ask `spec-author` to STAMP first
  (you cannot certify confirmation the spec never recorded).

## Verify / Done when

- **STAMP:** a `readiness:` banner (verdict + IDB SHA + gate G4 + confirmed/static-only/open counts) sits
  atop the spec; every load-bearing fact carries an inline confidence tag; `READY` only when all are
  confirmed and nothing is open; a paired `journal.md` entry recorded.
- **CHECK:** all six checks ran on the committed spec; the verdict is READY or NOT-READY with each gap
  named + routed (`re-validator` for G2, `re-orchestrator` for G1, `spec-author` for G3); confirmed/
  static-only tally reported; **nothing under `_dirty/` was opened**; no file edited to pass the gate.

## Pitfalls (never)

- **Never** stamp `READY` on a spec with a static-only load-bearing fact — that is the exact failure G4
  exists to catch; it MUST be debugger- or capture-confirmed (G2) first.
- **Never** open `_dirty/` or IDA to "confirm" a fact here — this is the clean side; confirmation is
  `re-validator`'s job at G2, recorded in the spec by `spec-author` at G3.
- **Never** record a confirmation that didn't happen (no "probably confirmed") — the readiness banner is a
  truth claim engineers stake byte-exact code on.
- **Never** edit the spec (beyond the STAMP banner) or any C# to make CHECK pass — report and route back.
- **Never** trust a banner whose IDB SHA differs from `names.yaml` — addresses/layouts don't transfer
  across builds; a stale-IDB spec is NOT-READY.

> North star N1→N2: G4 is the last clean-room checkpoint — it lets the 1:1 C# port build only from
> binary-confirmed facts, so fidelity is earned (debugger-confirmed), never assumed (static-only).

## Hard rules

- Clean-room only: **NO IDA**, and **never** read `_dirty/`. Work on committed spec text alone.
- A load-bearing static-only fact is NOT implementation-ready — it must be confirmed at G2 (`re-validator`)
  before this gate may pass.
- STAMP writes only the `readiness:` banner + inline confidence tags into the committed spec; CHECK writes
  nothing. Neither edits C#. Both route gaps back to the RE domain rather than fixing them.
- Readiness is pinned to the `names.yaml` IDB SHA; pair any STAMP banner change with a `journal.md` entry
  (via `preservation`) — never edit `journal.md`/`names.yaml` directly here.
