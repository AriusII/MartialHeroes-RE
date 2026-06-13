# Chat subsystem: input, log/scrollback, channels, overhead bubbles (clean-room spec)

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

> **Headline correction — everyday chat is ONE opcode, not the friend/relation cluster.**
> The everyday channels — say, party, guild, shout, alliance, and whisper — are **all the same C2S
> message `(2:7)`**, emitted by a **single chat sender**, distinguished only by the **first payload
> byte = channel code** (`0`/`1`/`2`/`3`/`6`/`7`/`9`/`15`). This **supersedes** any earlier note that
> read `(2:82)`, `(2:83)`, `(2:84)`, or `(3:21)` as carriers of general say-chat: those openers are
> **friend-note, announce, and relation submits**, driven by a separate UI command/button dispatcher,
> **not** the text-chat parser. The chat input editbox only ever emits `(2:7)` for normal typed text.

> **Capture-unverified, prominently.** No live network capture was available during this analysis.
> Every claim about *which opcode carries chat* and *how the channel code selects routing/colour* is
> a hard static fact (read from the client's input parser, sender installer, and log-append sink).
> But the **byte framing of the text body** — both the C2S tail and the S2C `(5:7)` body — is **not**
> pinned statically and must be treated as a hypothesis until a capture confirms it.

| Area | Grade | Confidence note |
|---|---|---|
| Three-class decomposition (input / output-log / overhead-bubble) | CODE-CONFIRMED | Read from class layout + draw order |
| Everyday channels all ride `(2:7)`, first byte = channel code | CODE-CONFIRMED · CAPTURE-UNVERIFIED | From the input parser's sender calls |
| Channel code → opcode / log colour / bubble slot table | CODE-CONFIRMED · CAPTURE-UNVERIFIED | From parser branch constants |
| `(2:82)`/`(2:83)`/`(2:84)`/`(3:21)` are NOT say-chat | CODE-CONFIRMED | Reclassified by their UI callers |
| Log = 1000-line ring, 36-byte records, 12 visible lines | CODE-CONFIRMED | Buffer sizes + render loop bound |
| Per-channel filter checkboxes and colour table | CODE-CONFIRMED | Read from BuildScene + render filter |
| Overhead-bubble fields living ON the Actor struct, 5000 ms life | CODE-CONFIRMED | Field-block offsets + expiry stamp |
| C2S `(2:7)` text-tail framing (length prefix / NUL inclusion) | PLAUSIBLE · CAPTURE-UNVERIFIED | Inferred from sender read order |
| S2C `(5:7)` body framing past the 36-byte header | PLAUSIBLE · CAPTURE-UNVERIFIED | Only the header is firm |
| `channel == 11` special log-insert path | PLAUSIBLE | Purpose not recovered |
| `channel > 100` floating-notice routing | CODE-CONFIRMED (route) · PLAUSIBLE (purpose) | A separate text system, not the log |

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

Whisper is channel **9**. It is reached when a whisper / GM mode is active and the text is entered in
the whisper form (a slash-style prefix path). It carries a **target character name** and uses the
whisper log colour. The whisper text cap is **119 characters** (distinct from the 100-char editbox
cap that applies to the general say box). See `Docs/RE/packets/2-7_whisper.yaml` for the on-wire
header layout already promoted for the named-target case.

---

## 3. Channel model — code → opcode, colour, and bubble slot

A small integer **channel code** is the spine of the whole subsystem: it selects the C2S send path
(always `(2:7)`, with this code as the first payload byte), the **log line colour**, and **which
overhead-bubble slot** the line fills. The active channel comes from the selected chat tab
(Section 5) or, for whisper, from the whisper-mode prefix path (Section 2.3).

> **Reading note.** Colours are listed as 32-bit **ARGB** (`0xAARRGGBB`). The "world height ×"
> column is the multiplier applied to the actor's scale to lift the bubble above its head
> (Section 7). Channel `9` (whisper) is **log-only** — no overhead bubble.

| Code | Channel (proposed) | C2S | Log colour (ARGB) | Log colour name | Bubble slot (Section 7) | Bubble colour (ARGB) | World height × |
|---|---|---|---|---|---|---|---|
| 0  | say / normal      | (2:7) | `0xFFFFFFFF` | white    | say                | PC `0xFFFFFF00` / NPC white | scale × 10 |
| 1  | shout             | (2:7) | `0xFFCC99FF` | lavender | shout-area         | `0xFFFF0055`               | scale × 7  |
| 2  | party             | (2:7) | `0xFF00FFFF` | cyan     | party              | `0xFF00FFFF`               | scale × 12 |
| 3  | guild             | (2:7) | `0xFF33FF66` | green    | guild / alliance   | `0xFF365C66` (green)        | scale × 9  |
| 6  | event ("misia")   | (2:7) | `0xFFFFFF00` | yellow   | party-area         | `0xFFFF0055`               | scale × 7  |
| 7  | special event     | (2:7) | `0xFFFF797C` | pink     | special            | `0xFFFFFF00` (yellow)       | scale × 9  |
| 9  | whisper           | (2:7) | `0xFFFF797C` | pink     | — (log only)       | —                          | —          |
| 15 | alliance          | (2:7) | `0xFF82C4FF` | blue     | guild / alliance   | `0xFF82C4FF` (blue)         | scale × 9  |

Notes:
- **Guild vs alliance share one bubble slot** (the guild/alliance slot) and are distinguished by a
  **blue-variant flag** on that slot: guild renders green, alliance renders blue (Section 7).
- The "world height ×" multipliers observed are `{7, 8, 9, 10, 12}`; the table above lists the value
  per channel. Mob/NPC say-bubbles do not use a flat multiplier — they anchor to the visual model's
  bounding-box top instead (Section 7).

---

## 4. Send path — the unified chat sender `(2:7)`

### 4.1 One sender for all everyday channels (CODE-CONFIRMED · CAPTURE-UNVERIFIED)

All everyday chat — say, shout, party, guild, alliance, event, special, and whisper — is sent through
**one chat sender** as opcode **`(2:7)`**. The input parser selects the network target/handler for
the channel and then calls this sender; the **channel code is written as the first byte of the
payload**. There is no separate "say" opcode versus "party" opcode at the C2S layer — only the
channel byte differs.

This is the **key correction** to earlier recon. The openers `(2:82)`, `(2:83)`, `(2:84)`, and
`(3:21)` are **not** general say-chat:

- `(2:83)` is a **friend-note** submit (a name + contents form).
- `(3:21)` is a **special announce / channel-broadcast** path.
- `(2:82)` and `(2:84)` are **relation** submits.

These are driven by a separate **UI command/button dispatcher** (a UI action-id switch), not by the
chat input parser. They are catalogued under the social subsystem; see `Docs/RE/specs/social.md`
Sections 2.1 and 4. A re-implementation must route everyday typed chat through `(2:7)` and must not
assume `(2:83)`/`(3:21)` carry say-chat.

> **Naming note for the catalog.** Because `(2:7)` is now understood to carry **all** everyday chat
> channels (not only named whispers), the spec-author recommends renaming the catalog entry for
> `(2:7)` from `CmsgWhisper` to **`CmsgChat`** (with whisper as channel 9), and folding the channel
> enumeration below into `Docs/RE/packets/2-7_whisper.yaml` and `opcodes.md`. This is a proposal for
> the orchestrator-owned `names.yaml` / `opcodes.md`; confirm before committing the rename.

### 4.2 Payload framing (PLAUSIBLE · CAPTURE-UNVERIFIED)

The `(2:7)` payload is a **fixed header followed by a length-prefixed text tail**. The header is
**19 bytes** payload-relative; the first byte is the **channel code**, a flag byte follows, then a
fixed **16-byte target-name buffer** (used by whisper; NUL-padded), and a trailing header byte. After
the header comes a `u32` text-length prefix and that many CP949 bytes. The length prefix is believed
to **include the terminating NUL** (consistent with the other C2S chat senders), and the body is
copied with a hard cap (**119** for whisper). The exact off-by-one of the NUL inclusion and the
meaning of the flag/trailing header bytes are **capture-blocked**. See
`Docs/RE/packets/2-7_whisper.yaml` for the byte-by-byte header table.

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
  | `CHAT_WINDOW_SIZE`    | `1`  | window-size mode |
  | `CHAT_WINDOW_FONT_SIZE` | `12` | font size |

### 6.2 The line record and the ring buffer (CODE-CONFIRMED)

The scrollback is a **1000-line ring buffer** of fixed-stride records. Each record is **36 bytes**:

| Offset (record-relative) | Size | Type | Meaning |
|---|---|---|---|
| `+0x00` | 28 | CP949 string | line text (small-string-optimised: 16-byte inline buffer + ptr / len / cap) |
| `+0x1C` | 4  | u32 / ARGB | line colour |
| `+0x20` | 4  | int | channel code |

Record stride is **36 bytes** (`0x24`). The panel zero-initialises **1000** such records at build
time in **two parallel arrays** — the raw lines and their word-wrapped form — so the ring length is
**1000 lines**. A line counter (capped at 1000) and a ring start index track the live window.

### 6.3 Append sink (CODE-CONFIRMED · route) / (PLAUSIBLE · special paths)

The **log-append sink** takes `(text, channel, colour)` and is the single entry point for both local
echo and inbound chat. Its routing on the channel value:

- **`channel < 100`** → append to the chat log: bump the line counter (capped at 1000), build a line
  record, and insert it into the ring.
- **`channel == 11`** → a **distinct insertion path** (purpose not recovered — possibly a
  system/important or "pinned" style line; Open Question 3).
- **`channel > 100 && channel != 110`** → **route to a separate floating "system / notice" text
  system**, *not* the chat log. This is its own on-screen scrolling-notice subsystem and is out of
  scope for the chat log.
- **`channel == 110`** → dropped.

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
  split across multiple visible rows. The max-chars value is read from a panel field whose source
  derivation (from `CHAT_WINDOW_SIZE` / `CHAT_WINDOW_FONT_SIZE`?) was not traced (Open Question 6).
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

| Slot (channel family) | text @ | len @ | cap @ | expiry @ | flag @ | Colour (ARGB) | World height × |
|---|---|---|---|---|---|---|---|
| say / normal      | `+1564` | `+1560` | `+1584` | `+1588` | — | PC `0xFFFFFF00`, mob bbox-based | scale × 10 (PC) |
| party             | `+1592` | `+1612` | `+1616` | `+1620` | — | `0xFF00FFFF` | scale × 12 |
| shout-area        | `+1628` | `+1644` | `+1648` | (shared) | — | `0xFFFF0055` | scale × 7 |
| guild / alliance  | `+1696` | `+1712` | `+1716` | `+1720` | `+1724` | guild `0xFF365C66` / alliance `0xFF82C4FF` | scale × 9 |
| special event     | `+1732` | `+1748` | `+1752` | `+1756` | — | `0xFFFFFF00` | scale × 9 |

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

### 8.1 Routing (CODE-CONFIRMED · structure) / (PLAUSIBLE · body)

Inbound chat arrives as **`(5:7)` `SmsgChatBroadcast`**. The handler reads a **36-byte fixed header**,
then a **variable text body**, and:

- **appends the line to the same 1000-line log ring** via the shared log-append sink (Section 6.3),
  using the per-channel colour — so remote chat lands in the scrollback exactly like local echo;
- **resolves the speaker actor** by a `(sort, id)` pair (gated on the local player existing); and
- **writes the speaker actor's overhead-bubble slot** (same Actor field block as Section 7), stamping
  the **5000 ms** expiry relative to the local clock — so remote players' chat floats above their
  heads;
- **routes whisper** (channel `6` / `7` on the inbound side) through a dedicated whisper-display path
  (the inbound whisper / reply-target wiring was not decompiled in depth — Open Question 7).

### 8.2 Header layout (CODE-CONFIRMED · header) / (CAPTURE-UNVERIFIED · body)

The 36-byte header is, payload-relative:

| Offset | Size | Field | Meaning |
|---|---|---|---|
| `+0x00` | 1  | sender sort | actor-category discriminator |
| `+0x04` | 4  | sender id   | sender actor id |
| `+0x08` | 4  | context id  | target / room / whisper-peer id |
| `+0x0D` | 1  | sub-command | chat verb / sub-command |
| `+0x0E` | 1  | channel     | channel code (`6` / `7` ⇒ whisper) |
| `+0x10` | 20 | sender name | sender display name (fixed buffer, NUL-terminated) |

The **text body** that follows is **variable** and its framing is **not** pinned statically
(length-prefixed vs NUL-terminated vs "rest of frame"). Reasonable default hypothesis until a capture
lands: `body_length = frame.size − 8 (frame header) − 36 (chat header)`, then NUL-trimmed. See
`Docs/RE/packets/5-7_chat_broadcast.yaml`. **Capture required.**

> **Channel routing on the S2C side.** The same `channel > 100` rule from the append sink applies:
> a channel above 100 routes to the **separate floating notice-text system**, not the chat log
> (Section 6.3).

---

## 9. Open questions (capture- or trace-blocked)

1. **`(5:7)` body framing** — length-prefixed vs NUL-terminated vs rest-of-frame. Only the 36-byte
   header is firm. **Capture required.**
2. **`(2:7)` on-the-wire confirmation** — high confidence that everyday typed chat is `(2:7)` (the
   input parser emits only `(2:7)` for normal text), but a capture of "type in the say box" versus
   "click friend-note" versus "GM announce" would confirm which opcode actually carries each, and
   pin the off-by-one of the text-length prefix's NUL inclusion.
3. **`channel == 11` special log-insert path** — distinct insertion routine; purpose (system /
   important / pinned line?) not recovered.
4. **`channel > 100` floating-notice system** — a separate on-screen scrolling/notice text subsystem,
   not the chat log; deserves its own lane.
5. **Chat-macro `"<name>_CHATSHORTCUT"` table and the `/option` / `/msgchk` command grammar** —
   enumerated but not fully decoded; a "chat commands" sub-spec could exhaust them.
6. **Max-chars-per-wrapped-line field** — read by the renderer but its source value (derived from
   `CHAT_WINDOW_SIZE` / `CHAT_WINDOW_FONT_SIZE`?) was not traced. The 12-line visible window and the
   1000-line ring are firm.
7. **Inbound whisper routing** (channel `6` / `7` in the `(5:7)` handler) — the whisper-display /
   reply-target wiring was not decompiled in depth.

---

## 10. Cross-references

- `Docs/RE/packets/2-7_whisper.yaml` — C2S `(2:7)` header byte layout (proposed rename to `CmsgChat`).
- `Docs/RE/packets/5-7_chat_broadcast.yaml` — S2C `(5:7)` `SmsgChatBroadcast` header.
- `Docs/RE/specs/social.md` — the wider social wire protocol; reclassifies `(2:82)` / `(2:83)` /
  `(2:84)` / `(3:21)` as friend-note / announce / relation submits (Sections 2.1, 4).
- `Docs/RE/opcodes.md` — authoritative opcode catalogue and frame model.

> **Implementation guidance (presentation).** Chat window = `448 × 324`, **12** visible lines over a
> **1000**-line scrollback, **7** channel-filter toggles, per-channel ARGB colours (Section 3),
> CP949 word-wrap. Overhead bubbles = centre-anchored multi-line world text, **14 px** leading,
> lifted `scale × {7, 8, 9, 10, 12}` above the actor, **5000 ms** lifetime, per-channel colour. The
> local player's own line is echoed to the log and stamped as a bubble at send time; remote lines
> arrive via `(5:7)` and do the same on the speaker actor.
