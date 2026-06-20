---
status: confirmed
sample_verified: partial
verification: re-verified against doida.exe IDB SHA 263bd994, CYCLE 7 (2026-06-20); confirmed (client-side routing/sizes/offsets/control-flow); capture/debugger-pending (server-authored quest values + on-wire VALUE meanings)
ida_reverified: 2026-06-20
ida_anchor: 263bd994
evidence: [static-ida]
conflicts:
  - "2/110 quest NPC step is unsupported by static evidence; the real second quest channel is 2/152 (capture/protocol-pending)."
---

# Quest & NPC Dialog — Clean-Room Specification

> **Verification banner — re-verified against doida.exe IDB SHA 263bd994, CYCLE 7 (2026-06-20)** (static IDA only — no live capture/debugger this pass).
> - **[confirmed]** (control-flow-confirmed at the handler/sender/panel/loader sites on `263bd994`): the opcode routing (2/28 C2S, 5/68 + 5/73 S2C, 2/152 second C2S channel), the 2/28 **12-byte** body, the 5/68 **452-byte** parse + every snapshot offset, the 5/73 **344-byte** parse, the accept-gate **threshold 26** and its branch polarity, the give-up `abandonable` gate, the quest-log window action map, the runtime quest-record gate-field map, the **single 4960-byte (0x1360) quest-record stride** (on-disk == runtime, loader-proven), the eligibility status-state evaluator gate chain, the requirement-type → notice-id taxonomy, the **240-byte objective sub-record stride**, and that **5/73 = SmsgQuestComplete** (the completion reward-result panel; GuildWar refuted).
> - **[static-hypothesis]** (single inference, not control-flow-locked): the 2/28 body field split beyond the leading bytes, the 5/68 `+0` header / `+4` owner_id semantics, the meaning of `panel_b`/`panel_c`.
> - **[capture/debugger-pending]** (server-authored magnitudes + on-wire VALUE meanings need a live capture): the 2/28 & 2/152 framed wire sizes and `quest_id` endianness, the **exact wire byte offsets of the ≤10-entry reward list inside the 5/73 body** (the client renders it, but the wire→panel field mapping is runtime-only), the exact provenance of the level-gate word, and the literal integer values of the eligibility status states (runtime-resolved globals).
> - **CYCLE 7 reversals vs the prior pass:** (1) the **3720-vs-4960 quest-record stride conflict is RESOLVED** — the loader reads/allocates/copies exactly 4960 bytes, so on-disk == runtime, byte-for-byte; the "3720 on-disk" figure is **REFUTED** and the dual-stride language is dropped. (2) the **objective sub-record stride is 240 bytes** (the earlier "48-byte sub-record" was a marker *field value* inside each block, not the stride). (3) **5/73 carries a ≤10-entry reward list** {icon, name[17], count} — it is not a pure display blob. (4) the full quest-record byte layout now lives in `structs/quest_record.md`.
> - **Conflicts:** the mission's **2/110 quest NPC step** has no static support — the real second quest channel is **2/152**.

> Neutral, rewritten behavioural specification. No legacy symbols, no binary
> addresses, no decompiler pseudo-code. Describes the *observed behaviour*, state
> model, opcode tuples, data-table linkage and constants of the legacy client's
> NPC-interaction, quest-dialog, quest-log and reward-verdict subsystems, so the
> .NET core can be reimplemented from scratch.
>
> **Update (2026-06-13) — world lane.** This pass adds the in-game **quest-log
> window** (a three-tab browser with per-tab list + detail renderer), the
> **two distinct sources of quest text** (UI captions/headers/notices from a
> message-string database vs. quest dialogue body from the NPC-script records),
> the **install-table confirmation** of the quest opcodes, a **second C2S quest
> channel** observed but not yet opcode-traced, and the **sample-verified record
> layouts** of the quest-data tables (`quests.scr`, `autoquestion_cl.scr`,
> `npc.scr`, `discript.sc`). The detailed byte/stride tables for those data files
> live in `formats/config_tables.md`; this spec keeps them behavioural and links
> across. New material is in §§15–19; the quest-template record size and the
> `npc.scr` string-slot description are **corrected** inline where noted.
>
> Scope: the NPC click → panel dispatch flow, the unified C2S quest-action request,
> the two S2C quest-state pushes (quest-log snapshot, completion verdict), and the
> on-disk data tables that feed quest/NPC text. Reward *granting* is server-side;
> the client only renders the verdict and reacts to side-channel item/exp/gold
> updates — those side-channel opcodes are specified elsewhere (`inventory_trade.md`,
> `combat.md`, `handlers.md`) and are referenced, not restated, here.

## Status & confidence

- **Re-verified against doida.exe IDB SHA 263bd994, CYCLE 7 (2026-06-20) (static IDA only).**
  The opcode routing, the message body **sizes** (2/28 = 12 B, 5/68 = 452 B, 5/73 = 344 B),
  every 5/68 / 5/73 parse offset, the **single 4960-byte quest-record stride** (loader-proven),
  the eligibility evaluator gate chain, and the **240-byte objective sub-record stride** were read
  directly at the loader / handler / sender / panel / evaluator sites and are **CONFIRMED** for the
  client side. **No live network capture or debugger was used this pass:** the on-wire *framed*
  total sizes, `quest_id` endianness, the **exact wire offsets of the 5/73 reward list**, and any
  server-authored quest *values* (reward magnitudes, level thresholds beyond the literal compares
  below) remain **capture/debugger-pending**.
- **The full quest-record byte layout now lives in `structs/quest_record.md`** (the 4960-byte
  record: identity / name / dialogue handles / the objective sub-array @ +0x68 with its 240-byte
  stride / the high-region gate fields @ +0x1329..+0x135C). This spec keeps the quest record
  *behavioural* and links across.
- **Opcode → handler routing is a hard static fact.** The `(major:minor)` tuples
  below were observed at the client's dispatch / send sites; their *direction* and
  *family* are reliable. The message body **sizes** are now confirmed; the per-field
  *meanings* of undecoded blocks are the hypotheses.
- **Per-claim confidence** is tagged inline as `CONFIRMED` (proven at the relevant
  code site on `263bd994` or cross-checked against an existing committed spec),
  `LIKELY` (single consistent site), or `UNVERIFIED` (inferred / boundary not pinned).
- **Korean text fields use the CP949 (EUC-KR) code page.** All inbound and on-disk
  text in this subsystem (NPC names, quest names, dialog/objective lines, tutorial
  text) must be decoded as CP949, never UTF-8.
- Builds on / cross-references: `formats/npc_spawns.md` (the 28-byte `.arr`
  placement record), `structs/actor.md` (the actor descriptor whose NPC-classifier
  fields are reused here), `opcodes.md` and `inventory_trade.md` (the NPC-merchant
  request/ack family that shares the same NPC dispatcher).
- A consolidated **UNVERIFIED / open-questions** list is in §13. Do not implement any
  field tagged `UNVERIFIED` without first widening capture evidence or asking for an
  analyst cross-check.

---

## 1. Subsystem overview

Quest and NPC interaction in this client is **panel-driven and server-authoritative**:

1. The world is populated from a per-area **NPC placement array** (`.arr`, 28-byte
   records — see `formats/npc_spawns.md`) plus an **NPC definition table** (`npc.scr`,
   404-byte records) loaded into an NPC template lookup map. The placement record
   carries the index used to bind a wire-spawned NPC back to its placement slot.
2. Clicking an NPC runs a central **NPC-interaction dispatcher** that switches on the
   NPC's **KIND** classifier byte and opens the matching service panel (shop, repair,
   guild, gather, confession, quest dialog, etc.).
3. The quest-dialog panels read static quest text from a **quest template table**
   (`quests.scr`, 4960-byte records) via a quest lookup map. On a dialog button press
   they emit a single unified **C2S quest-action** message whose first payload byte
   selects accept / proceed / give-up.
4. The server pushes a full **quest-log snapshot** (S2C) and a **quest-completion +
   reward verdict** (S2C). The client mirrors these into a client-side quest-log state
   object and plays reward / failure audio.
5. Tutorials are a sibling system (`Tutor.scr` definitions + `tutor.lua` panel layout /
   localized strings) — summarised in §12, full Lua linkage in `lua_scripting.md`.

### 1.1 Quest state model — there is no client progress FSM

There is **no per-quest accept / in-progress / completed runtime FSM enum on the client**. Quest
*progress* is **server-authoritative** and arrives in the quest-log snapshot (5/68, §6) — *which
table / tab* a quest lands in (ACTIVE / COMPLETABLE / AVAILABLE) **is** the effective progress
state. What the client owns instead is an **eligibility / status-color state machine** that grades
each *available* quest through the gate chain in §15.7 into a **~13-state status set**, mapped to
localized labels (message ids **18501..18515**; case 2 and case 15 share **18503**). The literal
integer values of those status states are **runtime-only** (globals initialised once at startup).

The recoverable client-side **state vocabulary** is therefore:

```
not-tracked  →  available (gradeable)  →  active  →  completable  →  (verdict: granted | denied)
```

- **available (gradeable):** ungraded/graded by the eligibility evaluator (§15.7).
- **active / completable:** driven by the 5/68 snapshot plus two adjacent runtime `u16[]` active-id
  scratch tables (one ACTIVE range, one for the "same-id already active" gate / COMPLETABLE detection).
- **granted | denied:** the completion verdict arrives as 5/73 (§7).
- There is **no client "failed" enum** — the give-up / abandon path (`sub_action = 4`) simply clears
  the local active-quest marker. CONFIRMED (no failed-state byte exists).

---

## 2. Opcodes touched by this subsystem

All opcodes are written as `major/minor` tuples; direction is from the **client's**
point of view (`C2S` = client→server, `S2C` = server→client). Payload sizes and
field offsets are **payload-relative** (relative to the first payload byte, not
counting the frame header), matching `opcodes.md` convention.

### 2.1 Quest-specific opcodes

| Opcode | Dir | Size | Proposed name | Role | Conf (routing) |
|---|---|---|---|---|---|
| 2/28 | C2S | 12 (body) | `CmsgQuestAction` | Unified quest-dialog action (accept / proceed / give-up). See §4. | CONFIRMED |
| 2/152 | C2S | 8 (body) | quest list-row request | Second quest channel — AVAILABLE-tab list-row request (two u32 args). See §4.4. | CONFIRMED |
| 5/68 | S2C | 452 | `SmsgQuestList` | Full quest-log snapshot push. See §6. | CONFIRMED |
| 5/73 | S2C | 344 | `SmsgQuestComplete` | Quest-completion + reward verdict. See §7. | CONFIRMED |

> **2/28 is a C2S opcode** not previously in the catalog. Family 2 (game-action) is
> entirely client-emitted; there is no inbound major-2 handler. The outbound
> send-builder hard-codes the major and the minor (28) and appends a **fixed 12-byte
> body** (see §4).
>
> **2/152 is the "second quest channel"** that earlier passes recorded as *untraced*
> (the old §13a/§15.5 open item). It is now RESOLVED: a distinct C2S send-builder
> emitting a **two-u32 body**, fired only from the quest-log window's AVAILABLE-tab
> list-row actions (see §4.4, §15.5). The earlier mission note of a "2/110 quest NPC
> step" has **no static support** — there is no quest send-builder for minor 110
> (2/152 = 0x98, 2/110 = 0x6E are distinct minors); the real second channel is 2/152.
> This is flagged as a conflict for protocol/capture re-confirmation (§13a).

### 2.2 Related opcodes consumed via the shared NPC dispatcher (already cataloged)

The NPC-merchant branches of the same dispatcher drive the item/shop family, which is
specified in `inventory_trade.md`. Listed here only to show the linkage:

| Opcode | Dir | Role here |
|---|---|---|
| 4/19 | S2C | NPC shop buy / acquire ack (merchant KIND). |
| 4/20 | S2C | NPC sell-item ack. |
| 4/21 | S2C | NPC shop slot-clear ack. |
| 3/8  | S2C | Shop page update (opened from the merchant dispatch branch). |
| 4/148 | S2C | Playtime-reward result (driven by `playtime_reward.scr`). |

Reward side-channels: a granted quest's items/exp/gold arrive through the existing
item-panel, exp-gain and gold-balance opcodes (see `inventory_trade.md` and `combat.md`),
**not** through 5/73. The 5/73 push is a UI verdict only (§7).

---

## 3. NPC interaction model (KIND → panel dispatch)

When the player clicks an NPC, a central **interaction dispatcher** runs. It records
the clicked NPC's descriptor and id, then switches on the NPC's **KIND** classifier
and opens the corresponding service panel by toggling that panel's visibility.

### 3.1 NPC classifier fields (read from the actor descriptor)

The NPC actor descriptor (see `structs/actor.md`) exposes three classifier fields
used by the dispatcher and the NPC template lookup:

| Field | Type | Role | Conf |
|---|---|---|---|
| `Name` | CP949 string | NPC display name. | CONFIRMED |
| `Kind` | u8 | Primary dispatch key — selects which service panel opens. ~35 distinct values are handled. | CONFIRMED |
| `Job`  | u16 | Secondary classifier (logged alongside Kind/Id; finer role/sub-class). | CONFIRMED |

> The exact byte offsets of these fields belong in `structs/actor.md`; do not hard-code
> them from this spec. The diagnostic the client logs for each interacted NPC is
> `Job[d] Kind[d] Id[d] Name[s]` (CP949 name), which is the evidence that all three are
> read together.

### 3.2 Dispatch behaviour by KIND

The dispatcher handles roughly 35 KIND values. Only a subset are mapped to a named
panel this pass; the precise KIND value for the **quest-giver** NPC is **not pinned**.

| KIND group | Behaviour | Conf |
|---|---|---|
| 1, 2, 3, 4, 6, 7, 11, 24 | Return immediately — no panel opens. | LIKELY |
| 5, 8, 9 (0x0A), 14 (0x0E) | Open a specific service panel (the merchant / repair / guild / etc. family). | LIKELY |
| 15 (0x0F), 17 (0x11), 18–21 (0x12–0x15) | Open further service panels via helper toggles. | LIKELY |
| 22 (0x16), 23 (0x17) | Open two further distinct panels. | LIKELY |
| 25 (0x19) | Send a 1-byte "request to open" round-trip to the server (the panel is opened on the server's reply rather than locally). | LIKELY |

> **UNVERIFIED:** which numeric KIND is the quest-giver versus the named service NPCs
> (the client carries distinct artwork handles for guild / confession / product /
> helper NPCs, but the KIND→artwork mapping is not resolved). The quest-dialog panels
> (§5) are opened from this dispatcher and/or from the quest-list flow; the exact KIND
> entry point is open. See §13.

### 3.3 Event-gating hook

One placement-record path can promote an NPC's dialog kind from a default selector to
an alternate (event/tutorial) selector when a record condition holds together with two
global event flags. This is an **event/tutorial gating hook**; its precise trigger
condition is `UNVERIFIED` and not required for the baseline quest flow.

The event/spawn-rate definitions that feed this gating live in the on-disk **`events.scr`**
table — a fixed-record file of **520-byte records** with this confirmed shape (the full
stride/field byte table is owned by `formats/config_tables.md`; the offsets are recorded
here only as the interoperability facts that link the event hook to the data):

| Off | Type | Field | Meaning | Conf |
|----:|------|-------|---------|------|
| +0x00 | u32 | `event_id` | Event identifier (map key). | CONFIRMED |
| +0x64 | u32 | `mode_flag` | Event mode / enable flag. | CONFIRMED |
| +0x68 | u32[50] | `rate_array` | 50 spawn/rate weights. | CONFIRMED |
| +0x130 | u32[52] | `actor_array` | 52 actor/spawn references. | CONFIRMED |

> The `events.scr` **record size (520 B)** and the field offsets above are CONFIRMED on
> `263bd994`; they are an on-disk data-table layout, not a wire packet. The server-authored
> *values* in the rate/actor arrays (which actors spawn at what rate per event) are
> data-driven and capture/debugger-pending at the gameplay level. See
> `formats/config_tables.md` for the full byte table.

---

## 4. C2S quest-action request (2/28)

A single unified message carries all three quest-dialog button actions. The send
builder writes `major = 2`, `minor = 28`, and appends a **fixed 12-byte body** (the
send copies exactly `0xC` bytes from a stack struct). Three call sites send
`sub_action` 2 / 3 / 4.

### 4.1 Body layout (12 bytes — body size CONFIRMED on 263bd994; field split static-hypothesis)

| Off | Type | Field | Meaning | Conf |
|----:|------|-------|---------|------|
| +0 | u8  | `sub_action` | Selects the quest action — see table 4.2. | CONFIRMED |
| +1 | u8  | `npc_kind`   | The NPC KIND / slot byte taken from the active dialog panel instance. | CONFIRMED (is a panel u8); static-hypothesis (that it is the KIND) |
| +2 | u32 | `quest_id`   | The active quest id (from the local player/selection state). | CONFIRMED |
| +6 | u8[6] | `reserved` | Trailing reserved bytes (within the 12-byte body); not individually populated, assumed zero/alignment. | static-hypothesis |

**The body is exactly 12 bytes** *(corrected 2026-06-16: the earlier "6 logical bytes
packed into a reserved frame" was a static size-inference; the send appends `0xC`
bytes from a stack struct)*. The leading `{ u8 sub_action, u8 npc_kind, u32 quest_id }`
occupy the first 6 bytes; the remaining 6 are reserved/zero within the 12-byte body.

> **Capture/debugger-pending:** the exact *framed* total wire size (the 12 B is the
> appended body, not the full frame), the endianness of `quest_id`, the exact width of
> `npc_kind` on the wire, and whether the trailing reserved bytes are required to be
> zero. **Do not commit a `packets/2-28_*.yaml` until the framed total size is
> capture-confirmed.**

### 4.2 `sub_action` enumeration

| Value | Meaning | Sent from | Conf |
|------:|---------|-----------|------|
| 2 | **Accept** — accept the offered quest. | NPC quest-talk panel, "accept" widget (after gates pass). | CONFIRMED |
| 3 | **Proceed / continue** — advance an in-progress quest dialog. | NPC quest panel, when the panel's phase byte equals 2. | CONFIRMED |
| 4 | **Give up / abandon** — abandon the active quest. | Quest give-up panel, on confirm. | CONFIRMED |

> The three send sites are confirmed on `263bd994`: the accept path sends `(2, …)`, the
> proceed path sends `(3, …)`, and the give-up path sends `(4, …)`.
>
> **UNVERIFIED:** whether `sub_action` 0 and 1 exist (e.g. open / refuse). They were not
> observed at any send site; they may be server-only or unused.

### 4.3 Field sourcing (client-side state, for the implementer)

- `quest_id` always comes from the **active / selected quest id** held in the local
  player/selection state (the same value the give-up path clears on abandon). All three
  sends source it from the same selected-quest accessor. CONFIRMED.
- `npc_kind` comes from the **active dialog panel instance** (the panel stores the KIND /
  slot byte that opened it): the accept path passes the panel's instance byte; the
  give-up path passes the give-up panel's npc-kind byte. CONFIRMED (a panel u8).
- **Proceed** is sent only when the panel's **phase byte equals 2** (the confirm handler
  reads the panel phase byte and emits `sub_action = 3` only on `phase == 2`). CONFIRMED.
- On **accept**, the client applies a **level gate with an account/billing bypass** before
  sending. The branch is: the send is taken when a **billing/account bypass predicate is
  set** OR the level word is **below 26**; the **block-and-show-notice** path runs when the
  bypass is **clear** AND the level word is **>= 26**.
  *(corrected 2026-06-16 — POLARITY: the earlier text said "blocks when the value is below
  26"; that is the inverse of the code. `>= 26` is the **blocking** side, conditioned on the
  bypass being clear.)* The literal threshold **26** is CONFIRMED. The gated word is used as
  the **character level**: it is compared against the quest record's min-level and max-level
  fields in the eligibility evaluator (§10), strongly resolving the old "level vs cash/VIP"
  open question toward **level**. The exact raw provenance of the level word (raw level vs a
  derived status) is capture/debugger-pending.
- On **give-up**, after the send the client also clears its local active-quest marker state
  and re-highlights nearby mobs. CONFIRMED.

### 4.4 C2S 2/152 — second quest channel (AVAILABLE-tab list-row request)

The quest-log window's AVAILABLE-tab list-row "request" buttons drive a **second, distinct
C2S send-builder** (not 2/28). It is now RESOLVED on `263bd994` as **opcode 2/152** with a
**two-u32 body** (the builder appends two `u32` args; there is no fixed 12-byte form). It is
fired only from the quest-log window's AVAILABLE-tab row actions.

| Off | Type | Field | Meaning | Conf |
|----:|------|-------|---------|------|
| +0 | u32 | `arg0` | First request arg, sourced from the window's per-row table (28-byte row stride). | CONFIRMED (two-u32 body); static-hypothesis (field meaning) |
| +4 | u32 | `arg1` | Second request arg, sourced from a second per-row table (44-byte stride). | CONFIRMED (two-u32 body); static-hypothesis (field meaning) |

> This retires the earlier "untraced second channel" / "2/110" notes. Most plausibly a
> "request quest detail / claim row" round-trip, but the **semantic meaning of the two
> u32 args and the framed wire size are capture/debugger-pending**. Do **not** assume it
> reuses 2/28's body shape. See §13a and §15.5.

---

## 5. Quest-dialog panels and population

Several distinct quest-related UI panels exist. Their grouping (from the client's panel
classes) is summarised for the implementer; exact widget layout is presentation-side and
not required for the protocol core.

| Panel role | Purpose |
|---|---|
| NPC quest offer / list | Lists the quests a single NPC can offer; entry point for accept/proceed. |
| NPC quest talk / dialog | The accept-flow dialog (sends `sub_action = 2`). |
| NPC quest short message | Short message variant. |
| Quest log window | The player's quest-log window (mirrors the snapshot in §6). |
| Quest info / tracked list | Tracked-quest list. |
| Quest result | Result / objective-progress display (driven by §7). |
| Quest give-up confirm | Abandon-confirm dialog (sends `sub_action = 4`). |
| Quest-item keep / hand-in | Quest-item retention / turn-in. |
| Generic request / yes-no | Shared request and yes/no confirm dialogs. |

The over-actor and HUD quest markers (a quest icon and a quest-tracking marker) are
driven by quest-log state (§6).

### 5.1 Quest-dialog population

When a quest dialog opens it is populated from the **quest template** (the `quests.scr`
record for `quest_id`, see §8):

- The dialog reads up to **6 dialog / objective text lines** from the quest record's
  step list and writes them into the panel's text slots (CP949).
- The panel **title** is taken from the **localized string table id 18022**.
- The panel stores `npc_kind`, `quest_id` and a `mode` byte on its own instance (these
  feed the later 2/28 send).
- An **info-only** layout hides the accept buttons; the **offer** layout shows the
  accept controls and, for an **in-progress** quest, formats an objective counter as
  `"current / total"` (a 1-based current step over the total step count) into a panel
  field.

> `LIKELY` for the 6-line cap, the title-id 18022, and the "current / total" counter
> format. The internal layout of the per-step records is `UNVERIFIED` (§8).

---

## 6. S2C quest-log snapshot (5/68)

The server pushes a full **quest-log snapshot**. The client parses it into a client-side
quest-log state object, rebuilds the quest-log UI list, and refreshes the tracking HUD.
After parse, if the **active/tracking flag** transitions 0 → non-zero, the client opens
the tracking panel and plays the **quest-tracking-on** sound (`862300001`); a non-zero →
0 transition closes the panel.

### 6.1 Wire payload layout (452 bytes — every offset CONFIRMED against the parser on 263bd994)

Offsets are payload-relative. The mirror-copy routine was fully read on `263bd994`; the
handler reads exactly `0x1C4` (452) bytes into a stack block and every offset below matches
the parser's reads. The exact name stride (17) and the per-entry loops are confirmed.

| Off | Type | Field | Meaning | Conf |
|----:|------|-------|---------|------|
| +0   | u32 | `header` | Header / reserved — **not read** by the mirror-copy (`+0..+7` untouched there). | static-hypothesis |
| +4   | u32 | `owner_id` | Actor / owner id — not read by the mirror-copy; meaning undecoded. | static-hypothesis |
| +8   | u8  | `panel_c` | Stored into quest-log state (UI selector C). | CONFIRMED (stored); static-hypothesis (meaning) |
| +9   | u8  | `tracking_flag` | Active / tracking flag — this is the flag the handler diffs to open/close the tracking panel. | CONFIRMED |
| +10  | u8  | `panel_b` | Stored into quest-log state (UI selector B). | CONFIRMED (stored); static-hypothesis (meaning) |
| +11  | u8[10]  | `slot_a_flags` | One flag per tracked slot → slot-A table (10-entry loop). | CONFIRMED |
| +21  | u8[10]  | `slot_b_flags` | One flag per tracked slot → slot-B table (10-entry loop). | CONFIRMED |
| +32  | u32[20] | `quest_ids` | 20 quest ids (parallel loop into the quest-entry table). | CONFIRMED |
| +112 | char[20][17] | `quest_names` | 20 quest names, CP949, stride **17** (16 chars + forced NUL written at index 16). | CONFIRMED |

Summed: 4 + 4 + 1 + 1 + 1 + 10 + 10 + 80 + 340 = 451 bytes of described content within a
452-byte payload (one trailing/alignment byte unaccounted — treat as reserved until a
capture confirms the exact field span).

> **Total payload size 452 (0x1C4) is CONFIRMED** from the parser's read extent on
> `263bd994`. The name stride (**17**) and all `+8..+451` field offsets are confirmed
> against the parser reads. Still **capture/debugger-pending:** the endianness of
> `quest_ids` on the wire, and the semantics of `header` (+0), `owner_id` (+4) and
> `panel_b`/`panel_c` (the latter three are stored/untouched, not interpreted by the
> client this pass).

### 6.2 Client-side quest-log state model

The parser mirrors the snapshot into a local quest-log state object. The implementer can
model it as the following logical tables (exact base offsets are an internal layout
detail — model the *shape*, not the legacy offsets):

- **Slot-A table:** 10 entries, **32-byte stride**; each entry stores its flag.
- **Slot-B table:** 10 entries, **32-byte stride**; each entry stores its flag.
- **Quest-entries table:** 20 entries, **32-byte stride**; each entry stores a `quest_id`
  (u32, in-memory at entry `+8`) and the CP949 quest name (in-memory at entry `+12`).
- **Scalar fields:** `tracking_flag` (u8), `panel_b` (u8), `panel_c` (u8).
- Two small slot-table header blocks (two 12-byte blocks) are zero-cleared on every refresh.

> The in-memory mirror uses a **32-byte entry stride** (id at entry `+8`, name at entry
> `+12`); this is the *in-memory* stride and is distinct from the **17-byte wire name
> stride** of §6.1. Both are confirmed on `263bd994`; model the shape, not the literal
> offsets.

> Implementer note: model this as `QuestLogEntry[20]` (id + name) plus two
> `bool[10]`/`byte[10]` flag tables and three scalar selectors. After applying a snapshot,
> rebuild the quest-log list view and refresh the tracking HUD. The parallel "name table"
> stride implies the id table and name table are kept index-aligned (entry *i* in
> `quest_ids` pairs with entry *i* in `quest_names`).

---

## 7. S2C quest-completion + reward verdict (5/73)

The server pushes a **quest-completion verdict**. The client acts on it only when an
**apply** flag is set, then branches on a **reward-state** byte to show a result panel
and play audio. **The reward itself (items / exp / gold) is applied server-side**; this
push is the UI verdict, not the reward payload.

### 7.1 Wire payload layout (344 bytes — size + read fields CONFIRMED on 263bd994)

Only two leading fields are read by the handler; the remainder of the body is copied
wholesale into the result panel. The handler reads exactly `0x158` (344) bytes and acts
only when `apply == 1`.

| Off | Type | Field | Meaning | Conf |
|----:|------|-------|---------|------|
| +0  | u8[8] | `header` | Leading header / context (not individually decoded; `+0..+7` not read by the handler). | static-hypothesis |
| +8  | u32 | `apply` | Apply selector — the handler acts only when this equals **1**. | CONFIRMED |
| +12 | u8  | `reward_state` | Verdict — see table 7.2. | CONFIRMED |
| +13 | u8[331] | `body_remainder` | Remaining bytes, copied wholesale into the completion result panel. **CYCLE 7: this region carries a ≤10-entry reward list** (see §7.3); the client renders it (icon / name / count), but the exact wire byte offsets of the reward records are **capture/debugger-pending**. | capture/debugger-pending (per-field offsets) |

Summed: 8 + 4 + 1 + 331 = 344 bytes (0x158). **The total size (344) and the read of
`apply` (+8) and `reward_state` (+12) are CONFIRMED** on `263bd994`. **CYCLE 7 correction:**
the body remainder is **not** a pure display blob — the completion result panel renders it as a
**reward list of up to 10 entries** (§7.3). The handler itself only reads `apply` and
`reward_state` and copies the full body to the panel; the panel does the reward-list render. The
**exact wire byte offsets** of the 10 reward records inside the 344-byte body are
**capture/debugger-pending** (the panel reads them from its own copied storage; the wire→panel
field mapping needs a live capture).

### 7.2 `reward_state` verdict

| Value | Meaning | Client reaction | Conf |
|------:|---------|-----------------|------|
| 1 | **Grant** | Call the actor-manager hook, then **play** the completion sound (`910036000`, positive variant). | CONFIRMED |
| 2 | **Deny / fail** | **STOP** the matching completion sound (`910036000`) — it does **not** play a negative cue. | CONFIRMED |

> *(corrected 2026-06-16)* The earlier text said `reward_state == 2` "plays the negative
> variant of `910036000`". That is wrong: on `263bd994` the deny path **stops** the
> matching sound id rather than playing anything. Only the grant path (`reward_state == 1`)
> plays a sound.

In both apply cases the full 344-byte body is handed to the completion-result panel.

### 7.3 Completion-result panel behaviour (reward-result panel — MainWindow slot 224)

The 5/73 handler forwards the full 344-byte body to a **quest-completion reward-result panel**
(MainWindow slot **224**); it is the **sole writer** of that panel. The panel's behaviour proves
the QuestComplete attribution (see §7.5):

- The panel **double-buffers** the previous body and copies the new 344-byte body into
  its own storage (two 344-byte block copies — old ← stored, then stored ← new).
- It tracks a **phase** byte: `0 = idle`, `1 = showing result (open)`, `2 = closed`. On
  `phase == 1` it builds + shows (open-animation slot); on `phase == 2` it runs the
  close animation.
- When the **previous body's `+12` byte (the `reward_state`) equals 1** and the phase is
  idle, it advances to phase 1 and **auto-shows** the result panel (open animation), with
  the close animation on dismiss.
  *(corrected 2026-06-16: the auto-show key is the prior body's `+12` reward_state field,
  not its `apply` byte — confirmed on `263bd994`.)*
- Result captions use **localized string table ids 309 and 618**.
- **CYCLE 7 — reward list.** The panel renders a **reward list of up to 10 entries**, each
  `{ icon, name, count }`:

  | Reward row element | Render | Conf |
  |---|---|---|
  | Reward **icon / button** | per-entry handle → reward-row button builder (texture `data/ui/moonpa.dds`). | CONFIRMED |
  | Reward **name / label** | name field, **17-byte stride**. | CONFIRMED |
  | Reward **count / amount** | per-entry numeric value, rendered as decimal text. | CONFIRMED |
  | Entries iterated | up to **10** rows. | CONFIRMED |

  So, as the client consumes it, the completion reward record is a 10-slot table of
  `{ icon-handle, name[17], u32 count }` carried inside the 344-byte body remainder. The
  client renders ≤10 reward rows (CONFIRMED); their **precise wire offsets are
  capture/debugger-pending** (§7.1). No per-row "select reward" round-trip was found, so the
  list appears to be a flat *granted* list, not a pick-one choice set (UNVERIFIED).

### 7.4 Reward delivery (cross-reference)

The actual reward **grant** (the inventory / exp / gold changes) is **server-authoritative**.
After a grant verdict the client receives the concrete item/exp/gold changes through the existing
side-channel opcodes (item-panel updates, exp-gain, gold-balance — see `inventory_trade.md` and
`combat.md`); 5/73 does **not** call any inventory / exp grant path. CONFIRMED. The 5/73 body
remainder **does carry a reward list** (§7.3), but that list is **display data** the result panel
renders — the authoritative granting still happens via the side channels.

### 7.5 5/73 = SmsgQuestComplete (GuildWar refuted)

**CYCLE 7 disambiguation.** 5/73 is the **quest-completion verdict** `SmsgQuestComplete`, **not**
a guild-war info update. The consumer panel (MainWindow slot 224) is unambiguously a
quest-completion reward-result panel: it double-buffers the 344-byte body, uses the quest-result
captions (message ids 309 / 618), auto-shows when the prior body's `reward_state` (+12) == 1,
plays the quest-completion sound family, and renders a reward list of ≤10 `{icon, name, count}`
rows. Nothing about it touches guild / war state or guild opcodes, and the 5/73 handler is the
panel's only writer. **The "SmsgGuildWarInfoUpdate" alternative is REFUTED.** (The opcode catalogue
and packet YAMLs are owned by other lanes; this spec records the fact and references 5/73 by name.)

---

## 8. Quest template table — `quests.scr`

Static quest text/definition data lives in an on-disk fixed-record table loaded into a
quest template lookup map keyed by `quest_id`. This is the single static-quest-data
access point used across the quest-dialog cluster.

| Property | Value | Conf |
|---|---|---|
| Logical path | `data/script/quests.scr` | CONFIRMED |
| Record size | **4960 bytes (0x1360), fixed** — on-disk == runtime, byte-for-byte (loader-proven) | CONFIRMED |
| Lookup key | `quest_id` (sparse `u16`, range 1..617; the file is a sparse flat array, not index-keyed — the runtime keys a map on the id; a slot is empty when its leading `u16` quest id is 0) | CONFIRMED |
| Title | Localized message-string id **18022** (not stored inline as text) | LIKELY |
| Full byte layout | See **`structs/quest_record.md`** (4960-byte record offset table) | CONFIRMED |

> **Single stride 4960 (0x1360) — the 3720-vs-4960 conflict is RESOLVED (CYCLE 7).** The
> `quests.scr` loader reads exactly **4960 bytes** per record from the VFS file, allocates a
> **4960-byte** runtime object, and copies **4960 bytes** verbatim into it before registering it —
> there is **no expansion, no padding, no field synthesis** between disk and runtime. The on-disk
> record *is* the runtime record, byte-for-byte. The earlier "**3720 bytes**" figure came from a
> black-box VFS byte measurement that disagreed with the loader; per the ground-truth doctrine the
> **binary loader wins**, and 3720 is **REFUTED**. There is **no "missing 1240 bytes"** — the
> high-region gate fields (the `abandonable` flag at +0x1329, min/max level at +0x1350/+0x1352, the
> class/stat/prereq gates at +0x1348..+0x135C, §15.7) are **authored on-disk data** read straight
> out of the loaded record, not runtime scratch. The full offset table is in
> `structs/quest_record.md`; the on-disk catalogue framing context is in `formats/config_tables.md`.

### 8.1 Known record fields

The byte layout of the on-disk record is now sample-verified and documented in
`formats/config_tables.md` (quest-data family). The on-disk view and the runtime
view differ in vocabulary — the code path reaches dialogue by *handle*, the sample
sees *offsets* — so both are recorded:

| Off | Type | Field | Meaning | Conf |
|----:|------|-------|---------|------|
| +0x000 | u16 | `quest_id` | Sparse quest id (1..617); 0 = empty slot. Map key. | SAMPLE-VERIFIED |
| +0x002 | char[≤32] | `quest_name` | CP949 quest name, null-terminated, in a ~62-byte name buffer ending by ~+0x3F. | SAMPLE-VERIFIED |
| +0x040 | u8[6] | `step_codes` | Six step/objective code bytes (e.g. `05 06 07 08 09 0A`); three distinct patterns observed; 100% occupancy. | SAMPLE-VERIFIED |
| +0x054 | u32 | `quest_ref_a` | Composite chain/category reference, decimal-digit encoded `730 R QQQQQ 04` (R = region/category, QQQQQ = quest id). | SAMPLE-VERIFIED (pattern) |
| +0x058 | u32 | `quest_ref_b` | Same composite, always `+1` from `quest_ref_a` (the `…05` sibling). | SAMPLE-VERIFIED |
| +0x064 | u32 | `quest_type_or_step_count` | Constant value 2 across the occupied records; meaning unresolved (quest type? step count?). | SAMPLE-VERIFIED (value) |
| +0x068 | objective_record[..] | embedded **objective sub-array** | Begins at +0x68 with a **240-byte stride** per objective sub-record (CONFIRMED at the ACTIVE-tab renderer); each sub-record carries a step-key word at sub-record +0x3E and a per-step reward-value array at sub-record +0x4C. Interiors otherwise UNVERIFIED. The on-disk view also sees CP949 objective/step description strings in this region. | CONFIRMED (stride/base); UNVERIFIED (interior) |

> **Objective sub-record stride is 240 bytes (CYCLE 7 correction).** The earlier "48-byte
> sub-record" reading was the recurring `u32 = 48` *field value* (a marker / type constant inside
> each block), **not** the stride — the real objective sub-array stride is **240 bytes**. The full
> record layout, the dialogue handle offsets (+0x54 offer / +0x58 turn-in), and the high-region
> gate fields are now consolidated in **`structs/quest_record.md`**.
>
> **The 240-byte objective sub-record interior is still UNVERIFIED.** Reward item ids and
> per-objective target counts (the kill / collect / talk action-type and its target NPC / item /
> count) are not yet field-decoded; the **client does not switch on an objective-action-type enum
> locally** (that taxonomy is data-driven inside the objective blocks and server-tracked). The
> title is a message-string id (18022), not inline text. Required level, the class/stat gates, and
> the prerequisite-quest chain have confirmed offsets in `structs/quest_record.md §4` (the same
> single 4960-byte record — there is no separate on-disk view to reconcile, §8).

---

## 9. Supporting NPC / placement data tables

### 9.1 NPC definition table — `npc.scr` / `npcs.scr`

| Property | Value | Conf |
|---|---|---|
| Logical path | `data/script/npc.scr` (sibling catalogue `npcs.scr`, stride 1916 — see `formats/config_tables.md`) | CONFIRMED |
| Record size | 404 bytes (0x194), fixed | SAMPLE-VERIFIED |
| Record count | 2510 entries (sequential `u32` id 1..2510 at +0, mirrored at +4) | SAMPLE-VERIFIED |
| String sub-fields | **Three CP949 description paragraphs at +0x14, +0x50, +0x90** *(corrected 2026-06-13: the earlier "6 × 64-byte string slots" was a static estimate; the sample shows three segmented paragraphs plus three `48`-marker sub-section boundaries at +0xD0/+0x110/+0x150)* | SAMPLE-VERIFIED |
| Empty-string convention | A string sub-field whose first character is ASCII `'0'` is treated as empty / cleared. | LIKELY |

These records carry the multi-paragraph NPC **class/archetype description text** the
client shows in UI panels. The quest dialogue **body** lines (the 6 lines populated
into a quest dialog) are reached through the quest record's dialogue handles and the
NPC-script dialog map; the body text store is the NPC-script record set — see §16 and
§17, and the byte tables in `formats/config_tables.md`.

> **`npc.scr` vs `npcs.scr`.** `npc.scr` (stride 404, 2510 records) holds the
> description text above; `npcs.scr` (stride 1916, 812 records) is the NPC catalogue
> proper (ids / quests / dialog links). The key linking the two ID spaces is an open
> item (§13). The actor factory uses an NPC template lookup keyed by the actor
> descriptor's NPC-id field; see `structs/actor.md` for the descriptor fields.

### 9.2 NPC placement array — `.arr`

The per-area NPC placement array is the 28-byte `.arr` record fully specified in
`formats/npc_spawns.md`. Quest/NPC linkage facts relevant here:

- The placement record's leading **id / key** field is the search key for placement
  lookups, and the same value is returned by the id-by-index accessor.
- The runtime prepends a zeroed sentinel record at slot 0; valid placement indices are
  ≥ 1 (see `formats/npc_spawns.md` for the load/indexing model).
- The placement array is loaded as the **last** file in a per-area load (after the
  map/region tables).
- The server's **area entity snapshot** (the area-entity update opcode in `handlers.md`)
  references NPCs **by placement-array index** — this is the binding between a
  wire-spawned NPC and its `.arr` placement slot. `LIKELY`.

> The other fields of the 28-byte `.arr` record are detailed (with their own confidence)
> in `formats/npc_spawns.md`; only the id/index linkage matters for the quest/NPC flow.

---

## 10. Constants recovered (numeric reference)

| Constant | Meaning | Conf |
|---|---|---|
| 2/28 body = 12 bytes | Fixed `0xC`-byte body appended by the send-builder *(corrected 2026-06-16: was "6 logical bytes")*. | CONFIRMED |
| 2/28 `sub_action` 2 / 3 / 4 | accept / proceed / give-up. | CONFIRMED |
| 2/152 body = 8 bytes (two u32) | Second quest channel (AVAILABLE-tab list-row request). | CONFIRMED |
| Accept gate threshold = 26 | Client **blocks** "accept" + shows a notice when (account/billing bypass clear) **AND** the level word is **>= 26**; the send goes through when the bypass is set **OR** the level word is **< 26** *(corrected 2026-06-16 — polarity)*. The gated word is used as character level (compared vs the record min/max level). | CONFIRMED (threshold + branch); capture/debugger-pending (raw provenance) |
| Quest-dialog max text lines = 6 | Up to 6 dialog/objective lines populated per quest dialog (6-label build loop confirmed). | CONFIRMED |
| Quest-name field = 16 chars + NUL (17 bytes) | Per-entry name **wire** stride in the 5/68 snapshot; CP949 (in-memory mirror uses a 32-byte stride). | CONFIRMED |
| Quest-log entries = 20 | 20 quest ids + 20 names in the snapshot and local table. | CONFIRMED |
| Quest-log slot flag tables = 10 + 10 | Two 10-entry flag tables (slot-A, slot-B). | CONFIRMED |
| 5/68 payload = 452 bytes (0x1C4) | Quest-log snapshot size. | CONFIRMED |
| 5/73 payload = 344 bytes (0x158) | Quest-completion verdict size. | CONFIRMED |
| 5/73 `apply` == 1 | Handler acts only when the apply selector (+8) is 1. | CONFIRMED |
| 5/73 `reward_state` 1 / 2 | grant (plays 910036000) / deny (**stops** 910036000) *(corrected 2026-06-16)*. | CONFIRMED |
| 5/73 phase 0 / 1 / 2 | result-panel phase: idle / showing / closed; auto-show keyed off the prior body's `+12` reward_state. | CONFIRMED |
| `quests.scr` = **4960-byte (0x1360) records**, single stride (on-disk == runtime, loader-proven) | Quest template table; full layout in `structs/quest_record.md`. The "3720 on-disk" figure is REFUTED (§8). | CONFIRMED |
| Quest-record objective sub-array @ +0x68, **240-byte stride** | Embedded objective sub-records (the "48" was a marker value, not the stride). | CONFIRMED |
| Quest-record gate fields @ +0x1329..+0x135C | abandonable / prereq / min&max level / accepted / class-race mask / sec-stat min&max / tertiary-stat (§15.7, `structs/quest_record.md`). | CONFIRMED |
| 5/73 reward list = ≤10 × {icon, name[17], count} | Completion result panel (slot 224) reward display; wire offsets capture-pending (§7.3). | CONFIRMED (render); capture-pending (wire offsets) |
| Status-state labels 18501..18515 (case 2 == case 15 == 18503) | Eligibility status-color grade labels (§15.7.1). | CONFIRMED |
| Requirement notices 18003..18071 | Requirement-type → notice taxonomy (§15.7.2). | CONFIRMED |
| `npc.scr` = 404-byte records, 2510 entries, **3 CP949 paragraphs @ +0x14/+0x50/+0x90** | NPC description table *(corrected 2026-06-13: was "6 × 64-byte strings")*. | SAMPLE-VERIFIED |
| `autoquestion_cl.scr` = 92-byte records, 1300 entries | Anti-bot arithmetic-quiz question pool (CP949 question @ +4). | SAMPLE-VERIFIED |
| `discript.sc` = 68-byte records, 33 entries | UI context-menu label table (u32 menu id @ +0, u32 category @ +4, CP949 label @ +8). | SAMPLE-VERIFIED |
| `Tutor.scr` = 1660-byte records, ~86 lessons | Tutorial definition table. | CONFIRMED (size); LIKELY (count) |
| String-table id 18022 | Quest-dialog title caption. CP949. | LIKELY |
| String-table ids 309 / 618 | Quest-result captions. CP949. | LIKELY |
| Sound id 862300001 | Quest-tracking-on SFX (tracking-flag 0→non-zero). | LIKELY |
| Sound id 910036000 | Quest-completion SFX — **played** on grant (`reward_state == 1`), **stopped** on deny (`reward_state == 2`); no negative cue (§7.2). | LIKELY |

> The "string-table ids" above are runtime **localized message codes** (CP949 strings),
> not opcodes and not enum values on the wire. Keep them as display constants only.

---

## 11. End-to-end flow (for the implementer)

```
Click NPC ─► interaction dispatcher switches on KIND
              ├─ service KIND ─► open shop/repair/guild/etc. panel (inventory_trade.md)
              ├─ KIND 25      ─► send 1-byte "open" request, panel opens on reply
              └─ quest KIND   ─► open quest-dialog panel (KIND value UNVERIFIED)
                                  │
   quest-dialog panel populated from quests.scr[quest_id] (title id 18022, up to 6 lines)
                                  │
        ┌── "accept"  (gate: send when bypass||level<26;   ─► C2S 2/28 (12B) { sub_action=2, npc_kind, quest_id }
   user │              blocked when !bypass && level>=26)
        ├── "proceed" (panel phase == 2)   ─► C2S 2/28 (12B) { sub_action=3, npc_kind, quest_id }
        └── "give up"  (confirm, abandonable set) ─► C2S 2/28 (12B) { sub_action=4, npc_kind, quest_id }
                                                  + clear local active-quest state
   (AVAILABLE-tab row request) ───────────► C2S 2/152 (8B) { u32 arg0, u32 arg1 }
                                  │
   server ─► S2C 5/68 QuestList (452B) ─► parse into quest-log state, rebuild list,
                                          refresh tracking HUD; tracking 0→non-zero
                                          opens tracking panel + plays 862300001
   server ─► S2C 5/73 QuestComplete (344B), apply==1 ─►
                 reward_state==1 GRANT ─► result panel + plays 910036000
                 reward_state==2 DENY  ─► stops 910036000 (no negative cue)
             (actual items/exp/gold arrive via side-channel opcodes)
```

The overall shape and the body sizes are CONFIRMED on `263bd994`; the **quest-giver KIND
value** and the **framed wire sizes** of 2/28 / 2/152 are the unpinned (capture-pending)
edges.

---

## 12. Tutorial subsystem (sibling of quests)

Tutorials are a parallel system to quests, summarised here; full Lua linkage is in
`lua_scripting.md`.

- **`Tutor.scr`** (capital T) is the tutorial **definition** table: 1660-byte (0x67C)
  fixed records, ~86 lessons; each record carries a lesson id at the start and a
  description string in the body (CP949). The client logs `Tutor Id[d] Desc[s]` per
  lesson. `CONFIRMED` (record size); `LIKELY` (count).
- **`tutor.lua`** supplies the tutorial **panel layout** and localized strings, loaded
  from plain disk (not the `.pak` VFS). Direction of control: the Lua scripts are
  **data / config / text tables**; the host application drives tutorial (and quest) logic.
  See `lua_scripting.md` for the script-loading model.
- A tutorial **step index** value drives tutorial step progression.
- Adjacent reward/control tables: `system_control.scr` and `playtime_reward.scr` (the
  latter feeds the 4/148 playtime-reward result).

---

## 13. UNVERIFIED / open questions

1. **No live capture/debugger this pass.** The client-side parse extents and offsets are
   CONFIRMED from static IDA on `263bd994` (2/28 = 12 B body; 5/68 = 452 B layout;
   5/73 = 344 B layout), but the **framed on-wire totals**, `quest_id`/`quest_ids`
   endianness, and the 5/73 body remainder still need a real capture before implementation.
2. **2/28 framed wire size** — *body resolved to 12 bytes* (the send appends `0xC` bytes;
   leading `{ u8 sub_action, u8 npc_kind, u32 quest_id }` + 6 reserved). Still unknown: the
   exact *framed* total on the wire, the endianness of `quest_id`, the wire width of
   `npc_kind`, and whether trailing bytes must be zero. **Do not commit `packets/2-28_*.yaml`
   until the framed total size is capture-confirmed.**
3. **2/28 `sub_action` 0 and 1** — not observed at any send site; may be server-only or
   unused. Only 2 (accept), 3 (proceed), 4 (give-up) are proven (CONFIRMED at the 3 sends).
4. **5/68 trailing byte** — 451 of 452 bytes are accounted for; the final byte is reserved.
   The wire name stride (**17**) is now CONFIRMED against the parser (no longer open).
5. **5/68 `panel_b` / `panel_c` semantics** — stored into quest-log state (CONFIRMED stored)
   but their UI meaning is unknown; `header` (+0) and `owner_id` (+4) are not read by the
   mirror-copy at all.
6. **5/73 body remainder (331 bytes)** — copied wholesale into the result panel, not
   field-decoded by the client. Whether it carries any reward item list (or is purely a
   display blob) is **capture/debugger-pending**; only `apply` (+8) and `reward_state` (+12)
   are read.
7. **Quest-giver KIND value** — the interaction dispatcher handles ~35 KIND values but
   only a subset map to identified panels; which KIND opens the quest dialog (vs guild /
   confession / gather / repair / merchant) is not resolved. Needs the `npc.scr` field
   decode to label KIND values.
8. **`quests.scr` record layout** *(CYCLE 7)* — the stride is now CONFIRMED as a **single
   4960-byte (0x1360) record** (on-disk == runtime, loader-proven); the full offset table lives in
   `structs/quest_record.md`. Still unresolved (UNVERIFIED): what the six step-code bytes at
   +0x040 index, the **interior of the 240-byte objective sub-record** (reward item ids, objective
   NPC / kill / collect targets, the objective action-type — which the client does *not* switch on
   locally), and the meaning of the constant 2 at +0x064. See §8.1, §19.
9. **`npc.scr` record layout** *(updated 2026-06-13)* — stride 404 B, the `u32` id mirrored
   at +4, and the three CP949 description paragraphs at +0x14/+0x50/+0x90 are sample-verified
   (§9.1). Still unresolved: the key that links `npc.scr` (2510 descriptor records) to
   `npcs.scr` (812 catalogue records), and the non-string fields.
10. **Accept gate (threshold 26)** — *largely resolved*. The branch polarity is now
    CONFIRMED (blocks on the `!bypass && level >= 26` side); the literal threshold **26** is
    proven; the gated word is used as **character level** (compared vs the quest record's
    min/max level in the eligibility evaluator, §10). Still capture/debugger-pending: the
    raw provenance of that level word (raw character level vs a derived status).
11. **Reward delivery** — assumed server-authoritative via side-channel item/exp/gold
    opcodes; not directly proven that 5/73 carries no reward list (related to item 6).
12. **Event/tutorial gating hook (§3.3)** — the precise placement-record + global-flag
    condition that promotes an NPC's dialog kind is not pinned.

13a. **Second C2S quest channel (window list-row actions) — RESOLVED to 2/152.** The
    quest-log window's AVAILABLE-tab list-row "request" buttons (§15.3) drive a network send
    that is **distinct from 2/28**: it is **opcode 2/152**, with a **two-u32 body** (see
    §4.4, §15.5), fired only from the AVAILABLE-tab row actions. The earlier "untraced"
    note and the mission's "2/110 quest NPC step" are both superseded — there is **no quest
    send-builder for minor 110** (2/152 = 0x98 ≠ 2/110 = 0x6E). The 2/110 claim has no static
    support and is raised as a **conflict** for protocol/capture re-confirmation; the
    semantic meaning of the two 2/152 u32 args and its framed wire size are capture-pending.

---

## 15. Quest-log window (the in-game quest browser)

The in-game quest log is a single HUD window — the project glossary calls its class the
**quest panel** — identified by the per-character config key `CHAR_QUEST_TRACKING`. It is a
**three-tab quest browser** over the player's held and offered quests. Each tab has its own
selectable row list, its own selection index, and a shared per-tab **detail renderer**
(§16). This section is the window/UI/flow layer that sits on top of the protocol and
data-table model in §§1–10. All facts here are **CODE-CONFIRMED** at the relevant builder /
event-handler / renderer sites unless tagged otherwise; no capture was involved (window
behaviour is not wire-observable).

### 15.1 The three tabs

| Tab | Role | Rows | Detail mode | Row action group |
|---|---|---|---|---|
| **Tab 0 — ACTIVE** | Quests in progress. | up to 6 selectable rows | mode 0 (§16) | row-select actions, one per slot |
| **Tab 1 — COMPLETABLE** | Quests ready to turn in. | up to 6 rows | mode 1 (§16) | a second row-select action group |
| **Tab 2 — AVAILABLE** | Offered quests (walks the global quest-template map by chapter). | up to 10 rows | mode 2 (§16) | a third row-select action group |

The window keeps **three parallel display tables**, one per tab, each holding the per-row
quest entries for that tab, plus a **per-tab selected-row index**. These window display
tables are a presentation-side structure and are **distinct from** the 5/68 quest-log
snapshot mirror in §6 (they use a different entry stride); the copy path from the snapshot
mirror into the window tables was not fully traced (§18). The three tab buttons switch the
active tab, then re-run the detail renderer for the newly selected tab.

### 15.2 Window input model (tabs, scrolling, ESC, buttons)

The window's event handler dispatches on an event-type byte:

- **Key events.** The **ESC** key closes the window when it is visible. A billing/lock gate
  can intercept input first and show a notice instead (the same lock gate guards the button
  path).
- **Scrollbar drag.** Dragging within the scrollbar track sets a **scroll pixel position**
  (0..239) and a derived **scroll line offset** (0..40, computed as `40 − 40·pixel/239`),
  then re-lays the visible list rows. The list holds **40 line slots** internally.
- **Widget/button activation.** The clicked widget's **action id** selects a branch in the
  window action map (§15.3). The whole button path is also behind the billing/lock gate.

### 15.3 Window action map (selected actions)

Buttons and list rows are bound to numeric **action ids**. The behaviourally important ones:

| Action group | Effect | Conf |
|---|---|---|
| Tab buttons (0/1/2 + a sort toggle) | Switch active tab, then re-render detail via the renderer (§16) for the new tab's mode. | CODE-CONFIRMED |
| ACTIVE-tab row select | Set the ACTIVE selected index (0..5) and render mode 0 for that row. | CODE-CONFIRMED |
| COMPLETABLE-tab row select | Set the COMPLETABLE selected index (0..5), render mode 1, and set the active quest id from the active-quest index table. | CODE-CONFIRMED |
| AVAILABLE-tab row select | Set the AVAILABLE selected chapter id and render mode 2 (the index is a chapter id). | CODE-CONFIRMED |
| Category / chapter filter | A string-match over the list rows filters the AVAILABLE list by chapter/category (a fixed bank of filter slots). | CODE-CONFIRMED |
| **Give up / abandon** | Opens the abandon-confirm panel (§15.4). | CODE-CONFIRMED |
| **View dialog** | Opens the NPC quest-dialog populator in **info-only** layout for the selected active quest (read the full dialogue without the accept controls). | CODE-CONFIRMED |
| **Close** | Closes the window. | CODE-CONFIRMED |
| **Help** | Shows a help/info sub-panel; caption = message id **16003**. | CODE-CONFIRMED |
| **HUD-track checkbox** | Toggles quest tracking and persists the flag (§15.6). | CODE-CONFIRMED |
| **List-row network request** | The AVAILABLE-tab per-row request actions send via the **second C2S send-builder = opcode 2/152** (two u32 args; §4.4, §15.5). *(resolved 2026-06-16 — was "untraced".)* | CODE-CONFIRMED (tuple 2/152 + two-u32 body); capture-pending (arg semantics) |

### 15.4 Abandon / give-up panel (gated by an `abandonable` flag)

The **Give up** action does not abandon directly. It first looks up the selected active
quest's **runtime quest record** (via the quest-id keyed registry) and reads a one-byte
**`abandonable` flag** at **runtime offset +4905 (0x1329)**. **Only when the flag is set**
does the abandon-confirm panel open: the panel is filled with the quest id and an NPC-kind
byte, its prompt caption is message id **18027** and its confirm button caption is message
id **18028**, and it is shown. Confirming on that panel is what emits the C2S **give-up**
action (`sub_action = 4`, §4.2) and clears the local active-quest state. If the
`abandonable` flag is clear, the window shows a "cannot abandon this quest" notice and
nothing is sent. CONFIRMED on `263bd994`.

> The `abandonable` flag lives at offset **+0x1329 (4905)** of the quest record. There is a
> **single 4960-byte record** (on-disk == runtime, byte-for-byte, loader-proven — §8), so this is
> authored on-disk data, not runtime scratch; the earlier "on-disk vs runtime stride conflict" /
> "3720-byte sample" reading is **REFUTED** (CYCLE 7). The give-up gate (captions 18027/18028, the
> `abandonable` byte) is CODE-CONFIRMED. The offset is documented in `structs/quest_record.md §4`.

### 15.5 Second C2S quest channel (list-row request) — RESOLVED: opcode 2/152

The AVAILABLE-tab list-row request actions (window action ids in the 100..105 group) drive
a network send through a **different send-builder than the unified 2/28 quest action**. It
is now RESOLVED on `263bd994` as **opcode 2/152** with a **two-u32 body** (see §4.4): the
two args are sourced from the window's per-row tables (a 28-byte-stride row table for the
first arg, a 44-byte-stride table for the second). It is most plausibly a "request quest
detail / claim row" round-trip. The tuple and body shape are CONFIRMED; the **semantic
meaning of the two u32 args and the framed wire size are capture/debugger-pending**. The
earlier "untraced" note and the mission's "2/110 quest NPC step" are superseded — there is
no quest send-builder for minor 110 (§4.4, §13a). Do not assume it reuses 2/28's body shape.

### 15.6 HUD quest-tracking checkbox (per-character INI flag)

One window widget is a checkbox that toggles **HUD quest tracking** (the over-actor quest
marker). Toggling it drives the tracking-on / tracking-off helpers that show or hide the
marker, and **persists the on/off state to the per-character configuration**: it writes the
`CHAR_QUEST_TRACKING` key under a per-character section named `<billing_id>_<player_name>_CHARACTER`.
So the tracking preference is config-backed and scoped to the specific character. This is
the same `CHAR_QUEST_TRACKING` key that identifies the window (§15).

### 15.7 Quest eligibility / status-color evaluator and the runtime quest-record gate map

The client carries a substantial **quest eligibility evaluator** (used by the AVAILABLE tab
and the NPC quest menus to color/grade each quest). Given a quest id it looks up the
**runtime quest record** (via the quest-id keyed registry) and returns one of ~13 distinct
**status / color state ids** after checking, in order: record-exists, **min level**, **max
level**, an accepted/availability flag, **class/race match**, two **secondary-stat bounds**,
a **tertiary-stat bound**, a scan over the active-quest id table for a same-category quest
already active, a scan over a second active range for the same quest id, and finally a
**prerequisite-chain** compare. This is the single best map of the **runtime** quest-record
gate fields. CONFIRMED on `263bd994`.

The quest-record gate fields (offsets into the **single 4960-byte record**, the same object the
give-up gate reads — full table in `structs/quest_record.md §4`):

| Record off | Field role | Conf |
|----:|---|---|
| +2 | category/class byte (used in the already-accepted scan). | CONFIRMED |
| +4905 (0x1329) | `abandonable` flag (the give-up gate, §15.4). | CONFIRMED |
| +4936 (0x1348) | prerequisite-chain reference (u32, compared against a global prereq state). | CONFIRMED |
| +4944 (0x1350) | **min level** (u16) — compared against the character-level word (the accept-gate "26" word, §4.3). | CONFIRMED |
| +4946 (0x1352) | **max level** (u16). | CONFIRMED |
| +4948 (0x1354) | per-index accepted/availability flag (indexed by an active-quest index). | CONFIRMED |
| +4953 (0x1359) | class/race match byte. | CONFIRMED |
| +4954 (0x135A) | secondary-stat minimum. | CONFIRMED |
| +4955 (0x135B) | secondary-stat maximum. | CONFIRMED |
| +4956 (0x135C) | tertiary-stat bound. | CONFIRMED |

> These offsets are into the **single 4960-byte quest record** (on-disk == runtime, §8); the full
> offset table is `structs/quest_record.md`-owned. The server-authored *magnitudes* these gate
> against (the actual level/stat thresholds a player must meet) are quest-data values and
> capture/debugger-pending at the data-table level; the *gate fields and their order* are
> client-side CONFIRMED.

#### 15.7.1 Eligibility status states → localized labels

The grade-label setter maps a 0..15 status-class byte to a localized message id:

| Status class | Message id | Status class | Message id |
|---:|---|---:|---|
| 0 | 18501 | 8 | 18509 |
| 1 | 18502 | 9 | 18510 |
| 2 | **18503** | 10 | 18511 |
| 3 | 18504 | 11 | 18512 |
| 4 | 18505 | 12 | 18513 |
| 5 | 18506 | 13 | 18514 |
| 6 | 18507 | 14 | 18515 |
| 7 | 18508 | 15 | **18503** (shared with case 2) |

> ~13 distinct labels across the 16 cases (case 2 and case 15 share **18503**). The literal integer
> values of the status-state globals the evaluator returns are **runtime-only** (initialised once at
> startup); the mapping above (status-class byte → message id) is CONFIRMED.

#### 15.7.2 Requirement type → notice message id taxonomy

When the requirement-notice renderer reports *why* a quest is not currently accept-able, it emits a
per-requirement notice. Each requirement dimension reads a quest-record gate field and selects a
notice message id by the field's small-integer value:

| Requirement dimension | Quest-record gate field | Value(s) decoded | Notice message id(s) | Conf |
|---|---|---|---|---|
| Min level | `min_level` (+0x1350, u16) | magnitude | 18003 | CONFIRMED |
| Max level | `max_level` (+0x1352, u16) | magnitude | 18004 | CONFIRMED |
| Accepted / availability slot | `accepted_flags` (+0x1354, u8 indexed) | index 1 / 2 / 3 / 4 | 18005 / 18006 / 18007 / 18008 | CONFIRMED |
| Class / race | `class_race_mask` (+0x1359, u8 bitmask) | 1 / 2 / 4 | 18010 / 18011 / 18012 | CONFIRMED |
| Secondary stat — min | `secondary_stat_min` (+0x135A, u8) | tier 1..7 | 18054 .. 18060 | CONFIRMED |
| Secondary stat — max | `secondary_stat_max` (+0x135B, u8) | tier 1..7 | 18061 .. 18067 | CONFIRMED |
| Tertiary stat | `tertiary_stat_bound` (+0x135C, u8) | single | 18016 | CONFIRMED |
| Same-category quest active | `category` (+0x002) scan | — | 18017 (+ detail 18018) | CONFIRMED |
| Prerequisite chain | `prereq_chain_id` (+0x1348, u32) | chapter-progress compare | 18019 | CONFIRMED |
| Same quest id already active | `quest_id` (+0x000) scan | — | 18071 | CONFIRMED |

> `class_race_mask` taking values 1 / 2 / 4 (powers of two) means it is a **bitmask** — a quest can
> be open to several classes at once. These notice ids are localized CP949 message codes, not
> opcodes and not wire enum values.

---

## 16. Per-tab detail renderer (modes 0 / 1 / 2)

A single **detail renderer** populates the right-hand detail area for the selected row,
parameterised by the **tab mode** and the selected index. It first clears the internal list
line slots (40 lines), then by mode:

| Mode | Tab | Quest lookup | Dialogue source | Header caption | Body |
|---|---|---|---|---|---|
| **0** | ACTIVE | Active display table by selected index. | The quest record's **active dialogue handle**. | message id **18031** | 6 dialogue body lines, then an objective progress line. |
| **1** | COMPLETABLE | Completable display table by selected index. | The quest record's **turn-in dialogue handle**. | message id **18033** | 6 dialogue body lines. |
| **2** | AVAILABLE | Walk the global quest-template map for the record whose chapter id matches the selected index. | The quest record's **offer dialogue handle**. | (per template) | 6 dialogue body lines. |

For **mode 0 (ACTIVE)** the renderer also walks the quest's per-quest **objective array**
(an embedded fixed-stride sub-array on the quest record — consistent with the `48`-marker
sub-records of §8.1), finds the active objective for the quest's current step, and formats
an **objective progress line** using message id **18032** (a `%d` counter rendered as the
1-based current step). The progress value and the per-objective reward value are read from
that objective sub-record. This matches the §5.1 "up to 6 lines + current/total counter"
behaviour and pins the source: the **6 body lines come from the NPC-script dialogue records**
(§17), the **captions come from the message-string database** (§17).

> The dialogue handles are pinned in the 4960-byte quest record (CYCLE 7): **offer at +0x54**,
> **turn-in at +0x58**, and the **active dialogue + objectives via the objective sub-array at
> +0x68** (240-byte stride). See `structs/quest_record.md §2`–§3.

---

## 17. Quest text has two distinct sources

Quest-related text is drawn from **two separate stores**, and conflating them is the main
trap for a reimplementer. Both are **CODE-CONFIRMED**.

### 17.1 UI captions / headers / notices → message-string database (`msg.xdb`)

All quest **UI captions, tab headers, and error/notice strings** are numeric **message
ids** resolved through a message-string accessor backed by a `std::map`, populated once at
startup from **`data/script/msg.xdb`** (loaded in the engine's main init path). A lookup
miss yields an `"Id[%d] msg not found."` placeholder. These are localized CP949 message
codes, **not** opcodes and **not** wire enum values.

`msg.xdb` itself is a flat record table (sample-verified stride 48 bytes, ~28,423 records —
see `formats/config_tables.md`); an adjacent `msginfo.do` table holds message-id index /
metadata, not the quest text itself.

Quest message ids observed at the window/dialog sites this pass:

| Message id(s) | Use |
|---|---|
| 16003 | Help / info notice (window Help button). |
| 18022 | Quest-dialog title caption. |
| 18027 | Give-up confirm prompt. |
| 18028 | Give-up confirm button caption. |
| 18031 | ACTIVE-tab detail header. |
| 18032 | ACTIVE-tab objective progress line (`%d` = current step, 1-based). |
| 18033 | COMPLETABLE-tab detail header. |
| 18003 … 18071 | Requirement / availability notices (a contiguous block; e.g. 18003–18019, 18054–18071). |
| 309, 618 | Quest-completion result panel captions (also noted in §7.3). |

### 17.2 Quest dialogue body → NPC-script records (`npc.scr`)

The **6 dialogue body lines** of every offer / active / turn-in quest dialog come from the
**NPC-script record set** (`npc.scr`), reached through the quest record's dialogue handles
(§16). These are the multi-paragraph CP949 strings of the 404-byte `npc.scr` record (§9.1).
The dialogue body is therefore **never** stored in `msg.xdb` and **never** inline-text in
`quests.scr`; only the captions/headers above are message ids.

---

## 18. Opcode install-table confirmation (routing CODE-CONFIRMED, bodies capture-unverified)

The quest opcodes were re-verified against the client's **live push-handler install table**,
which strengthens the routing claims of §2 from "observed at a dispatch/send site" to
"present in the installed handler table":

| Tuple | Name | Dir | Size | Install status |
|---|---|---|---|---|
| **5/68** | `SmsgQuestList` | S2C | 452 B | Installed in the major-5 push table (quest-log snapshot handler). CODE-CONFIRMED. |
| **5/73** | `SmsgQuestComplete` | S2C | 344 B | Installed in the major-5 push table (quest-completion verdict handler). CODE-CONFIRMED. |
| **2/28** | `CmsgQuestAction` | C2S | 12 B body | Major-2 is **send-only**; there is correctly **no inbound handler slot**. The send-builder appends a 12-byte body and fills `sub_action` 2/3/4. CODE-CONFIRMED. |
| **2/152** | quest list-row request | C2S | 8 B body | The "second channel" (§4.4, §15.5) — a separate send-builder appending two u32 args; AVAILABLE-tab row actions only. *(resolved 2026-06-16 — was "untraced".)* CODE-CONFIRMED (tuple + body shape). |

> **These routing facts are CONFIRMED on `263bd994`; the on-wire body *sizes* (12 B / 8 B /
> 452 B / 344 B) are confirmed from the client parse/send extents.** What remains
> capture/debugger-pending is the **framed** wire totals, field endianness, the 5/73 body
> remainder, and any server-authored quest *values* (§13 #1, #6, #10). The 2/110 "quest NPC
> step" tuple was searched for and **not found** — there is no quest send-builder for minor
> 110 (conflict, see banner / §4.4 / §13a).

---

## 19. Quest-data tables (sample-verified) — behavioural summary

The on-disk quest-data files are now **SAMPLE-VERIFIED** via VFS byte inspection (no IDA).
This spec keeps them **behavioural**; the **full stride / field byte tables live in
`formats/config_tables.md`** (quest-data family). Summary:

### 19.1 `quests.scr` — quest template catalogue
Sparse flat array of **single 4960-byte (0x1360) records** (on-disk == runtime, byte-for-byte,
loader-proven). Quest id is a `u16` at +0 (range 1..617; 0 = empty slot), CP949 name at +0x03, six
step-code bytes at +0x040, dialogue/chain references at +0x054 (offer) / +0x058 (turn-in), the
embedded **objective sub-array at +0x68 (240-byte stride)**, and the high-region gate fields
(`abandonable` +0x1329, prereq +0x1348, min/max level +0x1350/+0x1352, accepted/class/stat
+0x1354..+0x135C). The **3720-byte "on-disk" figure is REFUTED** (§8); there is no on-disk-vs-runtime
stride split to reconcile. **Full byte layout: `structs/quest_record.md`.**

### 19.2 `autoquestion_cl.scr` — anti-bot quiz pool
Flat array, **stride 92 B, 1300 records**. Sequential `u32` question id at +0, CP949 question
text at +4 (simple Korean-language multiplication problems), CP949 answer-hint text after it.
The numeric answer is checked server-side; the file holds only the display text. This is the
anti-bot human-verification question bank, a sibling of the quest system rather than a quest
table proper.

### 19.3 `npc.scr` — NPC description text
Flat array, **stride 404 B, 2510 records**. `u32` id at +0 mirrored at +4, then three CP949
description paragraphs at +0x14 / +0x50 / +0x90 with `u32 = 48` sub-section markers between
them (§9.1). This is the **dialogue-body / archetype-description store** (§17.2), distinct
from the `npcs.scr` catalogue.

### 19.4 Open data items (carried forward)
- The six `quests.scr` step-code bytes at +0x040 — index into a step-type enum, or objective
  template references? (Three distinct patterns observed.)
- The **interior of the 240-byte objective sub-record** (record +0x68, 240-byte stride) — objective
  NPC ids, kill/collect counts, reward item ids and EXP are the expected contents but are **not yet
  field-decoded** (UNVERIFIED); the client does **not** switch on an objective-action-type enum
  locally (the taxonomy is data-driven and server-tracked).
- The **exact wire byte offsets of the ≤10-entry 5/73 reward list** inside the 344-byte body
  (capture/debugger-pending); the client renders {icon, name[17], count} but the wire→panel mapping
  is runtime-only (§7.3).
- The literal integer values of the eligibility status-state globals (runtime-only; the
  status-class → message-id mapping itself is CONFIRMED, §15.7.1).
- The `npc.scr` ↔ `npcs.scr` linking key (the 1..2510 description id space vs the catalogue
  NPC ids).
- *(RESOLVED, CYCLE 7)* The `abandonable` flag is at **+0x1329** of the single 4960-byte record;
  the dialogue handle offsets are **+0x54 (offer) / +0x58 (turn-in)**; there is no stride conflict.

### 19.5 `discript.sc` — UI context-menu labels (sibling table)
Flat array, **stride 68 B, 33 records**. `u32` menu id at +0, `u32` category code at +4, CP949
label at +8 (party actions, currency names, window-toggle labels, school/clan actions). Not a
quest file, but it is referenced from quest/interaction UI menus, so it is catalogued with the
quest-data family in `formats/config_tables.md`.

---

## 14. Cross-references

| Spec | Relationship |
|---|---|
| `formats/npc_spawns.md` | The 28-byte `.arr` NPC placement record (id/index linkage in §9.2). |
| `structs/actor.md` | The NPC actor descriptor whose Kind / Job / Name classifier fields drive §3. |
| `inventory_trade.md` | The NPC-merchant request/ack family sharing the same dispatcher; quest reward item side-channel. |
| `combat.md` | Exp-gain / vitals side-channel opcodes referenced by reward delivery (§7.4). |
| `handlers.md` | The area-entity snapshot opcode that references NPCs by placement index (§9.2). |
| `lua_scripting.md` | The `tutor.lua` panel-layout / string-table loading model (§12). |
| `opcodes.md` | Authoritative opcode catalog; 2/28, 5/68, 5/73 routing rows. |
| `formats/config_tables.md` | **The full sample-verified stride/field byte tables for the quest-data family** (`quests.scr`, `autoquestion_cl.scr`, `npc.scr`, `discript.sc`, `msg.xdb`, `msginfo.do`). This spec keeps those behavioural and links across (§§8–10, §17, §19). |

- **Glossary:** see `Docs/RE/names.yaml`
- **Provenance:** see `Docs/RE/journal.md`
