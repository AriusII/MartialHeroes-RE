# Format: events_scr  (`.scr` script tables: game events, anti-bot captcha, and the `.scr` role index)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file.
>
> Promoted from black-box VFS harness observation AND neutral static-loader notes under
> EU Software Directive 2009/24/EC Art. 6. No decompiler output and no binary virtual addresses
> appear anywhere in this file.
> C# parsers MUST cite this spec on every offset / magic / stride: `// spec: Docs/RE/formats/events_scr.md`.
>
> status: sample-verified (the events.scr stride, 1848 record count, all field offsets/sizes, and
>   the four consumed fields match real VFS bytes) + loader-confirmed (the runtime loader's
>   field-consumption contract is established from neutral static control-flow notes). Genuinely
>   ambiguous items (mode_flag's exact 0/1 semantics; the 5 anomalous records' @0x08 sub-block) stay
>   capture/debugger-pending.
> ida_reverified: 2026-06-24
> ida_anchor: 263bd994c927c20a38624cf0ca452eaef365057fa9db1543d8f668c14a6fd8ee
> evidence: [static-ida, vfs-sample]
> CORRECTED CYCLE 1 (ida_anchor 263bd994, 2026-06-19): events.scr runtime linkage = event_id→record
>   exact-match map, consumed by item/shop/exchange UI only (NOT timer/zone/captcha; captcha =
>   autoquestion_cl.scr); see Runtime-linkage section (§1.9). Byte layout (§1.1–§1.8) unchanged.
> CYCLE 7 note (verification: IDB SHA 263bd994, CYCLE 7 (2026-06-20)): disambiguated the table's `event_id` keys from the
>   runtime timed-event ENGINE ids (e.g. 10003 = death-respawn countdown, 10001 = engine-quit); those
>   engine ids are NOT events.scr records — see §1.10. No byte-layout change.
> RE-VERIFIED (2026-06-24, ida_anchor 263bd994c927c20a38624cf0ca452eaef365057fa9db1543d8f668c14a6fd8ee,
>   evidence [static-ida, vfs-sample]): independent full-file re-confirmation against the 960960-byte
>   sample (1848 records, remainder 0). Every field offset/size/type in §1.3, the four CONSUMED fields,
>   the 5 anomalous records (event_ids 30133–30137, @0x08 = 1,000,000/500,000, @0x0C = 20/50/200,
>   zeroed trailer), and the day-window distribution (14 distinct (start,end) pairs; see §1.4 wording
>   correction below) all re-confirmed with zero contradictions. The "/script/events.scr" path string
>   location inside the script-path pool consumed by the master bulk-asset loader is re-confirmed
>   (static, same IDB SHA). No layout correction; §1.4 wording tightened (13→14 distinct pairs).
> conflicts: none open in §1/§2. §3 is an estimate index only — its `userlevel.scr` stride estimate
>   (120) is REFUTED (see §3); the authoritative value is 60 B / 300 records (scr.md / config_tables.md).
>
> The runtime client loader for `events.scr` has been characterized from neutral RE notes (which
> fields it actually dereferences); the per-field VALUE distributions come from a full-file black-box
> harness pass over 1848 records. Where the two disagree on a field's ROLE, the loader is
> authoritative for "what the client consumes" and the harness is authoritative for "what values
> appear in the blob".

The `.scr` extension is used throughout `data/script/` for a large family of unrelated flat data
tables (no shared wire format). This document fully specifies the two `.scr` tables owned by this
lane — `events.scr` and `autoquestion_cl.scr` — and provides a role-classification index of the
remaining `.scr` files so an engineer knows which table does what before opening one. Tables already
owned by other specs/lanes (`items.scr`, `citems.scr`, `npc.scr`, `quests.scr`, `mapsetting.scr`,
and the `discript.sc` / help / guild text group) are out of scope here and only referenced.

---

## Section 1 — `events.scr` (timed game-event definition table)

**Confidence: CONFIRMED (static loader) that the client has a runtime loader for this file and that
it consumes only four record fields (see §1.6). Stride 520 B and the record-count rule are CONFIRMED
by the loader's own size-validation AND SAMPLE-VERIFIED by VFS geometry (file size ÷ 520 = 1848
records, remainder 0). Per-field offsets, sizes and value distributions SAMPLE-VERIFIED across all
1848 records of one sample (re-verified 2026-06-16, build 263bd994).**

### 1.1 Identification

- **Extension:** `.scr`
- **Found in:** `.pak` / VFS archive; logical path: `data/script/events.scr`. The client opens this
  file by name from the master bulk-asset load pass (the same pass that loads `items.scr`,
  `skills.scr`, `mobs.scr`, `npc.scr`, `quests.scr`, …). It is a normal client-loaded table.
- **Magic / signature:** None. No file-level header, no version field.
- **Endianness:** Little-endian throughout.
- **Encoding:** No text in this file — all fields are numeric / ID-based. A full-record scan of 20
  representative records for printable CP949 runs found none. CONFIRMED all-numeric.
- **Record count source:** Derived from file size divided by stride. No stored count. The client
  loader explicitly validates `file_size % 520 == 0` and sets `record_count = file_size / 520`.
  CONFIRMED. (Different builds carry different record totals: one harness sample divides exactly as
  1848 records; another build sample divides as a smaller record count — both with zero remainder.)
- **Record stride:** 520 bytes (0x208). CONFIRMED by the loader's modulo check.

### 1.2 Container structure

A headerless flat array of fixed-size 520-byte records. Record count = `file_size / 520`.

At load time the client performs a blind bulk copy of the entire file into one heap buffer, then
iterates the records reading ONLY the first u32 of each (`event_id` @0x00) to build an
`event_id → record` lookup map. No other field is parsed at load time. Every other field is read
later, on demand, by the consumers that look a record up by `event_id`. CONFIRMED.

The array may be sparsely populated: in standard records, the array fields (§1.3) are zero-terminated,
so a record with all-zero arrays carries no participants/rewards.

### 1.3 Record layout (standard record, 520 bytes)

Offsets are relative to the start of each record. The **"Client read?"** column is the load-bearing
distinction for an engineer: only the four fields marked CONSUMED are dereferenced by the client at
runtime. Every other field is present in the in-memory blob but is never read — do NOT spend effort
modelling or validating the unread fields beyond skipping over them.

| Offset | Size | Type | Field | Client read? | Notes / observed values | Confidence |
|-------:|-----:|------|-------|--------------|-------------------------|------------|
| 0x00 | 4 | u32LE | `event_id` | **CONSUMED** (primary key) | Unique non-zero identifier. Map key at load; lookup argument for every consumer. Observed range 10551–31704; two allocation epochs — see §1.4. | CONFIRMED (loader) / HIGH (values) |
| 0x04 | 2 | u16LE | `event_type` | not read | Observed value 1 in all standard records (1 in the 5 anomalous records, see §1.5). | SAMPLE-VERIFIED (single value) |
| 0x06 | 2 | u16LE | `day_count` | not read | Observed 7 in most records; 1 in the 5 anomalous records. Likely "active days per cycle". | SAMPLE-VERIFIED |
| 0x08 | 68 | u8[68] | `reserved_a` | **not read** | Zero in all 1843 standard records. NON-ZERO structured block only in the 5 anomalous records (§1.5). The client loader and all consumers ignore this region entirely. | CONFIRMED not-consumed / HIGH (values) |
| 0x4C | 4 | u32LE | `level_min` | not read | Observed 100 in standard records, 0 in anomalous records. Plausibly minimum eligible level, but NO consumer dereferences it in the recovered client paths. | SAMPLE-VERIFIED value / CONFIRMED not-consumed |
| 0x50 | 4 | u32LE | `level_max` | not read | Observed 1000 in standard records, 0 in anomalous records. Same status as `level_min`. | SAMPLE-VERIFIED value / CONFIRMED not-consumed |
| 0x54 | 2 | u16LE | `day_window_start` | **not read** | Inclusive start day of an eligible window within a 144-day cycle (see §1.4); 0 = no day restriction. Present in the blob but NOT dereferenced by the client. | HIGH (values) / CONFIRMED not-consumed |
| 0x56 | 2 | u16LE | `day_window_end` | **not read** | Inclusive end day; pairs with `day_window_start` in 12-day windows over a 144-day cycle (§1.4). Not dereferenced by the client. | HIGH (values) / CONFIRMED not-consumed |
| 0x58 | 2 | u16LE | `pad_58` | not read | Always 0 across all 1848 records. Padding. | HIGH |
| 0x5A | 2 | u16LE | `sub_flag_a` | not read | 0 or 1; value 1 in 1179/1848 records. All four sub-flags (0x5A–0x60) are always equal within a record. Not dereferenced by the client. | SAMPLE-VERIFIED / CONFIRMED not-consumed |
| 0x5C | 2 | u16LE | `sub_flag_b` | not read | Identical distribution to `sub_flag_a`; equals it in every record. Not read. | SAMPLE-VERIFIED / CONFIRMED not-consumed |
| 0x5E | 2 | u16LE | `sub_flag_c` | not read | Identical distribution to `sub_flag_a`. Not read. | SAMPLE-VERIFIED / CONFIRMED not-consumed |
| 0x60 | 2 | u16LE | `sub_flag_d` | not read | Identical distribution to `sub_flag_a`. Not read. | SAMPLE-VERIFIED / CONFIRMED not-consumed |
| 0x62 | 2 | u8[2] | `pad_62` | not read | Always 0. Padding. | HIGH |
| 0x64 | 2 | u16LE | `mode_flag` | **CONSUMED** | LIVE display/eligibility mode flag. One consumer branches on `== 1`, another branches on `== 0`, to switch display modes. Observed 1 in 1233/1848 records, 0 otherwise. Earlier drafts mislabeled this region as reserved/padding — it is an actively read field. See §1.6. | CONFIRMED consumed (loader) / PLAUSIBLE (exact semantics) |
| 0x66 | 2 | u8[2] | `pad_66` | not read | Always 0. Padding. | HIGH |
| 0x68 | 200 | u32LE[50] | `rate_array` | **CONSUMED** | Fixed slot of up to 50 u32 entries (0x68–0x12F), zero-terminated (first zero slot ends the populated range). The client consumes each value as a **rate/percentage**: it divides by 1,000,000 and displays the result as a `%` against the associated actor in `actor_array`. NOT an ID array. Observed raw values span small integers, 100k–500k, and 500k–1,000,000 — consistent with fractional rates scaled by 1e6. DISJOINT from `actor_array` value space. See §1.6 / §1.7. | CONFIRMED consumed (loader) / HIGH (÷1e6 rate role) |
| 0x130 | 208 | u32LE[52] | `actor_array` | **CONSUMED** | Fixed slot of up to 52 u32 entries (0x130–0x1FF), zero-terminated (iteration stops at the first non-positive/zero slot). The client iterates these and resolves each as an **actor ID**. Values use the 9-digit ID namespace shared with `items.scr` / `citems.scr` (e.g. 213010002, 215010101; one consumer compares an entry against a 9-digit constant). Pairs positionally with `rate_array` (slot N rate ↔ slot N actor). | CONFIRMED consumed (loader) / HIGH |
| 0x200 | 8 | u8[8] | `record_trailer` | **not read** | Fixed 8-byte tail. In standard records it is the constant byte pattern decoding as three u16 = 1 followed by u16 = 0; in the 5 anomalous records it is all-zero. NOT a checksum (identical across all standard records regardless of content). Never dereferenced by the client. | CONFIRMED not-consumed / HIGH (constant) |

- **Record count source:** `file_size / 520` (loader-validated `% 520 == 0`).
- **Record stride:** 520 bytes.

### 1.4 Field-pair semantics (day window and ID allocation)

These describe observed VALUE structure. They are useful for understanding the data but are NOT
required to parse what the client consumes — the client does not read the day-window fields.

- **Day window (`day_window_start` @0x54, `day_window_end` @0x56):** when non-zero, the pair encodes
  an inclusive day range within a repeating 144-day cycle. Observed combinations partition the cycle
  into twelve consecutive 12-day windows (1–12, 13–24, …, 133–144), plus a full-cycle variant
  (start = 1, end = 144) and a no-restriction state (both 0). 14 distinct (start, end) pairs seen
  across all 1848 records: the 12 window pairs, (1, 144) full-cycle, and (0, 0) no-restriction.
  HIGH (deterministic across the sample).
- **`event_id` allocation epochs:** the early ~144 records use a step of 10 between consecutive IDs;
  after a single large jump, the later ~1700 records use a step of 1 (dense consecutive IDs). The
  early step-10 epoch's record count aligns with the 144-day cycle length. SAMPLE-VERIFIED.

### 1.5 Anomalous 5-record sub-type (event_ids 30133–30137)

Exactly five records carry a DIFFERENT sub-layout. They have `event_type = 1`, `day_count = 1`,
`level_min = level_max = 0`, all day-window/sub-flag fields zero, empty `rate_array` and
`actor_array`, and a ZEROED `record_trailer`. Their distinguishing data lives in the `reserved_a`
region @0x08, which for these records holds a structured block of u32 values rather than zeros:

- The first u32 (@0x08) is a large round value (observed 500,000 or 1,000,000) — plausibly a
  gold / currency threshold.
- The second u32 (@0x0C) is a small even value (observed 20, 50, 100, 150, 200) — plausibly a
  percentage or step.
- Further u32 slots (@0x10–0x20) hold packed values that may decode as paired u16 sub-fields.

This sub-type's exact semantics are UNVERIFIED (no loader path consumes these records' @0x08 block —
the client never reads `reserved_a`). An engineer should treat these five records as data-present but
client-unconsumed, identical in handling to standard records (only `event_id` is indexed; their
`rate_array`/`actor_array` are empty so consumers find nothing to display). SAMPLE-VERIFIED layout /
UNVERIFIED semantics.

### 1.6 What the client actually consumes (authoritative parse contract)

The runtime loader and its consumers dereference exactly four record fields. An `Assets.Parsers`
implementation that reproduces client behaviour needs ONLY these:

1. **`event_id` (u32 @0x00)** — read at load to build the `event_id → record` map; the lookup key
   for every consumer.
2. **`mode_flag` (u16 @0x64)** — a display/eligibility mode selector; consumers branch on `== 0` vs
   `== 1`.
3. **`rate_array` (u32[50] @0x68)** — zero-terminated; each entry divided by 1,000,000 and shown as a
   percentage against the positionally-paired actor.
4. **`actor_array` (u32[52] @0x130)** — zero-terminated (stop at first zero/non-positive); each entry
   is a 9-digit actor ID resolved against the actor/item namespace.

All other fields (`event_type`, `day_count`, `reserved_a`, `level_min`, `level_max`,
`day_window_*`, `sub_flag_*`, all `pad_*`, `record_trailer`) are present in the on-disk/in-memory
record but are NEVER read by the client. A faithful parser may expose them for documentation but must
not gate any behaviour on them.

### 1.7 Semantic role

Each record defines one timed game event: an `event_id` key, a `mode_flag` that selects how the event
panel is displayed, and two positionally-paired fixed-capacity arrays — `rate_array` (per-slot
contribution/drop rate, value ÷ 1e6 = percentage) and `actor_array` (the 9-digit actor/item ID each
rate applies to). The remaining numeric fields (day window, level bounds, sub-flags) describe the
event in the data but are not exercised by this client build.

### 1.8 Known unknowns (events.scr)

- The exact meaning of `mode_flag` @0x64 (which two display modes `0`/`1` select) is PLAUSIBLE only;
  confirming the branch under live data is an optional debugger task.
- Whether `rate_array` slot N is strictly positionally paired with `actor_array` slot N (versus some
  other association) is HIGH-confidence but not byte-proven; an engineer should assume positional
  pairing and verify against rendered output.
- The semantics of the 5 anomalous records' @0x08 sub-block (gold threshold + percentage steps +
  packed flags) are UNVERIFIED — no client path consumes them.
- Why `sub_flag_a..d` appear as four lockstep copies, and the role of the day-window fields, are
  UNVERIFIED — and moot for parsing, since the client reads none of them.
- `event_type` and `day_count` were observed with a near-single value each; whether other values
  occur in other game versions is unknown (and also client-unconsumed).

### 1.9 Runtime linkage / consumer surface (how the table is loaded, looked up, and used)

This section describes the runtime WIRING around `events.scr` — what loads it, where the parsed table
lives in memory, what selects a row, and what consumes it — as distinct from §1.3's byte layout. It
is the operative reference for re-implementing the table's behaviour faithfully.

**Confidence: CONFIRMED (static) for the loader → storage → map chain, the single lookup accessor and
its consumer call sites, and the key rule. PLAUSIBLE for the exact per-panel meaning of each consumed
event (drop-rate display vs exchange-choice list vs goods toggle). CONFIRMED that the anti-bot captcha
is NOT sourced here. One OPEN-RISK is carried (the item-side join column — see §1.9.5).**

#### 1.9.1 Loader linkage

`events.scr` is loaded **once, at boot**, by the data-table corpus loader (the same bulk pass that
loads the other `data/script/*.scr` tables). The events.scr table is the first table that pass loads.
A dedicated loader routine takes the logical path `data/script/events.scr`, validates that the file
size is a whole multiple of the 520-byte record stride, sets `record_count = file_size / 520`,
bulk-copies the entire file into one heap buffer, and then iterates the records reading **only the
leading `event_id` u32 (@0x00) of each** to populate an `event_id → record` lookup index. No other
field is touched at load time. (Consistent with §1.2 and §1.6.) CONFIRMED.

#### 1.9.2 Parsed-table storage (three cooperating roles)

The loader writes the parsed table into **three cooperating storage roles** that together form one
owning unit:

1. **Record vector** — the container object that owns the contiguous blob of fixed 520-byte records
   (it carries the 520-byte element stride and handles sizing/growth). It is the lifetime owner of
   the record data.
2. **Record-blob base pointer** — the data pointer of that vector, i.e. the base address of the
   contiguous record blob (the bulk-loaded file image). Every record-field read indexes off this
   base: **record N lives at `base + 520·N`**.
3. **`event_id → record` map** — the root of a balanced (red-black) tree index keyed on the record's
   `event_id` (u32 @0x00), whose value is a pointer to that record inside the blob. One entry is
   inserted per record at load. This tree is the **lookup index** every consumer uses.

A teardown/reset routine clears the map first, then the record vector — confirming the three roles
are one owning unit released together. CONFIRMED.

#### 1.9.3 The KEY RULE — `event_id` exact-match lookup (load-bearing)

> A runtime trigger always carries an **`event_id` (u32)**. The client maps it to a row by an
> **exact-match search of the `event_id → record` balanced tree** — there is a single shared lookup
> accessor that performs this tree search and is the **only** read path into the table.
>
> - **Map MISS** (no record with that `event_id`): the accessor yields "none", and the consumer
>   **no-ops** — the tooltip shows nothing, the eligibility predicate is false, the choice list is
>   empty.
> - **Map HIT**: the accessor yields the record pointer. The consumer then reads the `mode_flag`
>   (u16 @0x64) — the eligibility/display-mode selector (value `1` vs `0`) — and walks the two
>   positionally-paired fixed arrays: the `rate_array` (u32[50] @0x68, each value **divided by
>   1,000,000** to yield a percent) and the `actor_array` (u32[52] @0x130, each entry an actor/item
>   id, **zero-terminated**, each resolved through the actor manager by id).

There is **NO row-index addressing and NO per-row type-byte dispatch.** Row selection is purely the
`event_id` primary-key map lookup; once a row is found the consumer branches on the `mode_flag`, never
on a "row type". CONFIRMED. (This matches §1.6: only `event_id`, `mode_flag`, `rate_array`,
`actor_array` are ever consumed.)

#### 1.9.4 Consumer surface — item / shop / exchange UI ONLY

The consumer surface for `events.scr` is **entirely an item / shop / exchange UI lookup system**.
There is **no** timer, clock, day-window, level-gate, zone-entry, or anti-bot trigger that reads this
table in this build. Every consumer reaches a row through the single `event_id` lookup accessor
(§1.9.3); the distinct consumer behaviours observed are:

- **Tooltip / display builder** — reads `mode_flag`, then iterates the `actor_array`, resolving each
  entry to a name via the actor manager and annotating it with its positionally-paired `rate_array`
  value (÷ 1,000,000) shown as a `%`. It builds choice/option lines into a tooltip/display buffer.
  This is the path reached when an item in a shop/storage/exchange-style panel is hovered or opened.
  (The "drop / contribution rate display" role of §1.7.)
- **Eligibility predicate** — returns true only when a record is present **and** its `mode_flag`
  equals `1`. It is the gate at the head of the selection flow.
- **Actor choice-list builder** — when the eligibility predicate passes, iterates the `actor_array`
  (zero/non-positive-terminated, capped at 50 entries) to materialise a selectable list of actors for
  the player. The selection flow chains these: predicate first (abort if false), then build the list;
  an empty list broadcasts a localized "nothing available" chat message.
- **Goods-panel select** — for a goods row of a specific actor-class tag, looks up the item's stored
  `event_id` and tests the **first** `actor_array` entry against a fixed actor id to toggle a UI
  element.

The exact per-panel semantics (rate display vs exchange choice vs goods toggle) are PLAUSIBLE; the
fact that every one of these is an item/shop/exchange UI interaction (and none is a timer/zone/captcha
trigger) is CONFIRMED from the call graph.

#### 1.9.5 The trigger — a UI interaction with an event_id-bearing item/slot

A row **fires on a UI interaction with an item or slot that carries an `event_id`** — never on a
timer, clock, day-window, level, or anti-bot condition. Concretely:

- **Hovering / opening** an item in a shop/storage/exchange panel renders the event's `actor_array`
  with per-actor `%` rates into the tooltip (display builder).
- **Selecting / clicking** an option in such a panel runs the `mode_flag == 1` eligibility gate, then
  materialises the actor choice list for the player to pick from.
- **Selecting** a goods-panel row of the relevant actor-class tag branches on the event's first
  `actor_array` entry.

In every case the `event_id` is supplied by the **UI context** — the item record or panel slot the
player interacted with stores the `event_id` that joins it to the `events.scr` row. The record's
day-window, level, and sub-flag fields are never read by any of these paths (matches §1.6).

> **OPEN-RISK:** exactly which item/slot field stores the `event_id` join column on the item side has
> not been byte-pinned — it was observed indirectly (one panel reads it off the item record at an
> item-side offset; the tooltip/select panels receive it from slot context). Treat the **item → event_id
> join column** as a follow-up to pin against the item-record / slot layout.

#### 1.9.6 Not the captcha source (correction)

The anti-bot **captcha** question pool is the **separate `autoquestion_cl.scr` table (Section 2)**,
which is **server-pushed and has no client loader** — `events.scr` is NOT the captcha source. Any
earlier framing of `events.scr` as a "captcha + role index" is superseded: `events.scr` is an
item/shop/exchange event table only. This corroborates §2.4 (no client load path for
`autoquestion_cl.scr`; the captcha text arrives over the wire). CONFIRMED.

Likewise, the record's day-window, level-bound, and sub-flag fields (§1.3) are **never read by any
consumer** — only `event_id`, `mode_flag`, `rate_array`, and `actor_array` participate in the runtime
linkage above.

#### 1.10 NOT the runtime timed-event engine (id-namespace disambiguation)

There is a SEPARATE runtime **timed-event engine** in the client — a generic enqueue/tick scheduler
that the gameplay code arms with a numeric `event_id`, a tick interval, and a remaining-count, and
which is unrelated to this VFS table. Its ids share the small-integer 5-digit shape of some
`events.scr` keys, so it is easy to confuse the two. They are **distinct**: the timed-event engine
keys are NOT looked up in the `events.scr` `event_id → record` map, and conversely no `events.scr`
record drives a timer (the entire consumer surface is item / shop / exchange UI — §1.9.4).

Two timed-event-engine ids surfaced while reverse-engineering the death/respawn flow; recorded here
ONLY to fence them off from this table's key space:

| Timed-event engine id | Role | Notes |
|----------------------:|------|-------|
| **10003** | **Death-respawn countdown** | Local-player respawn timer: a one-second tick that counts a remaining-seconds value down to zero (auto-respawn fires at zero). The countdown duration is **600 seconds** for a normal death (an alternate short **20-second** value is selected in a PvP-zone branch — **region-gated**, the same per-region zone-type that `region_grid.md` describes), and a short one-shot for the special no-modal death cause. The countdown also drives the visual revive of remote actors. CONFIRMED (static IDA). The behavioural death/respawn flow (state fields, the modal, the respawn opcode/choice) belongs to `world_exit.md` — see the cross-reference there; this table does not participate. |
| **10001** | Engine-quit signal | Handled in the same timed-event tick path as a teardown/shutdown trigger; entirely unrelated to respawn. Recorded only to avoid mistaking it for an `events.scr` key. CONFIRMED (static IDA). |

Operative takeaway for an engineer: **do NOT look up 10003 or 10001 in `events.scr`.** An `events.scr`
record is reached only by a UI interaction that carries the record's own `event_id` (§1.9.5); the
death-respawn countdown and the engine-quit signal are runtime timed-event-engine ids handled outside
this table. CONFIRMED.

---

## Section 2 — `autoquestion_cl.scr` (NPC anti-bot captcha Q&A table)

**Status correction (CONFIRMED): the client has NO runtime loader for this file.** The captcha
question/prompt the client displays is supplied at runtime by the server (network packet), not loaded
from a client-side `.scr`. The byte layout below remains valid as a description of the file as it
exists in the VFS, but an `Assets.Parsers` implementation does NOT need to parse it to reproduce
client behaviour — the client never opens it.

**sample_verified: stride confirmed across all 1300 records; string decode confirmed CP949 for the
first 5 records; field-slot layout verified at sample level.**

### 2.1 Identification

- **Extension:** `.scr`
- **Found in:** `.pak` / VFS archive; logical path: `data/script/autoquestion_cl.scr`. Present in the
  VFS but NOT referenced by any client load path (see §2.4). CONFIRMED absent from the client's
  `.scr` loader table; no `autoquestion`/`autoq`/`captcha` path or string exists in the client.
- **Magic / signature:** None. No file-level header, no version field.
- **Endianness:** Little-endian for the numeric ID field.
- **Encoding:** CP949 (EUC-KR), null-terminated, for the two text fields. Register the code page once
  at startup (`Encoding.GetEncoding(949)`) per project convention.
- **Record count source:** file size / stride. A known sample yields exactly 1300 records,
  sequentially numbered, densely packed, no trailer.
- **Record stride:** 92 bytes (0x5C).

### 2.2 Record layout (92 bytes)

The `_cl` suffix would suggest a client-side table, but in this build it is NOT client-read (§2.4).
It holds only display text; no numeric answer is present.

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| 0x00 | 4 | u32LE | `question_id` | 1-based sequential identifier (1, 2, 3, …). | HIGH |
| 0x04 | 84 | char[] (CP949) | `text_block` | An 84-byte block (0x04–0x57) holding two consecutive null-terminated CP949 strings: the question text, then the answer-prompt instruction text. See split note below. | HIGH |
| 0x58 | 4 | u8[4] | `record_padding` | Zero padding before the next record. | HIGH |

**Split within `text_block`:** the question text starts at 0x04 and is null-terminated; the
answer-prompt string begins immediately after that null and is itself null-terminated; the remainder
of the 84-byte block is zero-filled. The boundary between the two strings is data-dependent (the
position of the first null), not a fixed offset. A parser must read the first null-terminated string
from 0x04, then read the second null-terminated string starting at the byte after that terminator,
both bounded by the end of the 84-byte block.

- **Record count source:** file size / 92 (no stored count).
- **Record stride:** 92 bytes.

### 2.3 Semantic role

An NPC auto-question (captcha) table for anti-bot / anti-AFK-farm checks: an NPC poses a simple
arithmetic question (Korean word-form) and the player types the numeric answer. The first text string
is the question; the second is a constant UI instruction. In THIS client build the file is unused —
the displayed question is server-pushed (see §2.4).

### 2.4 Client integration (CONFIRMED via neutral loader notes)

- **No client loader exists for `autoquestion_cl.scr`.** It is absent from the master `.scr` loader
  table; no path string, path-builder, or filename for it appears anywhere in the client. The only
  related artefact is a UI panel class (`AutoQuestionPanel`) that displays the captcha — with NO
  associated data-file load path. CONFIRMED.
- **The correct answer is NOT stored in this file**, and — because the client never even reads the
  file — the client cannot grade the answer locally. The captcha question/prompt is delivered to the
  panel at runtime via a server packet; the client only displays it and submits the player's input;
  the server validates. A faithful client must source the captcha text from the wire, not from this
  VFS table, and must not attempt local grading. CONFIRMED.
- The second text string was observed constant across the first 5 records only (the answer-prompt
  instruction); SAMPLE-VERIFIED.

---

## Section 3 — Role-classification index of the remaining `.scr` tables

This is a routing aid, NOT a byte-layout spec. It tells an engineer what each `.scr` table is for
and how reliably that role was determined, so a parser is written against the right table. Most
strides listed here are first-pass divisor estimates; treat any row still tagged UNVERIFIED as
unconfirmed until promoted into its own spec section. The role guesses derive from CP949 name
decoding, first-field patterns, and clean file-size divisors.

> **Re-verification note (2026-06-16, build 263bd994, VFS sample):** several rows below were
> corroborated by VFS geometry and are now tagged **SAMPLE-VERIFIED** — `userlevel.scr` (60 B / 300,
> the 120/150 estimate REFUTED), `userpoint.scr` (32 B / 301), `system_control.scr` (8 B / 114),
> `playtime_reward.scr` (32 B / 5), `viplevels.scr` (92 B / 9), `mobs.scr` (488 B / 3997),
> `statue.scr` (36 B / 430), `skillcategory.scr` (564 B / 17). The remaining UNVERIFIED rows were
> not re-surveyed. The authoritative byte layouts for these tables live in `scr.md` /
> `config_tables.md`, not here.

Out of scope (owned elsewhere): `items.scr`, `citems.scr`, `npc.scr`, `quests.scr`,
`mapsetting.scr`, and the `discript.sc` / help / guild-text group.

| Filename | Text/Binary | Stride / records (estimate) | Role guess | Confidence |
|----------|-------------|-----------------------------|------------|------------|
| `mobs.scr` | binary + CP949 | 488 B / 3997 records | Monster definition table (mob_id, name, stats, AI params). | SAMPLE-VERIFIED (stride + count) |
| `mobsitem.scr` | binary + CP949 | variable (UNVERIFIED) | Mob drop / loot table (per-mob item lists). Shares the `mobs.scr` name header; likely variable-length records. | HIGH (role) / UNVERIFIED (stride) |
| `skills.scr` | binary + CP949 | stride 280 | Skill definition table (skill_id, name, stats, class tags). | MEDIUM |
| `npcs.scr` | binary + CP949 | UNVERIFIED | NPC stat / profile table (shop + AI info), distinct from `npc.scr` dialogues. | HIGH (role) |
| `products.scr` | binary + CP949 | UNVERIFIED | Crafting product / recipe catalog (craftable item names). | HIGH (role) |
| `productcollect.scr` | binary | UNVERIFIED | Product ingredient / material requirement join table (product_id → ingredients). | MEDIUM |
| `productrandname.scr` | binary + CP949 | small table | Random-craft affix (prefix/suffix) name table. | MEDIUM |
| `upgradeitems.scr` | binary | variable (UNVERIFIED) | Item upgrade / enhancement recipe table (materials + success-rate floats per step). Likely variable-length records. | HIGH (role) / UNVERIFIED (stride) |
| `itemscale.scr` | binary | stride ~8 (u32 + f32 pairs) | Item visual-scale parameter table (scale per item_id). | HIGH |
| `userlevel.scr` | binary | **60 B / 300 records** (the earlier 120/150 estimate is REFUTED) | Per-character-level stat / multiplier table. See scr.md / config_tables.md. | SAMPLE-VERIFIED |
| `userpoint.scr` | binary | 32 B / 301 records | Loyalty / point-reward threshold table (stat-point allocation curve). | SAMPLE-VERIFIED (stride + count) |
| `viplevels.scr` | binary | 92 B / 9 records | VIP tier definition (thresholds + benefits); very small file. | SAMPLE-VERIFIED (stride + count) |
| `nicktofame.scr` | binary + CP949 | UNVERIFIED | Title / fame-rank name table (rank → title). | HIGH (role) |
| `minds.scr` | binary + CP949 | UNVERIFIED | Mind-technique / inner-cultivation definition table. | HIGH (role) |
| `setitemname.scr` | binary + CP949 | 36 B / 61 records | Set-item group name table. | SAMPLE-VERIFIED (stride + count) |
| `skillcategory.scr` | binary + CP949 | 564 B / 17 records | Skill-category / skill-tree name table. | SAMPLE-VERIFIED (stride + count) |
| `skillneedset.scr` | binary | stride ~8 (u16 pairs) | Skill prerequisite-link table (skill-ID requirement pairs). | MEDIUM |
| `statue.scr` | binary | 36 B / 430 records | World statue / monument definition (ID + coordinates). | SAMPLE-VERIFIED (stride + count) |
| `tiphelp.scr` | binary + CP949 | UNVERIFIED | Loading-screen tip / help text table. | HIGH (role) |
| `tutor.scr` | binary + CP949 | UNVERIFIED | Tutorial step / narrative text table. | HIGH (role) |
| `letters.scr` | binary + CP949 | UNVERIFIED | In-game mail / letter template table. | HIGH (role) |
| `warstoneinfo.scr` | binary + CP949 | small (~5 records) | War-stone / warshard item info lookup. | MEDIUM |
| `system_control.scr` | binary | 8 B / 114 records | Global game-rate table (id + f32 multiplier pairs). | SAMPLE-VERIFIED (stride + count) |
| `repair.scr` | binary | UNVERIFIED | Item repair-cost / durability table; small file. | MEDIUM |
| `oblist.scr` | binary | single 12-byte record | Object list / global limit or enable flag; purpose unclear. | LOW |
| `playtime_reward.scr` | binary | 32 B / 5 records | Play-time reward table (online-time thresholds → item rewards). | SAMPLE-VERIFIED (stride + count) |
| `users.scr` | binary | UNVERIFIED | Account-type definition table; purpose unclear, possibly unused. | LOW |
| `script_newserver/items.scr` | binary + CP949 | same schema as `items.scr` | "New server" config override of the items table (same format, different data). | HIGH (role) |
| `script_newserver/npcs.scr` | binary + CP949 | same schema as `npcs.scr` | "New server" config override of the NPC table. | HIGH (role) |

---

## Cross-references

- **`actor_array` IDs** in `events.scr` use the 9-digit item/actor-ID namespace shared with
  `items.scr` / `citems.scr` (owned by another lane) — resolve referenced actors/items there.
- **`rate_array`** carries scaled rates (raw value ÷ 1,000,000 = percentage), NOT IDs — do not route
  its values through the item-ID resolver.
- **Mob / skill / NPC roles** in the §3 index feed the recovered actor chains; see
  `Docs/RE/formats/actormotion.md`, `Docs/RE/formats/npc_spawns.md`, and `Docs/RE/specs/`.
- **Timed-event engine ids** (§1.10) — the death-respawn countdown (id 10003, 600 s, region-gated)
  and its full behavioural flow are owned by `Docs/RE/specs/world_exit.md`; the per-region zone-type
  that gates the 600 s ↔ 20 s choice is described in `Docs/RE/formats/region_grid.md`. These are NOT
  `events.scr` records.
- **CP949 handling** convention: see project notes (register `CodePagesEncodingProvider`, use code
  page 949) — same as `Docs/RE/formats/misc_data.md`.
- Glossary: see `Docs/RE/names.yaml` (candidate names for `names.yaml`: `event_id`, `mode_flag`,
  `rate_array`, `actor_array`, `day_window_start`, `day_window_end`, `sub_flag_a..d`, `event_type`,
  `day_count`, `level_min`, `level_max`, `record_trailer`, `question_id`, `question_text`,
  `answer_prompt`). NOTE the renames in this revision: former `flag_b`/`flag_c` → `day_window_start`/
  `day_window_end`; former `flag_e..h` → `sub_flag_a..d`; former `ids_array_a` → `rate_array`; former
  `ids_array_b` → `actor_array`; former `record_trailer` retained; the live u16 @0x64 (previously
  inside `reserved_b`/padding) → `mode_flag`.
- Provenance: see `Docs/RE/journal.md` (a provenance entry pairs this spec).
  - CAMPAIGN 10 / D9 (ida_reverified 2026-06-16, ida_anchor 263bd994; two-witness: static loader +
    VFS sample): `events.scr` re-confirmed end-to-end — stride **520 B**, **1848 records** (size ÷ 520
    remainder 0), all field offsets/sizes byte-matched, and the four CONSUMED fields
    (`event_id`@0x00, `mode_flag`@0x64, `rate_array` u32[50]@0x68, `actor_array` u32[52]@0x130)
    re-verified [sample-verified]; record-0 `rate_array` raw values (≈824760–950000 ÷ 1e6 = 0.82–0.95%)
    and `actor_array` 9-digit IDs corroborated; the 5 anomalous records (event_ids 30133–30137,
    `reserved_a`@0x08 = 1,000,000 / @0x0C = 20) located, their empty-array/zero-trailer status not
    re-probed (carried as static-hypothesis). `autoquestion_cl.scr` stride **92 B / 1300 records**
    [sample-verified]; no-client-loader claim [confirmed]. §3 estimate index re-surveyed: **the
    `userlevel.scr` 120 B / 150-record estimate is REFUTED** — authoritative is 60 B / 300 records;
    several other §3 strides promoted to sample-verified (see §3 note).
