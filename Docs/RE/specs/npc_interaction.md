---
status: confirmed
sample_verified: false
verification: confirmed (client-side routing/sizes/offsets/formulas, control-flow-confirmed); capture/debugger-pending for server-authored magnitudes and on-wire VALUE meanings
ida_reverified: 2026-06-16
ida_anchor: 263bd994
evidence: [static-ida]
conflicts: 2/151 / 2/152 / 2/153 reassigned away from ProductPanel (see §9.1); JOB rank gate scoped to KIND 27-29 (see §2.3); ProductPanel true buy opcode unmapped (see §5.2)
---

# NPC Interaction — Dialogue, Shops, Repair, Storage & Services — Clean-Room Specification

> **Verification banner (re-verified 2026-06-16 on build 263bd994, evidence [static-ida]).**
> The NPC-service routing, the panel-slot construction table, the catalog-source paths,
> the send-builder `(major,minor)` tuples, the fixed body sizes, the struct-relative field
> offsets, the bag-count gate thresholds, the price divisors and the message-db id ranges
> are **[confirmed]** from the IDB control-flow and operands this pass. What remains
> **[capture/debugger-pending]**: the on-wire *meaning* of server-authored VALUE fields
> — specifically the storage `op`-byte enum (which value is deposit vs withdraw), the
> precise semantics of the compared player-rank value, and any server-paced magnitudes.
> No live network capture was available this pass, so a body's *field meaning* (as opposed
> to its size, which is exact) should still be byte-confirmed against a capture before an
> engineer hard-codes it.

> Neutral, rewritten behavioural specification. No legacy symbols, no binary
> addresses, no pseudo-code. Describes the *observed behaviour*, panel-slot model,
> opcode tuples, catalog-source paths and constants of the legacy client's
> **click-an-NPC** subsystem — the central interaction router, the pre-built service
> panels it opens, and the client→server messages those panels emit — so the .NET
> core can be reimplemented from scratch.
>
> Scope: the client send-side (C2S) of the NPC-service family and the rules the
> client applies before opening a panel or before sending. It completes and
> **supersedes** the partial KIND→panel dispatch sketch in `specs/quests.md §3`,
> and shares the shop buy/sell/open opcodes already specified in
> `specs/inventory_trade.md §7` (2/19 interact-open, 2/20 sell) without restating
> their payload tables field-by-field.

## Status & confidence

- **Re-verified on build 263bd994 by static IDA analysis; no live network capture this
  pass.** Every protocol fact below is a **hard static fact** read directly from the
  client (the KIND dispatch, the panel-slot construction table, the catalog-source paths,
  the send-builder `major/minor` tuples and their fixed body sizes). These are graded
  **[confirmed]**.
- **All nine NPC-lane body sizes are now exact** (read from the send-wrapper immediates) —
  the prior pass's inferred `8*` sizes are corrected (§9). What stays
  **[capture/debugger-pending]** is the *meaning* of server-authored VALUE fields inside a
  body (e.g. the storage `op`-byte deposit/withdraw enum, the player-rank compare value).
- **Per-claim confidence** is tagged inline as `[confirmed]` (read directly from the
  binary control-flow + operands this pass), `[static-hypothesis]` (single consistent site
  / strong inference), or `[capture/debugger-pending]` (server-authored value meaning that
  needs a live capture or the debugger). `[sample-verified]` marks a fact also corroborated
  by a real VFS sample.
- **Korean text fields use the CP949 (EUC-KR) code page.** NPC names, shop/repair item
  labels and any dialog text must be decoded as CP949, never UTF-8.
- Builds on existing committed specs:
  - `specs/inventory_trade.md` — 2/19 interact-open (12 B), 2/20 sell (12 B), the
    flat 20-slot item model, the NPC-buy and NPC-sell acks. **Not restated here.**
  - `specs/quests.md` — the quest-giver path and the unified quest-action opcode 2/28.
    This spec **completes** that spec's partial KIND table and adds the distinct
    quest-NPC *step* opcode 2/110.
  - `structs/npc.md` — the `mobs.scr` / `npc.scr` template + `npc.arr` spawn records.
    The shop/repair catalog region documented here lives inside the per-NPC template.
  - `opcodes.md` — the wire frame header and the `(major:minor)` convention.
- A consolidated **open-questions** list is in §10. Do not implement any field tagged
  `[capture/debugger-pending]` without widening capture evidence or asking for an analyst
  cross-check.

---

## 1. Interaction model overview

Click-an-NPC is a **panel-driven, server-authoritative** flow built from three pieces:

1. A single **central click router** that, on a confirmed NPC click, reads the NPC's
   **KIND** classifier byte and opens exactly one matching pre-built **service panel**
   (§2, §3).
2. A fixed inventory of **pre-constructed UI panels** owned by the main UI handler.
   All service panels are built up-front during world setup and merely *shown* by the
   router; they are never created on click (§4).
3. A set of **client→server (C2S) send wrappers** that each panel emits when the user
   acts inside it — open a shop, buy, sell, repair, deposit/withdraw, request a quest
   step, etc. (§5–§8, §9).

The client holds essentially no NPC-service authority of its own: it formats lists,
gates on bag-count and gold, and emits requests; the **server resolves the outcome**.
There is in particular **no client-side teleport/warp opcode** — NPC map travel is
resolved server-side off the generic interact (§8.4).

---

## 2. Central click router (KIND dispatch)

On a confirmed NPC click the interaction router runs a small pre-gate, then switches on
the NPC's **KIND** classifier and opens the matching pre-built panel. `[confirmed]` —
the router is a single function whose `switch(KIND)` covers 35 cases.

### 2.1 Pre-gate and stored target

Before dispatching, the router:

- checks the local player's progress/ready state and a busy/dead predicate (the shared
  "am I in game / not busy / not dead" gate used across action send paths), plus a
  "resolve the nearby known interact target" pre-check, and
- stores the clicked NPC's **descriptor pointer** and **id** into two fixed fields on
  the UI handler (the "active interaction target") — the descriptor pointer at UI-handler
  field **+393** and the id at field **+394** (4-byte-indexed). Subsequent panel sends
  read the stored id back from **+394** rather than re-resolving the click. `[confirmed]`
  (the storage 2/142 builder reads the id back from +394 end-to-end — §7.2).

The router reads the dispatch key as **KIND** = the NPC descriptor's classifier byte,
and the quest-giver band additionally reads **JOB** = a u16 NPC role/job code. Both are
struct-relative fields on the NPC descriptor; see §2.4 and `structs/npc.md`. The router's
own debug format string surfaces all four — `JOB`, `KIND`, `ID`, `NAME` — confirming the
descriptor offsets below.

### 2.2 KIND → panel → purpose table

The router handles **35 KIND cases** (an IDA "switch 35 cases" jumptable). The full
recovered mapping (this completes and supersedes the partial table in
`specs/quests.md §3.2`); every row below was verified against the jumptable this pass:

| KIND (dec / hex) | Action | Panel slot → class | Purpose |
|---|---|---|---|
| 1, 2, 3, 4, 6, 7, 11, 24 | return immediately, no panel | — | non-interactive / decorative NPC |
| 5 (0x5) | show panel | 149 → **NpcPanel** | generic **merchant / menu** NPC (shop entry) |
| 8 (0x8) | toggle inventory sub-panel, then show menu | 173 + 149 → ItemPanel + **NpcPanel** | merchant variant that also opens the bag sub-panel |
| 9 (0x9) | show panel | 152 → **KeepNpcPanel** | **storage / bank ("keep")** NPC menu (§7) |
| 10 (0xA) | show panel | 151 → **QuestNpcPanel** | quest-step NPC (§8.3) |
| 14 (0xE) | show panel | 150 → **RepairNpcPanel** | item **repair / refine** NPC (§6) |
| 15 (0xF) | build + show | 153 → **GuildNpcPanel** | guild NPC (atlas-swap panel) |
| 16 (0x10) | toggle inventory sub-panel only | 173 | inventory toggle, no service panel |
| 17 (0x11) | build + show | 154 → **ConfessionNpcPanel** | confession NPC (atlas-swap panel) |
| 18–21 (0x12–0x15) | build + show | 155 → **GatherNpcPanel** | **gathering** NPC (§8.1) |
| 22 (0x16) | show service panel | 233 → confession/rank service | fame / respawn / rank service |
| 23 (0x17) | show info panel | 239 → named-NPC info | reads two name strings from the descriptor |
| 25 (0x19) | emit empty 1-byte net request → **C2S 2/100** | — | server round-trip "request open NPC" |
| 26 (0x1A) | build + show | 256 → **NpcSearch** | NPC locator / helper panel |
| 31 (0x1F) | build + show, caption set A (mode 0) | 257 → two-mode service | rank / duel-registration service (mode A) |
| 32 (0x20) | build + show | 259 → sundry item-shop | simple item shop (§5.1) |
| 33 (0x21) | build + show, caption set B (mode 1) | 257 → two-mode service | rank / duel-registration service (mode B) |
| 35 (0x23) | open req → **C2S 2/143** | 262 → **QuestItemKeepPanel** | quest-item hold/keep open request (§8.3) |
| 27, 28, 29 (default, JOB-gated) | quest-giver path → quest-message panel, JOB → rank gate | 287 → quest-message panel | **JOB-gated quest giver** (rank gate runs only for KIND 27–29 — §2.3) |
| 12, 13, 30, 34 (default, no JOB gate) | quest-giver path → quest-message panel, **no rank gate** | 287 → quest-message panel | default quest giver, skips straight to the message branch (§2.3) |

> The KIND values are **the dispatch key**; they are NPC-descriptor classifier values,
> not opcodes. The "Panel slot" is the fixed UI-handler service slot (§4); the "class"
> is the panel's role. Grade: dispatch structure `[confirmed]`; the *purpose* labels for
> the two-mode service NPC (KIND 0x1F / 0x21) are `[static-hypothesis]` (it reuses the
> shop-list template+128 source path and is a rank / duel-registration candidate, **not** a
> teleporter — see §8.4 and open question 6).

### 2.3 Quest-giver default path (JOB → rank gate, scoped to KIND 27–29)

The dispatcher's **default** case is the quest giver, but the **JOB → rank gate runs only
for KIND ∈ {27, 28, 29}** (`(u8)(KIND − 27) <= 2`). The other default KINDs (12, 13, 30,
34) skip straight to the quest-message branch with **no rank gate**. `[confirmed]` — this
corrects the prior pass's implication that *all* default-KIND quest givers got the gate.

When the gate does run, **JOB does not select a single "required rank"** — it selects a
**rank RANGE band** `[start, end]` plus a **compare value**. `[confirmed]`:

| NPC JOB code | band start | band end | compare value |
|---|---|---|---|
| 2549 | 2 | 7 | 1 |
| 2550 | 7 | 12 | 2 |
| 2551 | 12 | 17 | 3 |
| 2552 | 17 | 21 | 4 |
| 2553 | 21 | 24 | 5 |

- The gate compares the **player's current rank value** to the JOB's **compare value**.
  On a match it programs the quest-message panel (slot 287) with the band `[start,
  start−1, end]` written at panel **+396 / +400 / +404** and shows it; on a mismatch it
  shows a "need rank N" notice **without** opening the panel. `[confirmed]` (the struct
  offsets and the equality compare); the precise *semantics* of the compared player-rank
  value is `[capture/debugger-pending]`.
- Message ids on mismatch (CP949, looked up through the message DB, which is why a raw
  immediate-search misses them): **63009** when the player-rank value is 1, **63010** when
  it is ≥ 7, otherwise **63008** formatted with `(rank − 1)` and prefixed via **63007**.
  `[confirmed]` for all four ids.

> The prior doc's "JOB → required player rank" column maps onto the **compare value**
> (1..5), not a minimum rank. Read the table's last column as the *compare value* against
> the player's current rank, gated to KIND 27–29.

### 2.4 NPC descriptor fields read by the router

Struct-relative fields on the NPC descriptor used by the interaction path, re-pinned to
build 263bd994. See `structs/npc.md` for the full descriptor; only the interaction-relevant
fields are listed here.

| Offset | Size | Type | Field | Grade |
|---|---|---|---|---|
| +0 | var | char[] | NPC **name** (CP949, NUL-terminated) | [confirmed] |
| +0x22 (+34) | 1 | u8 | **KIND** — dispatch key (§2.2 table) | [confirmed] |
| +0x34 (+52) | 2 | u16 | **JOB** — quest-giver rank band 2549–2553 (§2.3) | [confirmed] |
| +164 / +165 | — | str | two name strings used by the KIND 0x17 info panel | [confirmed] |

> All four offsets re-confirmed this pass from the router's debug format string
> (`JOB` = descriptor+52, `KIND` = descriptor+34, `NAME` = descriptor+0) and the switch
> selector (`*(u8*)(descriptor + 34)`).

---

## 3. Why a router, not per-NPC scripts

Every NPC click resolves through the **same** router and the **same** fixed pool of
panels. There is no per-NPC dialog-tree interpreter on the interaction path:

- The "talk" service panels (Guild / Confession / Gather / Product / NpcSearch) are
  **atlas-swap panels** — opening one simply rebinds a fixed DDS texture atlas onto the
  panel's child widgets. They carry **no per-NPC dialog string list**. `[confirmed]`.
- The only per-NPC *content* surfaced on click is data-driven: shop/repair item lists
  from the NPC template (§5, §6), and quest dialog text from the quest tables (owned by
  `specs/quests.md`). The general NPC **name** is the descriptor's name field (CP949).
- Whether one of the NPC-template string sub-fields is a spoken greeting line is
  **unconfirmed** — the on-click panels observed here render fixed atlas + shop/quest
  data and did not visibly pull a greeting line (open question 5).

This is the structural reason the client is so thin here: the router is a switch over
35 KIND values onto ~14 pre-built panels; everything dynamic comes from the template
tables and the server.

---

## 4. Pre-built service-panel inventory

All NPC service panels are constructed **up-front** during world setup and stored as
fixed **service slots** on the main UI handler (a flat array of panel pointers indexed
by slot number, off the main-window singleton). The router only **shows** an already-built
panel; it never constructs one on click. The full in-game panel-slot inventory is
**117 panels**, `[static-hypothesis]` (not individually re-counted this pass; carried from
the prior pass — the contiguous service band 149–155 and slots 181 / 191 / 262 / 287 are
all confirmed present). `[confirmed]` for the construction-up-front / show-only model.

### 4.1 NPC-service slots (the subset that matters)

| Slot | Panel class | Role | Grade |
|---|---|---|---|
| 146 | ItemPanel | inventory / bag panel | [static-hypothesis] |
| 147 | TradePanel | player-trade window | [static-hypothesis] |
| 149 | **NpcPanel** | generic merchant / menu NPC (shop entry) | [confirmed] |
| 150 | **RepairNpcPanel** | item repair NPC (§6) | [confirmed] |
| 151 | **QuestNpcPanel** | quest-step NPC (§8.3) | [confirmed] |
| 152 | **KeepNpcPanel** | storage / bank ("keep") NPC menu (§7) | [confirmed] |
| 153 | **GuildNpcPanel** | guild NPC | [confirmed] |
| 154 | **ConfessionNpcPanel** | confession NPC | [confirmed] |
| 155 | **GatherNpcPanel** | gathering NPC (§8.1) | [confirmed] |
| 173 | inventory sub-panel | toggled alongside the merchant menu | [confirmed] |
| 180 | StallKeeperPanel | player personal-shop keeper | [static-hypothesis] |
| 181 | **ProductPanel** | full paged item shop with buy-confirm popup (§5.2) | [confirmed] (class exists) |
| 182 | personal-shop list panel | stall / list view | [static-hypothesis] |
| 191 | **KeepPanel** | **storage grid — 60 slots** (§7) | [confirmed] |
| 214 | QuestItemKeepPanel | quest-item hold | [static-hypothesis] |
| 215 | NpcQuestPanel | quest offer list | [static-hypothesis] |
| 216 | NpcQuestMsgPanel | quest short message | [static-hypothesis] |
| 218 | NpcQuestTalkPanel | quest accept / talk | [static-hypothesis] |
| 232 | GatherSlotPanel | gather progress slots | [static-hypothesis] |
| 233 | confession / rank service (KIND 0x16) | fame / respawn | [confirmed] |
| 239 | named-NPC info (KIND 0x17) | two-string info panel | [confirmed] |
| 256 | NpcSearch | NPC locator / helper | [confirmed] |
| 257 | two-mode service (KIND 0x1F / 0x21) | rank / duel-registration | [confirmed] |
| 259 | sundry item-shop (KIND 0x20) | simple 6-entry shop | [confirmed] |
| 262 | QuestItemKeepPanel open target (KIND 0x23) | quest-item keep | [confirmed] |
| 287 | quest-message / giver panel | default-KIND quest giver | [confirmed] |

> The service groupings to pin: **149 NpcPanel / 150 Repair / 151 QuestNpc / 152 KeepNpc /
> 153 Guild / 154 Confession / 155 Gather** (the contiguous service band, each confirmed
> from the router case + its panel ctor), **181 ProductPanel** (full shop, ctor confirmed),
> **191 KeepPanel** (60-slot storage grid, ctor confirmed), and **262 / 287** (quest-item
> keep / quest-giver, confirmed from the router). The 215 / 216 / 218 quest sub-panels are
> `[static-hypothesis]` this pass.

### 4.2 NpcPanel (slot 149) menu dispatch

The merchant NpcPanel routes its menu buttons by a **selected menu index** stored on the
panel. The observed mapping:

| Menu index | Action |
|---|---|
| 0 | **open shop** → emit **C2S 2/19** with the stored active-target id; gated by the bag-count gate for KIND 5, limit 30 (§9) |
| 1 | toggle the inventory sub-panel (slot 173) |
| 2 | check the quest-offer panel (slot 215); show a "no quest" notice if empty |
| 3 | quest path |

GatherNpcPanel (slot 155) is structurally identical and also opens via **2/19** from its
own menu index 0. 2/19 is the shared interact/open opcode (owned by
`specs/inventory_trade.md`); the NpcPanel menu-index→2/19 binding is `[static-hypothesis]`
this pass (not re-opened beyond confirming 2/19 is the shared open opcode).

The shared UI event ABI for these panels: an event carries a **type** byte at event+0;
**type 2 = key** (a keycode follows at event+4; 27 = ESC closes the panel) and
**type 6 = button** (a widget sub-id at event+12; sub-id == 1 = primary click). `[confirmed]`
— verified in the QuestNpcPanel and KeepNpcPanel event handlers.

KeepNpcPanel (slot 152) dispatches its menu by a **selected-index field at panel +180**;
the index that opens the 60-slot KeepPanel (slot 191) first passes the bag-count gate for
KIND 9 (limit 50), then shows slot 191 and hides the world HUD panels. `[confirmed]`
(matches §7.1).

---

## 5. Shop catalog source and shop windows

There are **two distinct shop windows**, both driven from data **baked into the NPC
template** (`npc.scr`), not pushed by the server at open time.

### 5.1 Sundry / simple item shop (6-entry static catalog)

The simple sundry shop (reached via the NpcPanel menu or KIND 0x20) reads a **6-entry
static catalog** from the NPC's template at **template offset +128 (0x80)**:

- The shop populator looks up the NPC template by id (through the script-data map), then
  iterates **6 catalog entries** at template+128. Each catalog **entry stride is 32 bytes**:
  the **item id** is at entry+0 and the **amount/price** at entry+16. `[confirmed]` — this
  REFINES the prior pass's open question 3: the sundry entry is 32 B with `item_id@+0`,
  `amount@+16`.
- The displayed price is **amount / 1,000,000** — the global gold↔display divisor used
  throughout the client. The line is formatted with the shop item-line string id (45016)
  as `<name> <price>`; the player's gold line uses string id 45015 (formatted with the
  player's gold). `[confirmed]`.
- **The sundry shop catalog is therefore part of the on-disk NPC template, not
  server-pushed.** `[confirmed]` (the +128 catalog source, the 32-byte entry stride and
  the /1e6 divisor); the exact on-disk field widths/order beyond `item_id@+0` /
  `amount@+16` are `[static-hypothesis]` pending an `npc.scr` sample (open question 3).
- The shop atlas is the literal `data/ui/itemshop.dds` (DXT5). `[confirmed]` as a string
  literal; whether the `.dds` exists in the supplied VFS was not opened this pass
  (`[static-hypothesis]`).

See `structs/npc.md` for the NPC-template record; the +128 catalog region documented here
is the shop/repair sub-table inside that record.

### 5.2 Full paged Product shop (slot 181)

The full **ProductPanel** (slot 181) is the paged buy window with a buy-confirm popup. It
composites several DDS atlases (the item-shop base atlas plus a product atlas, a popup
atlas and a buy-window atlas) and adds **paged buy / confirm** controls on top of the
list. `[confirmed]` (the window composition and the class ctor).

**ProductPanel's true buy/commit opcode is currently unmapped.** The prior pass attributed
ProductPanel's paging/commit to 2/151 / 2/152 / 2/153 — that attribution is **wrong**
(those three opcodes belong to other panels — §9.1). ProductPanel's event handler drives
its server interaction through two internal cell-action / cell-validate routines (over its
item-cell widget ids), and the wire opcode they emit was **not isolated this pass**. The
**sundry** shop buy (not ProductPanel) is what rides **C2S 2/115** (§9). Mapping
ProductPanel's own commit opcode is a follow-up for the protocol analyst (open question 4).

### 5.3 Buy / sell opcodes are shared with the item subsystem

The shop **open** (2/19) and **sell** (2/20) opcodes, their 12-byte bodies, and the NPC
buy/sell acks (4/19, 4/20) are specified in `specs/inventory_trade.md §7` and are **not
restated here**. This spec adds only the NPC-lane minors that subsystem did not cover:
the **sundry-shop** buy/order commit **2/115**, the repair commit **2/113**, the storage op
**2/142**, the quest-NPC step **2/110**, the open-request **2/100**, and the quest-item-keep
open **2/143** (§9). The minors **2/151 / 2/152 / 2/153** are *not* part of the NPC-shop
lane after re-verification — see §9.1.

---

## 6. Repair flow (C2S 2/113)

KIND 0xE opens **RepairNpcPanel** (slot 150). The repair item list and the commit:

### 6.1 Repair list source — the SAME template+128 region

The repair/refine list is read from the **same NPC-template +128 region** as the sundry
shop (§5.1), but the **runtime repair record is 16 bytes** (the list is paged 6 entries
per page). Each 16-byte repair record carries: **item id** at record+0, **cost** at
record+4, and an **aux/qty** value at record+8. `[confirmed]` (the 16-byte stride and the
three fields, read at runtime from the panel's list base).

The sundry catalog (32-B entries, §5.1) and the repair list (16-B records) read the **same
template+128 base with different strides**, so the prior open question 3 ("same bytes vs
two sub-tables") now leans toward **two views/sub-regions of the +128 block**.
`[static-hypothesis]` — needs an `npc.scr` sample to settle the byte layout (open
question 3).

### 6.2 Commit and client-side gating

On confirm, the panel reads the selected repair entry (selected-index field at panel
**+324**; a value of −1 → message 65005 "no selection"), applies a **client-side price/gold
check**, and on pass emits **C2S 2/113** with the active-target field and the selected
index. The price check uses the **/1,000,000** display divisor on two branches:

- In **cash-mode** (a flag at panel +366), it compares the entry cost to the player's gold
  / 1,000,000 → failure message **65014**.
- Otherwise it compares against the player's **cash** balance → failure message **65006**.

Additionally, before sending the panel scans the bag (a 12-byte-stride inventory region):
a **duplicate-item** match → message **65013**, and if the count of active repairable items
is **≥ 25** → message **65007** (a count cap). `[confirmed]` — these two checks were
**missed by the prior pass**.

On pass it emits **2/113** with `(target_id = active-target id, index = panel +324)`. The
repair notices use the localized message-id range **65005–65014** (CP949; 65005, 65006,
65007, 65013, 65014 observed). `[confirmed]` (the flow, the opcode, the divisor and the
message-id range). The 2/113 body is **8 bytes** = `(u32 target, u32 index)` `[confirmed]`
size; the precise *meaning* of the index value on the wire is `[capture/debugger-pending]`.

---

## 7. Storage / bank ("keep") — KIND 9 (C2S 2/142)

KIND 9 opens **KeepNpcPanel** (slot 152), whose "open storage" menu option opens the
**KeepPanel** storage grid (slot 191).

### 7.1 Open flow and the 60-slot grid

- Selecting the keep menu's "open storage" option is gated by the bag-count gate for
  KIND 9 (limit 50; §9). On pass it opens **KeepPanel** (slot 191) and hides the world
  HUD panels (inventory / skill / etc.). `[confirmed]`.
- KeepPanel's constructor loops over **60 slots** (a `count = 60` open loop over the slot
  pointer-array at panel +171), confirming the storage capacity is **60 item slots**. The
  grid object holds four 240-byte (0xF0) blocks plus an item-actor pointer array region;
  the four blocks are likely four tabs/pages of 15 slots each (15 × 16 B = 240 B). Selection
  cursors initialize to −1. Capacity = **60 slots** is `[confirmed]`; the tab interpretation
  is `[static-hypothesis]`.

### 7.2 Deposit / withdraw — C2S 2/142 (16-byte body)

Deposit and withdraw both emit **C2S 2/142** with a **16-byte body**, built only when the
requested amount is greater than zero. The body is now **fully decoded from the builder**:

| Off | Size | Type | Field | Grade |
|---|---|---|---|---|
| +0 | 4 | i32 | `target` — the stored active-target NPC id, read back from UI-handler +394 | [confirmed] |
| +4 | 1 | u8 | `op` — operation code = (widget action id − 7) | [confirmed] (formula); value enum [capture/debugger-pending] |
| +5 | 3 | — | padding / stack gap | [confirmed] |
| +8 | 8 | i64 | `amount` — quantity (built only when `amount > 0`) | [confirmed] |

The **op** byte is the panel widget's action id **minus a base of 7**; it distinguishes
deposit vs withdraw vs move. The formula `op = action − 7` and the `amount > 0` send
condition are now **[confirmed]** from the builder (upgraded from the prior pass's
`PLAUSIBLE`). The `+0` field is **[confirmed]** to be the stored active-target id read back
from UI-handler +394 — closing the "active interaction target" plumbing end-to-end (§2.1).
**Which numeric `op` value is deposit vs withdraw is `[capture/debugger-pending]`** (the
widget action-id base is server/UI-authored — open question 1).

### 7.3 Storage contents arrive via the shared item-panel acks

The storage grid is **filled by the same item-model controller and item-panel server
acks used for the bag** (the per-slot item-panel chunk / refresh family routed to the
storage view), not by a dedicated "storage snapshot" opcode. No standalone storage-list
S2C minor was isolated this pass — confirm whether storage reuses the bag item-panel
chunks with a storage-view flag or has its own minor (open question 2).
`[static-hypothesis]`.

---

## 8. Other services

### 8.1 Gather (KIND 0x12–0x15)

GatherNpcPanel (slot 155) opens via **C2S 2/19** like the merchant (§4.2). The gather
session uses gather-table data files and the GatherSlotPanel (slot 232) for the progress
UI; the result returns via the already-cataloged gather-result push (S2C 4/137).
`[confirmed]` (the open path); the gather session detail is out of scope here.

### 8.2 Guild / confession / info / search

KIND 0xF (Guild, slot 153), 0x11 (Confession, slot 154), 0x16 (rank/fame service, slot
233), 0x17 (named-NPC info, slot 239) and 0x1A (NpcSearch, slot 256) open atlas-swap
service panels (§3). KIND 0x16 uses a gate notice (message id 49045). These carry no
per-NPC dialog string list. `[confirmed]` (the open targets).

### 8.3 Quest-NPC step (C2S 2/110) and quest-item keep (C2S 2/143)

- KIND 0xA opens **QuestNpcPanel** (slot 151), whose step request is **C2S 2/110**, a
  **4-byte body** `{ u8 mode, u8, u8, u8 }`, emitted from the quest-NPC family. The panel
  maps its current quest phase into the mode byte. This is the quest-**step** opcode and is
  **distinct** from the unified quest accept/proceed/give-up opcode **2/28** specified in
  `specs/quests.md §4`. `[confirmed]` (the opcode, the 4-byte size, and the distinction
  from 2/28); the mode-byte phase map is `[static-hypothesis]`.
- KIND 0x23 opens **QuestItemKeepPanel** (slot 262 / 214) by emitting **C2S 2/143**, a
  **4-byte** open request (the router case sends an all-zero 4-byte body). `[confirmed]`.
  This resolves the prior apparent 2/143 conflict: the KIND-0x23 case calls the same
  builder that wraps 2/143.

### 8.4 No teleport / warp opcode — NPC travel is server-resolved

**No dedicated client teleport / warp opcode exists** on the interaction path. NPC-driven
map travel routes through the **generic interact (2/19)** and is resolved server-side (the
area-change mechanism). The two-mode service NPC (KIND 0x1F / 0x21, slot 257) toggles
caption sets (mode A = 0, mode B = 1) but reuses the shop-list (template+128) source path —
it is a rank / duel-registration candidate, **not** a teleporter (open question 6). Treat
"click NPC to travel" as: emit 2/19, let the server resolve the destination; do **not**
look for a client warp message. `[confirmed]` (no warp opcode found).

---

## 9. NPC-lane C2S send map

Each NPC-service panel emits its action through a fixed send wrapper that writes
`major = 2` and the minor into the frame header (see `opcodes.md` for the frame format)
and appends a fixed-length body. Every wrapper has the same shape (alloc buffer → write
major/minor WORDs at buffer+4 / buffer+6 → append a fixed-size body → hand to the
send-convergence point, which stamps a tick timestamp, runs the outbound cipher + compress
stages and queues). **All body sizes below are exact immediates** read this pass. The
NPC-lane rows:

| Opcode | Dir | Body size | Role (this lane) | Emitting panel |
|---|---|---|---|---|
| 2/19 | C2S | 12 | NPC interact / **shop-open** (also gather-open) | NpcPanel, GatherNpcPanel |
| 2/20 | C2S | 12 | NPC **sell** one item | inventory/merchant path |
| 2/100 | C2S | 1 | KIND 0x19 **"request open NPC"** (round-trip, body = 0) | the router itself |
| 2/110 | C2S | 4 | **QuestNpcPanel step** request | QuestNpcPanel (slot 151) |
| 2/113 | C2S | 8 | **REPAIR commit** = (u32 target, u32 index) | RepairNpcPanel (slot 150) |
| 2/115 | C2S | 8 | **SUNDRY-shop BUY / order commit** = (u32 target, u32 2·index) | sundry shop (NpcPanel) |
| 2/142 | C2S | 16 | **STORAGE deposit / withdraw** | KeepPanel (slot 191) |
| 2/143 | C2S | 4 | KIND 0x23 **quest-item-keep open** (body = all-zero) | QuestItemKeepPanel |

> The `(major:minor)` tuples and the body sizes are `[confirmed]`. The body *field
> meanings* (server-authored VALUEs) are `[capture/debugger-pending]` until a capture
> confirms them; the index/target *positions* are `[confirmed]`.
>
> **2/19 and 2/20** bodies are already specified in `specs/inventory_trade.md §7`. **2/28**
> (unified quest action) is in `specs/quests.md §4`. Pair **storage 2/142** with the
> still-unidentified storage-snapshot S2C (open question 2).

### 9.1 Reassigned minors — 2/151 / 2/152 / 2/153 are NOT in the NPC-shop lane

Re-verification this pass found the prior doc's attribution of **2/151 / 2/152 / 2/153** to
ProductPanel paging/commit is **wrong** on both **owner** and (for two of them) **size**.
These three opcodes belong to other subsystems and are recorded here only so an
implementer does not wire them into the NPC-shop panels. **They are out of scope for this
spec** — their authoritative homes are the opcode catalog and packet YAMLs (Block D / E):

| Opcode | Body size (corrected) | Actual owner / role | Prior (wrong) doc claim |
|---|---|---|---|
| 2/151 | **1** (was claimed 8) | Goods-box / default-menu request (a 1-byte request, body = 0) | "ProductPanel buy/confirm, 8 B" |
| 2/152 | **8** = (u32, u32) | **quest action** emitted from the quest panel | "ProductPanel page/select, 8 B" |
| 2/153 | **4** (was claimed 8) | **inventory-sort** request (one arg = 40·bag_count; carries a **60-second cooldown**) | "ProductPanel action, 8 B" |

`[confirmed]` (owner, size and — for 2/153 — the 60 s cooldown and the `40·bag_count`
argument). ProductPanel's *own* buy/commit opcode is unmapped (§5.2, open question 4).

> **Catalog / packet-YAML handoff.** The NPC-lane minors **2/100, 2/110, 2/113, 2/115,
> 2/142, 2/143** are documented here but are **not yet rows in `opcodes.md`** and have **no
> dedicated `packets/*.yaml`** field specs; the reassigned **2/151, 2/152, 2/153** need
> their owners and corrected sizes (1 / 8 / 4, not 8 / 8 / 8) fixed in the catalog and
> packet YAMLs by Block D / E. Promotion handoff (§11): add catalog rows and (where a body
> is worth pinning) YAML stubs — notably `2-142_storage_op.yaml` (the fully-sized 16-byte
> body, §7.2) and a `2-110_quest_npc_step.yaml` (4-byte). 2/19 / 2/20 already have catalog
> context via the 4/19 / 4/20 ack rows.

---

## 10. Constants recovered (numeric reference)

| Constant | Meaning | Grade |
|---|---|---|
| KIND at descriptor +0x22 (u8) | interaction dispatch key (§2.2) | [confirmed] |
| JOB at descriptor +0x34 (u16) | quest-giver rank band selector | [confirmed] |
| Router switch = 35 KIND cases | central dispatch (§2.2) | [confirmed] |
| Active-target: descriptor ptr at UI-handler +393, id at +394 | stored interaction target, read back by sends (§2.1) | [confirmed] |
| 117 panels total; service slots 149–155, 181, 191, 262, 287 | pre-built UI panel inventory (§4) | [confirmed] (band); [static-hypothesis] (117 count) |
| Storage capacity = 60 slots | KeepPanel ctor open loop, `count = 60` (§7.1) | [confirmed] |
| KeepPanel block = 240 B (0xF0) ×4 | four tabs of 15 × 16 B slots (interpretation) | [static-hypothesis] |
| Sundry shop = 6-entry catalog at template +128, **entry stride 32 B** (item_id@+0, amount@+16) | static, baked into npc.scr (§5.1) | [confirmed] |
| Repair list = N × **16-byte** records at template +128, 6/page (id@+0, cost@+4, aux@+8) | same +128 region, different stride (§6.1) | [confirmed] |
| Shop atlas literal `data/ui/itemshop.dds` (DXT5) | sundry shop atlas (§5.1) | [confirmed] (literal); VFS presence [static-hypothesis] |
| Price divisor = 1,000,000 | gold/cash amount → displayed price | [confirmed] |
| 2/142 body = 16 bytes; `target@+0` (=UI-handler +394 id); `op = action − 7` @+4; `amount@+8`, sent only when `amount > 0` | storage deposit/withdraw (§7.2) | [confirmed] (size + fields); op-value enum [capture/debugger-pending] |
| 2/110 body = 4 bytes `{mode,_,_,_}` | quest-NPC step (§8.3) | [confirmed] (size); [static-hypothesis] (mode map) |
| 2/113 body = 8 bytes = (u32 target, u32 index) | repair commit (§6.2) | [confirmed] |
| 2/115 body = 8 bytes = (u32 target, u32 2·index) | sundry-shop buy commit (§9) | [confirmed] |
| 2/143 body = 4 bytes (all-zero) | KIND 0x23 quest-item-keep open (§8.3) | [confirmed] |
| 2/100 body = 1 byte (= 0) | KIND 0x19 open request (§9) | [confirmed] |
| 2/151 = 1 B (goods-box req) / 2/152 = 8 B (quest action) / 2/153 = 4 B (inventory-sort, 60 s cooldown, arg = 40·bag_count) | reassigned — NOT NPC-shop opcodes (§9.1) | [confirmed] |
| Repair confirm: dup-item → 65013; active-repairable count ≥ 25 → 65007 | repair pre-send gating (§6.2) | [confirmed] |
| Bag-count gate: KIND 5 > 30, KIND 9 > 50, KIND 15 > 5, KIND 0 (a2=75) > 75 | per-kind open thresholds (§10.1 gate) | [confirmed] |
| JOB 2549 / 2550 / 2551 / 2552 / 2553 → compare value 1 / 2 / 3 / 4 / 5 (band [2,7] / [7,12] / [12,17] / [17,21] / [21,24]); gate only for KIND 27–29 | quest-giver rank gate (§2.3) | [confirmed] (table); player-rank semantics [capture/debugger-pending] |
| Message ids 63007–63010 | quest-giver "need rank N" notices (CP949) | [confirmed] |
| Message ids 65005–65014 | repair price / eligibility notices (CP949) | [confirmed] |
| Message id 49045 | KIND 0x16 fame/confession-service gate notice (CP949) | [confirmed] |
| Message ids 45015 / 45016 | shop gold-line / shop item-line format (CP949) | [confirmed] |

### 10.1 Per-kind open gate (bag-count, not level)

Opening a service panel is gated by a **bag-count gate**, not a level gate: a dedicated
gate function switches on KIND, reads the current inventory item-count byte, and returns
"refuse" when the count exceeds a per-kind threshold (showing a notice and aborting
client-side). The recovered thresholds:

| KIND | Threshold (refuse if count >) | Service |
|---|---|---|
| 5 | 30 (0x1E) | merchant shop |
| 9 | 50 (0x32) | storage / bank |
| 15 | 5 | (guild / gather variant) |
| 0 (with the 75-arg call) | 75 (0x4B) | generic |

This is why opening a shop or storage with a near-full bag is refused **client-side**
before any send. `[confirmed]` (all four thresholds exact). Note this **bag-count gate is
distinct from the router's pre-gate**: the pre-gate is a "resolve the nearby known interact
target" check, while this threshold gate is the dedicated KIND/count function called across
the NPC panel family (KeepNpc, Guild, Confession, Gather, etc.).

---

## 11. Open questions

1. **2/142 `op`-byte enum.** `op = widget action − 7` distinguishes deposit / withdraw /
   move, but which value is which is unproven without the widget-id base or a capture.
   `[capture/debugger-pending]`.
2. **Storage-snapshot S2C.** Deposit/withdraw out (2/142) is confirmed, but the storage
   *contents* appear to arrive through the shared bag item-panel ack family routed to the
   storage view; no dedicated storage-list S2C minor was isolated. Confirm whether storage
   reuses the bag item-panel chunks with a storage-view flag or has its own minor.
3. **NPC-template +128 field widths.** The sundry catalog (32-B entry, item_id@+0,
   amount@+16) and the repair list (16-B record, id@+0, cost@+4, aux@+8) read the **same**
   template+128 base with different strides — they are two views/sub-regions of the +128
   block, but whether they overlap the same physical bytes or are adjacent sub-tables needs
   an `npc.scr` sample (asset analyst / struct cartographer; see `structs/npc.md`).
4. **ProductPanel (slot 181) true buy/commit opcode.** ProductPanel does **not** use 2/151
   / 2/152 / 2/153 (those are reassigned — §9.1). Its event handler emits through internal
   cell-action / cell-validate routines whose wire opcode was not isolated this pass; trace
   it (re-protocol-analyst). The sundry-shop buy (2/115) is confirmed and separate.
5. **NPC spoken-dialog source.** Whether one of the `npc.scr` string sub-fields is a
   spoken greeting line is unconfirmed; the on-click panels render fixed atlas + shop/quest
   data and did not visibly pull a greeting line. Needs an `npc.scr` sample.
6. **Two-mode service NPC (KIND 0x1F / 0x21, slot 257).** Reuses the template+128 list
   source and toggles caption sets (mode 0 / mode 1) — a rank / duel-registration
   candidate, **not** a teleporter. Needs the `npc.scr` KIND/JOB decode to label it
   precisely.
7. **Player-rank semantics in the JOB gate.** The gate compares the player's current-rank
   value to the JOB compare value; the exact meaning of that rank value is
   `[capture/debugger-pending]` (maintainer F9 / capture).

---

## 12. Cross-references

| Document | Relationship |
|---|---|
| `Docs/RE/specs/inventory_trade.md` | Owns 2/19 interact-open (12 B), 2/20 sell (12 B), the 20-slot item model, and the 4/19 / 4/20 acks. This spec adds the rest of the NPC-lane minors. |
| `Docs/RE/specs/quests.md` | Owns the quest-giver path and the unified quest-action opcode 2/28. This spec **completes** its partial KIND→panel table (§2.2) and adds the distinct quest-NPC *step* opcode 2/110. Note 2/152 is a quest-panel action (§9.1), owned on the catalog side. |
| `Docs/RE/structs/npc.md` | The `mobs.scr` / `npc.scr` template and `npc.arr` spawn records; the +128 shop/repair catalog region (32-B sundry entries / 16-B repair records) lives inside the NPC template. |
| `Docs/RE/opcodes.md` | The wire frame header and `(major:minor)` convention; the NPC-lane minors here are flagged for promotion into the catalog (§9), and 2/151 / 2/152 / 2/153 need their owners + sizes corrected there (§9.1). |

- **Glossary:** see `Docs/RE/names.yaml`
- **Provenance:** see `Docs/RE/journal.md`
