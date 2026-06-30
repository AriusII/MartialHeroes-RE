<!--
verification: CONSUMER-CONFIRMED (CYCLE 15, 2026-06-30; f61f66a9) — all three opcode
  handlers were traced to their install site and the full switch/branch logic was
  statically confirmed. The sub-kind discriminator of SmsgCubeGambleResult, the
  bet-type selector (15 / 16 / 17 / 18 / 100), the result-code → message-id mapping
  (codes 1..8 → 58001..58030), the phase state machine (phases 3 / 4 / 5 + 0xFF reset
  sentinel), the settled-money u64 at payload offset 0x10 of SmsgCubeGambleSpinUpdate,
  the signed-delta win/loss/push evaluator, the four-dice reel-match evaluator
  (high/low/seven, odd/even, pair-sum compare, 6-entry special-combination table), and
  the CmsgCubeGambleSubmit 76-byte layout are all CONSUMER-CONFIRMED from the handler
  branch logic and field read-widths.
  Body sizes (16 / 188 / 76 bytes) are taken from literal field-reader operands and
  send-buffer append-byte counts; these are capture-UNVERIFIED (see §5). Resolves
  open items R-11 and §7-17.
ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
ida_reverified: 2026-06-30
evidence: [static-ida, consumer-confirmed]
sample_verified: false
note: |
  New spec created CYCLE 15 (2026-06-30). Covers the cube-gamble dice/cube minigame panel
  (MainWindow[252]): SmsgCubeGambleResult (S2C 4/99, 16-byte body),
  SmsgCubeGambleSpinUpdate (S2C 4/100, 188-byte body), and CmsgCubeGambleSubmit
  (C2S 2/141, 76-byte body). First promotion from dirty-room note C15-S15.
-->

# Cube-Gamble Dice/Cube Panel — Clean-Room Specification

> Neutral, rewritten specification promoted from dirty-room analyst notes under **EU Software
> Directive 2009/24/EC Art. 6** (decompilation permitted solely to achieve interoperability). It
> contains **no decompiler output, no pseudo-code, no legacy symbol names, and no binary virtual
> addresses**.
>
> **Spec path (cite this):** `// spec: Docs/RE/specs/cube_gamble.md`
>
> **Scope.** The client-side **dice/cube high-low gambling minigame panel** (`MainWindow[252]`).
> This is **NOT** a combat subsystem. Three opcodes drive it:
> - **S2C `4/99` `SmsgCubeGambleResult`** — bet-echo or textual result announce (16-byte body).
> - **S2C `4/100` `SmsgCubeGambleSpinUpdate`** — reel phase update: dice, settled money, board
>   state (188-byte body).
> - **C2S `2/141` `CmsgCubeGambleSubmit`** — the player's bet-sheet submitted to the server
>   on phase-3 land (76-byte body).
>
> The wire-frame header and opcode catalogue of record are owned by neighbours and cited here,
> not duplicated:
> - `opcodes.md` — the 8-byte wire frame header + the opcode catalogue of record.
> - `specs/npc_interaction.md` — the dealer-table NPC the player approaches to open the panel.
> - `specs/net_contracts.md` — the major-4 S2C inbound dispatch table that installs the handlers.
> - `specs/network_dispatch.md` — the cross-handler serial convention for the leading 8-byte region.

---

## Overview

The cube-gamble panel drives a dice-rolling minigame: the player places bets on the outcome
of a pair of dice rolls, the server spins the reels (up to two intermediate phases plus a
settlement phase), and the outcome — win, loss, or push — is determined by the server-authored
settled-money value and reflected in the client's visual reel indicators. The client computes
**no win rate** and performs **no authoritative money computation**; the server's settled-money
value in `SmsgCubeGambleSpinUpdate` is the sole financial authority. *(CONSUMER-CONFIRMED.)*

**Leading 8 bytes (both inbound bodies, offsets `0x00..0x07`):** not consumed by either
handler — they carry a cross-handler serial or actor key common to the major-4 Response family.
Only fields at offset `0x08` and beyond are meaningful to the panel.

---

## 1. `SmsgCubeGambleResult` — S2C `4/99` (16-byte body)

A **discriminated** message: a sub-kind byte at offset `0x08` routes the packet to either the
**bet-echo** path (a server echo of a placed bet) or the **result-announce** path (a textual
outcome string posted to the panel text area).

### 1.1 Offset table

| Offset | Size | Type | Field | Used when |
|-------:|-----:|------|-------|-----------|
| `0x00` | 8 | — | leading bytes — **not consumed** | — |
| `0x08` | 1 | u8 | **sub-kind discriminator** (`1` = bet-echo; any other value = result-announce) | always |
| `0x09` | 1 | u8 | **result code** (1..8) | only when `0x08 ≠ 1` |
| `0x0A` | 1 | u8 | **bet-type selector** (15 / 16 / 17 / 18 / 100) | only when `0x08 == 1` |
| `0x0B` | 1 | — | padding (not read) | — |
| `0x0C` | 4 | u32 | **wager amount** | only when `0x08 == 1` |

*(CONSUMER-CONFIRMED: all offsets, field widths, and the discriminator split.)*

### 1.2 Bet-echo route (`0x08 == 1`)

The server echoes a bet the player just placed. The panel deducts or credits its running stake
field and refreshes the per-bet labels. A 2D sound is played on each echo. The bet-type
selector at `0x0A` determines which bet slot is updated:

| `0x0A` value | Meaning |
|-------------:|---------|
| 15 | Bet category A — low-group bet slot 0 |
| 16 | Bet category B — high-group bet slot 1 |
| 17 | Bet category C — slot 2 |
| 18 | Bet category D — slot 3 |
| 100 | **Buy/refill chips** — wager amount (`0x0C`) is treated as a chip purchase (deducted from player money); no bet-slot write |

For values 15..18 the wager amount at `0x0C` is written into the matching bet-slot field,
clamped to the per-group maximum held in the panel, and the slot's amount/limit label is
rebuilt as `"{amount}/{max}"`. *(CONSUMER-CONFIRMED: all five selector values and their branch
logic.)*

### 1.3 Result-announce route (`0x08 ≠ 1`)

The result code at `0x09` (range 1..8) selects one or two message-DB string ids posted to
the panel's text area. No further wire fields are consumed on this path.

| Code `0x09` | Message ids posted | Notes |
|------------:|--------------------|-------|
| 1 | 58001, 58002 | two-line announce |
| 2 | 58003, 58004 | two-line announce |
| 3 | 58005 (with computed payout number), then 58006 | payout from config lookup — see §1.4 |
| 4 | 58007 (with computed payout number), then 58008 | payout from config lookup — see §1.4 |
| 5 | 58009 | |
| 6 | 58010 | |
| 7 | 58011, then 58006 | |
| 8 | 58030 | |
| other | *(no action — default branch)* | |

*(CONSUMER-CONFIRMED: the full switch structure and all eight code → message mappings.)*

### 1.4 Computed-payout number (codes 3 and 4)

For codes 3 and 4 the displayed payout number is **not a wire field** — it is computed from
two **panel-internal** state fields:

- **Denomination/sub-kind toggle** (panel field `+0x8A8`): value `0`, `1`, or other selects a
  numeric-config table id.
  - Code 3: toggle `0` → config id 45; toggle `1` → config id 48; other → no payout number displayed.
  - Code 4: toggle `0` → config id 44; toggle `1` → config id 47; other → no payout number displayed.
- **Double flag** (panel field `+0x8A4`): when `== 1` the looked-up config value is doubled before
  display.
- The **numeric-config table** is a global game-configuration table keyed by the config id; it
  returns a stored numeric value (float stored as integer). *(CONSUMER-CONFIRMED: all branches and
  the double-flag multiply.)*

The **source writer** of the denomination/sub-kind toggle (`+0x8A8`) is a board button handler
that was not located in this session. See §5 open questions.

---

## 2. `SmsgCubeGambleSpinUpdate` — S2C `4/100` (188-byte body)

Carries the per-spin **phase**, reel faces, settled money, history/index packs, and the full
board state block. Drives the reel animation and result, and — on phase 3 — triggers the
automatic bet-sheet submit.

### 2.1 Offset table (188 bytes)

| Offset | Size | Type | Field | Consumer behaviour |
|-------:|-----:|------|-------|-------------------|
| `0x00` | 8 | — | leading bytes — **not consumed** | — |
| `0x08` | 1 | u8 | **phase** (3 / 4 / 5) | reel-lifecycle selector; stored at panel `+1184` — see §2.2 |
| `0x09` | 1 | u8 | **per-spin "double" / sub-kind flag** | stored at panel `+2212`; selects which winning-indicator set is highlighted (`== 1` → alternate set) |
| `0x0A` | 1 | u8 | **reel-digit pack** (two BCD-style dice: tens and ones), **or** `0xFF` = reset sentinel | if `≠ 0xFF` stored at panel `+1196` and split into two 0..9 dice values; `0xFF` → idle/reset the panel if it is not open (see §2.2) |
| `0x0B` | 1 | — | padding (not read) | — |
| `0x0C` | 4 | u32 | **animation / throw value** | stored at panel `+1192`; stamped into the swing-window on phase 3 |
| `0x10` | 8 | u64 | **settled player money** (little-endian u64; low word at `0x10`, high word at `0x14`) | match-evaluator input — see §2.3 |
| `0x18` | 5 | u8[5] | **reel-history pack** — 5 bytes, each a tens/ones dice pair | lights a 5 × 2 history-reel grid on the panel |
| `0x1D` | 5 | u8[5] | **reel-index pack** — 5 bytes, each a 0..N component index | lights 5 row indicators on the panel |
| `0x22` | 2 | — | padding (align to `0x24`) | not read |
| `0x24` | 152 | u8[152] | **board/reel state block** (0x98 bytes) | copied whole into panel `+1200`; rendered as the bet-board state |

*(CONSUMER-CONFIRMED: all offsets and field widths. Total: 8 + 1 + 1 + 1 + 1 + 4 + 8 + 5 + 5 + 2 + 152 = 188 bytes. Capture-UNVERIFIED — see §5.)*

### 2.2 Phase byte (`0x08`) — the reel state machine

Stored at panel `+1184`. Three phase values and one sentinel control the reel lifecycle.

| Value | Name | Behaviour |
|------:|------|-----------|
| **5** | **Spin start / settlement** | Zeroes the swing-stop counter; sets the panel "spinning" flag (`+2236 = 1`). Runs the money-diff + match evaluator (§2.3). Writes the phase-5 dice into panel `+1185` (tens) and `+1186` (ones) from the `@0x0A` pack. |
| **4** | **Intermediate reel advance** | Plays the reel-tick sound. Writes the phase-4 dice into panel `+1187` (tens) and `+1188` (ones) from the `@0x0A` pack; highlights those two reel faces. |
| **3** | **Land / reveal** | If the prior phase was 5 (or 0) the panel auto-submits the pending bet sheet via `CmsgCubeGambleSubmit` (C2S `2/141` — see §3). Stamps the swing land-timestamp. Clears the "spinning" flag and resets the throw/animation counters. |
| `0xFF` at `0x0A` | **Reset sentinel** (read from the `@0x0A` byte) | If the panel is **not** currently open: fully reset/close it — reload textures, refresh the daily-play allowance, post "remaining plays" (msg 58029) or "out of plays" (msg 58030). No reel write occurs. |

*(CONSUMER-CONFIRMED: all three phase branches and the reset-sentinel path. Phases 5 → 4 → 3
form one spin cycle: phase 5 reveals the first dice pair and settles money, phase 4 advances
the second pair, phase 3 lands and — if a bet was pending — submits.)*

### 2.3 Match evaluator (triggered on phase 5)

Two independent layers settle a spin. Money is **server-authoritative**; visual win-line
indicators are **client-computed** from the dice values.

#### (a) Money settlement — authoritative win / loss / push

- Let `newMoney` = the u64 at payload offset `0x10`.
- Let `oldMoney` = the local player's current money (a 64-bit currency value in the player-money
  global, also updated by the shared set-money routine).
- Compute `delta = newMoney − oldMoney` (signed 64-bit arithmetic).

| `delta` | Outcome | Client action |
|--------:|---------|---------------|
| `> 0` | **WIN** | Green result text (msg 58017 with `delta`), win-stakes line (msg 58018), win sound, win banner raised. |
| `< 0` | **LOSS** | Result text (msg 58017 with the negative `delta`), loss sound. |
| `== 0` | **PUSH** | Result text (msg 58017 with 0), neutral sound. |

After outcome display: store `newMoney` into panel `+2224` / `+2228`, update the player-money
global to `newMoney`, and if the spin's stake field (`+2232`) was `> 0` increment the local
player's daily-play counter. *(CONSUMER-CONFIRMED: the full delta branch and commit logic.)*

#### (b) Visual win-line evaluation — reel matcher

Four dice are in play after a complete spin cycle:

- Phase-5 dice: `d5a` (panel `+1185`, tens) and `d5b` (`+1186`, ones) — each `0..9` on the
  wire, treated as face value `1..10` in the evaluator (each digit `+1`).
- Phase-4 dice: `d4a` (panel `+1187`) and `d4b` (`+1188`) — same `+1` conversion.

Let `pairA = (d5a+1) + (d5b+1)` and `pairB = (d4a+1) + (d4b+1)`.

The matcher evaluates the following win lines and lights the corresponding panel indicators:

| Win line | Condition | Indicator |
|----------|-----------|-----------|
| **All-four-equal** (jackpot) | `d5a == d5b == d4a == d4b` (raw wire values) | top / jackpot indicator |
| **Pair-sum tie** | `pairA == pairB` | tie indicator |
| **Pair-sum high** | `pairA > pairB` | high-pair indicator |
| **Pair-sum low** | `pairA < pairB` | low-pair indicator |
| **Phase-5 low** | `pairA < 7` | low-line indicator |
| **Phase-5 seven** | `pairA == 7` | seven indicator |
| **Phase-5 high** | `pairA > 7` | high-line indicator |
| **Phase-5 odd** | both `(d5a+1)` and `(d5b+1)` are odd | odd indicator |
| **Phase-5 even** | both `(d5a+1)` and `(d5b+1)` are even | even indicator |
| **Special combo** | see table below; match in either face order | indicator slot `a5 ∈ {0, 1, 2}` |

**Special-combination table** — a 6-entry, two-group table evaluated against the phase-5 face
pair `(d5a+1, d5b+1)` in either order. Indicator slot `a5` is set to the first matching row
(scanning slot 0 → 1 → 2):

| Indicator slot `a5` | Group 1 — face pair | Group 2 — face pair |
|--------------------:|---------------------|---------------------|
| 0 | (1, 2) | (5, 6) |
| 1 | (2, 3) | (4, 5) |
| 2 | (1, 4) | (3, 6) |

*(CONSUMER-CONFIRMED: the full 4-dice structure, all pair-sum / high-low / seven / odd-even
conditions, and the 6-entry special-combination table. This evaluator controls only which board
indicators glow; it does NOT determine the money outcome — that is `delta` in §2.3(a).)*

---

## 3. `CmsgCubeGambleSubmit` — C2S `2/141` (76-byte body)

On phase-3 land, if a bet was pending (panel flag `+2237` is set), the panel auto-submits the
bet sheet:

1. Zeroes the running stake (panel `+2232 = 0`) and clears all on-board bet-row labels.
2. Copies the bet-amount block (56 bytes from panel `+2140`) and the bet-line block (16 bytes
   from panel `+2196`) into the send buffer.
3. Appends one trailing byte: the **cube-gamble dealer-table index** — the index of the nearest
   NPC of the cube-gamble dealer type, located by scanning nearby NPCs for the matching NPC
   type and required state flags.
4. Pads to 76 bytes and sends C2S `2/141`.

| Body region | Size | Content |
|-------------|-----:|---------|
| Bet-amount block | 56 bytes | per-bet-slot wager amounts (copied from panel `+2140`) |
| Bet-line block | 16 bytes | per-bet-slot line flags (copied from panel `+2196`) |
| Dealer-table index | 1 byte | index of the cube-gamble NPC at the table |
| Alignment / padding | 3 bytes | pad to 76-byte total |

*(CONSUMER-CONFIRMED: the build sequence and the 76-byte total. Capture-UNVERIFIED — see §5.
The server then drives the spin via `SmsgCubeGambleSpinUpdate` and the textual result via
`SmsgCubeGambleResult`.)*

---

## 4. Panel state-machine field map (`MainWindow[252]`)

Panel-internal field offsets recovered from the handler traces. These describe the client's
in-memory state machine, not wire fields.

| Panel offset | Meaning | Set by |
|-------------:|---------|--------|
| `+0x8C` (140) | Panel-open / visible flag | UI open/close |
| `+1184` (`0x4A0`) | Current phase (3 / 4 / 5) | `4/100 @0x08` |
| `+1185..+1188` | Four reel dice: phase-5 tens/ones, phase-4 tens/ones | `4/100 @0x0A` split |
| `+1192` (`0x4A8`) | Last animation/throw value | `4/100 @0x0C` |
| `+1196` (`0x4AC`) | Last reel-digit pack byte | `4/100 @0x0A` |
| `+1200` (`0x4B0`) | 152-byte board/reel state block | `4/100 @0x24` (copied whole) |
| `+2212` (`0x8A4`) | Per-spin "double" / sub-kind flag | `4/100 @0x09` |
| `+2216` (`0x8A8`) | Denomination/sub-kind toggle (drives payout config id in `4/99` codes 3/4) | Board button handler — source not yet located (see §5) |
| `+2224` / `+2228` | Last settled money (u64 mirror, low word / high word) | `4/100 @0x10` |
| `+2232` (`0x8B8`) | Current spin stake | Bet flow; cleared to 0 on submit |
| `+2236` (`0x8BC`) | "Spinning" flag | Set by phase 5; cleared by phase 3 |
| `+2237` (`0x8BD`) | "Bet pending / submit on land" flag | Bet flow; cleared on submit |

---

## 5. Capture-verification status and R-CAP items

### Confirmed (CONSUMER-CONFIRMED, non-blocking)

- Sub-kind discriminator (`4/99 @0x08`), all bet-type selector values (15 / 16 / 17 / 18 / 100),
  and the result-code → message-id mapping (codes 1..8 → 58001..58030).
- Phase state machine (phases 3 / 4 / 5 + 0xFF reset sentinel) with full branch logic.
- Settled-money u64 at `4/100 @0x10`, the signed-delta win/loss/push computation, and the
  four-dice reel-match evaluator (pair-sum, high/low/seven, odd/even, 6-entry special table).
- `CmsgCubeGambleSubmit` build sequence: 56-byte amounts block + 16-byte lines block +
  1-byte dealer index + 3-byte pad = 76 bytes.

### R-CAP items (capture-pending, non-blocking, scoped to this subsystem)

**R-CAP-CG-1 — Body sizes (16 / 188 / 76 bytes)**
Sizes are taken from literal field-reader operands and append-byte counts; no live packet
capture was run this session.
*Breakpoint plan:* intercept `SmsgCubeGambleResult` and `SmsgCubeGambleSpinUpdate` handler
entry points on live session; read the frame-length field and confirm 16 / 188 / 76.

**R-CAP-CG-2 — Dual role of `4/100 @0x0A` (reel-digit pack vs 0xFF reset sentinel)**
A live capture should confirm that an idle/reset packet actually sends `0xFF` at this position.
*Breakpoint plan:* observe a `4/100` packet with phase `@0x08` outside the 3/4/5 set in the
capture stream; verify `@0x0A == 0xFF`.

**R-CAP-CG-3 — Denomination/sub-kind toggle source (`+0x8A8`)**
The writer of panel field `+0x8A8` (consumed by `4/99` codes 3/4 payout computation) is a
board button handler not located this session.
*Breakpoint plan:* place a write breakpoint on panel `+0x8A8` during a live cube-gamble session;
identify the writing instruction and its call site.

### Open questions (non-blocking)

- **Leading 8 bytes (`@0x00..@0x07`):** both handlers ignore them entirely. Most likely a
  cross-handler serial or actor key shared across the major-4 Response family. Resolving this
  requires tracing the major-4 dispatch layer — see `specs/network_dispatch.md`.

---

## 6. Cross-reference map

| This spec covers | Owned elsewhere — cite, don't duplicate |
|---|---|
| Opcodes `4/99`, `4/100`, `2/141` wire format (§1–§3) | `opcodes.md` (catalogue of record); `packets/` (wire-field specs) |
| Cube-gamble dealer NPC and its type/state lookup (§3) | `specs/npc_interaction.md` |
| Major-4 S2C inbound dispatch that installs both handlers | `specs/net_contracts.md` |
| Cross-handler serial/actor-key convention (leading 8 bytes) | `specs/network_dispatch.md` |
| Game numeric-config table (§1.4 payout lookup) | (config-table spec — not yet promoted; mark as a known gap if implementing) |
