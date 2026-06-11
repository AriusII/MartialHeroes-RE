---
status: hypothesis
sample_verified: false
---

# Quest & NPC Dialog — Clean-Room Specification

> Neutral, rewritten behavioural specification. No legacy symbols, no binary
> addresses, no decompiler pseudo-code. Describes the *observed behaviour*, state
> model, opcode tuples, data-table linkage and constants of the legacy client's
> NPC-interaction, quest-dialog, quest-log and reward-verdict subsystems, so the
> .NET core can be reimplemented from scratch.
>
> Scope: the NPC click → panel dispatch flow, the unified C2S quest-action request,
> the two S2C quest-state pushes (quest-log snapshot, completion verdict), and the
> on-disk data tables that feed quest/NPC text. Reward *granting* is server-side;
> the client only renders the verdict and reacts to side-channel item/exp/gold
> updates — those side-channel opcodes are specified elsewhere (`inventory_trade.md`,
> `combat.md`, `handlers.md`) and are referenced, not restated, here.

## Status & confidence

- **No live network capture was available for this pass.** Every wire field offset
  and message size below is a **static inference** from the client's parse/send
  behaviour. Treat all payload offsets/sizes as hypotheses to confirm against a
  capture before relying on them.
- **Opcode → handler routing is a hard static fact.** The `(major:minor)` tuples
  below were observed at the client's dispatch / send sites; their *direction* and
  *family* are reliable. The *payload field layouts* are the hypotheses.
- **Per-claim confidence** is tagged inline as `CONFIRMED` (proven at the relevant
  code site or cross-checked against an existing committed spec), `LIKELY` (single
  consistent site), or `UNVERIFIED` (inferred / boundary not pinned).
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

---

## 2. Opcodes touched by this subsystem

All opcodes are written as `major/minor` tuples; direction is from the **client's**
point of view (`C2S` = client→server, `S2C` = server→client). Payload sizes and
field offsets are **payload-relative** (relative to the first payload byte, not
counting the frame header), matching `opcodes.md` convention.

### 2.1 Quest-specific opcodes

| Opcode | Dir | Size | Proposed name | Role | Conf (routing) |
|---|---|---|---|---|---|
| 2/28 | C2S | see §4 | `CmsgQuestAction` | Unified quest-dialog action (accept / proceed / give-up). | CONFIRMED |
| 5/68 | S2C | 452 | `SmsgQuestList` | Full quest-log snapshot push. See §6. | CONFIRMED |
| 5/73 | S2C | 344 | `SmsgQuestComplete` | Quest-completion + reward verdict. See §7. | CONFIRMED |

> **2/28 is a newly-identified C2S opcode** not previously in the catalog. Family 2
> (game-action) is entirely client-emitted; there is no inbound major-2 handler. The
> outbound send-builder hard-codes the major and the minor (28); only a small body is
> filled (see §4).

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

---

## 4. C2S quest-action request (2/28)

A single unified message carries all three quest-dialog button actions. The send
builder writes `major = 2`, `minor = 28`, a fixed frame size, and fills a small body.

### 4.1 Body layout (static inference — capture-UNVERIFIED)

| Off | Type | Field | Meaning | Conf |
|----:|------|-------|---------|------|
| +0 | u8  | `sub_action` | Selects the quest action — see table 4.2. | LIKELY |
| +1 | u8  | `npc_kind`   | The NPC KIND / slot byte taken from the active dialog panel instance. | LIKELY |
| +2 | u32 | `quest_id`   | The active quest id (from the local player/selection state). | LIKELY |

The send builder reserves a fixed frame and only fills the leading body bytes; the
remaining bytes of the frame are alignment / zero. The meaningful body is the three
fields above (a u8, a u8, and a u32 = 6 logical bytes packed into the reserved block).

> **UNVERIFIED:** the exact total payload size, the endianness of `quest_id`, the exact
> width of `npc_kind` on the wire, and whether the trailing reserved bytes are required
> to be zero. The send builder reserves a frame larger than the 6 logical bytes; treat
> the surplus as alignment until a capture pins the real on-wire size. **Do not commit a
> `packets/2-28_*.yaml` until at least the total size is capture-confirmed.**

### 4.2 `sub_action` enumeration

| Value | Meaning | Sent from | Conf |
|------:|---------|-----------|------|
| 2 | **Accept** — accept the offered quest. | NPC quest-talk panel, "accept" widget (after gates pass). | LIKELY |
| 3 | **Proceed / continue** — advance an in-progress quest dialog. | NPC quest panel, when the panel's phase byte equals 2. | LIKELY |
| 4 | **Give up / abandon** — abandon the active quest. | Quest give-up panel, on confirm. | LIKELY |

> **UNVERIFIED:** whether `sub_action` 0 and 1 exist (e.g. open / refuse). They were not
> observed at any send site; they may be server-only or unused.

### 4.3 Field sourcing (client-side state, for the implementer)

- `quest_id` always comes from the **active quest id** held in the local player/selection
  state (the same value the give-up path clears on abandon).
- `npc_kind` comes from the **active dialog panel instance** (the panel stores the KIND /
  slot byte that opened it).
- On **accept**, the client applies a **level / billing gate** before sending: a
  threshold of **26** is compared against a runtime status field (see §10). If the gate
  fails the client shows a notice and does **not** send. Whether the gated quantity is
  character level or a cash/VIP status is `UNVERIFIED`; only the literal threshold 26 is
  proven.
- On **give-up**, after the send the client also clears its local active-quest state.

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

### 6.1 Wire payload layout (452 bytes — static inference, capture-UNVERIFIED)

Offsets are payload-relative.

| Off | Type | Field | Meaning | Conf |
|----:|------|-------|---------|------|
| +0   | u32 | `header` | Header / reserved. | UNVERIFIED |
| +4   | u32 | `owner_id` | Actor / owner id. | LIKELY |
| +8   | u8  | `panel_c` | Stored into quest-log state (UI selector C). | UNVERIFIED |
| +9   | u8  | `tracking_flag` | Active / tracking flag — drives the tracking panel open/close. | LIKELY |
| +10  | u8  | `panel_b` | Stored into quest-log state (UI selector B). | UNVERIFIED |
| +11  | u8[10]  | `slot_a_flags` | One flag per tracked slot → slot-A table. | LIKELY |
| +21  | u8[10]  | `slot_b_flags` | One flag per tracked slot → slot-B table. | LIKELY |
| +32  | u32[20] | `quest_ids` | 20 quest ids (parallel to the name table). | LIKELY |
| +112 | char[20][17] | `quest_names` | 20 quest names, CP949, 16 chars + forced NUL terminator at index 16. | LIKELY |

Summed: 4 + 4 + 1 + 1 + 1 + 10 + 10 + 80 + 340 = 451 bytes of described content within a
452-byte payload (one trailing/alignment byte unaccounted — treat as reserved until a
capture confirms the exact field span).

> **Total payload size 452 (0x1C4) is a static inference from the parser's read extent.**
> The endianness of `quest_ids`, the exact name stride (17 vs an aligned value), and the
> meaning of `panel_b`/`panel_c` are `UNVERIFIED`.

### 6.2 Client-side quest-log state model

The parser mirrors the snapshot into a local quest-log state object. The implementer can
model it as the following logical tables (exact base offsets are an internal layout
detail — model the *shape*, not the legacy offsets):

- **Slot-A table:** 10 entries, fixed stride; each entry stores its flag.
- **Slot-B table:** 10 entries, fixed stride; each entry stores its flag.
- **Quest-entries table:** 20 entries, fixed stride; each entry stores a `quest_id`
  (u32) and the CP949 quest name.
- **Scalar fields:** `tracking_flag` (u8), `panel_b` (u8), `panel_c` (u8).
- Two small slot-table header blocks are zero-cleared on every refresh.

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

### 7.1 Wire payload layout (344 bytes — static inference, capture-UNVERIFIED)

Only two leading fields are read by the handler; the remainder of the body is copied
wholesale into the result panel.

| Off | Type | Field | Meaning | Conf |
|----:|------|-------|---------|------|
| +0  | u8[8] | `header` | Leading header / context (not individually decoded). | UNVERIFIED |
| +8  | u32 | `apply` | Apply selector — the handler acts only when this equals **1**. | LIKELY |
| +12 | u8  | `reward_state` | Verdict — see table 7.2. | LIKELY |
| +13 | u8[331] | `body_remainder` | Remaining bytes, copied wholesale into the result panel; not field-decoded by the client. | UNVERIFIED |

Summed: 8 + 4 + 1 + 331 = 344 bytes (0x158). The body remainder (331 bytes) is **not**
decoded by the client beyond the wholesale copy into the result panel.

### 7.2 `reward_state` verdict

| Value | Meaning | Client reaction | Conf |
|------:|---------|-----------------|------|
| 1 | **Grant** | Open the completion/result panel; play the **positive** completion sound (`910036000`). | LIKELY |
| 2 | **Deny / fail** | Play the **negative** variant of the completion sound (`910036000` with the fail path). | LIKELY |

In both apply cases the full 344-byte body is handed to the completion-result panel.

### 7.3 Completion-result panel behaviour

- The panel **double-buffers** the previous body and copies the new 344-byte body into
  its own storage.
- It tracks a **phase** byte: `0 = idle`, `1 = showing result (open)`, `2 = closed`.
- When the previous body's `apply` byte equals 1 and the phase is idle, it advances to
  phase 1 and **auto-shows** the result panel (open animation), with the close animation
  on dismiss.
- Result captions use **localized string table ids 309 and 618**.

### 7.4 Reward delivery (cross-reference)

The actual reward is **server-authoritative**. After a grant verdict the client receives
the concrete item/exp/gold changes through the existing side-channel opcodes (item-panel
updates, exp-gain, gold-balance — see `inventory_trade.md` and `combat.md`). 5/73 carries
the **verdict only**. `LIKELY` (the 331-byte body remainder is not item-decoded by the
client, consistent with this; `UNVERIFIED` that it carries no reward list at all — §13).

---

## 8. Quest template table — `quests.scr`

Static quest text/definition data lives in an on-disk fixed-record table loaded into a
quest template lookup map keyed by `quest_id`. This is the single static-quest-data
access point used across the quest-dialog cluster.

| Property | Value | Conf |
|---|---|---|
| Logical path | `data/script/quests.scr` | CONFIRMED |
| Record size | 4960 bytes (0x1360), fixed | CONFIRMED |
| Record count | ~366 quests (derived from file size / record size) | LIKELY |
| Lookup key | `quest_id` | CONFIRMED |
| Title | Localized string-table id **18022** (not stored inline as text) | LIKELY |

### 8.1 Known record fields

| Off | Type | Field | Meaning | Conf |
|----:|------|-------|---------|------|
| +72 (0x48) | handle | `step_list` | Pointer/handle to the quest's objective/dialog **step list**, iterated to produce the up-to-6 dialog/objective lines and the objective counter (§5.1). | LIKELY |

> **UNVERIFIED — most of the 4960-byte record is undecoded.** Reward item ids, required
> level, prerequisite-quest chain, and objective target counts are all unknown. Only the
> step-list handle (+72) and the title-id usage are proven. A dedicated quest-record
> struct pass needs a real `quests.scr` sample (asset analyst). Do **not** guess reward or
> prerequisite fields from this spec.

---

## 9. Supporting NPC / placement data tables

### 9.1 NPC definition table — `npc.scr` / `npcs.scr`

| Property | Value | Conf |
|---|---|---|
| Logical path | `data/script/npc.scr` (sibling variant `npcs.scr`) | CONFIRMED |
| Record size | 404 bytes (0x194), fixed | CONFIRMED |
| Record count | ~2510 entries | LIKELY |
| String sub-fields | 6 × 64-byte string slots per record (name + dialog/label slots), CP949 | LIKELY |
| Empty-string convention | A sub-field whose first character is ASCII `'0'` is treated as empty / cleared. | LIKELY |

These records feed the **NPC template lookup map** keyed by NPC id (the actor
descriptor's NPC-id field). The actor factory uses the lookup to resolve an NPC's model
and interaction record when an actor is of the NPC sort. See `structs/actor.md` for the
descriptor fields.

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
| 2/28 `sub_action` 2 / 3 / 4 | accept / proceed / give-up. | LIKELY |
| Accept gate threshold = 26 | Client blocks "accept" and shows a notice when a runtime status value is below 26. Whether the gated value is character level or cash/VIP status is unknown. | LIKELY (threshold); UNVERIFIED (meaning) |
| Quest-dialog max text lines = 6 | Up to 6 dialog/objective lines populated per quest dialog. | LIKELY |
| Quest-name field = 16 chars + NUL (17 bytes) | Per-entry name in the 5/68 snapshot; CP949. | LIKELY |
| Quest-log entries = 20 | 20 quest ids + 20 names in the snapshot and local table. | LIKELY |
| Quest-log slot flag tables = 10 + 10 | Two 10-entry flag tables (slot-A, slot-B). | LIKELY |
| 5/68 payload = 452 bytes (0x1C4) | Quest-log snapshot size. | LIKELY |
| 5/73 payload = 344 bytes (0x158) | Quest-completion verdict size. | LIKELY |
| 5/73 `apply` == 1 | Handler acts only when the apply selector is 1. | LIKELY |
| 5/73 `reward_state` 1 / 2 | grant / deny verdict. | LIKELY |
| 5/73 phase 0 / 1 / 2 | result-panel phase: idle / showing / closed. | LIKELY |
| `quests.scr` = 4960-byte records, ~366 quests, +72 = step-list handle | Quest template table. | CONFIRMED (size); LIKELY (count); LIKELY (+72) |
| `npc.scr` = 404-byte records, ~2510 entries, 6 × 64-byte strings | NPC definition table. | CONFIRMED (size); LIKELY (count, strings) |
| `Tutor.scr` = 1660-byte records, ~86 lessons | Tutorial definition table. | CONFIRMED (size); LIKELY (count) |
| String-table id 18022 | Quest-dialog title caption. CP949. | LIKELY |
| String-table ids 309 / 618 | Quest-result captions. CP949. | LIKELY |
| Sound id 862300001 | Quest-tracking-on SFX (tracking-flag 0→non-zero). | LIKELY |
| Sound id 910036000 | Quest-completion SFX (positive on grant, negative variant on deny). | LIKELY |

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
        ┌── "accept"  (gate: status >= 26) ─► C2S 2/28 { sub_action=2, npc_kind, quest_id }
   user ├── "proceed" (panel phase == 2)   ─► C2S 2/28 { sub_action=3, npc_kind, quest_id }
        └── "give up"  (confirm)            ─► C2S 2/28 { sub_action=4, npc_kind, quest_id }
                                                  + clear local active-quest state
                                  │
   server ─► S2C 5/68 QuestList (452B) ─► parse into quest-log state, rebuild list,
                                          refresh tracking HUD; tracking 0→non-zero
                                          opens tracking panel + plays 862300001
   server ─► S2C 5/73 QuestComplete (344B), apply==1 ─►
                 reward_state==1 GRANT ─► result panel + positive 910036000
                 reward_state==2 DENY  ─► negative 910036000
             (actual items/exp/gold arrive via side-channel opcodes)
```

`LIKELY` for the overall shape; the **quest-giver KIND value** and the exact 2/28 body
size are the unpinned edges.

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

1. **No live capture this pass.** Every wire field offset/size (2/28 body, 5/68 452-byte
   layout, 5/73 344-byte layout) is a **static inference**. All sizes and offsets must be
   byte-confirmed against a capture before implementation.
2. **2/28 total payload size and packing** — the send builder reserves a fixed frame and
   fills only the leading `{ u8, u8, u32 }` body; the exact total size, the endianness of
   `quest_id`, the wire width of `npc_kind`, and whether trailing bytes must be zero are
   unknown. **Do not commit `packets/2-28_*.yaml` until at least the total size is
   capture-confirmed.**
3. **2/28 `sub_action` 0 and 1** — not observed at any send site; may be server-only or
   unused. Only 2 (accept), 3 (proceed), 4 (give-up) are proven.
4. **5/68 trailing byte** — 451 of 452 bytes are accounted for; the final byte and the
   exact name-field stride (17 vs an aligned value) need capture confirmation.
5. **5/68 `panel_b` / `panel_c` semantics** — stored into quest-log state but their UI
   meaning is unknown; `header` (+0) likewise.
6. **5/73 body remainder (331 bytes)** — copied wholesale into the result panel, not
   field-decoded by the client. Whether it carries any reward item list (or is purely a
   display blob) is unknown; only `apply` (+8) and `reward_state` (+12) are read.
7. **Quest-giver KIND value** — the interaction dispatcher handles ~35 KIND values but
   only a subset map to identified panels; which KIND opens the quest dialog (vs guild /
   confession / gather / repair / merchant) is not resolved. Needs the `npc.scr` field
   decode to label KIND values.
8. **`quests.scr` record layout** — only the step-list handle (+72) and the title-id are
   proven. Reward item ids, required level, prerequisite chain, and objective targets are
   all unknown. Needs a `quests.scr` sample (asset analyst).
9. **`npc.scr` record layout** — record size and the 6 × 64-byte string-slot pattern are
   known; the meaning of each of the 6 string slots, and the non-string fields, are not
   decoded.
10. **Accept gate (threshold 26)** — whether the gated runtime value is character level
    or a cash / VIP status is a guess; only the literal threshold 26 is proven.
11. **Reward delivery** — assumed server-authoritative via side-channel item/exp/gold
    opcodes; not directly proven that 5/73 carries no reward list (related to item 6).
12. **Event/tutorial gating hook (§3.3)** — the precise placement-record + global-flag
    condition that promotes an NPC's dialog kind is not pinned.

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

- **Glossary:** see `Docs/RE/names.yaml`
- **Provenance:** see `Docs/RE/journal.md`
