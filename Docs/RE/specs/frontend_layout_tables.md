# Front-End Layout Tables — Login / PIN / Server List / Load / Opening (the build oracle)

```
verification: confirmed
ida_reverified: 2026-06-18
anchor: 263bd994
evidence: [static-ida]
capture_verified: false
status: CODE-CONFIRMED (geometry literals); seed/permutation + a few render rects DEBUGGER-PENDING (flagged)
```

> This is the authoritative numeric oracle for the pre-character-select front end. Every constant here
> was recovered from `doida.exe` (clean-room: rewritten in neutral prose, no addresses, no pseudo-C).
> It **supersedes** the approximate coordinates in earlier `frontend_scenes.md` notes where they
> disagree. C# in layers 04/05 cites this file (`// spec: Docs/RE/specs/frontend_layout_tables.md §N`).

## 0. Load-bearing corrections to prior specs (read first)

1. **UI geometry is HARD-CODED, not data-driven.** `data/script/uiconfig.lua` is parsed only for a
   single integer (`NEW_SERVER_INDEX`). The login/select/opening windows are built by straight-line
   code with literal widget rects. There is **no widget layout file** — the port must encode the
   literal tables below as constants. Retire the "login built from uiconfig.lua, ~340 widgets" claim.
2. **The login flow has ONE sub-state field** (login-window object, conceptual `flowSubState`). Earlier
   "+0x17C vs +0x238" was the same dword seen through two object views. Treat it as one integer.
3. **No ±64/tick alpha fade in the login window.** The only animated quantity in the login intro is
   the curtain Y-offset (+5/tick). Grouped panels are shown/hidden by a boolean visible flag, not by
   alpha ramping. (The ±64 fade belonged to a mis-attributed earlier note.)
4. **Version gate = single u32 equality.** `game.ver` field index-5 of `data/cursor/game.ver` must
   equal field index-5 of `game.ver`; mismatch shows msg 2204 and quits. Not a 7×u32 struct compare.
5. **The Loading screen is NOT a widget tree.** It is an immediate-mode renderer of two textured quads
   (full-screen background + progress bar) under an ortho projection — see §5.
6. **Opening alpha ceiling is 250 (0xFA), not 255**, and the credit crawl increments in +Y (DirectX
   Y-down) so a Godot Y-up port must invert the sign to scroll upward — see §6.
7. **The PIN keypad is 100 stacked buttons** (10 digit-buttons per position × 10 positions); the
   scramble shows exactly one per position — see §3.

## 1. Conventions

- **Reference canvas: 1024 × 768**, top-left origin, +Y down. The GU windows are authored in this
  space; the port scales the canvas to the window. The Loading/Opening scenes draw ortho quads in a
  center-or-screen space (noted per-scene).
- **Widget rect contract:** `(x, y, w, h, srcX, srcY)` → destination rect `(x, y, w, h)` on canvas;
  source rect in the atlas = `(srcX, srcY, w, h)` (right = srcX+w, bottom = srcY+h). 3-state buttons
  carry separate Normal / Hover / Pressed source origins (same w,h).
- **Login atlases** (from `data/ui/`, loaded in this order):
  - **A1 = `login_slice1.dds`**, **A2 = `loginwindow.dds`**, **A3 = `InventWindow.dds`**,
    **A4 = `loginwindow_02.dds`**. `tex=0` = solid panel / text-only label.
- **Font slots** (15 hard-coded CP949 slots, index → face/size/weight): 0 DotumChe 12 · 1 Dotum 10 ·
  2 DotumChe 32 w800 · 3 DotumChe 18 w800 · 4 DotumChe 12 w800 · 5 BatangChe 12 · 6 BatangChe 18 w700 ·
  7 BatangChe 12 w700 · 8 BatangChe 12 w700 · 9 DotumChe 12 w700 · 10 Dotum 16 w800 · 11 DotumChe 10 ·
  12 DotumChe 12 · 13 DotumChe 14 · 14 DotumChe 16. Default label/textbox slot = 0.
- **Text** comes from `data/script/msg.xdb` by id (CP949). All text is CP949.

## 2. Login window (scene state 1)

### 2.1 Widget table (by parent group)

**ROOT panel (the login window itself):**

| Widget | type | x | y | w | h | srcX | srcY | atlas | action | init |
|---|---|---|---|---|---|---|---|---|---|---|
| Background | image | 0 | 110 | 1024 | 490 | 0 | 0 | A2 | — | hidden |
| Notice panel | panel | — | — | — | — | 0 | 490 | A2 | — | hidden |
| Server-list root | panel (opaque) | 0 | 0 | 1024 | 398 | 0 | 0 | A1 | — | hidden |
| Login-form host strip | panel | 0 | 326 / 768 | 1024 | 442 | 0 | 582 | A1 | — | — |
| PIN keypad panel | panel | 347 | 173 | 329 | 422 | — | — | password.dds | — | hidden |
| PIN yes/no panel | panel | 0 | 356 | 531 | 313 | 132 | 0 | — | — | hidden |
| Exit (quit) modal | panel | 342 | 289 | 340 | 190 | 318 | 647 | A3 | — | hidden |
| Error modal | panel | 342 | 289 | 340 | 190 | 318 | 647 | A3 | — | hidden |

**NOTICE panel children** (the central notice/agreement column):

| Widget | type | x | y | w | h | srcX | srcY | atlas | action |
|---|---|---|---|---|---|---|---|---|---|
| Notice labels ×22 | label | 50 | 100 (+18 each) | 383 | 50 | — | — | text | — |
| Scroll-up button | button1 | 467 | 86 | 13 | 10 | 483 | 490 | A2 | 106 |
| Scroll-down button | button1 | 467 | 455 | 13 | 10 | 505 | 490 | A2 | 107 |
| Scroll thumb | button1 | 469 | 98 | 9 | 9 | 496 | 490 | A2 | 108 |
| Title plate | image | 207 | 44 | 70 | 17 | 70 | 980 | A2 | — |

- Notice text = msg ids **4001..4022** (built empty then text-assigned). Role is governed by msg.xdb
  content; treat as the notice/agreement body column, **not** an EULA gate (there is no accept gate).

**SERVER-LIST panel tree** — see §4 (it has its own display model).

**LOGIN-FORM host (the bottom credential strip):**

| Widget | type | x | y | w | h | srcX | srcY | atlas | action | notes |
|---|---|---|---|---|---|---|---|---|---|---|
| Server-list submit button | button3 | 456 | 166 | 112 | 39 | N154,398 / H378,398 | A1 | 102 | reveals server-list |
| Server-list plate | image | 265 | 0 | 494 | 113 | 0 | 469 | A1 | — | shown |
| ID label plate | image | 340 | 30 | 38 | 13 | 0 | 398 | A1 | — | |
| PW label plate | image | 507 | 30 | 49 | 13 | 38 | 398 | A1 | — | |
| Save-ID label plate | image | 619 | 86 | 67 | 13 | 87 | 398 | A1 | — | |
| **ID textbox** | textbox | 390 | 32 | 102 | 13 | 615 | 404 | A1 | 109 | IME mode 16; maxlen 6 |
| **PW textbox** | textbox | 568 | 32 | 102 | 13 | 615 | 404 | A1 | 110 | IME mode 12; maxlen 129; masked |
| **Save-ID checkbox** | checkbox | 694 | 86 | 13 | 13 | off 717,398 / on 730,398 | A1 | 104 | |
| **OK / Login button** | button3 | 456 | 64 | 112 | 39 | N266,398 / H490,398 | A1 | 103 | |
| Help/Quit strip | button3 | 456 | −3 | 111 | 38 | N792,398 / H602,416 | A1 | 105 | on the form strip |
| Help plate | image | 407 | −3 | 210 | 70 | 743 | 398 | A1 | — | |

**PIN yes/no panel children** (the "use second password?" prompt):

| Widget | type | x | y | w | h | srcX | srcY | atlas | action |
|---|---|---|---|---|---|---|---|---|---|
| Prompt plate A | image | 67 | 48 | 178 | 13 | 0 | 437 | A1 | — |
| Prompt plate B | image | 0 | 100 | 313 | 32 | 289 | 437 | A1 | — |
| Yes button | button3 | 40 | 82 | 110 | 38 | N520,492 / P635,492 | A2 | 111 |
| No button | button3 | 164 | 82 | 110 | 38 | N750,492 / P865,492 | A2 | 112 |

**Confirm modals A/B** (single-OK message popups, msg 4023 / 4024):

| Widget | type | x | y | w | h | srcX | srcY | atlas | action |
|---|---|---|---|---|---|---|---|---|---|
| Confirm-A panel | panel | 342 | 289 | 340 | 190 | 318 | 647 | A3 | — |
| Confirm-A label (msg 4023) | label center | 10 | 100 | 330 | 20 | — | — | text | — |
| Confirm-A OK | button3 | 120 | 136 | 113 | 40 | N302,900 / P415,900 | A3 | 113 |
| Confirm-B panel | panel | 342 | 289 | 340 | 190 | 318 | 647 | A3 | — |
| Confirm-B label (msg 4024) | label | 10 | 100 | 330 | 20 | — | — | text | — |
| Confirm-B OK | button3 | 120 | 136 | 113 | 40 | N302,860 / P415,860 | A3 | 114 |

### 2.2 Sub-state machine (`flowSubState`, init = 1)

```
1  intro one-shot: play curtain SFX 861010105 (cat 2); reset curtain offset 0; show form bg; hide
   server-list/PIN/notice/confirm.                                            → 2
2  curtain opening: each frame offset += 5; top curtain Y = −offset; bottom curtain Y = offset+326;
   at offset>200 snap the server-list submit plate to (494,469); at offset>222 → 3
3  curtain done: show login-form group; hide server-list.                      → 4
4  form idle (steady "enter credentials"); Enter → 5                           (event)
5  commit form (show form panel + title; hide notice).                          → 6
6  validate-armed idle: OK button (103) or Enter, requires flowSubState==6, runs the game.ver gate.
   → 29
29 validate: ID length ≥ 4 (else → 6 + msg 4025); PW length ≠ 0 (else → 6 + msg 4026); both OK →
   persist Save-ID if checked → 31 (and the PIN keypad is raised on the 31→32 edge)
31 PIN entry: keypad modal shown.                                              (UI)
32 PIN poll: keypad visible AND submitted → 33
33 start server-list fetch worker (TCP port 10000)                              → 34
34 (re)start fetch / restart entry (also reached by Help action 105, throttled 10 s)  → 35
35 fetching: show "loading server list" progress widgets                        (worker → 36)
36 fetch result: 0 records → msg 4027 (no servers) → 37; error → msg 4028 → 37; else paint plates → 37
37 server list shown: user picks a plate (400/401) or pages (115..124). Plate commit guard:
   record.status==0 && record.load<2400 → persist Lastserver → 38
38 channel-endpoint fetch (TCP port 10000 + server_id)                          → 39
39 start join worker                                                            → 40
40 connecting overlay                                                           (worker → 41)
41 hand-off: build TAB credential string, build secure context + login packet 0x2B, arm 30 s connect
   timeout, leave the login scene to the connect/SMSG path.
```

**OnEvent action map** (widget action id → effect): 101 quit · 102 show server-list · 103 OK/login
(game.ver gate → 29) · 104 Save-ID toggle (persist/clear account) · 105 Help/restart fetch (→ 34,
10 s throttle) · 109/110 focus ID/PW · 111 PIN-yes (→ 5) · 112 PIN-no · 113/114 confirm-A/B OK (→ 34) ·
115..124 server-list **pager** (only in 37; page = action−115; repaint, no commit) · 400/401 server
**plate pick** (only in 37; LEFT=400, RIGHT=401; record = (action−400) + 2·page). Keys: TAB (9) toggles
ID/PW focus; ENTER (10) → if state 6 run OK path, if state 4 → 5.

### 2.3 Curtain geometry

Two full-width panels driven by one offset (start 0, +5/tick): **top** curtain Y = −offset; **bottom**
curtain Y = offset + 326. At offset > 200 snap the server-list submit plate to **(494, 469)**. Stop at
offset > 222 (→ sub-state 3). No alpha animation. Curtain start SFX = 2D cue **861010105** (category 2).

### 2.4 Version & length gates

- **game.ver:** parse `data/cursor/game.ver` and `game.ver`, compare **field index 5** (u32) for
  equality; mismatch → msg **2204** + quit. Runs only when the VFS is mounted.
- ID length **≥ 4** else msg **4025**; PW length **≠ 0** else msg **4026**; no servers → msg **4027**;
  fetch failed → msg **4028**.

### 2.5 Save-ID persistence

On build, read the saved account from the options store; if present and ≠ `"(null)"`, pre-fill the ID
textbox and move focus to the PW box (else focus stays on ID). On checkbox toggle (104): if checked,
store the current ID text; if unchecked, clear it. On validate success, store the ID text. (On-disk
key proposed as `DoOption.ini [DO_OPTION] OPTION_ID`; exact section/key is STATIC-PENDING — see
login_flow.md.) The selected server id persists to registry `HKLM\SOFTWARE\crspace\do : Lastserver`.

### 2.6 Credential hand-off (sub-state 41)

Build a single TAB-delimited string `"<account>\t<password>\t<PIN>\t<host> <port>"` (host/port from
the channel-endpoint fetch, space-separated). Field caps: account < 20, password = 17, PIN < 5
(≤4 digits). This feeds the secure-context builder → login packet **0x2B** (see `packets/login.yaml`,
`login_flow.md`). A 30 s connect timeout is armed.

## 3. PIN keypad (second password) — sub-states 31/32

- **Container panel:** screen dst **(347, 173)**, size **329 × 422**; children textured from
  **`data/ui/password.dds`**. All child coords below are panel-relative.
- **Digit positions: 10 cells, 5 columns × 2 rows, each cell 52 × 52.** Column X ∈ {28, 83, 138, 193,
  248} (= 55·col + 28); row Y ∈ {170, 230}.
- **Each cell is a stack of 10 digit-buttons (digits 0..9), 100 buttons total.** Per-digit atlas
  source origins: Normal `(d·52, 560)`, Pressed `(d·52, 612)`, Hover `(d·52, 664)` for digit `d`. The
  scramble makes exactly **one** digit-button visible per cell. Each digit-button's action id = the
  digit value `d` (0..9).
- **Reset** button `(243, 133, 58, 30)` tag **11**; **OK** `(90, 290, 154, 58)` tag **12**; **Cancel**
  `(90, 350, 154, 58)` tag **13**.
- **Masked input field:** a label at panel-relative `(81, 138, 150, 22)`, rendered as N `*` characters
  (digits never drawn). **Max length 4.**
- **Dragon frame** decoration: size **340 × 190**, atlas source origin `(318, 647)`, centered.
- **Scramble:** seed `srand(time)` (whole-second wall clock), Fisher–Yates shuffle of the digits 0..9;
  re-rolls on open, Reset, OK, Cancel. The **runtime seed value + permutation are DEBUGGER-PENDING** —
  reproduce the *mechanism* (a fresh time-seeded shuffle each open); do not hard-code a permutation.
- **Raise:** on the 31→32 edge (after credential validation). **Submit:** OK (tag 12) copies up to 4
  digits to the login singleton (becomes field #3 of the credential string).

## 4. Server-list display model — sub-states 33..37

- **Page tabs:** 10 small 3-state strips across the top, each `(13 + 47·i, 66, 47 × 18)`, atlas A2,
  Normal `(596, 985)` / Hover `(643, 985)`, action **115 + i** (i = 0..9). These are **page selectors**
  (page = action − 115), not servers. (Two strips are re-skinned: index 1 → N`(690,985)`/H`(737,985)`,
  index 2 → N`(784,985)`/H`(831,985)`.)
- **Two detail plates** (the two servers on the current page), built by a 2-column loop (i = 0,1) with
  X base 30, step 233, action base **400** (LEFT = 400, RIGHT = 401):
  - name label `(30 + 233·i, 390, 174 × 21)`, action 400+i
  - icon image `(30 + 233·i + 47, 97, 100 × 372)`, atlas A4 src `(448 + 124·i, 6)`
  - select button3 `(30 + 233·i − 6, 97, 202 × 372)`, atlas A4 N`(9,6)`/H`(220,6)`, action 400+i
  - info label `(30 + 233·i, 410, 174 × 20)`
  - info label `(30 + 233·i, 430, 174 × 20)`, font slot 4
- **Record decode** (8-byte LE, see `packets/lobby.yaml`): `{server_id, status_code, load, open_time}`
  (in-record order of server_id vs open_time is capture-pending). Display index from a page =
  `record = (action − 400) + 2·page`.
- **Commit guard:** `status_code == 0 && load < 2400` → write selected server_id, persist Lastserver,
  advance to channel-endpoint fetch (port 10000 + server_id).
- **Status / load coloring** (UI only): load > 1200 red (msg 6001) · > 800 orange (6002) · > 500
  yellow (6003) · ≤ 500 green. status_code 3 = scheduled-open (msg 6004 "preparing" / 6005 HH:MM).

## 5. Loading window (scene state 2) — immediate-mode, NOT a widget tree

- Design resolution 1024 × 768, scaled to the backbuffer by `screenW/1024`, `screenH/768`.
- **Background quad:** full-screen `(0, 0, screenW, screenH)` blit of a randomly chosen DDS:
  `rand()%3` → `data/ui/loading.dds` | `data/ui/loading06.dds` | `data/ui/loading08.dds`.
  (Earlier `loading_01/02.dds` names are wrong for this build.)
- **Progress bar quad:** a second textured quad, design-space literals X span −499..−170 (329 px wide),
  Y span −363..−140 (223 px tall), depth 0.108, near-white per-vertex tint, lower-center. The exact
  on-screen rect (center-relative ortho) is DEBUGGER-PENDING; reproduce a lower-center bar 329 px wide.
- **Fill:** progress from `VFS_GetProgress()` (0..100). Fill = `clamp(223 · pct / 100, 0, 223)`,
  normalized `/1024` → max U **223/1024 ≈ 0.2178**. No text label (percentage shown by fill only).
- **Completion / advance:** a "loading active" flag is cleared by the background loader thread after it
  finishes its corpus load + a 500 ms grace; the per-frame callback then ends the blocking scene loop,
  and the state machine advances to the destination chosen at case-2 entry (Opening or Select).
- **Skip-Opening gate (decided at case-2 entry):** `GetPrivateProfileInt("OPENNING", "SKIP", 0, ini)`
  (section spelled **OPENNING**, key **SKIP**, default 0). Non-zero → go to Select (4, skip Opening);
  zero → go to Opening (3).
- **Boot corpus** (loaded behind the bar, ordered): data tables incl. `system_control.scr`,
  `mapsetting.scr`, `items.scr`, `skills.scr`, `musajung.do`, `npcs.scr`, `mobs.scr`, `quests.scr`,
  `citems.scr`, the four `.xdb` (effectscale / creature_item / vehicle / buff_icon_position),
  `UiTex.txt`, char manifests, guild crests, + subsystem inits.
- **Audio:** looped 2D cue **920100100**, category 0 (single direct voice → cannot double-stack).

## 6. Opening scene (scene state 3) — ortho quads

- **Backdrops:** 4 full-screen quads `openning_001.dds` .. `openning_004.dds` (1024 × 768). One phase
  index selects the active backdrop. Each phase dwells **17 500 ms** (4 × 17.5 s ≈ 70 s). A single
  alpha byte ramps **±1 per frame up to 250 (0xFA)**, broadcast to all 4 ARGB channels (fade-out then
  swap, not a true A/B cross-blend).
- **Credit crawl:** texture `openning_scenario.dds`, built **1024 × 2048**, centered at
  X = `screenW/2 − 512`, starting Y = `screenH − 200`. After a **1000 ms** delay it translates the
  quad's destination Y at **30 units/second** (wall-clock) up to a bound of **1843**. It is a
  positional translate (not a UV offset). The code increments **+Y (DirectX Y-down)**; a **Godot Y-up
  port must invert the sign** so the crawl reads upward.
- **Skip:** keyboard Enter (10) / ESC (27) / Space (32), or the 3-state skip button (action **100**) at
  dst `(screenW − 120, 10, 110 × 32)` on `mainwindow.dds`, source Normal/Hover `(761, 165)` / Pressed
  `(634, 165)`. Skipping writes `[OPENNING] SKIP = 1` to the same option INI the Load scene reads
  (Opening then permanently skipped), and advances to Select (4).
- **Auto-exit:** when the fade machine finishes phase 4, an arming flag triggers a final fade 0→250
  then advances to Select (4). (The exact arming write-site is STATIC-PENDING — not load-bearing.)
- **Audio:** looped 2D BGM **910061000**, started at scene build, stopped on teardown.

## 7. Front-end audio cues (2D, category < 5 → `data/sound/2d/<id>.ogg`)

| Cue id | Where |
|---|---|
| 861010105 | login curtain intro (sub-state 1) |
| 920100100 | loading screen BGM (looped, category 0) |
| 910061000 | opening cinematic BGM (looped) |
| (UI click SFX per button) | login/server-list buttons |

## 8. Networking summary (authoritative detail in `login_flow.md` / `packets/{login,lobby}.yaml`)

- **Lobby host resolution (TCP, plaintext):** file `ip.txt` (single token, ≤19 chars) → else
  `list.dat` CIPList keyed by registry `HKLM\SOFTWARE\crspace\do : servername` → else default
  `211.196.150.4`. Lobby connects via `inet_addr` (dotted IPv4, no DNS), **port base 10000**.
- **Server-list query** (port 10000) and **channel-endpoint query** (port 10000 + server_id): 8-byte
  frame wrapper `[u32 total_size][u16 record_count][u16 unused]` + LZ4 payload. Channel-endpoint payload
  first 30 bytes = ASCII `"<host> <port>"` (single SPACE, NUL-padded).
- **Game server** is dynamic from that `"host port"` string, connected via **DNS (gethostbyname)** —
  host may be a name or a dotted quad.
- **Login handshake on the game connection:** inbound **0/0** key-exchange (62-byte blob: RSA modulus
  then exponent + 2 scalars + timestamp); reactive outbound **1/4** secure credential reply (0x2B
  pre-image + RSA half + per-dword XOR 0x29 whitening before cipher+LZ4). Frame header
  `[u32 size][u16 major][u16 minor]`.
