---
status: hypothesis
sample_verified: false
---

# NPC Interaction — Dialogue, Shops, Repair, Storage & Services — Clean-Room Specification

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

- **No live network capture was available for this pass.** Every protocol fact below
  is a **hard static fact** read directly from the client (the KIND dispatch, the
  panel-slot construction table, the catalog-source paths, the send-builder
  `major/minor` tuples and their fixed body sizes). These are graded **CODE-CONFIRMED**.
  But because no Wireshark oracle was available, **every wire body is at best
  `CODE-CONFIRMED-as-written` and never `CAPTURE-VERIFIED`** — treat all *field
  meanings and field widths* inside a body as hypotheses to confirm against a capture.
  This caveat applies to the whole document and is restated where it matters most.
- **Per-claim confidence** is tagged inline as `CODE-CONFIRMED` (read directly from
  the binary this pass), `LIKELY` (single consistent site / strong inference), or
  `UNVERIFIED` / `PLAUSIBLE` (inferred body layout or unsettled meaning). The
  capture-unverified banner above overrides any apparent certainty in a body table.
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
- A consolidated **UNVERIFIED / open-questions** list is in §10. Do not implement any
  field tagged `UNVERIFIED` / `PLAUSIBLE` without widening capture evidence or asking
  for an analyst cross-check.

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
the NPC's **KIND** classifier and opens the matching pre-built panel. `CODE-CONFIRMED`.

### 2.1 Pre-gate and stored target

Before dispatching, the router:

- checks the local player's progress/ready state and a busy/dead predicate (the shared
  "am I in game / not busy / not dead" gate used across action send paths), and
- stores the clicked NPC's **descriptor pointer** and **id** into two fixed fields on
  the UI handler (the "active interaction target"). Subsequent panel sends read the
  stored id from this active-target field rather than re-resolving the click.

The router reads the dispatch key as **KIND** = the NPC descriptor's classifier byte,
and the quest-giver band additionally reads **JOB** = a u16 NPC role/job code. Both are
struct-relative fields on the NPC descriptor; see §2.4 and `structs/npc.md`.

### 2.2 KIND → panel → purpose table

The router handles roughly **30 KIND cases**. The full recovered mapping (this completes
and supersedes the partial table in `specs/quests.md §3.2`):

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
| 31 (0x1F) | build + show, caption set A | 257 → two-mode service | rank/duel-registration service (mode A) |
| 32 (0x20) | build + show | 259 → sundry item-shop | simple item shop (§5.1) |
| 33 (0x21) | build + show, caption set B | 257 → two-mode service | rank/duel-registration service (mode B) |
| 35 (0x23) | open req → **C2S 2/143** | 262 → **QuestItemKeepPanel** | quest-item hold/keep open request (§8.3) |
| default (incl. JOB-gated quest givers) | quest-giver path → quest-message panel | 287 → quest-message panel | **default = quest giver**, gated by NPC JOB → required rank (§2.3) |

> The KIND values are **the dispatch key**; they are NPC-descriptor classifier values,
> not opcodes. The "Panel slot" is the fixed UI-handler service slot (§4); the "class"
> is the panel's role. Grade: dispatch structure `CODE-CONFIRMED`; the *purpose* labels
> for the two-mode service NPC (KIND 0x1F / 0x21) are `LIKELY` (it reuses the shop-list
> source path and is a rank/duel-registration candidate, **not** a teleporter — see §8.4
> and open question 6).

### 2.3 Quest-giver default path (JOB → rank gate)

The dispatcher's **default** case is the quest giver. It selects a **required-rank band**
from the NPC's **JOB** code, compares against the player's current rank, and on a
mismatch shows a "need rank N" notice **without** opening the panel; on a pass it programs
the quest-message panel (slot 287) with the rank range and shows it.

| NPC JOB code | Required player rank |
|---|---|
| 2549 | 1 |
| 2550 | 2 |
| 2551 | 3 |
| 2552 | 4 |
| 2553 | 5 |

- On rank mismatch the client shows localized message ids **63007 / 63008 / 63009 /
  63010** ("need rank N", CP949). `CODE-CONFIRMED` for the JOB→rank table and message
  ids; the precise semantics of the compared "current rank" value are `LIKELY`.

### 2.4 NPC descriptor fields read by the router

Struct-relative fields on the NPC descriptor used by the interaction path. See
`structs/npc.md` for the full descriptor; only the interaction-relevant fields are
listed here.

| Offset | Size | Type | Field | Grade |
|---|---|---|---|---|
| +0 | var | char[] | NPC **name** (CP949, NUL-terminated) | CODE-CONFIRMED |
| +0x22 (+34) | 1 | u8 | **KIND** — dispatch key (§2.2 table) | CODE-CONFIRMED |
| +0x34 (+52) | 2 | u16 | **JOB** — quest-giver rank band 2549–2553 (§2.3) | CODE-CONFIRMED |
| +164 / +165 | — | str | two name strings used by the KIND 0x17 info panel | CODE-CONFIRMED |

---

## 3. Why a router, not per-NPC scripts

Every NPC click resolves through the **same** router and the **same** fixed pool of
panels. There is no per-NPC dialog-tree interpreter on the interaction path:

- The "talk" service panels (Guild / Confession / Gather / Product / NpcSearch) are
  **atlas-swap panels** — opening one simply rebinds a fixed DDS texture atlas onto the
  panel's child widgets. They carry **no per-NPC dialog string list**. `CODE-CONFIRMED`.
- The only per-NPC *content* surfaced on click is data-driven: shop/repair item lists
  from the NPC template (§5, §6), and quest dialog text from the quest tables (owned by
  `specs/quests.md`). The general NPC **name** is the descriptor's name field (CP949).
- Whether one of the NPC-template string sub-fields is a spoken greeting line is
  **UNVERIFIED** — the on-click panels observed here render fixed atlas + shop/quest
  data and did not visibly pull a greeting line (open question 5).

This is the structural reason the client is so thin here: the router is a switch over
~30 KIND values onto ~14 pre-built panels; everything dynamic comes from the template
tables and the server.

---

## 4. Pre-built service-panel inventory

All NPC service panels are constructed **up-front** during world setup and stored as
fixed **service slots** on the main UI handler (a flat array of panel pointers indexed
by slot number). The router only **shows** an already-built panel; it never constructs
one on click. The full in-game panel-slot inventory is **117 panels**. `CODE-CONFIRMED`.

### 4.1 NPC-service slots (the subset that matters)

| Slot | Panel class | Role |
|---|---|---|
| 146 | ItemPanel | inventory / bag panel |
| 147 | TradePanel | player-trade window |
| 149 | **NpcPanel** | generic merchant / menu NPC (shop entry) |
| 150 | **RepairNpcPanel** | item repair NPC (§6) |
| 151 | **QuestNpcPanel** | quest-step NPC (§8.3) |
| 152 | **KeepNpcPanel** | storage / bank ("keep") NPC menu (§7) |
| 153 | **GuildNpcPanel** | guild NPC |
| 154 | **ConfessionNpcPanel** | confession NPC |
| 155 | **GatherNpcPanel** | gathering NPC (§8.1) |
| 173 | inventory sub-panel | toggled alongside the merchant menu |
| 180 | StallKeeperPanel | player personal-shop keeper |
| 181 | **ProductPanel** | full paged item shop with buy-confirm popup (§5.2) |
| 182 | personal-shop list panel | stall / list view |
| 191 | **KeepPanel** | **storage grid — 60 slots** (§7) |
| 214 | QuestItemKeepPanel | quest-item hold |
| 215 | NpcQuestPanel | quest offer list |
| 216 | NpcQuestMsgPanel | quest short message |
| 218 | NpcQuestTalkPanel | quest accept / talk |
| 232 | GatherSlotPanel | gather progress slots |
| 233 | confession / rank service (KIND 0x16) | fame / respawn |
| 239 | named-NPC info (KIND 0x17) | two-string info panel |
| 256 | NpcSearch | NPC locator / helper |
| 257 | two-mode service (KIND 0x1F / 0x21) | rank / duel-registration |
| 259 | sundry item-shop (KIND 0x20) | simple 6-entry shop |
| 262 | QuestItemKeepPanel open target (KIND 0x23) | quest-item keep |
| 287 | quest-message / giver panel | default-KIND quest giver |

> The three storage/quest groupings to pin: **149 NpcPanel / 150 Repair / 151 QuestNpc /
> 152 KeepNpc / 153 Guild / 154 Confession / 155 Gather** (the contiguous service band),
> **181 ProductPanel** (full shop), **191 KeepPanel** (60-slot storage grid), and
> **215 / 216 / 218** (the quest sub-panels). Grade: `CODE-CONFIRMED` (each slot recovered
> from its construction site and resolved class).

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
own menu index 0. `CODE-CONFIRMED`.

The shared UI event ABI for these panels: an event carries a **type** byte; **type 2 = key**
(a keycode follows; 27 = ESC closes the panel) and **type 6 = button** (a widget sub-id
follows). `CODE-CONFIRMED`.

---

## 5. Shop catalog source and shop windows

There are **two distinct shop windows**, both driven from data **baked into the NPC
template** (`npc.scr`), not pushed by the server at open time.

### 5.1 Sundry / simple item shop (6-entry static catalog)

The simple sundry shop (reached via the NpcPanel menu or KIND 0x20) reads a **6-entry
static catalog** from the NPC's template at **template offset +128 (0x80)**:

- The shop populator looks up the NPC template by id, then iterates **6 catalog entries**
  at template+128. Each entry contributes an **item id** and an **amount/price** value.
- The displayed price is **amount / 1,000,000** — the global gold↔display divisor used
  throughout the client. The line is formatted with the shop item-line string id (45016)
  as `<name> <price>`; the player's gold line uses string id 45015.
- **The sundry shop catalog is therefore part of the on-disk NPC template, not
  server-pushed.** `CODE-CONFIRMED` (the +128 catalog source and the /1e6 divisor);
  the exact on-disk field widths/order of the (item_id, amount) pair are `PLAUSIBLE`
  pending an `npc.scr` sample (open question 3).

See `structs/npc.md` for the NPC-template record; the +128 catalog region documented here
is the shop/repair sub-table inside that record.

### 5.2 Full paged Product shop (slot 181)

The full **ProductPanel** (slot 181) is the paged buy window with a buy-confirm popup. It
composites several DDS atlases (the item-shop base atlas plus a product atlas, a popup
atlas and a buy-window atlas) and adds **paged buy / confirm** controls on top of the
list. The order/buy commit rides **C2S 2/115**; the paging and confirm minors are **2/151
/ 2/152 / 2/153** (§9). `CODE-CONFIRMED` (the window composition and the buy/page opcode
family); the buy-request body fields are `UNVERIFIED` (open question 4).

### 5.3 Buy / sell opcodes are shared with the item subsystem

The shop **open** (2/19) and **sell** (2/20) opcodes, their 12-byte bodies, and the NPC
buy/sell acks (4/19, 4/20) are specified in `specs/inventory_trade.md §7` and are **not
restated here**. This spec adds only the NPC-lane minors that subsystem did not cover:
the buy/order commit **2/115**, the repair commit **2/113**, the storage op **2/142**, the
quest-NPC step **2/110**, the open-request **2/100**, the quest-item-keep open **2/143**,
and the product paging trio **2/151 / 2/152 / 2/153** (§9).

---

## 6. Repair flow (C2S 2/113)

KIND 0xE opens **RepairNpcPanel** (slot 150). The repair item list and the commit:

### 6.1 Repair list source — the SAME template+128 region

The repair/refine list is read from the **same NPC-template +128 region** as the sundry
shop (§5.1), but interpreted as **16-byte records, paged 6 entries per page**. The two
shops index the same physical catalog region with different strides; whether they are the
same bytes read two ways or two adjacent sub-tables is `PLAUSIBLE` (open question 3).
Each 16-byte repair record carries an **item id** and a **cost** value (the qty / cost
field widths are `PLAUSIBLE`). `CODE-CONFIRMED` (the +128 source and the 6/page paging).

### 6.2 Commit and client-side gating

On confirm, the panel reads the selected repair entry, applies a **client-side price/gold
check**, and on pass emits **C2S 2/113** with the active-target field and the selected
index. The price check compares the entry cost against the player's gold (or against a
"cash" balance when a use-cash mode is active), again using the **/1,000,000** display
divisor. On failure it shows a repair notice. The repair notices use the localized
message-id range **65005–65014** (CP949). `CODE-CONFIRMED` (the flow, the opcode, the
divisor and the message-id range); the 2/113 body fields are `UNVERIFIED` (open
question 4).

---

## 7. Storage / bank ("keep") — KIND 9 (C2S 2/142)

KIND 9 opens **KeepNpcPanel** (slot 152), whose "open storage" menu option opens the
**KeepPanel** storage grid (slot 191).

### 7.1 Open flow and the 60-slot grid

- Selecting the keep menu's "open storage" option is gated by the bag-count gate for
  KIND 9 (limit 50; §9). On pass it opens **KeepPanel** (slot 191) and hides the world
  HUD panels (inventory / skill / etc.). `CODE-CONFIRMED`.
- KeepPanel's open routine loops over **60 slots** (an `i < 60` open loop), confirming the
  storage capacity is **60 item slots**. The grid object holds four 240-byte (0xF0) blocks
  plus an item-actor pointer array region; the four blocks are likely four tabs/pages of
  15 slots each (15 × 16 B = 240 B), `PLAUSIBLE`. Selection cursors initialize to −1.
  Capacity = **60 slots** is `CODE-CONFIRMED`; the tab interpretation is `PLAUSIBLE`.

### 7.2 Deposit / withdraw — C2S 2/142 (16-byte body)

Deposit and withdraw both emit **C2S 2/142** with a **16-byte body**, built only when the
requested amount is greater than zero:

| Off | Size | Type | Field | Grade |
|---|---|---|---|---|
| +0 | 4 | i32 | `target` — the active-target / select field | PLAUSIBLE |
| +4 | 1 | u8 | `op` — operation code = (widget action id − 7) | PLAUSIBLE |
| +5 | 3 | — | padding / stack | PLAUSIBLE |
| +8 | 8 | i64 | `amount` — quantity (must be > 0) | PLAUSIBLE |

The **op** byte is the panel widget's action id **minus a base of 7**; it distinguishes
deposit vs withdraw vs move. Which numeric value is deposit vs withdraw is **UNVERIFIED**
without the widget-id base or a capture (open question 1). The 16-byte body size and the
`amount > 0` send condition are `CODE-CONFIRMED-as-written`; the field meanings are
`PLAUSIBLE`.

### 7.3 Storage contents arrive via the shared item-panel acks

The storage grid is **filled by the same item-model controller and item-panel server
acks used for the bag** (the per-slot item-panel chunk / refresh family routed to the
storage view), not by a dedicated "storage snapshot" opcode. No standalone storage-list
S2C minor was isolated this pass — confirm whether storage reuses the bag item-panel
chunks with a storage-view flag or has its own minor (open question 2). `LIKELY`.

---

## 8. Other services

### 8.1 Gather (KIND 0x12–0x15)

GatherNpcPanel (slot 155) opens via **C2S 2/19** like the merchant (§4.2). The gather
session uses gather-table data files and the GatherSlotPanel (slot 232) for the progress
UI; the result returns via the already-cataloged gather-result push (S2C 4/137).
`CODE-CONFIRMED` (the open path); the gather session detail is out of scope here.

### 8.2 Guild / confession / info / search

KIND 0xF (Guild, slot 153), 0x11 (Confession, slot 154), 0x16 (rank/fame service, slot
233), 0x17 (named-NPC info, slot 239) and 0x1A (NpcSearch, slot 256) open atlas-swap
service panels (§3). KIND 0x16 uses a gate notice (message id 49045). These carry no
per-NPC dialog string list. `CODE-CONFIRMED` (the open targets).

### 8.3 Quest-NPC step (C2S 2/110) and quest-item keep (C2S 2/143)

- KIND 0xA opens **QuestNpcPanel** (slot 151), whose step request is **C2S 2/110**, a
  **4-byte body** `{ u8 mode, u8, u8, u8 }`. The panel maps its current quest phase into
  the mode byte. This is the quest-**step** opcode and is **distinct** from the unified
  quest accept/proceed/give-up opcode **2/28** specified in `specs/quests.md §4`.
  `CODE-CONFIRMED` (the opcode, the 4-byte size, and the distinction from 2/28); the
  mode-byte phase map is `LIKELY`.
- KIND 0x23 opens **QuestItemKeepPanel** (slot 262/214) by emitting **C2S 2/143**, a
  4-byte open request. `CODE-CONFIRMED`.

### 8.4 No teleport / warp opcode — NPC travel is server-resolved

**No dedicated client teleport / warp opcode exists** on the interaction path. NPC-driven
map travel routes through the **generic interact (2/19)** and is resolved server-side (the
area-change mechanism). The two-mode service NPC (KIND 0x1F / 0x21, slot 257) toggles
caption sets but reuses the shop-list (template+128) source path — it is a rank /
duel-registration candidate, **not** a teleporter (open question 6). Treat "click NPC to
travel" as: emit 2/19, let the server resolve the destination; do **not** look for a
client warp message. `CODE-CONFIRMED` (no warp opcode found).

---

## 9. NPC-lane C2S send map

Each NPC-service panel emits its action through a fixed send wrapper that writes the
`(major:minor)` into the frame header (see `opcodes.md` for the frame format) and appends
a fixed-length body. The NPC-lane rows recovered this pass:

| Opcode | Dir | Body size | Role (this lane) | Emitting panel |
|---|---|---|---|---|
| 2/19 | C2S | 12 | NPC interact / **shop-open** (also gather-open) | NpcPanel, GatherNpcPanel |
| 2/20 | C2S | 12 | NPC **sell** one item | inventory/merchant path |
| 2/100 | C2S | 1 | KIND 0x19 **"request open NPC"** (round-trip) | the router itself |
| 2/110 | C2S | 4 | **QuestNpcPanel step** request | QuestNpcPanel (slot 151) |
| 2/113 | C2S | 8* | **REPAIR commit** | RepairNpcPanel (slot 150) |
| 2/115 | C2S | 8* | sundry / product **BUY / order commit** | sundry shop, ProductPanel |
| 2/142 | C2S | 16 | **STORAGE deposit / withdraw** | KeepPanel (slot 191) |
| 2/143 | C2S | 4 | KIND 0x23 **quest-item-keep open** | QuestItemKeepPanel |
| 2/151 | C2S | 8* | ProductPanel **buy / confirm** | ProductPanel (slot 181) |
| 2/152 | C2S | 8* | ProductPanel **page / select** | ProductPanel (slot 181) |
| 2/153 | C2S | 8* | ProductPanel **action** | ProductPanel (slot 181) |

> Sizes marked `*` are inferred from the send wrapper's write-length argument and were not
> re-confirmed from the immediate this pass; **12 / 16 / 4 / 1 are exact**. The
> `(major:minor)` tuples and the send-only nature are `CODE-CONFIRMED`; the body *fields*
> inside each are `UNVERIFIED` until a capture confirms them.
>
> **2/19 and 2/20** bodies are already specified in `specs/inventory_trade.md §7`. **2/28**
> (unified quest action) is in `specs/quests.md §4`. Pair **storage 2/142** with the
> still-unidentified storage-snapshot S2C (open question 2).

> **Catalog / packet-YAML status.** As of this pass the NPC-lane minors **2/100, 2/110,
> 2/113, 2/115, 2/142, 2/143, 2/151, 2/152, 2/153** are documented here but are **not yet
> rows in `opcodes.md`** and have **no dedicated `packets/*.yaml`** field specs. Promotion
> handoff (§11): add catalog rows and (where a body is worth pinning) YAML stubs —
> notably `2-142_storage_op.yaml` (the only fully-sized 16-byte body, §7.2) and a
> `2-110_quest_npc_step.yaml` (4-byte). 2/19 / 2/20 already have catalog context via the
> 4/19 / 4/20 ack rows.

---

## 10. Constants recovered (numeric reference)

| Constant | Meaning | Grade |
|---|---|---|
| KIND at descriptor +0x22 (u8) | interaction dispatch key (§2.2) | CODE-CONFIRMED |
| JOB at descriptor +0x34 (u16) | quest-giver rank band selector | CODE-CONFIRMED |
| 117 panels total; service slots 149–155, 181, 191, 215/216/218 | pre-built UI panel inventory (§4) | CODE-CONFIRMED |
| Storage capacity = 60 slots | KeepPanel open loop `i < 60` (§7.1) | CODE-CONFIRMED |
| KeepPanel block = 240 B (0xF0) ×4 | four tabs of 15 × 16 B slots (interpretation) | PLAUSIBLE |
| Sundry shop = 6-entry catalog at template +128 | static, baked into npc.scr (§5.1) | CODE-CONFIRMED |
| Repair list = N × 16-byte records at template +128, 6/page | same +128 region, different stride (§6.1) | CODE-CONFIRMED |
| Price divisor = 1,000,000 | gold/cash amount → displayed price | CODE-CONFIRMED |
| 2/142 body = 16 bytes; `op = action − 7`; `amount > 0` to send | storage deposit/withdraw (§7.2) | CODE-CONFIRMED (size); PLAUSIBLE (fields) |
| 2/110 body = 4 bytes `{mode,_,_,_}` | quest-NPC step (§8.3) | CODE-CONFIRMED (size); LIKELY (mode map) |
| 2/100 body = 1 byte | KIND 0x19 open request (§9) | CODE-CONFIRMED |
| Bag-count gate: KIND 5 > 30, KIND 9 > 50, KIND 15 > 5, KIND 0 (a2=75) > 75 | per-kind open thresholds (§9 gate) | CODE-CONFIRMED |
| JOB 2549→rank1 … 2553→rank5 | quest-giver required-rank band (§2.3) | CODE-CONFIRMED |
| Message ids 63007–63010 | quest-giver "need rank N" notices (CP949) | CODE-CONFIRMED |
| Message ids 65005–65014 | repair price / eligibility notices (CP949) | CODE-CONFIRMED |
| Message id 49045 | KIND 0x16 fame/confession-service gate notice (CP949) | CODE-CONFIRMED |
| Message ids 45015 / 45016 | shop gold-line / shop item-line format (CP949) | CODE-CONFIRMED |

### 10.1 Per-kind open gate (bag-count, not level)

Opening a service panel is gated by a **bag-count gate**, not a level gate: the client
reads the current inventory item count and refuses to open when it exceeds a per-kind
threshold (showing a notice and aborting client-side). The recovered thresholds:

| KIND | Threshold | Service |
|---|---|---|
| 5 | 30 | merchant shop |
| 9 | 50 | storage / bank |
| 15 | 5 | (guild / gather variant) |
| 0 (with the 75-arg call) | 75 | generic |

This is why opening a shop or storage with a near-full bag is refused **client-side**
before any send. `CODE-CONFIRMED`.

---

## 11. UNVERIFIED / open questions

1. **2/142 `op`-byte enum.** `op = widget action − 7` distinguishes deposit / withdraw /
   move, but which value is which is unproven without the widget-id base or a capture.
2. **Storage-snapshot S2C.** Deposit/withdraw out (2/142) is confirmed, but the storage
   *contents* appear to arrive through the shared bag item-panel ack family routed to the
   storage view; no dedicated storage-list S2C minor was isolated. Confirm whether storage
   reuses the bag item-panel chunks with a storage-view flag or has its own minor.
3. **NPC-template +128 field widths.** The (item_id, amount/cost, qty) layout is read from
   the accessors, but the exact on-disk field sizes/order and whether the 6-entry shop
   catalog and the N×16 repair list are the same physical bytes (read two ways) or two
   adjacent sub-tables need an `npc.scr` sample (asset analyst; see `structs/npc.md`).
4. **2/113 / 2/115 / 2/151–153 body layouts.** Confirmed by `(major:minor)` and emitting
   panel, but the body bytes were not field-decoded this pass (the args are
   `(target, index)`-shaped). Decode against a capture before pinning fields.
5. **NPC spoken-dialog source.** Whether one of the `npc.scr` string sub-fields is a
   spoken greeting line is unconfirmed; the on-click panels render fixed atlas + shop/quest
   data and did not visibly pull a greeting line. Needs an `npc.scr` sample.
6. **Two-mode service NPC (KIND 0x1F / 0x21, slot 257).** Reuses the template+128 list
   source and toggles caption sets — a rank / duel-registration candidate, **not** a
   teleporter. Needs the `npc.scr` KIND/JOB decode to label it precisely.
7. **No capture this pass.** Every body field offset/size is a static inference; the
   `(major:minor)` routing, the panel-slot table, the catalog-source paths and the gate
   thresholds are `CODE-CONFIRMED`, but every body table should be byte-confirmed against a
   live capture before an engineer hard-codes its fields.

---

## 12. Cross-references

| Document | Relationship |
|---|---|
| `Docs/RE/specs/inventory_trade.md` | Owns 2/19 interact-open (12 B), 2/20 sell (12 B), the 20-slot item model, and the 4/19 / 4/20 acks. This spec adds the rest of the NPC-lane minors. |
| `Docs/RE/specs/quests.md` | Owns the quest-giver path and the unified quest-action opcode 2/28. This spec **completes** its partial KIND→panel table (§2.2) and adds the distinct quest-NPC *step* opcode 2/110. |
| `Docs/RE/structs/npc.md` | The `mobs.scr` / `npc.scr` template and `npc.arr` spawn records; the +128 shop/repair catalog region lives inside the NPC template. |
| `Docs/RE/opcodes.md` | The wire frame header and `(major:minor)` convention; the NPC-lane minors here are flagged for promotion into the catalog (§9). |

- **Glossary:** see `Docs/RE/names.yaml`
- **Provenance:** see `Docs/RE/journal.md`
