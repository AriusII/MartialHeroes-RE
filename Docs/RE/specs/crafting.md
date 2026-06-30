<!--
verification: CONSUMER-CONFIRMED (CYCLE 15, 2026-06-30; f61f66a9) — all previously
  control-flow-confirmed facts carry forward: the recipe table `products.scr` loader (212-byte fixed
  record, map-keyed by the first field), the gather-vs-NPC-built split on `result_display_id`
  (<= 100 vs > 100), the eight ingredient id+count slots, the `production_npc_id` field, the two C2S
  request opcodes (select 2/151, commit 2/153) with the 60-second client timeout armed on commit, and
  the inbound result message 4/79 SmsgCraftingResult (52-byte body: success flag, failure error-code
  1..5, produced-item dwords) are all confirmed; that this is a SINGLE "production" system with NO
  separate alchemy path is also confirmed.
  CYCLE 15 adds: the 2/153 commit builder stamps NO expected-reply minor field in the request body
  (the reply arrives unconditionally via the major-4 inbound dispatch table); the hypothesis that
  2/153 is answered by 3/8 SmsgShopPageUpdate or 4/113 SmsgItemShopPurchaseResult is REFUTED —
  those are replies to the buy-toggle 2/151 (see specs/cash_shop_browser.md §5). Two-selector split
  on 2/151 now documented (selector 0 = gold/regular shop, selector 200 = cash/Diamond goods panel).
  RUNTIME-ONLY (capture / ?ext=dbg-pending): the success-rate formula (computed server-side, never on
  the client), the exact meaning of each failure error code 1..5, the precise value->option mapping of
  the 2/153 slot tuple, and the semantic labels of the produced-item dwords (id vs count vs grade).
  The unread record regions (between the key and the result field, and the tail after the NPC field)
  are UNVERIFIED — no on-disk sample row was available; the client never reads them.
ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
ida_reverified: 2026-06-24
ida_reverified: 2026-06-27
ida_reverified: 2026-06-30
evidence: [static-ida, consumer-correlation]
sample_verified: false
note: |
  IDB SHA 263bd994, CYCLE 7 (2026-06-20) — promoted the production/crafting subsystem: the
  products.scr recipe table, the gather-vs-NPC-built result split, the C2S production request/commit
  opcodes, and the S2C SmsgCraftingResult layout. This is a single production system; there is NO
  separate alchemy subsystem.
  Spec-audit pass (2026-06-24) — control-flow re-confirmed products.scr loader end-to-end: 212-byte
  (0xD4) verbatim record, operator-new per row, inserted into an ordered map keyed on record +0x00
  (confirms §2.1 stride/key/single-system claims; §2.3 column offsets and §3/§4 opcode flow carried
  from prior cycles, no drift found).
  CYCLE 14 re-anchor (f61f66a9, 2026-06-27) — confirmatory pass: products.scr + productcollect.scr
  + productrandname.scr loaders present and registered in boot path-pointer table, cleanly relocated.
  1 re-confirmed SAME, 0 corrected.
  CYCLE 15 (f61f66a9, 2026-06-30) — promotion lane P-cashshop: confirmed 2/153 stamps no
  expected-reply field; reply routed unconditionally via major-4 inbound dispatch table to 4/79;
  refuted hypothesis that 2/153 replies with 3/8 or 4/113 (those are 2/151 buy-toggle replies —
  see specs/cash_shop_browser.md §5). Two-selector split on 2/151 promoted. 0 corrections to
  existing tables; all prior facts confirmed SAME.
-->

# Crafting / Production — Recipe Table & Make-Item Flow — Clean-Room Specification

> Neutral, rewritten specification promoted from dirty-room analyst notes under **EU Software
> Directive 2009/24/EC Art. 6** (decompilation permitted solely to achieve interoperability). It
> contains **no decompiler output, no pseudo-code, no legacy symbol names, and no binary virtual
> addresses**.
>
> **Spec path (cite this):** `// spec: Docs/RE/specs/crafting.md`
>
> **Scope.** How the client lets a player produce ("make"/"craft") an item: the recipe data table
> `products.scr` and its on-disk record layout, how a recipe's result is classified as a gathered
> item versus an NPC-built item, the request the client sends to commit a production, and the result
> message the server returns. Identities and field bytes owned by neighbours are cited, not duplicated:
> - `opcodes.md` — the wire frame header + the opcode catalogue of record.
> - `structs/item.md` — the item record / item-id semantics the ingredient and produced-item fields
>   reference (owned there; this spec names item ids only).
> - `specs/npc_interaction.md` — the production NPC the player opens to begin a production (owned
>   there; this spec names the production-NPC id only).

---

## 1. There is ONE production system — no separate alchemy

The client implements a **single** "production" subsystem driving all make-an-item flows. There is
**no separate alchemy loader, no separate alchemy opcode, and no second crafting path** — smithing,
gathering and any "alchemy" flavour are all the same code path over the one `products.scr` recipe
table. *(CONFIRMED.)*

> **Engineer note:** do **not** build a second crafting/alchemy subsystem. Any distinction between
> production *kinds* is **data-driven inside the recipe record** (and/or by which production NPC is
> involved), not a separate engine. The only binary-proven category axis is the gather-vs-NPC-built
> split of §2.2.

---

## 2. `products.scr` — the recipe table

### 2.1 Loader and container

`products.scr` is a flat table of **fixed 212-byte records**. The data-corpus loader reads each
record verbatim into an in-memory recipe object (no transform on load) and inserts it into a
**map keyed by the record's first field** (the recipe id). A public lookup returns the recipe
object for a given recipe id, or none if the id is absent. *(CONFIRMED: record size 212 bytes,
verbatim copy, map keyed by the first 4-byte field.)*

A sibling **gather/collect** table (`productcollect.scr`) and a produced-item random-name table
(`productrandname.scr`) load alongside it; they are out of scope here and named only for context.

### 2.2 The gather-vs-NPC-built split rule (CONFIRMED)

Each recipe carries a **`result_display_id`** field. The client classifies the recipe's *result* by
the magnitude of this value:

- **`result_display_id` ≤ 100 → GATHER result.** The value is treated as a **collected-item index**
  (a gathered/collect-type produced item).
- **`result_display_id` > 100 → NPC-BUILT result.** The value is treated as an **actor/model id**
  (an NPC-built item, resolved through the actor/model lookup).

This threshold is the **only binary-proven category axis** for production. A literal "alchemy vs
smithing" label is **UNVERIFIED** (it would live in an unread record column the client does not read).
*(CONFIRMED: the ≤ 100 vs > 100 split and its two interpretations.)*

### 2.3 Recipe record layout (212 bytes)

Offsets are CONFIRMED where a recovered consumer reads the field; widths/types for unread regions are
UNVERIFIED (no on-disk sample row was available — the client never touches those regions).

| offset | size | type | field | notes |
|------:|----:|------|-------|-------|
| 0x00 | 4 | u32 | `recipe_id` | the recipe/product id; also the map key. **CONFIRMED.** |
| 0x04 | 32 | — | (unread region) | not read by any recovered consumer; likely name / flags / category. **UNVERIFIED.** |
| 0x24 | 4 | u32 | `result_display_id` | the produced/result item; ≤ 100 = gather index, > 100 = NPC-built model id (see §2.2). **CONFIRMED (value + semantics).** |
| 0x28 | 32 | u32[8] | `ingredient_item_id[8]` | the required-ingredient item ids, one per slot (item ids — see `structs/item.md`). Array base CONFIRMED; 8-slot length is a **strong inference** matching the 8-slot UI and the 32-byte gap to the count array — treat as parser-consistent but sample-UNVERIFIED. |
| 0x48 | 32 | u32[8] | `ingredient_count[8]` | the required quantity for each ingredient slot, parallel to `ingredient_item_id[]`. Array base CONFIRMED; 8-slot length same inference as above. |
| 0x68 | 4 | u32 | `production_npc_id` | the production NPC that performs the build (consulted on the NPC-built branch). **CONFIRMED.** See `specs/npc_interaction.md`. |
| 0x6C | 4 | u32 | `layout_flag` | selects a UI button-geometry variant for the recipe panel; observed values 0 / 1. The flag's existence is **CONFIRMED**; its exact meaning beyond "UI variant" is UNVERIFIED. |
| 0x70 | 100 | — | (unread tail) | not read by any recovered consumer; candidates: required skill / level / success-rate / cost / grade. From the client's view these are **server-side only** (see §5). **UNVERIFIED.** |

The eight-slot ingredient model is the load-bearing read: a recipe lists up to **8 ingredients**,
each an item id (`ingredient_item_id[i]`) paired with a required quantity (`ingredient_count[i]`).
The recipe UI checks the player's held quantity of each ingredient against the required count, but
that possession check is the **only** local validation the client performs (see §5).

---

## 3. Client request — select then commit

Production requests travel on the persistent game socket (major **2**). Two distinct opcodes are
involved; the **commit (2/153)** is the message the result of §4 answers.

### 3.1 `2/151` — production select / buy toggle (CONFIRMED)

A **1-byte** request carrying a single selector byte. This is the buy/select toggle in the
goods/product flow — not the commit. Two callers, two selector values: selector `0` is the
gold/regular item-shop buy-confirm path; selector `200` (0xC8) is the cash/Diamond goods panel
path. *(CONFIRMED: opcode 2/151, 1-byte body, two-selector split — CYCLE 15.)*

| offset | size | type | field | notes |
|------:|----:|------|-------|-------|
| +0 | 1 | u8 | `select_flag` | subsystem selector: `0` = gold/regular item-shop buy-confirm; `200` (0xC8) = cash/Diamond goods panel. **CONFIRMED (two-selector split, CYCLE 15).** See `specs/cash_shop_browser.md §5` for the full selector→reply mapping. |

### 3.2 `2/153` — production commit / confirm (CONFIRMED)

The **commit** of a production: a **4-byte** request built from the confirm dialog, after which the
client **arms a 60-second timeout** (it expects the §4 result within that window; the timeout
corresponds to the server-side production-timeout failure code). *(CONFIRMED: opcode 2/153, 4-byte
body, 60-second client timeout armed on send.)*

| offset | size | type | field | notes |
|------:|----:|------|-------|-------|
| +0 | 1 | u8 | `slot_a` | a selected slot/index. **CONFIRMED present.** |
| +1 | 1 | u8 | `slot_b` | a selected slot/index. **CONFIRMED present.** |
| +2 | 1 | u8 | `list_slot` | the selected list slot. **CONFIRMED present.** |
| +3 | 1 | u8 | `production_npc_index` | the production NPC index; `0xFF` (255) = none. **CONFIRMED present.** |

> **RUNTIME-ONLY:** the exact meaning of the four commit bytes (which is recipe vs ingredient-slot vs
> quantity) is inferred from the source struct offsets, not proven end-to-end — confirm the
> value→option mapping via a capture / `?ext=dbg`.

> **CONSUMER-CONFIRMED (CYCLE 15):** The commit builder stamps **no expected-reply minor field** in
> the request body — the server decides the reply and routes it unconditionally through the major-4
> inbound dispatch table. The reply is always `4/79 SmsgCraftingResult` (§4). The opcodes `3/8
> SmsgShopPageUpdate` and `4/113 SmsgItemShopPurchaseResult` are replies to the buy-toggle `2/151`
> (see `specs/cash_shop_browser.md §5`), **not** replies to `2/153`.

---

## 4. Server result — `4/79` `SmsgCraftingResult` (CONFIRMED)

The server answers a production commit with **S2C `4/79`** `SmsgCraftingResult`, a **52-byte** body.
It carries a success flag, a failure error code, and the produced-item dwords. *(CONFIRMED: opcode
4/79, 52-byte body.)* `4/79` is the **sole** reply to `2/153`; the hypothesis that this commit
could be answered by `3/8 SmsgShopPageUpdate` or `4/113 SmsgItemShopPurchaseResult` is **REFUTED**
(CYCLE 15) — those are replies to the buy-toggle `2/151` (§3.1 and `specs/cash_shop_browser.md §5`).

| offset | size | type | field | notes |
|------:|----:|------|-------|-------|
| 0x08 | 1 | u8 | `success_flag` | `1` = success (apply the produced item); any other value = failure. **CONFIRMED.** |
| 0x09 | 1 | u8 | `error_code` | failure error code in range **1..5** (meaningful only when `success_flag != 1`). **CONFIRMED present; value meanings RUNTIME-ONLY (see §4.1).** |
| 0x0A | 1 | u8 | `result_subtype` | applier case selector (e.g. NPC-mediated vs item-make). **CONFIRMED present.** |
| 0x0C | 4 | u32 | `result_value_a` | a produced-result value (gate compared against the B/C values). **CONFIRMED present.** |
| 0x10 | 4 | u32 | `result_value_b` | a produced-result value. **CONFIRMED present.** |
| 0x14 | 4 | u32 | `result_value_c` | a produced-result value. **CONFIRMED present.** |
| 0x20 | 1 | u8 | `produced_slot` | destination slot index for the produced item. **CONFIRMED present.** |
| 0x24 | 4 | u32 | `produced_item_0` | a produced-item dword (item id — see `structs/item.md`). **CONFIRMED present.** |
| 0x28 | 4 | u32 | `produced_item_1` | a produced-item dword. **CONFIRMED present.** |
| 0x2C | 4 | u32 | `produced_item_2` | a produced-item dword (used as a count in the success notice text). **CONFIRMED present.** |
| 0x30 | 4 | u32 | `produced_item_3` | a produced-item dword. **CONFIRMED present.** |

> **RUNTIME-ONLY:** the precise semantic label of each produced-item dword (id vs count vs grade vs
> durability) and of the value-A/B/C triple needs a capture to settle. *Present-and-located* is
> CONFIRMED; the exact labels are inferred.

### 4.1 Failure error codes (`error_code`, 1..5)

On failure the client shows a localized message keyed by the error code and resets its local
production state. The **five codes are CONFIRMED to exist (1..5)**; their **exact meanings are
RUNTIME-ONLY** (capture / debugger-pending) and are summarized below only as the analyst's reading
of the fallback strings — do **not** treat the meanings as load-bearing:

| code | reading (RUNTIME-ONLY — not load-bearing) |
|----:|-------------------------------------------|
| 1 | a production request is already in flight |
| 2 | a reply arrived earlier than expected / out of sequence |
| 3 | ingredient slots not filled in the required order |
| 4 | the request body could not be read (malformed) |
| 5 | server-side production timeout |

### 4.2 Success path

When `success_flag == 1`, the client applies the produced item(s) (the `produced_item_*` dwords at
0x24..0x30) into its world/inventory state at the `produced_slot`, plays a production effect/sound,
and posts a localized success notice. The produced result is **server-authoritative** — the client
applies what the server reports; it does not decide what was produced.

---

## 5. Gating is server-side (CONFIRMED)

The **client performs NO success-rate computation and NO recipe gating beyond ingredient
possession.** It checks the player's held quantity of each ingredient against the recipe's required
counts (for UI display), then sends the commit; **the server alone** validates skill/level/cost,
rolls the success rate, consumes ingredients, and produces the result, reporting the outcome via
`4/79`. *(CONFIRMED: the client computes no success rate and does no server-side validation.)*

> **RUNTIME-ONLY:** the success-rate formula and any skill/level/cost gating live entirely on the
> server. If columns for those exist in the unread record regions (§2.3), they are server-side from
> the client's perspective and cannot be confirmed by client static analysis.

---

## 6. Recipe → result flow (neutral prose)

1. The player opens a production NPC (or the production panel) to begin — see
   `specs/npc_interaction.md` for the NPC interaction.
2. The available recipe ids for that context are gathered into a list; each is confirmed to exist in
   `products.scr` via the recipe lookup, then added to the panel.
3. Selecting a recipe resolves its 212-byte record (§2.3) and renders up to 8 ingredient slots —
   each ingredient id with its required count checked against the player's held quantity — plus the
   result/preview from `result_display_id` (gather item if ≤ 100, NPC-built model if > 100, §2.2).
4. The player confirms. The client sends the lighter select toggle `2/151` (1 byte, §3.1) and the
   commit `2/153` (4-byte slot tuple, §3.2), arming a 60-second timeout on the commit.
5. The server validates and rolls the outcome (server-side; the client does no rate roll, §5) and
   replies `4/79 SmsgCraftingResult` (52-byte body, §4):
   - `success_flag == 1` → apply the produced item(s), post a success notice, refresh the panel.
   - `success_flag != 1` → failure with `error_code ∈ {1..5}`; the client resets local production
     state.

So `products.scr` is the recipe database (recipe id, result, up to 8 ingredient ids + counts,
production NPC); the client UI binds to it read-only; ingredient consumption, the success roll, and
the produced result are all decided by the server and reported by `4/79`.

---

## 7. Cross-reference map

| This spec covers | Owned elsewhere — cite, don't duplicate |
|---|---|
| The production opcodes `2/151`, `2/153`, and `4/79` (§3, §4) | `opcodes.md` (catalogue of record), `packets/` (field specs) |
| The ingredient item ids and the produced-item dwords (item-id semantics) | `structs/item.md` |
| The production NPC the player opens / `production_npc_id` (§2.3, §6) | `specs/npc_interaction.md` |
| The cash/Diamond goods panel selector-200 path of `2/151` and its reply `4/113` | `specs/cash_shop_browser.md §5` |
