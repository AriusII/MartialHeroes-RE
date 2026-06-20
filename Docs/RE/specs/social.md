# Social systems: chat, whisper, party, guild, friend/relation (clean-room spec)

> **Verification banner.** verification: confirmed (control-flow-confirmed) for every opcode→routing
> link, the `(major:minor)` tuples, the total message sizes, the (2:7) 19-byte header, the
> (2:35/2:36/2:37) party-submit shapes, the (2:30) guild-action size, the per-opcode text-length
> NUL convention, and the (2:84) header-only-plus-30 s-cooldown shape; capture/debugger-pending for
> the absolute on-wire byte-order/endianness of each length prefix, the inner field meanings of the
> larger context/blob headers, and the on-wire VALUE meanings (mode/op/selector enumerations,
> auto-accept event codes). ida_reverified: 2026-06-20 · ida_anchor: 263bd994 · evidence: [static-ida].
> (Re-confirmed against IDB SHA 263bd994, CYCLE 7 (2026-06-20).)
> conflicts resolved this pass: (a) the (2:7) text-length prefix EXCLUDES the NUL — `(3:21)`/`(2:83)`
> INCLUDE it (per-opcode, not the prior uniform "verify the off-by-one"); (b) `(2:84)` carries NO
> text in its builder and is 30-second rate-limited (corrected from the prior "19 + text" catalog
> entry); (c) a 2-byte `(2:21)` sender exists and is distinct from `(3:21)`;
> (d) **CONFLICT-D-1 resolved (CYCLE 7, 2026-06-20)**: the bonded-relation byte was reported at two
> offsets; IDA proves these are two distinct co-existing fields, not a measurement error —
> **actor +96 (offset 0x60) = relation-TYPE byte** (the bond-category lookup/match key) and
> **actor +172 (offset 0xAC) = relation-PAIR-STATE byte** (set by the 5/64 remote-pair handler).
> The earlier note that +172 was a conflation is itself superseded — both offsets are genuine.
> CONFIRMED via static IDA, CYCLE 7.

Neutral, data-only model of the legacy *Martial Heroes* client's **social wire protocol** and the
**membership state** it maintains locally: chat channels, whispers, party/group, guild, and the
combined friend/block/relation ("FATE") system. Promoted from dirty-room recon and rewritten in our
own words — no decompiler identifiers, no binary addresses, no pseudo-code.

This document is design input for the **protocol engineer** (the social wire messages in
`Network.Protocol`) and the **application engineer** (the membership/roster state consumers in
`Client.Application`). Opcodes are expressed as `(major:minor)` tuples consistent with the
authoritative `opcodes.md` frame model (8-byte header: `size` @+0, `major` @+4, `minor` @+6,
payload @+8). **All field offsets in this document are payload-relative** (relative to frame +8).

---

## Status header (read first)

> **Headline finding — one combined "relationship" subsystem, not separate friend/block/party tables.**
> The client folds friend list, block list, party, and special bond relationships (couple,
> master-disciple, training — the developers' internal label is "FATE") into a **single relationship
> model** backed by one flat slot table of fixed-stride entries. "Party" appears to be one *mode*
> of this relationship system at the submit (C2S) layer, while a genuinely separate party roster
> exists at the display (S2C) layer. Re-implementations must keep these two views distinct and must
> not assume a C2S "party-invite" tuple is party-only until a capture proves it.

> **Capture-unverified, prominently.** No live network capture was available during this analysis.
> Every opcode→routing link below is a hard static fact (read from the client's dispatch/sender
> installers). Message **sizes** (total byte counts) are likewise hard facts. But field **meanings,
> signedness, and most field boundaries** are static inferences from sender/handler read order and
> must be treated as hypotheses until a capture confirms them.

| Area | Confidence |
|---|---|
| Frame model and `(major:minor)` opcode tuples for every message listed | CONFIRMED — read from buffer-header writes / dispatch installers |
| Total payload sizes (the literal byte counts) | CONFIRMED — literal copy/read sizes |
| `(2:7)` chat/whisper 19-byte header layout + EXCLUDES-NUL text prefix | CONFIRMED — clean header walk; text cap, self-guard, and prefix arithmetic observed |
| `(3:21)`/`(2:83)` chat builders (sizes, INCLUDES-NUL prefix, gates) and `(2:84)` header-only + 30 s cooldown | CONFIRMED — each builder's `+1` and cooldown gate read directly |
| Guild full-sync (4:65) 50-member array layout | MEDIUM — offsets walked; field meanings inferred |
| Guild member roster patch (5:65) and party stats (5:38) layouts | MEDIUM |
| Party roster event (5:21) and relation slot (5:26) layouts | MEDIUM |
| Party member-remove result (4:36) layout and auto-disband behaviour | MEDIUM — offsets walked; self-leave vs expel submode read |
| Chat context headers for 2:82 / 2:83 / 3:21 (per-field breakdown) | LOW — header sizes + 3:21 channel selector + text trailer confirmed; remaining inner sender/target/scope fields not broken out |
| C2S 2:35/36/37 are genuine party (not relation/FATE) | CONFIRMED — pinned to party-panel action handlers + "party healing actor ok" debug marker (corrected 2026-06-13, re-verified 2026-06-16) |
| "Relation vs FATE" labelling of the remaining C2S 2:60–2:76 cluster | LOW — flagged UNVERIFIED #2 |
| 2:122 short-name field width; auto-accept event codes | LOW — flagged UNVERIFIED #6/#7 |
| Actor-relation fields: +92 composite id, +96 relation-TYPE byte, +116 name, +172 relation-PAIR-STATE byte (CONFLICT-D-1) | CONFIRMED — read from the roster-search routine and the 5/64 remote-pair handler (Section 7.5) |
| FATE relation-type enum {0,1,3,4,5,6,7,9,10}; only `FATE_TYPE_TRAINING` is a literal name | CONFIRMED values, partial labels — labels other than training are behavioural inference (Section 7.6) |
| 2:76 / 2:62 / 2:63 / 2:66 / 5:26 / 5:64 / 4:71 relation network surface sizes | CONFIRMED — literal builder/handler read sizes (Section 7.7) |
| 4:76 / 5:76 party-pair fields (success @+8, relation-type @+10) | CONFIRMED — branch reads (Section 6.4 / 6.8) |
| No client auto-accept (party/guild/trade/duel invites always interactive) | CONFIRMED — option table has no auto-accept index (Section 9a) |

**UNVERIFIED list** is consolidated in Section 9. All Korean text fields referenced here are
**CP949 / EUC-KR** encoded (no BOM), NUL-padded in fixed buffers; they are never sent as managed
strings and must be modelled as fixed byte blobs on the wire.

---

## 1. Shared wire idioms (applies to every message below)

All social messages ride the same framing as the rest of the protocol and share a few invariants an
implementer can rely on:

- **Fixed header, optional text tail.** A message is a fixed-size payload header followed, for the
  text-bearing chat/whisper messages, by a **length-prefixed text body**: a `u32` byte-length
  prefix immediately after the fixed header, then that many CP949 bytes. Messages without text are
  pure fixed-size payloads.
- **Text length prefix convention — PER-OPCODE (CONFIRMED).** Where a text tail is present, the
  `u32` prefix is the text length, but the NUL-inclusion is **opcode-specific**, not uniform:
  - `(2:7)` whisper/chat — prefix = **string length, EXCLUDES** the terminating NUL.
  - `(3:21)` channel chat and `(2:83)` contextual chat — prefix = **string-length-plus-one, INCLUDES**
    the NUL.

  This supersedes the earlier "generally including the NUL — verify the off-by-one against a capture"
  framing: the `+1` is literally present (3:21, 2:83) or absent (2:7) in each builder, so the
  arithmetic is control-flow-confirmed. The only remaining capture-pending detail is the absolute
  on-wire byte-order/endianness of the prefix. The body itself is copied with a hard cap (see
  per-message caps below).
- **Self-target guard (key invariant).** The relation/guild/party *submit* (C2S) paths resolve the
  intended **target** actor id from the UI/command arguments and compare it against the **local
  player's own actor id**. On a self/stale mismatch they display a shared error message
  (string-table id `862010101`) and **send nothing**. Only a valid, non-self resolved target
  proceeds to the send. This is a client-side gate, not a wire field; a server re-implementation
  must still validate targets itself.
- **Local-player id sentinel.** The local player's actor id starts as the all-ones sentinel
  `0xFFFFFFFF` (unset) and is assigned the real actor id at enter-world. The self-target guard and
  all "is this me?" branches compare against it.
- **Actor resolution.** Inbound social handlers resolve the affected actor by a **(sort, id)** pair
  before mutating state, and most gate their effect on "the resolved actor is the local player".
  `sort` is the actor-category discriminator; `id` is the actor id within that category.

---

## 2. Opcode catalog (social subset)

Direction is from the **client's** point of view. Sizes are total payload byte counts (the fixed
header; `+text` marks an additional length-prefixed CP949 tail). Proposed names are spec-author
suggestions for `names.yaml`; confirm before committing.

### 2.1 Chat & whisper (C2S)

| Opcode | Proposed name | Dir | Size | Tail | Purpose / confidence |
|--------|---------------|-----|------|------|----------------------|
| 2:7  | `CmsgChat` (was `CmsgWhisper`) | C2S | 19 | +text (prefix EXCLUDES NUL) | Say-box chat for all everyday channels incl. whisper (ch 9); channel code at payload +0 (see `chat.md`). CONFIRMED shape (Section 3). |
| 2:21 | `CmsgChat21` (purpose tbd)     | C2S | 2  | — | 2-byte sender; distinct from 3:21, easy to confuse. Purpose unrecovered (likely a small toggle/ack). STATIC-HYPOTHESIS. |
| 2:82 | `CmsgChatContext82`  | C2S | 28 | (caller-appended) | Dispatcher chat variant; 28-byte context header, no text in the builder itself. Purpose UNVERIFIED #3. |
| 2:83 | `CmsgChatContextual` | C2S | 24 | +text (prefix INCLUDES NUL) | Dispatcher contextual chat; text gated `0 < len < 200`. CONFIRMED shape. |
| 2:84 | `CmsgChatVariant84_RateLimited` | C2S | 19 | **NONE** (header-only) | Dispatcher chat variant; **no text tail in the builder**, gated by a **30000 ms (30 s) client-side cooldown**. Purpose (plausibly an emote/macro broadcast trigger) capture-pending. CONFIRMED shape (corrected this pass — was "19 +text"). |
| 3:21 | `CmsgChannelChat`    | C2S | 56 | +text (prefix INCLUDES NUL) | Dispatcher general/channel chat; channel selector at header +4, `selector mod 10 == 5` bypasses the length gate (Section 4). CONFIRMED shape. |

### 2.2 Chat & system display (S2C — layouts not re-derived here, cite catalog)

| Opcode | Catalog name | Dir | Notes |
|--------|--------------|-----|-------|
| 5:7      | `SmsgChatBroadcast`     | S2C | Broadcast chat display (header + variable text). |
| 3:50000  | `SmsgGmChatMessage`     | S2C | GM/announcement chat display. |
| 4:140    | `SmsgColoredSystemText` | S2C | Coloured system/notice line. |

### 2.3 Party (S2C — genuine party roster)

| Opcode | Catalog / proposed name | Dir | Size | Purpose / confidence |
|--------|-------------------------|-----|------|----------------------|
| 4:35 | `SmsgPartyInviteState`        | S2C | 56  | Invite/roster state snapshot (Section 6.1). NOTE: catalog labels this "party"; see UNVERIFIED #2. |
| 4:36 | `SmsgPartyMemberRemoveResult` | S2C | 56  | Result of a member-remove action; left/expelled (Section 6.6). MEDIUM. |
| 4:37 | `SmsgPartyLeaderActionResult` | S2C | 56  | Result of a leader action. |
| 4:76 | `SmsgPartyAcceptResult`       | S2C | 52  | Result of accepting an invite; success flag @+8, relation-type @+10 (Section 6.8). CONFIRMED. |
| 5:21 | `SmsgPartyRosterEvent`        | S2C | 12  | Add/remove/update one member (Section 6.2). MEDIUM. |
| 5:38 | `SmsgPartyMemberStats`        | S2C | 100 | Full per-member vitals/buffs snapshot (Section 6.3). MEDIUM. |
| 5:76 | `SmsgPartyMemberJoined`       | S2C | 36  | Member-joined event with name + greeting mode (Section 6.4). MEDIUM. |

### 2.4 Party submits (C2S — genuine party; corrected 2026-06-13)

These three are now confirmed **genuine party** operations driven by the mini-party / party-panel
context-menu actions, not relation/FATE submits (corrected 2026-06-13: a later static pass pinned
them to the party-panel action handler and its "party healing actor ok" debug marker; the earlier
"party or relation" hedge is resolved in favour of party — see Section 6.6 and UNVERIFIED #2).

| Opcode | Proposed name | Dir | Size | Payload shape (inferred) |
|--------|---------------|-----|------|--------------------------|
| 2:35 | `CmsgPartyInvite`       | C2S | 8  | `[u8 mode @0][u32 id @4]` (mode 0 = accept/commit, 2 = invite) |
| 2:36 | `CmsgPartyLeaveOrKick`  | C2S | 8  | `[u8 mode @0][u32 id @4]` (mode 0 = self-leave, 1 = kick) |
| 2:37 | `CmsgPartyLeaderOp`     | C2S | 8  | `[u8 mode @0][u32 target id @4]` (leader / transfer op) |

### 2.5 Guild (C2S submits)

| Opcode | Proposed name | Dir | Size | Notes |
|--------|---------------|-----|------|-------|
| 2:8   | `CmsgGuildCreateOrCrest` | C2S | 241 | Large guild blob (likely create / crest). UNVERIFIED #8. |
| 2:30  | `CmsgGuildAction30`      | C2S | 8   | `[u32 op @0][u32 id @4]`; self-target guarded (Section 5.6). |
| 2:54  | `CmsgGuildToggle54`      | C2S | 1   | 1-byte guild/relation toggle. |
| 2:55  | `CmsgGuildMemberOp55`    | C2S | 32  | Guild member op (32-byte struct). |
| 2:56  | `CmsgGuildMemberOp56`    | C2S | 4   | Guild member op. |
| 2:57  | `CmsgGuildMemberOp57`    | C2S | var | 20-byte header; when `header[0]==1`, a variable `u8` list of `header[16]` entries follows (Section 5.3). |
| 2:58  | `CmsgGuildMemberOp58`    | C2S | 24  | Guild member op (24-byte struct). |
| 2:81  | `CmsgGuildDiplomacy`     | C2S | 18  | Diplomacy submit (Section 5.1). MEDIUM. |
| 2:103 | `CmsgGuildPanelText`     | C2S | 196 | Guild panel text submit (pairs S2C 4:103). |

### 2.6 Guild (S2C results / pushes)

| Opcode | Proposed name | Dir | Size | Notes |
|--------|---------------|-----|------|-------|
| 4:54  | `SmsgGuildRankSlotUpdate`     | S2C | —    | Rank-slot update. |
| 4:55  | `SmsgGuildInfoUpdateResult`   | S2C | —    | Info-update result. |
| 4:61  | `SmsgGuildStateChangeResult`  | S2C | —    | State-change result. |
| 4:62  | `SmsgGuildInviteJoinState`    | S2C | 80   | Invite/join-state block. |
| 4:63  | `SmsgGuildMemberRemoveResult` | S2C | —    | Member-remove result. |
| 4:64  | `SmsgGuildPositionChangeResult`| S2C | —   | Position-change result. |
| 4:65  | `SmsgGuildInfoFullSync`       | S2C | 1812 | Full guild record refresh, 50-member arrays (Section 5.4). MEDIUM. |
| 4:96  | `SmsgActorGuildRosterEntry`   | S2C | —    | Per-actor guild patch. |
| 4:103 | `SmsgGuildPanelTextUpdate`    | S2C | —    | Panel text update (pairs C2S 2:103). |
| 5:55  | `SmsgGuildNameDisplayUpdate`  | S2C | —    | Name-display refresh. |
| 5:65  | `SmsgGuildMemberRosterUpdate` | S2C | 32   | Per-actor guild roster patch (Section 5.5). MEDIUM. |

### 2.7 Friend / block / relation ("FATE") (C2S submits)

| Opcode | Proposed name | Dir | Size | Payload shape (inferred) |
|--------|---------------|-----|------|--------------------------|
| 2:47  | `CmsgRelationAckDrain`     | C2S | 8  | Ack-drain; pairs S2C 4:47. |
| 2:48  | `CmsgRelationOp48`         | C2S | 4  | Relation op. |
| 2:49  | `CmsgRelationNamedOp`      | C2S | 19 | Relation op carrying a name field. |
| 2:60  | `CmsgRelationOp60`         | C2S | 8  | Relation/FATE op. |
| 2:61  | `CmsgRelationSubmit61`     | C2S | 36 | Nine dwords. |
| 2:62  | `CmsgRelationNamedRequest` | C2S | 19 | Relation request by name: byte op + 17B CP949 name + pad (Section 7.7). CONFIRMED. |
| 2:63  | `CmsgRelationNamedAction`  | C2S | 17 | Relation action by name: 17B CP949 name (Section 7.7). CONFIRMED. |
| 2:64  | `CmsgRelationOp64`         | C2S | 8  | Relation/FATE op. |
| 2:65  | `CmsgRelationToggle65`     | C2S | 1  | 1-byte toggle. |
| 2:66  | `CmsgRelationToggle`       | C2S | 1  | 1-byte relation toggle (Section 7.7). CONFIRMED. |
| 2:74  | `CmsgRelationOp74`         | C2S | 32 | Relation/FATE op (32-byte struct). |
| 2:76  | `CmsgFateRelationRequest`  | C2S | 20 | FATE relation-request blob — the "FATE state / actor name / item-list count" path (Section 7.7). CONFIRMED. |
| 2:122 | `CmsgFriendListSubmit`     | C2S | 12 | `[u32 selector][u8 sub-op][short name tag]` (Section 7.1, UNVERIFIED #6). |
| 2:123 | `CmsgRelationAccept`       | C2S | 12 | `[u8 sub-op][u32 target id][u8 flag]`; used by the gift-receive confirm path. |
| 2:124 | `CmsgRelationToggle124`    | C2S | 1  | 1-byte relation toggle. |
| 2:126 | `CmsgRelationAcceptByte`   | C2S | 1  | 1-byte accept/decline. |
| 2:128 | `CmsgFriendListById`       | C2S | 4  | `[u32 target id]`; friend/relation list submit by id. |

### 2.8 Friend / relation (S2C)

| Opcode | Proposed name | Dir | Size | Notes |
|--------|---------------|-----|------|-------|
| 4:30 | `SmsgSocialPanelTarget`        | S2C | —    | Social-panel target update. |
| 4:47 | `SmsgRelationAckDrain`         | S2C | —    | Pairs C2S 2:47. |
| 4:71 | `SmsgRelationPartyPanelSnapshot`| S2C | 1092 | Big 8-slot relation / party panel snapshot (Section 7.7). CONFIRMED size. |
| 5:26 | `SmsgLocalPlayerRelationSlot`  | S2C | 28   | Local-player relation-slot update (Section 7.2). CONFIRMED size. |
| 5:64 | `SmsgRemoteActorRelationPair`  | S2C | 16   | Remote-actor relation pair; writes actor +172 on both sides (Section 7.8). CONFIRMED. |

---

## 3. Whisper — 2:7 (C2S)

A whisper is a **19-byte fixed header followed by a length-prefixed CP949 text body**.

| Off | Size | Type     | Field         | Meaning |
|-----|------|----------|---------------|---------|
| 0   | 1    | u8       | `ChannelType` | Channel / sub-type selector (first UI argument). |
| 1   | 1    | u8       | `Flag`        | Mode flag (second UI argument). |
| 2   | 16   | bytes[16]| `TargetName`  | Target character name, CP949, NUL-padded to 16 bytes. |
| 18  | 1    | u8       | `Pad`         | Trailing byte completing the 19-byte header; written as zero. |
| 19  | 4    | u32      | `TextLength`  | Byte length of the text tail (see Section 1 prefix convention). |
| 23  | n    | bytes    | `Text`        | Message text, CP949. Body is hard-capped at **119 characters**. |

Behaviour:

- Before sending, the builder resolves the target name to an actor id and applies the **self-target
  guard** (Section 1): if the resolved id is the local player's own id (a self-whisper), it aborts
  with error message id `862010101` and **does not send**.
- Whispers originate from the central chat/command parser (the path that handles `/`-prefixed slash
  commands and the reply UI). Slash commands observed on this path include `/option`, `/help`, and
  `/msgchk <n>`; these are parsed client-side and do not all map to wire messages.
- The client option that toggles whisper-notification is a **local INI/UI preference only** and is
  **not** carried on the wire. Do not model it as a packet field.

---

## 4. Chat family — 2:82 / 2:83 / 2:84 / 3:21 (C2S)

All four chat messages are emitted from the central chat/command parser, which fills a per-message
**context header** (sender / target / channel/scope fields) before the builder runs, then appends a
length-prefixed text body. Only the header *sizes*, the channel-selector dword in 3:21, and the text
trailer are decoded; the full per-field breakdown of the 24-byte (2:83) and 56-byte (3:21) context
structs is **not** recovered this pass.

| Opcode | Header size | Text tail | Gating observed |
|--------|-------------|-----------|-----------------|
| 2:82 | 28 | appended by caller, not by the builder | none in the builder |
| 2:83 | 24 | length-prefixed | text length gated `0 < len < 200` |
| 2:84 | 19 | length-prefixed | none observed |
| 3:21 | 56 | length-prefixed | text `< 200`, **unless** the channel selector takes the special path below |

**Channel selector (3:21).** The dword at header offset **+4** is a channel/scope selector. When
`selector mod 10 == 5`, the client takes a special path (a broadcast/shout-style channel) that
**bypasses the empty/length gate**, allowing empty or longer text. Treat `selector mod 10 == 5` as a
distinguished broadcast channel; the full enumeration of selector values is UNVERIFIED.

The inbound display counterparts are S2C `5:7` (broadcast), `3:50000` (GM message), and `4:140`
(coloured system text); their field layouts are not re-derived here — cite the catalog tuples.

---

## 5. Guild subsystem

### 5.1 Guild diplomacy submit — 2:81 (C2S, 18 bytes)

| Off | Size | Type      | Field            | Meaning |
|-----|------|-----------|------------------|---------|
| 0   | 1    | u8        | `DiplomacyState` | Diplomacy state code. |
| 1   | 16   | bytes[16] | `TargetGuildName`| Target guild name, CP949, NUL-padded. |
| 17  | 1    | u8        | `Trailing`       | Trailing byte. |

The developers' own debug label for this path is "submit diplomacy: state / target guild name",
which corroborates the field roles. Self-target guarded.

### 5.2 Other guild submits (C2S, fixed sizes)

| Opcode | Size | Inferred shape | Note |
|--------|------|----------------|------|
| 2:30 | 8   | two dwords | Guild action; self-target guarded. |
| 2:54 | 1   | one byte | Guild/relation toggle. |
| 2:55 | 32  | 32-byte struct | Guild member op. |
| 2:56 | 4   | one dword | Guild member op. |
| 2:58 | 24  | 24-byte struct | Guild member op. |
| 2:8  | 241 | large blob | Likely guild create / crest payload (full breakdown UNVERIFIED #8). |
| 2:103| 196 | text blob | Guild panel text submit (pairs S2C 4:103). |

### 5.3 Variable guild member op — 2:57 (C2S, variable)

A 20-byte fixed header. When `header[0] == 1`, a **variable-length `u8` list** follows, whose length
is given by the byte at header offset **+16** (`header[16]` = element count). When `header[0] != 1`,
no list is appended (pure 20-byte message). Model `size` as `var`; the codegen must read the count
from offset +16 and then read that many trailing bytes.

### 5.4 Guild full sync — 4:65 (S2C, 1812 bytes)

Full guild-record refresh. The guild is capped at **50 members**, and the member data is laid out as
parallel fixed-length arrays (struct-of-arrays), not an array-of-structs. Offsets are
payload-relative.

| Off  | Size | Type           | Field             | Meaning |
|------|------|----------------|-------------------|---------|
| 8    | 1    | u8             | `Gate`            | `1` = "left / no guild" path; any other value = full sync. |
| 10   | 18   | bytes[18]      | `GuildName`       | Guild name, ASCIIZ, CP949. |
| 28   | 2    | i16            | `GuildId`         | Guild id. |
| 32   | 4    | i32            | `Gold`            | Guild gold. |
| 36   | 8    | i64            | `Fund`            | Guild fund. |
| 44   | 8    | i64            | `Exp`             | Guild experience. |
| 52   | 4    | i32            | `CrestCode`       | Costume / crest code. |
| 60   | 200  | i32[50]        | `MemberIds`       | Member actor ids. |
| 260  | 50   | u8[50]         | `MemberRanks`     | Member ranks. |
| 310  | 850  | bytes[17][50]  | `MemberNames`     | Member names, CP949, 17 bytes each. |
| 1160 | 50   | u8[50]         | `MemberOnline`    | Member online flags. |
| 1212 | 200  | i32[50]        | `MemberPoints`    | Member points. |
| 1412 | 200  | i32[50]        | `MemberContrib`   | Member contribution. |
| 1612 | 200  | i32[50]        | `MemberLoginTime` | Member last-login times. |

Notes:
- Bytes `0..7` and the gap at `9` are header/padding not enumerated above; treat the message as a
  1812-byte block and read the named fields at the listed offsets. The 50-member arrays fully
  account for the bulk of the size; the exact use of the leading 8 bytes is UNVERIFIED.
- The **leave branch** (`Gate != 1` interpreted as "stay", `Gate == 1` as "leave/no-guild") clears
  the local player's guild fields and surfaces user-facing notices (string ids `10011` "left guild"
  and `2183` "exp refund"). These are display ids, not wire fields.
- A separate guild-info display path walks the 50 member-id slots (offset 60, stride 4) and patches
  each resolved actor's guild rank/flags. This is a local mirror, not a separate message.

### 5.5 Guild member roster patch — 5:65 (S2C, 32 bytes)

An actor-keyed guild roster patch. Resolves the actor by (sort, id) and, when the id matches the
local player, also mirrors the fields into the local-player guild state.

| Off  | Size | Type      | Field         | Meaning |
|------|------|-----------|---------------|---------|
| 0    | 4    | i32       | `Sort`        | Actor category. |
| 4    | 4    | i32       | `ActorId`     | Actor id. |
| 8    | 1    | u8        | `GuildFlag`   | Guild membership flag. |
| 9    | 17   | bytes[17] | `MemberName`  | Member name, CP949, NUL-padded. |
| 26   | 2    | u16       | `NamePresent` | Non-zero = apply name & add to roster; zero = clear. |
| 28   | 1    | u8        | `Rank`        | Member rank. |
| 29   | 1    | u8        | `Grade`       | Title / grade. |

Field widths sum to 30; bytes `30..31` complete the 32-byte block (treat as trailing pad). When
`NamePresent == 0`, the name is cleared rather than applied.

### 5.6 Guild action submit — 2:30 (C2S, 8 bytes)

The small guild/relation action channel. Its 8-byte payload is two dwords (added 2026-06-13):

| Off | Size | Type | Field | Meaning |
|-----|------|------|-------|---------|
| 0   | 4    | u32  | `Op`  | Guild action / operation selector. |
| 4   | 4    | u32  | `Id`  | Target actor / guild-member id. |

The submit wrapper resolves the target id from the payload, applies the **self-target guard**
(Section 1): on a self/stale mismatch it shows error id `862010101` and **sends nothing**; only a
valid non-self target sends the 8 bytes verbatim. The full enumeration of `Op` values is not
recovered (UNVERIFIED).

> **Tier note (re-verified this pass).** The **8-byte size** and the **2:30 opcode** are
> control-flow-confirmed. The **`Op`/`Id` two-dword split** is a STATIC-HYPOTHESIS: the wire builder
> copies an 8-byte caller-side struct verbatim, so the split is set by the *caller* and is inferred
> from the calling convention, not visible in the builder itself.

---

## 6. Party subsystem (genuine party — S2C confirmed)

The S2C side here is a true party roster, distinct from the relation/FATE submit cluster in
Section 7 (see UNVERIFIED #2).

### 6.1 Party invite state — 4:35 (S2C, 56 bytes)

A roster/invite-state snapshot. Members are a fixed array of up to 8 ids.

| Off | Size | Type    | Field       | Meaning |
|-----|------|---------|-------------|---------|
| 8   | 1    | u8      | `Gate`      | Path/branch gate. |
| 9   | 1    | u8      | `Error`     | Error / result code. |
| 10  | 1    | u8      | `State`     | Invite/roster state. |
| 16  | 4    | i32     | `PartyId`   | Party id (mirrored into local-player party state). |
| 20  | 32   | i32[8]  | `MemberIds` | Up to 8 member actor ids. |
| 52  | 4    | i32     | `TargetId`  | Target actor id (the subject of the invite/action). |

Bytes `0..7`, `11..15` are header/padding not enumerated; read the named fields at the listed
offsets within the 56-byte block.

### 6.2 Party roster event — 5:21 (S2C, 12 bytes)

A single add/remove/update for one member.

| Off | Size | Type | Field           | Meaning |
|-----|------|------|-----------------|---------|
| 0   | 1    | u8   | `Event`         | Roster event code (see enumeration below). |
| 1   | 1    | u8   | `MemberSlot`    | Party-slot index of the affected member (corrected 2026-06-13: the affected member is identified by a slot index at payload +1, resolved through the party slot table, rather than by an actor id at payload +4). |

**Event codes (`Event` at payload +0):**

| Event | Meaning      | Display notice id |
|-------|--------------|-------------------|
| 0     | member joined  | `2120` |
| 1     | member left    | `2119` |
| 2     | member updated | `2121` |
| 3     | party disbanded| `2122` |

The handler resolves the affected member through the party slot table (slot index at payload +1),
patches that party-panel entry, and posts the notice (colour code `0xFFFF8000`). A secondary
party-active flag (Section 6.6 / Section 8) gates a quickslot / auto-target relink for party members.
Those string ids are display-only.

### 6.3 Party member stats — 5:38 (S2C, 100 bytes)

A full per-member vitals/buffs snapshot, keyed by member id, that fills one party-panel entry. The
handler matches a member slot by member id, then writes a fixed 100-byte slot record. The offsets
below are the **slot-record** offsets read directly off the apply path (CYCLE 7); they are the wire
field order and several are now offset-pinned (CONFIRMED). The absolute on-wire byte boundaries that
remain un-pinned are flagged below.

| Off | Size | Type      | Field         | Meaning |
|-----|------|-----------|---------------|---------|
| 8   | 16   | bytes[16] | `MemberName`  | Member name, CP949, copied 16 bytes. CONFIRMED offset. |
| 42  | 2    | i16       | `LevelOrClass`| Member level (or class id — label UNVERIFIED). CONFIRMED offset. |
| 44  | 2    | i16       | `HpCurrent`   | Current HP — the animated vital (the panel runs an old→new HP-bar tween off this field). CONFIRMED. |
| 48  | 4    | i32       | `HpMaxOrVital`| Max HP / vital max (label UNVERIFIED). |
| 52  | 4    | i32       | `MpCurrent`   | Current MP (UNVERIFIED label). |
| 56  | 4    | i32       | `MpMax`       | Max MP (UNVERIFIED label). |
| 60  | 20   | i32[5]    | `StatBlock`   | Five-dword additional stat block (individual labels UNVERIFIED). |
| 80  | 30   | u8[30]    | `BuffCodes`   | Fixed 30-entry status/buff-icon array (see filter note). CONFIRMED offset + width. |

Notes:
- **Buff-code filter.** When ingesting `BuffCodes`, the client keeps a code only if it is
  `<= 0x50` **or** `>= 0x82`; codes in the range `0x51..0x81` are dropped. Re-implementations that
  mirror the client's buff display should apply the same filter; a server need not.
- **Member map position carried too (CONFIRMED, CYCLE 7).** Beyond the panel record, the handler
  also writes the member's live world position (x/y/z) onto the member's own actor object, saving the
  prior position into a per-actor "previous position" pair first. So 5:38 doubles as a position
  update for the member, not only a vitals snapshot. This is a local mirror step onto the actor, not
  separate wire data.
- If the member is also a visible player-character actor (`sort == 1`), the client additionally
  snapshots the member's current HP/MP/stamina into that actor's live vitals. This is a local mirror
  step, not extra wire data.

### 6.4 Party member joined / pair-interaction event — 5:76 (S2C, 36 bytes)

A dual-purpose event: a party member-joined notice **and** the player-pair / relation-interaction
event (couple ceremony / training duel). The relation TYPE code at record **+10** selects the pair
animation, so this message is shared between the party and FATE subsystems.

| Off | Size | Type      | Field          | Meaning |
|-----|------|-----------|----------------|---------|
| 4   | 4    | i32       | `ActorId`      | Joining / paired member's actor id. |
| 8   | 1    | u8        | `Event`        | Greeting / interaction mode: `4` = greet, `10` = combat / spar. |
| 9   | 1    | u8        | `Sort`         | Actor category. |
| 10  | 1    | u8        | `RelationType` | Relation TYPE code (Section 7.6): `4` / `10` drive the couple / training pair ceremony (both actors face each other + motion swap + cue). CONFIRMED. |
| 18  | 17   | bytes[17] | `Name`         | Member name, CP949. |

Bytes `0..3`, `5..7`, `11..17`, and `35` are header/padding. The handler plays a greeting or combat
motion keyed by `Event` / `RelationType` and may surface a rank-progress notice (display id).

### 6.5 Party membership state (local)

- The party roster/stats live in a single party-panel cache on the local-player UI singleton.
- Roster add/remove/update is applied from 5:21; member stats from 5:38; joins from 5:76.
- The local player's `PartyId` is mirrored from 4:35.
- Each actor carries its own **party id** at on-actor offset **+968**; the remove handler clears this
  field on a removed member, and a global **party-active flag** plus a secondary party flag (the
  quickslot / auto-target relink gate) track whether the local player is currently grouped. These are
  local in-memory state, not wire fields. (added 2026-06-13)
- **Local-player slot bases — two distinct stores (reconciled CYCLE 7).** Two nearby slot-array
  bases on the local-player struct were measured in separate passes; they are **distinct stores**,
  not one off-by-four measurement:
  - **Local-player +204** is the **party-slot id array** base (the local player's party-slot id
    array, indexed by party slot). This is the canonical party-roster base.
  - **Local-player +200** is the **relation-slot store** (records of 16-byte stride) written by the
    5:26 local relation-slot handler (Section 7.2), mirrored into a global slot table. It is the
    relationship membership store, not the party id array.

  Keep these separate in a re-implementation; the +200 store belongs to the relationship/FATE
  subsystem and the +204 array belongs to the party subsystem. (CONFIRMED bases; the exact element
  counts of each array are UNVERIFIED — see UNVERIFIED #9.)

### 6.6 Party member remove result — 4:36 (S2C, 56 bytes)

The result of a member-remove action (a self-leave or an expel/kick). The handler clears the removed
member's on-actor party id, updates the local roster, and may surface a notice or auto-disband. New
this pass (added 2026-06-13).

| Off | Size | Type    | Field         | Meaning |
|-----|------|---------|---------------|---------|
| 4   | 4    | i32     | `RequesterId` | Actor that triggered the removal. |
| 10  | 1    | u8      | `Submode`     | `0` = member left, `1` = member expelled/kicked. |
| 12  | 4    | i32     | `RemovedId1`  | Removed member id — read when `Submode == 1` (expelled). |
| 20  | 32   | i32[8]  | `MemberIds`   | Resulting party member-id array (up to 8), re-applied to the roster. |
| 52  | 4    | i32     | `RemovedId2`  | Removed member id — read when `Submode == 0` (left). |

Bytes `0..3`, `8..9`, `11..19`, and the gaps between named fields are header/padding not enumerated;
read the named fields at the listed offsets within the 56-byte block.

Behaviour:

- The handler clears the removed member's on-actor party id (offset +968). If the removed id equals
  `RequesterId` (a self-leave), it closes the party panel, clears the party-active flag, and clears
  the local player's own party id.
- Otherwise it surfaces a member-left / member-expelled notice (display ids `23004` "left" / `23005`
  "expelled") and removes the member from the roster.
- **Client-side auto-disband.** After applying the removal, the handler re-reads the 8-id member
  array (`MemberIds`) and counts the survivors. If **one or zero members remain**, it auto-sends
  `2:36` with `mode == 0` (a self-leave), collapsing the now-pointless one-person party. A
  re-implementation that mirrors the client must reproduce this auto-send so its local state matches.

### 6.7 Party invite confirmation popup (interaction-context model)

Party invitation, trade request, and one relation operation all share a single **timed confirmation
popup** mediated by a small integer **context-type code**. When the local player issues one of these
interactions from the right-click target menu, the client opens the shared confirm window with an
**8000 ms** countdown (the popup shows a `label - seconds` line, the seconds derived as
`timeout / 1000`) and stamps it with the context code so the deferred "commit" step routes to the
correct sender:

| Context code (hex / dec) | Interaction | Sender it commits to |
|--------------------------|-------------|----------------------|
| `0x320` / 800 | party invite      | `2:35` (mode 0 on commit) |
| `0x2C2` / 706 | trade request     | `2:23` (see `inventory_trade.md`) |
| `0x334` / 820 | relation op       | relation submit (out of this section's lane) |

This is a client-side input-flow detail (a deferred confirm), not a distinct wire message: the popup
times out after 8000 ms or routes to the listed sender when confirmed. Added 2026-06-13.

### 6.8 Party accept result — 4:76 (S2C, 52 bytes)

The result of accepting a party invite (and the pair-interaction accept). Added CYCLE 7.

| Off | Size | Type | Field          | Meaning |
|-----|------|------|----------------|---------|
| 8   | 1    | u8   | `SuccessFlag`  | `1` = accepted/ok; any other value surfaces a "cannot join" notice. CONFIRMED. |
| 10  | 1    | u8   | `RelationType` | Relation TYPE byte (Section 7.6): values `4` / `10` select the couple / training pair ceremony (both actors face each other + motion swap + cue). CONFIRMED. |

Bytes `0..7`, `9`, and `11..51` are header/padding not enumerated; read the named fields at the
listed offsets within the 52-byte block. On success, 5:21 then maintains the roster and 5:38 streams
each member's stats + position. The `RelationType` byte makes 4:76 a shared party / FATE result, the
S2C counterpart of the 5:76 pair-interaction event (Section 6.4).

---

## 7. Friend / block / relation ("FATE") subsystem

The client folds **friend list, block list, and special bond relationships** (couple,
master-disciple, training) into one relationship model. The developers' UI vocabulary includes
"FriendPanel", "RelationPanel", and a training-type flag; debug labels include `friend <a> <b>` and
`cut <name>` (the "cut" command is the block/sever action).

### 7.1 Friend list submit — 2:122 (C2S, 12 bytes)

| Off | Size | Type    | Field      | Meaning |
|-----|------|---------|------------|---------|
| 0   | 4    | u32     | `Selector` | Relation/list selector or target id. |
| 4   | 1    | u8      | `SubOp`    | Sub-operation. |
| 5   | 4    | bytes[4]| `NameTag`  | Short name source (copy length 4 within a 5-byte buffer). |

The 12-byte block leaves bytes `9..11` as trailing pad. The name field is unusually short (4 bytes),
implying a short tag rather than a full character name — **UNVERIFIED #6**, verify against a capture.

### 7.2 Local-player relation slot — 5:26 (S2C, 28 bytes)

The canonical client-side relationship membership update. It writes four payload dwords into a flat
**relation-slot table** indexed by a slot byte that is the **relation TYPE** (see Section 7.6 for the
type enum).

| Off | Size | Type | Field       | Meaning |
|-----|------|------|-------------|---------|
| 0   | 4    | i32  | `Sort`      | Actor category. |
| 4   | 4    | i32  | `ActorId`   | Actor id (gated == local player). |
| 8   | 1    | u8   | `SlotIndex` | 0-based slot index = the relation TYPE code; slot stride = 16 bytes. |
| 12  | 4    | i32  | `Field0`    | Partner / target id. |
| 16  | 4    | i32  | `Field1`    | Slot payload word 1. |
| 20  | 4    | i32  | `Field2`    | Slot payload word 2 — read with a `< 100` check, a count/progress (e.g. intimacy / training level). |
| 24  | 4    | i32  | `Field3`    | Slot payload word 3. |

Behaviour: the handler gates on "resolved actor is the local player", then writes the four dwords
(`Field0..Field3`, 16 bytes total) at slot index `16 * SlotIndex` into the **relation-slot store at
local-player +200** (Section 6.5), mirrored into a global slot table at the same stride. Because
`SlotIndex` is the relation type, the store is effectively one slot per relation type. This flat
16-byte-per-slot store is the **canonical client-side relationship membership store** and is distinct
from the party-slot id array at local-player +204. Bytes `9..11` are padding.

### 7.3 Other relation submits (C2S)

| Opcode | Size | Inferred shape / note |
|--------|------|-----------------------|
| 2:123 | 12 | `[u8 sub-op][u32 target id][u8 flag]`; accept/decline of a relation/gift offer (used by the gift-receive confirm path). |
| 2:124 | 1  | 1-byte relation toggle. |
| 2:126 | 1  | 1-byte accept/decline. |
| 2:128 | 4  | `[u32 target id]`; friend/relation submit by id, reached from the chat-command parser. |
| 2:49  | 19 | Relation op carrying a name field. |
| 2:60–2:66, 2:74, 2:76 | 8/36/19/17/8/1/1/32/20 | Relation/FATE submit cluster (sizes per Section 2.7). |

### 7.4 Friend/cut command parsing

The `friend <a> <b>` and `cut <name>` text commands are parsed by a dedicated command handler that
resolves a name and routes to the appropriate relation submit. The `/`-prefixed slash-command parser
(the same one used by whisper) handles the slash-prefixed entries. These are input-layer details;
the wire messages are those in Sections 7.1–7.3.

### 7.5 Actor relation fields (on-actor struct) — CONFLICT-D-1 resolution

FATE is **D.O. Online's bonded-relationship system** (master/disciple, couple/spouse, rival,
training/sparring), surfaced in the client as the **RelationPanel** ("relation" roster). It is not a
generic friend list; it is the bond system that drives paired actor animations and a per-actor
relation colour on the nameplate. These fields live on the **actor** object and are payload-independent
struct offsets (not packet fields):

| Actor offset | Size | Type | Field             | Meaning |
|--------------|------|------|-------------------|---------|
| +92  (0x5C)  | 4    | i32  | `ActorCompositeId`| Actor composite id / key. Used to compare a candidate against the local player. CONFIRMED. |
| +96  (0x60)  | 1    | u8   | `RelationTypeByte`| Relation-TYPE byte — the bond-category lookup / match key (the type enum of Section 7.6). CONFIRMED. |
| +116 (0x74)  | n    | str  | `ActorName`       | Actor display name, CP949. The relation-match name key. CONFIRMED. |
| +172 (0xAC)  | 1    | u8   | `RelationPairState`| Relation PAIR-STATE byte — set per pair when two remote actors become bonded (the 5:64 handler). The nameplate / relation-colour read; also doubles as a no-guild / neutral state marker (value `2` = left / no guild). CONFIRMED. |

> **CONFLICT-D-1 resolved (CYCLE 7, 2026-06-20).** The bonded-relation byte was reported at two
> offsets in earlier passes. Static IDA proves **actor +96 (relation-type lookup key)** and
> **actor +172 (relation pair-state, set by the 5/64 remote-pair handler)** are **two distinct
> co-existing fields**, not a measurement error. The earlier D6 note that +172 was a conflation is
> itself superseded — both offsets are genuine. CONFIRMED via static IDA, CYCLE 7.
>
> - Use **+96** to answer "which bond category does this actor hold with me" — the roster-search
>   routine matches a candidate by testing this byte against the requested relation-type **and**
>   comparing the actor's name (at +116).
> - **+172** is the per-pair state the 5:64 handler writes when two actors bond (Section 7.8), and is
>   the byte the nameplate / relation colour reads.

### 7.6 FATE relation-type enum — CONFIRMED values, partial labels

The relation type is a single byte (`RelationTypeByte` at actor +96; the same code rides packets as
the relation-type field, e.g. 5:26 `SlotIndex`, the 5:76/4:76 relation-type byte, the relation panel
record). The client switches on this code consistently across the relation-panel effect dispatcher,
the local-slot apply path, and the pair-interaction events. Observed enum span: `{0,1,3,4,5,6,7,9,10}`.

| Type | Proposed label | Confidence | Behaviour evidence |
|------|----------------|------------|--------------------|
| 0    | self / info             | label UNVERIFIED | self / own-name match path (record name == local player name); also drives a confirm-dialog category. |
| 1    | master / disciple bond  | label UNVERIFIED | the "teacher" line; consumed via the roster-search routine with type 1. A money fee applies on form (Section 7.8). Master-vs-disciple split UNVERIFIED. |
| 3    | dissolve / break        | label UNVERIFIED | clears the roster row and applies the same money fee as the bond-form path (Section 7.8). |
| 4    | couple / spouse         | label UNVERIFIED | the marriage/couple bond: swaps the partner name with the local-player name and sets a paired-member flag; drives a couple ceremony (both actors face each other + motion swap + cue). A money fee applies on form. |
| 5    | reject / fail           | label UNVERIFIED | surfaces a "refused" notice; no state mutation beyond clearing the pending-request flag. |
| 6    | request notification    | label UNVERIFIED | shows an "X requests…" notice and looks up the actor via the roster-search routine (with type 1). |
| 7    | rival                   | label UNVERIFIED | own switch case; also the **reciprocal/default pair code** the 5:64 handler writes onto the other side (Section 7.8). |
| 9    | training-NO             | CONFIRMED name family | the refuse branch of the training relation; logged with the literal `FATE_TYPE_TRAINING` family name. |
| 10   | training-YES            | CONFIRMED name family | the accept branch of the training/sparring relation (a training-duel pair: level-difference damage); logged with the literal `FATE_TYPE_TRAINING` family name. |

The literal name **`FATE_TYPE_TRAINING`** is confirmed in the binary for the training relation (types
9/10). All other type labels above are **inferred from behaviour and are UNVERIFIED**; a re-implementation
must not hard-code the non-training labels as ground-truth names. (The training family is sometimes
grouped behaviourally with types {1,6} in the apply path; that grouping is an implementation detail of
the dispatcher, not a separate type.)

### 7.7 FATE / relation network surface (C2S + S2C)

Sizes are total payload byte counts (CONFIRMED — literal builder/handler read sizes). On-wire VALUE
meanings stay capture/debugger-pending.

| Opcode | Proposed name | Dir | Size | Shape / meaning |
|--------|---------------|-----|------|-----------------|
| 2:76 | `CmsgFateRelationRequest` | C2S | 20 | FATE relation-request blob; the developers' debug label is "FATE state / actor name / item-list count" (the request carries a FATE state code, a target actor name, and an item-list count). Gated by a connection-id check before send. |
| 2:62 | `CmsgRelationNamedRequest` | C2S | 19 | Relation request by name: a byte op + a 17-byte CP949 name + pad. |
| 2:63 | `CmsgRelationNamedAction`  | C2S | 17 | Relation action by name: a 17-byte CP949 name. |
| 2:66 | `CmsgRelationToggle`       | C2S | 1  | 1-byte relation toggle. |
| 5:26 | `SmsgLocalPlayerRelationSlot` | S2C | 28 | Local-player relation-slot update (Section 7.2): writes a 16-byte stride at local-player +200 indexed by the relation type. |
| 5:64 | `SmsgRemoteActorRelationPair` | S2C | 16 | Remote-actor relation-pair (Section 7.8): two actor ids at body +4 / +8, a relation byte at body +12 (0xC); writes actor +172 on both sides. |
| 4:71 | `SmsgRelationPartyPanelSnapshot` | S2C | 1092 | The big 8-slot relation / party panel snapshot. Inner field breakdown UNVERIFIED. |

The relation/FATE record handed to the panel (and used by the 5:76 / 4:76 pair-interaction path) is a
fixed struct; the field offsets below are record-relative (CONFIRMED):

| Record off | Size | Type | Field | Meaning |
|------------|------|------|-------|---------|
| 0  | 1  | u8  | `SubAction`    | Relation sub-action / opcode echo. |
| 10 | 1  | u8  | `RelationType` | Relation TYPE code (the enum of Section 7.6). |
| 12 | 17 | str | `MemberName`   | Member name, CP949 (17 bytes). |
| 29 | 1  | u8  | `SlotIndex`    | Tracked-slot index (`0xFF` = none; for type 4 a non-`0xFF` value releases a tracked slot). |
| 32 | 4  | i32 | `MemberKey`    | Member actor composite key / id. |

### 7.8 Remote-actor relation pair (5/64) and the FATE bond economy

**Remote-pair handler (5/64, 16 bytes) — CONFIRMED.** When two remote actors become bonded, the
handler reads two actor ids (body +4 and body +8) and a relation code (body +12). It **skips** when
either actor is the local player or the pair is already bonded, then writes the payload's relation
code onto one actor's relation-pair-state byte (**actor +172**) and a **fixed code (value 7)** onto
the reciprocal actor's +172. This is the byte the nameplate / relation colour reads (Section 7.5);
its value `2` also marks a left/no-guild neutral state.

**Bond economy (CONFIRMED, value capture-pending).** Forming a couple (type 4) or master bond
(type 1), and dissolving a bond (type 3), deduct a **money fee** computed as a percentage of the
player's own wallet **plus** a draw on a **global currency pool** (a single client-side currency pool
used for FATE bond fees). The exact fee percentages and pool semantics are server-authoritative and
value-capture-pending. Couple (type 4) additionally swaps the partner/local names and sets a
paired-member flag (the pairing record). These are local effects driven by the relation type; the
authoritative charge is the server's.

### 7.9 No client auto-accept (CONFIRMED)

There is **no client-side party / guild / trade / duel auto-accept**: relation/FATE and party invites
are always interactive — the invite/accept path always prompts (e.g. the 4:35 invite-state →
4:76 accept-result flow for party, and the relation request flow for FATE). The client option table
contains only graphics/sound/UI toggles plus a whisper-notify flag and a combat auto-target flag;
none is a party/guild/trade auto-accept. A re-implementation must drive these through an interactive
confirm, not an automatic accept. (Any server-side auto-accept behaviour is out of scope and
capture-pending.)

> **PvP / fame / public-peace cross-reference.** The FATE bond system documented here is distinct
> from the PK / karma, fame, public-peace, and brood-war two-side faction subsystems, which are
> documented in `pvp.md`. The per-actor relation-pair-state byte (+172) is the social-bond colour,
> not a PK/karma score.

---

## 8. Membership-state model (summary)

| Store | What it holds | Updated by |
|-------|---------------|------------|
| Local player id sentinel | Local actor id; `0xFFFFFFFF` until enter-world. Drives the self-target guard. | enter-world |
| Relation-slot store (local-player +200) | Flat array of **16-byte slots** keyed by relation type (`SlotIndex`); mirrored into a global slot table. Distinct from the party-slot id array. | 5:26 |
| Party-slot id array (local-player +204) | The local player's party-slot id array. Distinct from the relation-slot store at +200. | party roster events |
| Actor relation fields | Per-actor: composite id (+92), relation-type byte (+96), name (+116), relation-pair-state byte (+172). | 5:64 (pair state), roster population |
| Party roster/stats | Single party-panel cache; party id, member ids (≤8), per-member vitals/buffs. Each actor also carries a party id at on-actor offset +968; a global party-active flag tracks whether the local player is grouped. | 4:35, 4:36, 4:76, 5:21, 5:38, 5:76 |
| Guild roster/cache | Up to **50 members** (struct-of-arrays); per-actor guild rank/title/flags; local-player guild name/grade. | 4:65 (full), 5:65 (per-actor) |

Caps and gates to model:
- **Guild members:** 50.
- **Party members:** ≤ 8 (from the 4:35 id array).
- **Whisper text:** 119 characters.
- **Chat text:** `< 200` characters, except the 3:21 broadcast channel (`selector mod 10 == 5`)
  which bypasses the empty/length gate.
- **Self-target guard:** relation/guild/party submits refuse a self/stale target with error id
  `862010101` and send nothing.

---

## 9. UNVERIFIED list (resolve with captures / analyst cross-check)

1. **No network capture cross-check performed.** All field layouts are static inferences from
   sender/handler read order. Sizes (literal byte counts) are hard facts; field meanings,
   signedness, and most field boundaries are hypotheses.
2. **Party vs relation/FATE labelling.** (corrected 2026-06-13: the **2:35/2:36/2:37** cluster is now
   resolved to **genuine party** — pinned to the mini-party / party-panel action handlers and a
   "party healing actor ok" debug marker — and is documented as party in Sections 2.4 and 6. Only
   the separate **2:60–2:76** cluster remains relationship/FATE-flavoured and capture-unverified.)
   The 2:60–2:76 cluster is still relationship/FATE-flavoured at the submit layer; genuine party is
   the C2S 2:35/2:36/2:37 plus the S2C 4:36 / 5:21 / 5:38 / 5:76 + party panel. (CYCLE 7 refined the
   FATE half: 2:76 = FATE relation request, 2:62 = relation-by-name request, 2:63 = relation-by-name
   action, 2:66 = relation toggle — see Section 7.7. The non-training relation-type *labels* in the
   enum of Section 7.6 still want a couple/training-vs-friend-add capture to settle.)
3. **2:82 (28-byte chat variant) purpose** — party chat? guild chat? trade chat? The builder writes
   a 28-byte header and no text in the thunk itself; a caller may append text separately.
4. **2:84 purpose (RESOLVED shape; capture-pending meaning).** Corrected this pass: 2:84 is a
   **header-only, 19-byte message with NO text tail in its builder**, gated by a **30000 ms (30 s)
   client-side cooldown** (not the prior "19-byte + text" reading). Its *purpose* (plausibly an
   emote / macro broadcast trigger) is the only remaining unknown — **capture-pending**.
5. **Text length-prefix convention — RESOLVED (per-opcode), only on-wire byte-order pending.** The
   NUL-inclusion of the `u32` text-length prefix is **opcode-specific**, not a single global
   convention: `(2:7)` uses the string length and **EXCLUDES** the NUL; `(3:21)` and `(2:83)` use
   length-plus-one and **INCLUDE** the NUL (the `+1` is literally present/absent in each builder —
   control-flow-confirmed; see Section 1). The earlier "model prefix = bytes that follow, verify the
   off-by-one against a capture" framing is superseded. The only residual is the absolute on-wire
   byte-order / endianness of the prefix — **capture-pending**.
6. **2:122 name field width** — a 4-byte copy into a 5-byte buffer implies a short tag rather than a
   full 16/17-char character name; unusual, verify.
7. **2:123 inbound relation-event codes (NO client auto-accept).** RESOLVED this pass: the client has
   **no party/guild/trade/duel auto-accept** — invites are always interactive (Section 7.9). The
   inbound relation-event numbering on the gift-receive confirm path is still inferred from branch
   constants and is **value-capture-pending**.
8. **2:8 (241-byte guild blob) field breakdown** — likely guild create / crest, but the internal
   layout is not decoded.
9. **Relation-slot store length** — the store lives at local-player +200 (distinct from the party-slot
   id array at +204; see Section 6.5), is indexed by the relation type with a 16-byte slot stride; the
   maximum slot count (array length) is not bounded by the apply path. Bound it from the UI or a
   capture.
10. **24-byte (2:83) and 56-byte (3:21) chat context headers** — only the channel selector dword
    (3:21 offset +4) and the text trailer are decoded; the remaining sender/target/scope header
    fields are not broken out field-by-field.
11. **String-table message ids** referenced here (`862010101` reject; `2119`–`2122` party roster;
    `23004`/`23005` party member left/expelled; `10011`/`2183` guild-leave; `67030` relation) are
    **display ids**, not wire fields — listed for context only.
