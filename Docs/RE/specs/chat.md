# Chat subsystem: input, log/scrollback, channels, overhead bubbles (clean-room spec)

> **Verification banner.** verification: confirmed (control-flow-confirmed) for all client-side
> routing, channel codes, message sizes, struct/field offsets, the (2:7) 19-byte header, and the
> per-opcode text-length-prefix NUL convention; capture/debugger-pending for the absolute on-wire
> byte-order/endianness of every length prefix, the (5:7) text-body framing, and the channel-code
> VALUE meanings (which routing/colour a given code drives on the live wire).
> ida_reverified: 2026-06-27 (CYCLE 14 re-anchor (f61f66a9): confirmatory — subsystem cleanly relocated, 1 re-confirmed SAME, 0 corrected; prior: 2026-06-22) · ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963 · evidence: [static-ida].
> readiness: IMPLEMENTATION-READY for the C# rebuild (control-flow-confirmed against IDB SHA 263bd994); items explicitly tagged debugger-pending / capture-pending / RD-* are NON-blocking runtime residuals to confirm later.
> CYCLE 11 World block (263bd994): the (5:7) S2C body framing is RESOLVED to an explicit `[u32 len][len CP949 bytes]` prefix (§8.2) — superseding the "rest of frame" hypothesis; the S2C-only notice codes 8/10/16/17 were added; code 7 = pink `0xFFFF797C` (§3) re-confirmed against the binary (a dirty mis-read was caught and reverted).
> conflicts resolved this pass: (a) the (2:7) text-length prefix EXCLUDES the NUL (the earlier
> "believed to include NUL" reading was wrong); (b) (3:21) is a genuine length-prefixed channel-chat
> sender, not merely a "special announce" — the earlier "NOT a chat carrier" framing understated it;
> (c) the NUL-inclusion of the length prefix is **per-opcode**, not a single global convention.
> CAMPAIGN 17 Phase F re-confront (263bd994): (d) the 36-byte line record's two trailing fields were
> TRANSPOSED — corrected to +0x1C = channel code, +0x20 = colour (the append sink writes channel into
> the next-to-last field and colour into the last); (e) Open Question 6 RESOLVED — the wrapped-line
> width derives from `CHAT_WINDOW_FONT_SIZE` and the background height from `CHAT_WINDOW_SIZE` +
> `CHAT_WINDOW_FONT_SIZE`.
> Re-verified against the live IDB SHA 263bd994, CYCLE 7 (2026-06-20): (f) the chat channel-code enum
> carried by the first `(2:7)` payload byte was re-confirmed directly from the channel getter/setter,
> the chat-tab selector, and the input-line dispatcher — the **CODE VALUES** are now CONFIRMED to be
> `{0,1,2,3,4,6,7,9,13,15}`. This **corrects** three earlier channel LABELS: code `1` is **whisper**
> (not "shout"), code `9` is **GM / system** (not "whisper"), and code `6` is the **Misia / world-shout**
> family. It also **adds** two codes the prior table omitted: `4` (reserved — accepted as input but
> emits no C2S send) and `13` (a secondary whisper/notice family that shares the log ring of code `7`).
> Only the channel CODE values are confirmed by this pass; the per-code routing/colour VALUE meanings
> stay capture-pending as before.

Neutral, data-only model of the legacy *Martial Heroes* client's **chat subsystem**: the on-screen
input panel, the scrollback log panel with per-channel filtering, the channel model that ties every
typed line to an opcode and a colour, and the overhead "speech bubble" text that floats above an
actor's head. Promoted from dirty-room recon and rewritten in our own words — no decompiler
identifiers, no binary addresses, no pseudo-code.

This document is design input for the **protocol engineer** (the chat wire messages in
`Network.Protocol`), the **application engineer** (the chat-send / chat-receive use cases and the
local log model in `Client.Application`), and the **Godot presentation engineer** (the chat window
layout, scrollback render, and overhead-bubble world text). It pairs with the social wire spec
`Docs/RE/specs/social.md`, the C2S spec `Docs/RE/packets/2-7_whisper.yaml`, and the S2C spec
`Docs/RE/packets/5-7_chat_broadcast.yaml`.

Opcodes are expressed as `(major:minor)` tuples consistent with the authoritative `opcodes.md` frame
model (8-byte header: `size` @+0, `major` @+4, `minor` @+6, payload @+8). **All wire field offsets
in this document are payload-relative** (relative to frame +8). In-memory struct offsets are stated
explicitly as such and are relative to the named object's base.

---

## Status header (read first)

> **Headline correction — the everyday say-box emits ONE opcode `(2:7)`, but `(3:21)` is a SECOND,
> genuine channel-chat carrier driven by a separate dispatcher.**
> The everyday channels typed into the chat **input box** — say, party, guild, shout, alliance, and
> whisper — are **all the same C2S message `(2:7)`**, emitted by a **single chat sender**, distinguished
> only by the **first payload byte = channel code** (`0`/`1`/`2`/`3`/`6`/`7`/`9`/`13`/`15`, plus `4`
> accepted-but-not-sent). The chat input
> editbox only ever emits `(2:7)` for normal typed text — that part is control-flow-confirmed.
> The openers `(2:82)`, `(2:83)`, `(2:84)`, and `(3:21)` are **not** emitted by the say-box; they are
> emitted by a **separate command/button dispatcher** (`Section 4.3`). But `(3:21)` is **not** a mere
> "special announce" — it is a **real length-prefixed channel-chat builder** (56-byte context header +
> CP949 text, channel selector at header `+4`, `selector mod 10 == 5` = a broadcast/shout path that
> bypasses the length gate). `(2:82)`/`(2:83)`/`(2:84)` are the contextual/variant chat builders on
> that same dispatcher. So both readings reconcile: the *input box* → `(2:7)` only; the *chat-command
> dispatcher* → `(2:82)`/`(2:83)`/`(2:84)`/`(3:21)`. See `Docs/RE/specs/social.md` Sections 2.1 and 4.

> **NUL convention is per-opcode (control-flow-confirmed).** The `u32` text-length prefix is computed
> differently per sender: `(2:7)` uses the string length and **EXCLUDES** the terminating NUL;
> `(3:21)` and `(2:83)` use length-plus-one and **INCLUDE** the NUL. This supersedes the earlier
> uniform "believed to include NUL" hypothesis. The prefix arithmetic is a hard static fact in each
> builder; only the absolute on-wire byte-order/endianness remains capture-pending.

> **What still needs a live capture.** No live network capture was available during this analysis.
> Every claim about *which opcode carries chat*, *how the channel code selects routing/colour*, the
> message sizes, the struct/field offsets, and the per-opcode NUL arithmetic are hard static facts
> (read from the client's input parser, sender builders, and log-append sink). What is **not** pinned
> and stays capture/debugger-pending: the absolute byte-order/endianness of each length prefix, the
> framing of the S2C `(5:7)` text body past its 36-byte header, and the on-wire VALUE meanings of the
> channel codes (which routing a given code actually drives server-side).

| Area | Grade | Confidence note |
|---|---|---|
| Three-class decomposition (input / output-log / overhead-bubble) | CONFIRMED | Read from class layout + draw order |
| Everyday say-box channels all ride `(2:7)`, first byte = channel code | CONFIRMED | From the input parser's single-sender calls |
| Channel code → opcode / log colour / bubble slot table | CONFIRMED (routing) · CAPTURE-PENDING (wire VALUE meaning) | From parser branch constants |
| Channel-code enum `{0,1,2,3,4,6,7,9,13,15}` (CYCLE 7) — code 1 = whisper, 9 = GM, 6 = Misia | CONFIRMED (code values) · CAPTURE-PENDING (per-code VALUE meaning) | Re-confirmed from the channel getter/setter, tab selector, and input dispatcher (263bd994) |
| `(2:82)`/`(2:83)`/`(2:84)`/`(3:21)` are NOT emitted by the say-box (separate dispatcher) | CONFIRMED | The chat-command dispatcher emits them, not the input parser |
| `(3:21)` is a genuine length-prefixed channel-chat carrier (56-byte hdr, selector `+4`) | CONFIRMED | Real chat builder; `mod 10 == 5` bypasses the gate |
| `(2:7)` text-length prefix EXCLUDES the NUL; `(3:21)`/`(2:83)` INCLUDE it (per-opcode) | CONFIRMED | The `+1` is literally present/absent per builder |
| `(2:84)` is header-only (no text tail) and 30-second rate-limited | CONFIRMED | No text appender in the builder; 30000 ms cooldown gate |
| Log = 1000-line ring, 36-byte records, 12 visible lines | CONFIRMED | Buffer sizes + render loop bound |
| Line record field order: +0x1C channel code, +0x20 colour (ARGB) | CONFIRMED | The append sink writes channel into +0x1C, colour into +0x20 |
| Per-channel filter checkboxes and colour table | CONFIRMED | Read from BuildScene + render filter |
| Overhead-bubble fields living ON the Actor struct, 5000 ms life | CONFIRMED | Field-block offsets + expiry stamp |
| Wrapped-line width from FONT_SIZE; bg height from SIZE+FONT_SIZE | CONFIRMED | Read from the relayout routines (Open Question 6 resolved) |
| Absolute on-wire byte-order of each length prefix | CAPTURE-PENDING | Static control flow firm; live confirmation not done |
| S2C `(5:7)` text body framing past the 36-byte header | CONFIRMED (header) · CAPTURE-PENDING (body byte-order) | Header + length-prefixed field reader firm |
| `channel == 11` special log-insert path | STATIC-HYPOTHESIS | Distinct insertion routine; purpose not recovered |
| `channel > 100` floating-notice routing | CONFIRMED (route) · STATIC-HYPOTHESIS (purpose) | A separate text system, not the log |

All chat text is **CP949 / EUC-KR** encoded (no BOM), and is modelled as fixed/length-prefixed byte
blobs on the wire and as CP949 byte runs in memory — never as managed strings. Word-wrap and caret
stepping are CP949 lead-byte aware (high bit set ⇒ 2-byte glyph).

---

## 1. Architecture — three cooperating components plus a per-actor field block

The chat subsystem is three distinct UI components, each with a clear role, plus a block of fields
that the overhead-bubble renderer reads directly off the **Actor** struct:

1. **Chat input panel** — the window that owns the **single-line input box**. Captures the typed
   line, runs the input parser on Enter, and hands the result to both the local log and the network
   sender. (Section 2.)
2. **Chat output / log panel** — the scrollback **log** with its **1000-line ring buffer**, its
   **12-line visible window**, its **per-channel filter checkboxes**, the **channel tabs** that pick
   the active send-channel, and the per-account window configuration. (Sections 3–6.)
3. **Overhead speech-bubble renderer** — a world-space text component that walks all visible actors
   each frame and draws each actor's live bubble strings projected above its head. (Section 7.)

The **log-append sink** in the output panel is the single point where text enters the scrollback. It
is called from **two** sources: the **local echo** (the input parser echoes the player's own line)
and the **inbound chat handler** (the `(5:7)` broadcast handler appends remote players' chat). Both
paths reuse the same record format, ring, colours, and filters.

---

## 2. Chat input panel

### 2.1 Layout and capacity (CODE-CONFIRMED)

- The input panel owns one **single-line input box** as a child component, created at panel-local
  position **(5, 4)** with a size of **330 × 20** pixels.
- The input box has a hard **maximum length of 100 characters**. The editbox itself is a generic
  CP949/IME-capable text field (focus flag, ~500 ms caret blink, IME registration) shared with other
  text panels; only the chat-specific wiring (size, cap, Enter handler) is in scope here.

### 2.2 Enter handler — the chat-input parser (CODE-CONFIRMED)

On Enter, the input parser runs the following pipeline. (The same parser is reused as a generic Enter
handler by other editbox panels; the chat editbox is its primary caller.)

1. **Read and clean the line.** Read the typed string out of the input box and strip trailing
   newline / carriage-return bytes.
2. **Handle utility / slash commands inline** (these do **not** send chat):
   - `/option <int> <int>` — writes a per-account option to the local INI (the in-client settings
     store).
   - `/msgchk <int>` — a server message-check request by id.
   - `/help` (and a bare `/`) — help text.
   - `/show 3dgage`, `/hide 3dgage` — toggle a debug/gauge overlay.
   - **GM-gated debug commands** — `/item`, `/killdrop`, `/sysctl`, `/sysicon` are handled by a
     dedicated system-slash-command handler, **gated on the GM flag** (an in-client GM-mode byte);
     they are not wire chat. (re-verified this pass; CONFIRMED)
   - Chat-macro / shortcut expansion — per-character slash macros keyed by a config entry of the
     form `"<name>_CHATSHORTCUT"`. The macro table grammar is enumerated but not fully decoded
     (Open Question 5).
3. **Otherwise it is chat.** Determine the **active channel** by reading the output panel's
   **selected-channel** field (set by the channel tabs, Section 5), or `0` (say) by default; or a
   slash-style prefix may force whisper mode (Section 2.3). Then:
   1. **Echo locally** — append the line to the local log via the log-append sink with the active
      channel's colour (Section 3).
   2. **Send `(2:7)`** — call the unified chat sender with the **channel code as the first payload
      byte** (Section 4).
   3. **Stamp a local overhead bubble** — write the typed text into the matching bubble slot on the
      **local player's** Actor struct and set its **expiry = current time + 5000 ms** (Section 7).

### 2.3 Whisper entry (CODE-CONFIRMED · routing) / (PLAUSIBLE · trigger)

Whisper is channel **1** (CYCLE 7 correction — an earlier draft labelled whisper as code 9; code 9 is
the GM / system path). It is reached when the input-line dispatcher **space-splits** the typed line
into a leading **target character name** and the remaining text; the name fills the 17-byte
target-name field of the `(2:7)` header and the line uses the whisper log colour. The whisper text cap
is **119 characters** (distinct from the 100-char editbox cap that applies to the general say box). See
`Docs/RE/packets/2-7_whisper.yaml` for the on-wire header layout already promoted for the named-target
case.

---

## 3. Channel model — code → opcode, colour, and bubble slot

A small integer **channel code** is the spine of the whole subsystem: it selects the C2S send path
(always `(2:7)`, with this code as the first payload byte), the **log line colour**, and **which
overhead-bubble slot** the line fills. The active channel comes from the selected chat tab
(Section 5) or, for whisper, from the whisper-mode prefix path (Section 2.3).

> **Reading note.** Colours are listed as 32-bit **ARGB** (`0xAARRGGBB`). The "world height ×"
> column is the multiplier applied to the actor's scale to lift the bubble above its head
> (Section 7). Channel `9` (whisper) is **log-only** — no overhead bubble.

> **CYCLE 7 (2026-06-20) channel-code re-confirmation.** The channel **CODE values** below were
> re-confirmed directly from the channel getter/setter on the chat-log object, the chat-tab selector,
> and the input-line dispatcher (which sends each code through the unified `(2:7)` sender with the code
> as a literal token). The full confirmed code span is `{0, 1, 2, 3, 4, 6, 7, 9, 13, 15}`. This pass
> **corrected three labels** versus the prior draft — code `1` is **whisper** (it space-splits a name
> off the line and uses the 17-byte target-name field), code `9` is **GM / system** (the "/" GM-prefix
> path), and code `6` is the **Misia / world-shout** broadcast family — and **added** codes `4`
> (reserved: accepted as a valid input code but routes to no `(2:7)` send) and `13` (a secondary
> whisper / notice family that shares the second log ring with code `7`). The CODE values are
> **CONFIRMED**; the per-code log colour, bubble slot, and on-wire routing **VALUE meanings** remain
> capture-pending (the colours/bubble columns below are the statically-read client behaviour, not the
> server's wire semantics).

| Code | Channel (label) | C2S | Log colour (ARGB) | Log colour name | Bubble slot (Section 7) | Bubble colour (ARGB) | World height × | Code status |
|---|---|---|---|---|---|---|---|---|
| 0  | say / normal           | (2:7) | `0xFFFFFFFF` | white    | say                | PC `0xFFFFFF00` / NPC white | scale × 10 | CONFIRMED |
| 1  | whisper                | (2:7) | `0xFFCC99FF` | lavender | — (log only)       | —                          | —          | CONFIRMED (label corrected) |
| 2  | party                  | (2:7) | `0xFF00FFFF` | cyan     | party              | `0xFF00FFFF`               | scale × 12 | CONFIRMED |
| 3  | guild                  | (2:7) | `0xFF33FF66` | green    | guild / alliance   | `0xFF365C66` (green)        | scale × 9  | CONFIRMED |
| 4  | reserved / local       | —     | —            | —        | —                  | —                          | —          | CONFIRMED (accepted, no send) |
| 6  | Misia / world-shout    | (2:7) | `0xFFFFFF00` | yellow   | party-area         | `0xFFFF0055`               | scale × 7  | CONFIRMED (label refined) |
| 7  | special Misia          | (2:7) | `0xFFFF797C` | pink     | special            | `0xFFFFFF00` (yellow)       | scale × 9  | CONFIRMED |
| 9  | GM / system            | (2:7) | `0xFFFF797C` | pink     | — (log only)       | —                          | —          | CONFIRMED (label corrected) |
| 13 | secondary whisper / notice | (2:7) | —        | —        | — (log only)       | —                          | —          | CONFIRMED (VALUE label capture-pending) |
| 15 | alliance               | (2:7) | `0xFF82C4FF` | blue     | guild / alliance   | `0xFF82C4FF` (blue)         | scale × 9  | CONFIRMED |

Notes:
- **Guild vs alliance share one bubble slot** (the guild/alliance slot) and are distinguished by a
  **blue-variant flag** on that slot: guild renders green, alliance renders blue (Section 7).
- The "world height ×" multipliers observed are `{7, 8, 9, 10, 12}`; the table above lists the value
  per channel. Mob/NPC say-bubbles do not use a flat multiplier — they anchor to the visual model's
  bounding-box top instead (Section 7).
- **Channel-tag literal prefixes / send tokens (CONFIRMED).** For the non-say channels the
  input-line dispatcher selects the sender by a **literal channel-key token** before the `(2:7)` send:
  `"party"` (ch 2), `"guild"` (ch 3), `"misia"` (ch 6), `"specialmisia"` (ch 7), and `"alliance"`
  (ch 15). **Whisper (ch 1)** is reached differently — the dispatcher **space-splits** the line into a
  leading target name and the remaining text, and the name fills the 17-byte target-name field of the
  `(2:7)` header (Section 4.2). A re-implementation that builds the `(2:7)` text tail must reproduce
  these tokens for those channels and the name/text split for whisper.
- **Codes 4 and 13 (CONFIRMED codes).** Code `4` is a **valid input code that produces no `(2:7)`
  send** (a reserved / local-only slot). Code `13` is a **secondary whisper / notice family**: the log
  enqueue routine routes codes `7` and `13` into the **same second log ring** (and code `10` into a
  third), confirming `7`/`13` are a related whisper/notice group. The exact display label of code `13`
  is capture-pending.
- **Channel-code VALUE meanings are capture-pending.** The CODE values themselves are CONFIRMED
  (CYCLE 7), but what a given code means on the live wire server-side — the per-code routing/colour and
  whether the proposed channel labels (Misia vs special-Misia, GM vs system, the code-13 family) are
  the server's own naming — needs a capture to confirm.

---

## 4. Send path — the say-box sender `(2:7)` and the chat-command dispatcher

### 4.1 One say-box sender for all everyday channels (CONFIRMED)

All everyday chat typed into the **input box** — say, shout, party, guild, alliance, event, special,
and whisper — is sent through **one chat sender** as opcode **`(2:7)`**. The input parser selects the
network target/handler for the channel and then calls this sender; the **channel code is written as
the first byte of the payload**. There is no separate "say" opcode versus "party" opcode at the C2S
layer for typed chat — only the channel byte differs. The sender sets `major = 2`, `minor = 7` and
writes the channel argument at payload `+0`.

The openers `(2:82)`, `(2:83)`, `(2:84)`, and `(3:21)` are **not** emitted by the say-box; they are
emitted by a **separate chat-command/button dispatcher** (Section 4.3). Their roles, re-derived this
pass, are:

- `(3:21)` is a **genuine channel-chat sender** — 56-byte context header + a length-prefixed CP949
  text tail, with a channel/scope selector at header `+4`. This is NOT a mere "special announce"; the
  earlier framing understated it (see the headline correction above and `social.md` Section 4).
- `(2:83)` is the **contextual chat** builder (24-byte header + length-prefixed text, gated
  `0 < len < 200`).
- `(2:82)` is a **28-byte context-header** chat variant (no text in the builder itself; any text is
  appended by the caller).
- `(2:84)` is a **header-only**, **30-second rate-limited** chat variant (19-byte header, no text
  tail in the builder).

These are catalogued under the social subsystem; see `Docs/RE/specs/social.md` Sections 2.1 and 4. A
re-implementation must route everyday typed chat through `(2:7)`, and must route the dispatcher-driven
channel/contextual chat through `(3:21)`/`(2:83)`/`(2:82)`/`(2:84)` respectively — not assume the
say-box carries them.

> **Naming note for the catalog.** Because `(2:7)` is now understood to carry **all** everyday say-box
> chat channels (not only named whispers), the spec-author recommends renaming the catalog entry for
> `(2:7)` from `CmsgWhisper` to **`CmsgChat`** (with whisper as channel 9), and folding the channel
> enumeration below into `Docs/RE/packets/2-7_whisper.yaml` and `opcodes.md`. This is a proposal for
> the orchestrator-owned `names.yaml` / `opcodes.md`; confirm before committing the rename.

### 4.2 `(2:7)` payload framing (CONFIRMED header/sizes · CAPTURE-PENDING wire byte-order)

The `(2:7)` payload is a **fixed 19-byte header followed by a length-prefixed text tail**. The header,
payload-relative, is: the **channel code** at `+0`, a **flag byte** at `+1` (the second UI argument),
a fixed **16-byte target-name buffer** at `+2` (used by whisper; the name is `strncpy`-copied capped
at 16 bytes into a NUL-cleared 17-byte field), and a **trailing header byte** at `+18` (written zero)
that completes the 19-byte header. After the header comes a `u32` text-length prefix and that many
CP949 bytes; the body is copied with a hard cap of **119** characters (whisper).

> **Length-prefix convention (CONFIRMED, per-opcode).** The `(2:7)` text-length prefix is the **string
> length and EXCLUDES the terminating NUL** (the builder appends the body with the length-prefixed
> appender using the plain string length, with no `+1`). This is the **corrected** reading — the
> earlier "believed to include the NUL" hypothesis was wrong for `(2:7)`. By contrast `(3:21)` and
> `(2:83)` use length-plus-one and DO include the NUL (Section 4.3). The arithmetic is control-flow
> firm; only the absolute on-wire byte-order/endianness of the prefix is capture-pending.

The exact meaning of the flag byte and the trailing header byte are still capture-pending. See
`Docs/RE/packets/2-7_whisper.yaml` for the byte-by-byte header table.

### 4.3 The chat-command dispatcher — `(2:82)` / `(2:83)` / `(2:84)` / `(3:21)` (CONFIRMED)

A **separate command/button dispatcher** (a UI action-id path, distinct from the say-box input
parser) is the single point that emits the four dispatcher chat openers. It fills a per-message
context header before the builder runs, then (for the text-bearing ones) appends a length-prefixed
CP949 body. The header sizes and gates, re-derived this pass:

| Opcode | Header size | Text tail | Length-prefix NUL | Gate observed |
|---|---|---|---|---|
| `(3:21)` channel chat   | 56 bytes | length-prefixed | **INCLUDES NUL** (`strlen + 1`) | selector at header `+4`; when `selector mod 10 == 5` the empty/`< 200` length gate is bypassed (a broadcast/shout path), otherwise text must be `0 < len < 200` |
| `(2:83)` contextual chat | 24 bytes | length-prefixed | **INCLUDES NUL** (`strlen + 1`) | text length gated `0 < len < 200` |
| `(2:82)` context variant | 28 bytes | none in builder (caller-appended) | — | none in the builder |
| `(2:84)` variant         | 19 bytes | **none** (header-only) | — | **30000 ms (30 s) client-side rate limit** before another `(2:84)` may send |

`(2:84)` is therefore **not** a text-bearing message in its builder — the social spec's earlier
"19 + text" entry is corrected to "19-byte header, no text tail, 30 s cooldown". Its purpose
(plausibly an emote/macro broadcast trigger) is capture-pending. See `Docs/RE/specs/social.md`
Section 4 for the per-message context-header field breakdowns and the `(3:21)` channel selector. A
separate **2-byte `(2:21)`** sender also exists (easy to confuse with `(3:21)`); its purpose is
unrecovered (a small toggle/ack — static-hypothesis) and it is **not** part of the chat-text family.

---

## 5. Channel tabs and the active channel (CODE-CONFIRMED)

The output panel hosts a strip of **channel tab buttons**. Clicking a tab sets the panel's
**active-tab channel** field and its **selected-channel** field (the value the input parser reads to
pick the send channel) and highlights the active tab button (active colour vs grey). The observed
tab → channel mapping is:

| Tab index | Channel code | Channel |
|---|---|---|
| 3 | 0     | say    |
| 4 | 3     | guild  |
| 5 | 15    | alliance |
| 6 | 2     | party  |
| 7 | 6 / 7 | event (misia / special) |
| 8 | 1     | shout  |
| 9 | 0     | notice |

This is how the input box "knows" which channel the next typed line is sent on.

---

## 6. Chat output / log panel

### 6.1 Panel geometry (CODE-CONFIRMED)

The output panel BuildScene assembles the whole window:

- **Root panel** **448 × 324**; a background sprite **448 × 227**; a bottom input bar **448 × 41** at
  `y = 227`; a **17 px-wide** right scrollbar column with scroll-up / scroll-down buttons.
- **Window position** is loaded from a **per-account INI section** keyed `"<billing_id>_<charname>_CHAT"`,
  with the following keys and defaults (`screenW` / `screenH` are the current screen dimensions):

  | INI key | Default | Stores |
  |---|---|---|
  | `CHAT_WINDOW_POS_X`   | `screenW / 2 − 512` | window x |
  | `CHAT_WINDOW_POS_Y`   | `screenH − 324` (clamped on-screen) | window y |
  | `CHAT_WINDOW_SIZE`    | `1`  | window-size mode (1–3; drives bg height + visible-cell pitch) |
  | `CHAT_WINDOW_FONT_SIZE` | `12` | font size (11–13; drives max-chars-per-wrapped-line) |

> The same `448 × 324` window is the in-game HUD chat host frame referenced by
> `specs/ui_hud_layout.md §1.2`; that spec's bottom anchor (`screenW/2 − 512`, `screenH − 324`) is the
> `CHAT_WINDOW_POS_X/Y` default above. The chat **input** editbox (Section 2.1, `330 × 20` at panel-local
> `(5, 4)`) is the separate input-panel class.

### 6.2 The line record and the ring buffer (CODE-CONFIRMED)

The scrollback is a **1000-line ring buffer** of fixed-stride records. Each record is **36 bytes**:

| Offset (record-relative) | Size | Type | Meaning |
|---|---|---|---|
| `+0x00` | 28 | CP949 string | line text (small-string-optimised: 16-byte inline buffer + ptr / len / cap) |
| `+0x1C` | 4  | int | channel code |
| `+0x20` | 4  | u32 / ARGB | line colour |

Record stride is **36 bytes** (`0x24`). The panel zero-initialises **1000** such records at build
time in **two parallel arrays** — the raw lines and their word-wrapped form — so the ring length is
**1000 lines**. A line counter (capped at 1000) and a ring start index track the live window.

> **Field order (re-confirmed, CODE-CONFIRMED).** The record store writes the text first, then the
> **channel code at `+0x1C`** and the **colour (ARGB) at `+0x20`** — in that order. The append sink
> passes `(text, channel, colour)` and writes them into the next-to-last and last record words
> respectively. An earlier draft listed these two fields transposed (`+0x1C` colour, `+0x20`
> channel); the order above is the corrected, binary-confirmed layout.

### 6.3 Append sink (CONFIRMED · route) / (STATIC-HYPOTHESIS · special-path purpose)

The **log-append sink** takes `(text, channel, colour)` and is the single entry point for both local
echo and inbound chat. Its routing on the channel value:

- **`channel < 100`** → append to the chat log: bump the line counter (capped at 1000), build a line
  record, and insert it into the ring. (`channel == 11` takes a distinct insertion routine within
  this band — see below.)
- **`channel == 11`** → a **distinct insertion path** (purpose not recovered — possibly a
  system/important or "pinned" style line; Open Question 3).
- **`channel > 100 && channel != 110`** → **route to a separate floating "system / notice" text
  system**, *not* the chat log. This is its own on-screen scrolling-notice subsystem and is out of
  scope for the chat log.
- **`channel == 100` and `channel == 110`** → **both dropped** (re-verified this pass: `100` is also
  dropped, not only `110`).

### 6.4 Render — 12 visible lines, filters, CP949 word-wrap (CODE-CONFIRMED)

Each frame the output panel renders a **12-line visible window** over the ring:

- The visible window height is **12 lines** minus the current **scroll offset**; the loop walks the
  ring from the start index with a 1000-entry cap check.
- **Per-channel filter checkboxes.** There are **7** filter checkboxes (each 10 × 10, placed along a
  row). A line of channel `c` is shown only if the checkbox governing that channel is enabled; a line
  is dropped when its channel matches a filter whose enable byte is `0`. The observed filter →
  channel governance is:

  | Filter | Governs channel(s) |
  |---|---|
  | general | general lines |
  | guild   | 3 (guild) |
  | alliance| 15 (alliance) |
  | party   | 2 (party) |
  | event   | 6 & 7 (event / special) |
  | shout   | 1 (shout) |
  | (name)  | per-line "is-name-line" flag |

  By default the first six filters are **checked (enabled)** and the seventh is **unchecked**.
- **CP949-aware word-wrap.** The renderer steps the text byte-by-byte with a CP949 lead-byte test
  (high bit set ⇒ 2-byte glyph, else 1 byte) and wraps at a **max-chars-per-line** field; long lines
  split across multiple visible rows. **Source of the max-chars field (Open Question 6, RESOLVED):**
  the relayout routines derive the wrapped-line width directly from `CHAT_WINDOW_FONT_SIZE` and the
  background height from both `CHAT_WINDOW_SIZE` and `CHAT_WINDOW_FONT_SIZE`:
  - **Font size → chars per wrapped line:** `11 → 85`, `12 → 71`, `13 → 61` characters (a fourth
    branch maps to `43`). So the default font size `12` yields **71** chars per line.
  - **Size mode → vertical cell-pitch factor:** mode `0 → 12`, `1 → 10`, `2 → 8`, `3 → 6`. The
    background height is then `2 × factor × (fontSize − 5) + 11`, and the visible-region height is
    `227 − that`. So `CHAT_WINDOW_SIZE` selects the pitch factor and `CHAT_WINDOW_FONT_SIZE` both the
    chars-per-line and (jointly with SIZE) the background height.
- Each visible line writes its text + colour into a pre-allocated child label and applies a per-line
  "is-name-line" flag.

---

## 7. Overhead speech bubbles

### 7.1 Where the bubble state lives (CODE-CONFIRMED)

Overhead bubble text and lifetime are **fields ON the Actor struct**, one slot per channel family.
The bubble renderer walks all visible actors each frame; for each actor it **first expires** any
stale bubble (clearing the slot when the current time has passed its expiry), **then draws** any
slot that is still live. Because the state lives on the actor, both the local player's own line
(stamped by the input parser, Section 2) and remote players' chat (stamped by the `(5:7)` inbound
handler, Section 8) display above the correct head with no extra plumbing.

All bubble slots are CP949 strings (small-string-optimised); the lifetime is a separate expiry
timestamp dword. Offsets below are **relative to the Actor struct base**:

Offsets re-pinned to build 263bd994. The `len @` column is the small-string-optimised slot base
(the inline-buffer / length word the bubble stamper writes through); `text @` is where the readable
CP949 run begins.

| Slot (channel family) | text @ | len @ | cap @ | expiry @ | flag @ | Colour (ARGB) | World height × |
|---|---|---|---|---|---|---|---|
| say / normal      | `+1564` | `+1560` | `+1584` | `+1588` | — | PC `0xFFFFFF00`, mob bbox-based | scale × 10 (PC) |
| party             | `+1592` | `+1612` | `+1616` | `+1620` | — | `0xFF00FFFF` | scale × 12 |
| shout-area        | `+1628` | `+1644` | `+1648` | (shared) | — | `0xFFFF0055` | scale × 7 |
| guild / alliance  | `+1692` | `+1712` | `+1716` | `+1720` | `+1724` | guild `0xFF365C66` / alliance `0xFF82C4FF` | scale × 9 |
| special event     | `+1728` | `+1748` | `+1752` | `+1756` | — | `0xFFFFFF00` | scale × 9 |

Supporting actor fields the bubble renderer reads:

| Actor field | Meaning |
|---|---|
| `+100`  | actor scale (float) — multiplies the bubble height factor |
| `+1064` | actor world position (float[3]) — the bubble anchor |

The **guild / alliance** slot's `flag @ +1724` is the **blue-variant selector**: `0` → guild (green),
`1` → alliance (blue).

### 7.2 Lifetime and placement (CODE-CONFIRMED)

- **Lifetime is 5000 ms.** Every bubble's expiry is stamped as **current time + 5000 ms** at creation
  (by the input parser for the local player, by the `(5:7)` handler for remote actors) and is cleared
  by the per-frame expiry check above.
- **Vertical placement** lifts the bubble above the actor's head by **`actor.scale × factor`** world
  units, where `factor ∈ {7, 8, 9, 10, 12}` per the channel (the "World height ×" column above). The
  lift is applied as a `+Y` world translation from the actor's world position, then the 3D anchor is
  projected to screen.
- **Mob / NPC say-bubbles** do **not** use a flat scale multiplier; they anchor to the **visual
  model's bounding-box top** (the model bbox top plus a small constant offset).

### 7.3 World-text layout — wrap, leading, anchoring (CODE-CONFIRMED)

The world-text placer draws a bubble's (possibly multi-line) string:

- It CP949-steps the string and inserts a line break every wrap-width's worth of pixels (the bubble
  wrap width default is **20 px**), splitting into lines.
- It projects the world anchor to a screen point, then draws the lines **bottom-up** with a fixed
  **14 px line leading**.
- The multi-line block is **vertically centred** about the anchor (offset by `14 − 14 × lineCount`),
  and each line is **horizontally centred** about the screen point (offset by roughly
  `−(12 × charCount) / 4`).

So overhead bubbles are **centre-anchored, multi-line, fixed 14 px leading**, drawn in the
per-channel colour.

---

## 8. Receive path — the `(5:7)` chat broadcast handler

### 8.1 Routing (CONFIRMED · structure) / (CAPTURE-PENDING · body byte-order)

Inbound chat arrives as **`(5:7)` `SmsgChatBroadcast`**. The handler reads a **36-byte fixed header**
(via the standard length-prefixed field reader), then a **variable text body**, and:

- **plays a chat-receive sound effect** — string-table sound id **862030103** at handler entry
  (re-verified this pass);
- **appends the line to the same 1000-line log ring** via the shared log-append sink (Section 6.3),
  using the per-channel colour — so remote chat lands in the scrollback exactly like local echo (the
  say branch formats the line with message-template id **17003**);
- **resolves the speaker actor** by a `(sort, id)` pair (gated on the local player existing); and
- **writes the speaker actor's overhead-bubble slot** (same Actor field block as Section 7), stamping
  the **5000 ms** expiry relative to the local clock — so remote players' chat floats above their
  heads;
- **routes whisper** (the `6` / `7` branch of the inbound `+0x0E` channel byte) through a dedicated
  whisper-display path (the inbound whisper / reply-target wiring was not decompiled in depth — Open
  Question 7). Note: the inbound `+0x0E` switch is the **S2C handler's own** branch and is distinct
  from the **C2S input channel enum** of Section 3 (where whisper is code `1` and `6`/`7` are the
  Misia broadcast family) — the two are not the same code space; only a capture can confirm how the
  server maps one to the other.

### 8.2 Header layout (CONFIRMED · header size + key offsets) / (CAPTURE-PENDING · body byte-order)

The 36-byte header is, payload-relative:

| Offset | Size | Field | Meaning |
|---|---|---|---|
| `+0x00` | 1  | sender sort | actor-category discriminator (CONFIRMED) |
| `+0x04` | 4  | sender id   | sender actor id (STATIC-HYPOTHESIS — exact span not isolated this pass) |
| `+0x08` | 4  | context id  | target / room / whisper-peer id (CONFIRMED — consumed by the whisper-peer/context path) |
| `+0x0D` | 1  | sub-command | chat verb / sub-command — drives a `0`/`1`/`2` branch (CONFIRMED) |
| `+0x0E` | 1  | channel     | channel code; `6` / `7` ⇒ whisper route, then the channel-dispatch switch (CONFIRMED) |
| `+0x10` | 20 | sender name | sender display name, fixed buffer NUL-terminated (`+0x10..+0x24`; STATIC-HYPOTHESIS — header size firm, exact name span inferred) |

The **text body** that follows is now resolved (CYCLE 11 re-walk): past the 36-byte header the body is
exactly **one length-prefixed segment — a `u32` byte-count followed by that many CP949 bytes**. The
client copies exactly that many bytes and appends its **own NUL** (the NUL is client-added and is NOT
part of the counted length). So the framing is `[u32 len][len CP949 bytes]`, NOT "rest of frame", and
the total frame size is:

`frame.size == 8 (frame header) + 36 (chat header) + 4 (body length word) + body_len`.

This **SUPERSEDES** the earlier `body_length = frame.size − 8 − 36` hypothesis, which over-counted by
the 4-byte length word. Only the **absolute on-wire byte-order/endianness of the `u32` length word**
(and whether the server's count ever includes a NUL) stays **capture-pending**. See
`Docs/RE/packets/5-7_chat_broadcast.yaml`.

> **Channel routing on the S2C side (re-walked).** The inbound channel switch carries its own code
> space (distinct from the C2S input enum of Section 3 — see the note above). Beyond the
> say/whisper/party/guild/alliance arms it has a **system/notice band**: codes **below 100** route to
> the chat-log ring (code `11` is a distinct insert); codes **above 100** (a contiguous `102..117`
> band) route to the **separate floating notice-text system**, not the chat log (Section 6.3); codes
> `100` and `110` are dropped by the client. Three S2C-only notice arms were confirmed this pass and
> are NOT C2S input codes: code **10** = yellow `0xFFFFFF00` event notice (message-template id
> `49079`); code **8** = a separate notice-composer arm; codes **16/17** = a red/orange `0xFFFF4040`
> notice tail. (Reconciliation note: a CYCLE 11 dirty pass briefly mis-read code `7` as red and code
> `8` as the yellow `49079` arm; the fresh counter-walk confirmed the binary keeps code `7` = pink
> `0xFFFF797C` — matching Section 3 — and puts the yellow `49079` notice on code **10**. Section 3 was
> already correct; the per-code routing/colour **VALUE meanings of the `102..117` notice band remain
> capture-pending**.)

---

## 9. Open questions (capture- or trace-blocked)

1. **`(5:7)` body byte-order** — the body IS read by a length-prefixed field reader (the framing
   shape is firm); the remaining unknown is the absolute on-wire byte-order/endianness of the
   length prefix and body. Only this — not the framing model — is **capture-pending**.
2. **On-wire prefix byte-order, not opcode carriage.** The opcode carriage is now resolved
   statically: the say-box emits only `(2:7)`; the chat-command dispatcher emits `(2:82)`/`(2:83)`/
   `(2:84)`/`(3:21)`; and the NUL convention is per-opcode (`(2:7)` excludes, `(3:21)`/`(2:83)`
   include). What a capture would still pin is the absolute byte-order/endianness of each length
   prefix — a wire-VALUE detail, not a control-flow question.
3. **`channel == 11` special log-insert path** — distinct insertion routine; purpose (system /
   important / pinned line?) not recovered.
4. **`channel > 100` floating-notice system** — a separate on-screen scrolling/notice text subsystem,
   not the chat log; deserves its own lane.
5. **Chat-macro `"<name>_CHATSHORTCUT"` table and the `/option` / `/msgchk` command grammar** —
   enumerated but not fully decoded; a "chat commands" sub-spec could exhaust them. (The GM-gated
   `/item` / `/killdrop` / `/sysctl` / `/sysicon` commands are now identified — Section 2.2.)
6. **Max-chars-per-wrapped-line field (RESOLVED).** The wrapped-line width is derived from
   `CHAT_WINDOW_FONT_SIZE` (`11 → 85`, `12 → 71`, `13 → 61` chars) and the background height from
   `CHAT_WINDOW_SIZE` + `CHAT_WINDOW_FONT_SIZE` (Section 6.4). The 12-line visible window and the
   1000-line ring are firm. No residual.
7. **Inbound whisper routing** (channel `6` / `7` in the `(5:7)` handler) — the whisper-display /
   reply-target wiring was not decompiled in depth.
8. **`(2:84)` and `(2:21)` purpose** — `(2:84)` is a confirmed header-only, 30 s rate-limited message
   (plausibly an emote/macro broadcast trigger); `(2:21)` is a confirmed 2-byte sender easy to confuse
   with `(3:21)`. Both purposes are **capture-pending**.

---

## 10. Cross-references

- `Docs/RE/packets/2-7_whisper.yaml` — C2S `(2:7)` header byte layout (proposed rename to `CmsgChat`).
- `Docs/RE/packets/5-7_chat_broadcast.yaml` — S2C `(5:7)` `SmsgChatBroadcast` header.
- `Docs/RE/specs/social.md` — the wider social wire protocol; catalogues the dispatcher-driven chat
  family `(2:82)` (28-byte context variant) / `(2:83)` (24-byte contextual chat) / `(2:84)` (19-byte
  header-only, 30 s rate-limited) / `(3:21)` (56-byte channel chat, selector `+4`) (Sections 2.1, 4).
- `Docs/RE/specs/ui_hud_layout.md §1.2` — the in-game HUD chat host frame (`448 × 324`) + input box
  (`330 × 20` at `(5, 4)`) placement, anchored at the `CHAT_WINDOW_POS_X/Y` defaults.
- `Docs/RE/opcodes.md` — authoritative opcode catalogue and frame model.

> **Implementation guidance (presentation).** Chat window = `448 × 324`, **12** visible lines over a
> **1000**-line scrollback, **7** channel-filter toggles, per-channel ARGB colours (Section 3),
> CP949 word-wrap. Overhead bubbles = centre-anchored multi-line world text, **14 px** leading,
> lifted `scale × {7, 8, 9, 10, 12}` above the actor, **5000 ms** lifetime, per-channel colour. The
> local player's own line is echoed to the log and stamped as a bubble at send time; remote lines
> arrive via `(5:7)` and do the same on the speaker actor.
