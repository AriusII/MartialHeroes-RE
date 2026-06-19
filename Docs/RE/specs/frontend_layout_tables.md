# Front-End Layout Tables — Login / PIN / Server List / Load / Opening (the build oracle)

```
verification: confirmed
ida_reverified: 2026-06-19
anchor: 263bd994
evidence: [static-ida]
capture_verified: false
status: CODE-CONFIRMED (geometry literals + PIN scramble seed + load-bar rect + login visibility edges + opening fade mechanism, CYCLE 18 Phase A static IDA; element/asset/src-rect construction re-confirmed + deepened against the LoginWindow / PIN keypad / server-list / Opening construct routines, 2026-06-19 element-level pass — PIN digit-face state bands + credential mask mechanism + curtain extent + server-list plate/pager/status art all pinned); residual = opening final-fade armed-flag producer site only
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
   The OK / Login submit (action 103) is **gated on this version check** before it advances to the
   credential-validation sub-state (element-level pass, 2026-06-19).
5. **The Loading screen is NOT a widget tree.** It is an immediate-mode renderer of two textured quads
   (full-screen background + progress bar) under an ortho projection — see §5.
6. **Opening alpha ceiling is 250 (0xFA), not 255**, and the credit crawl increments in +Y (DirectX
   Y-down) so a Godot Y-up port must invert the sign to scroll upward — see §6.
7. **The PIN keypad is 100 stacked buttons** (10 digit-buttons per position × 10 positions); the
   scramble shows exactly one per position — see §3.
8. **Login visibility is set IMPERATIVELY on transition edges, not by a declarative per-state table.**
   The binary has no per-state ApplyVisibility lookup; the render callback never recomputes visibility
   from the sub-state. The visible set at any state is the cumulative result of the show/hide calls
   fired on each edge plus the build-time initial set. The "visible when state ≥ N" **bands** in §2.1/§2.2
   are a port-side reconstruction of those cumulative edge effects, not a structure that exists in the
   binary. (Static IDA, CYCLE 18 Phase A.)
9. **There are FIVE distinct EXE-relative config INI files**, not one. The client builds, side by side
   in the EXE directory, `DoOption.ini`, `option.ini`, `panel.ini`, `combo.ini`, `TSIDX.ini`. The
   `[DO_OPTION]` options block (display, sound, brightness, saved login id) lives in **`DoOption.ini`**;
   the Opening-skip `[OPENNING] SKIP` flag lives in a **different** file, **`option.ini`**. Do not
   conflate the two. (Static IDA, CYCLE 18 Phase A — see §2.5, §5, §6.)
10. **Every front-end widget is a 1:1 atlas blit** (element-level pass, 2026-06-19). The widget builder
    stores a destination rect and a source origin sharing the SAME width/height — there is **no UV
    scaling** anywhere in the login/PIN/server-list/opening construction. The source rect right/bottom
    are `(srcX+w, srcY+h)`. A port may treat every table row below as "copy a `(w×h)` region from
    `(srcX,srcY)` in the named atlas to `(x,y)` on canvas."
11. **Password masking is a field-flag, not an IME mode** (element-level pass, 2026-06-19). The PW
    textbox is masked because the high bit (mask bit) of its length/flags field is set; the mask is
    rendered as the literal `*` glyph advancing 6 px per character, in font slot 0. The IME-mode value
    (16 for ID, 12 for PW) is unrelated to masking — it only selects the IME conversion mode. See §2.7.
12. **Front-end 3-state buttons take source origins in NORMAL, PRESSED, HOVER order** (the documented
    builder convention — see `ui_system.md §1.5`). When a construct call passes three `(srcX,srcY)`
    pairs, the 1st is the displayed/normal face, the 2nd is the PRESSED frame, the 3rd is the HOVER
    frame. On most front-end buttons PRESSED equals NORMAL, so the only distinct extra frame is HOVER;
    the PIN digit faces are an exception that use all three bands (see §3).

## 1. Conventions

- **Reference canvas: 1024 × 768**, top-left origin, +Y down. The GU windows are authored in this
  space; the port scales the canvas to the window. The Loading/Opening scenes draw ortho quads in a
  center-or-screen space (noted per-scene).
- **Widget rect contract:** `(x, y, w, h, srcX, srcY)` → destination rect `(x, y, w, h)` on canvas;
  source rect in the atlas = `(srcX, srcY, w, h)` (right = srcX+w, bottom = srcY+h). 3-state buttons
  carry separate source origins for normal / pressed / hover (in that argument order — §0.12), all
  sharing the same w,h. Text-only labels and solid panels pass no atlas (`tex = 0`).
- **Login atlases** (from `data/ui/`, loaded in this order):
  - **A1 = `login_slice1.dds`**, **A2 = `loginwindow.dds`**, **A3 = `InventWindow.dds`**,
    **A4 = `loginwindow_02.dds`**. `tex=0` = solid panel / text-only label. The asset→file mapping is
    the path string literal itself; the loader's numeric flag argument is a constant color-key/format
    cookie shared by all four loads, **not** a per-texture id.
- **Font slots** (15 hard-coded CP949 slots, index → face/size/weight): 0 DotumChe 12 · 1 Dotum 10 ·
  2 DotumChe 32 w800 · 3 DotumChe 18 w800 · 4 DotumChe 12 w800 · 5 BatangChe 12 · 6 BatangChe 18 w700 ·
  7 BatangChe 12 w700 · 8 BatangChe 12 w700 · 9 DotumChe 12 w700 · 10 Dotum 16 w800 · 11 DotumChe 10 ·
  12 DotumChe 12 · 13 DotumChe 14 · 14 DotumChe 16. Default label/textbox slot = 0. The credential
  textboxes and the PIN masked-entry label use slot 0; the server-list population (count) label uses
  slot 4.
- **Text** comes from `data/script/msg.xdb` by id (CP949). All text is CP949.

## 2. Login window (scene state 1)

### 2.1 Widget table (by parent group)

> **Visibility model (CONFIRMED, static IDA, CYCLE 18 Phase A):** the `init` column and the §2.2
> bands describe a port-side reconstruction. In the binary, visibility flags are toggled imperatively
> on the transition edges of §2.2 (and by an "instant curtain-open" reset that runs for any state ≠ 1);
> there is no declarative per-state visibility table. Encode the bands as constants, but understand they
> are the cumulative effect of edge-fired show/hide calls.

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
  The 22-label loop lays the labels at x = 50, y = 100 + 18·i (stride 18), w = 383, h = 50, while
  y < 496 (element-level pass: built into the separate notice panel, hidden in the credential view).

**SERVER-LIST panel tree** — see §4 (it has its own display model).

**LOGIN-FORM host (the bottom credential strip):**

| Widget | type | x | y | w | h | srcX | srcY | atlas | action | notes |
|---|---|---|---|---|---|---|---|---|---|---|
| Quit/exit-confirm button | button3 | 456 | 166 | 112 | 39 | N154,398 / H378,398 | A1 | 102 | opens the quit/exit-confirm panel (NOT the server-list); always visible at rest |
| Server-list plate | image | 265 | 0 | 494 | 113 | 0 | 469 | A1 | — | shown (decoration banner of the form strip) |
| ID label plate | image | 340 | 30 | 38 | 13 | 0 | 398 | A1 | — | "ID" caption graphic |
| PW label plate | image | 507 | 30 | 49 | 13 | 38 | 398 | A1 | — | "Password" caption graphic |
| Save-ID label plate | image | 619 | 86 | 67 | 13 | 87 | 398 | A1 | — | small notice/caption strip |
| **ID textbox** | textbox | 390 | 32 | 102 | 13 | 615 | 404 | A1 | 109 | IME mode 16; maxlen 6; **unmasked** (mask bit clear); font slot 0 |
| **PW textbox** | textbox | 568 | 32 | 102 | 13 | 615 | 404 | A1 | 110 | IME mode 12; **masked** (mask bit set; `*` glyph, 6 px/char); font slot 0 — see §2.7 |
| **Save-ID checkbox** | checkbox | 694 | 86 | 13 | 13 | off 717,398 / on 730,398 | A1 | 104 | 13×13; initial state seeded from saved-id (see §2.5) |
| **OK / Login button** | button3 | 456 | 64 | 112 | 39 | N266,398 / H490,398 | A1 | 103 | gated on the game.ver index-5 check before advancing to validate (§2.4) |
| Server-list open/refresh strip | button3 | 456 | −3 | 111 | 38 | N792,398 / H602,416 | A1 | 105 | opens/refreshes the server-list (→ sub-state 34); HIDDEN at rest, shown only with the server-list (states 35..37) |
| Server-list strip deco plate | image | 407 | −3 | 210 | 70 | 743 | 398 | A1 | — | hidden at rest; shown with the server-list strip |

> **Source-rect note (element-level pass):** the N/H pairs above are the NORMAL and HOVER source
> origins; per §0.12 the construct call's middle pair is the PRESSED frame. At the login button call
> sites the pressed and hover origins are passed identical (or pressed == normal), so the pressed face
> is not separately distinguished for these buttons — the distinct face is HOVER. The N/H pairs are
> confirmed; treat pressed = normal unless a separate pressed origin is later observed.

> **Action semantics (CORRECTED vs the binary dispatch — the buttons are mislabelled by appearance):**
> action **102** = quit/exit-confirm (opens the exit panel; does NOT open the server-list; always visible at rest);
> action **105** = server-list open/refresh (sets sub-state 34 → the FSM shows the server-list 34→35→37; the 105
> strip + its deco are HIDDEN at rest and shown only with the server-list). The server-list is also reached
> automatically in the login flow after credential validation (state 33→34→…→37), not only by the 105 strip.
> Action 101 = immediate quit; 103 = login submit (→29); 104 = save-ID; 109/110 = ID/PW focus; 111 = continue (→5);
> 112/113/114 = exit-confirm / re-fetch popups; 115..124 = server-list paging (state 37); 400/401 = select left/right
> server plate (→38). The genuine quit closes via the exit panel's Yes (close event 10001 / engine run-flag clear).
> The login **form host strip + credential group RIDE the bottom curtain to canvas Y=548** (the closed build Y is
> 326); freezing them at 326 puts the whole form ~222 px too high (the "piled-up" disorder). Oracle-verified.

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

> **Single 32-bit integer field, written = 1 at construction (CONFIRMED, static IDA, CYCLE 18 Phase A).**
> Transition edges below are the confirmed edge ladder. Visibility is set **on these edges**, not by a
> per-state table (see §0.8); the §2.2-band column under each group is a reconstruction of the
> cumulative show/hide effect.

```
1  intro one-shot: play curtain SFX 861010105 (cat 2); reset curtain offset 0; show background; set the
   two curtain host panels to their closed positions; hide notice / server-list-content / quit-strip /
   help-plate / credential-group / PIN keypad; show the server-list submit plate.       → 2
2  curtain opening: each frame offset += 5; top curtain Y = −offset; bottom curtain Y = offset+326;
   at offset>200 snap the server-list submit plate to (494,469); at offset>222 → 3
3  curtain done: clear curtain offset 0; reposition curtains; show submit plate; hide notice; show the
   server-list ROOT panel; hide quit-strip/help-plate. Does NOT yet show the credential group.  → 4
4  form idle (steady "enter credentials"); Enter → 5                              (event)
5  advance/commit (also reached by action 111 and the notice-OK paths).            → 6
6  validate-armed idle: OK button (103) or Enter, requires flowSubState==6, runs the game.ver gate.
   Entering 6 shows the inner credential group; hides the PIN keypad and PIN-yes/no panel.   → 29
29 validate (game.ver index-5 gate runs only when VFS mounted; mismatch msg 2204, can quit): ID length
   ≥ 4 (else → 6 + msg 4025); PW length ≠ 0 (else → 6 + msg 4026); both OK → capture the typed ID into
   the account singleton, hide the credential group, show the PIN keypad, hide the notice.  → 31
   (The PIN keypad is raised on THIS edge — the moment state becomes 31.)
31 PIN entry: keypad modal shown; hide credential group.                          → 32
32 PIN poll: keypad visible AND submitted → 33
33 start server-list fetch worker (TCP port 10000); hide the credential group + PIN keypad.  → 34
34 show the server-list CONTENT panel; hide the submit plate; show the quit-strip + help-plate; start
   the fetch thread.                                                              → 35
35 fetching: show "loading server list" progress widgets                          (worker → 36)
36 fetch result: 0 records → msg 4027 (no servers) → 37; error (−1) → msg 4028 → 37; else paint plates → 37
37 server list shown: user picks a plate (400/401) or pages (115..124). Plate commit guard:
   record.status==0 && record.load<2400 → persist Lastserver → 38
38 channel-endpoint fetch (TCP port 10000 + server_id)                            → 39
39 start join worker                                                              → 40
40 hand-off: build the TAB credential string, build secure context + login packet 0x2B, set the global
   game-state to the connect phase, arm 30 s connect timeout, leave the login scene.  → 41
41 post-hand-off idle (connect/SMSG path).
```

- **Edge confidence:** all edges 1..41 and their numeric boundaries are **CONFIRMED (HIGH, static IDA,
  CYCLE 18 Phase A)**, except the **35 → 36 → 37 worker-completion timing**, which is a background-thread
  completion that static analysis cannot time-confirm — **MEDIUM** (would need a debugger pass).
- **Restart path:** Help / restart action (105) and the quit-confirm "yes" path set state 34 (throttled
  fetch restart): no-op while already at 35 or if a fetch ran within the last ~10 s.

**OnEvent action map** (widget action id → effect; re-confirmed from the binary OnEvent handler
2026-06-18 — **corrects two prior-spec errors**: 102 and 105 are NOT "show server-list"):
- **101** = app quit (engine run-flag clear). Fired from a sub-panel OK (the ExitPanel "yes").
- **102** = open the **quit-confirm ExitPanel** modal (a child object, distinct from the server-list).
  **It does NOT show the server-list.** (Same target as 112.)
- **103** = OK / login (game.ver index-5 gate → 29).
- **104** = Save-ID toggle (persist/clear account).
- **105** = **throttled restart of the server-list fetch** (→ 34): no-op if already fetching
  (sub-state 35) or if a fetch ran within the last ~10 s; else stamps the time and re-enters 34.
- **109/110** = focus ID/PW textboxes.
- **111** = advance (→ 5).
- **112** = open the quit-confirm ExitPanel modal (same as 102).
- **113/114** = hide confirm-A/B popup, then restart the fetch (→ 34).
- **115..124** = server-list **pager** (only in 37; page = action−115; repaint, no commit).
- **400/401** = server **plate pick** (only in 37; LEFT=400, RIGHT=401; record = (action−400) + 2·page;
  commit guard `status==0 && load<2400` → 38).
- Keys: TAB (9) toggles ID/PW focus; ENTER (10) → if state 6 run OK path, if state 4 → 5.

**The server-list is reached ONLY through the sub-state machine, never by a form button.** After PIN
submission (32) the tick advances 33 (fetch-start) → 34/35 → 37 (plates shown) automatically. The
list panel is a separate child painted by the tick machine; no `OnEvent` action opens it.

**Widget-group visibility bands (CORRECTED, static IDA, CYCLE 18 Phase A).** These are the
port-side reconstruction of the cumulative edge show/hide calls (§0.8). Reconstructed band = the range
of sub-states across which the group is continuously visible:

| Group | Reconstructed band | Notes (CONFIRMED edges) |
|---|---|---|
| Background | **visible from state 2** | Shown on the 1→2 edge (and by the instant-open reset for any state ≠ 1); never hidden afterward. **Off-by-one fix** from the prior "≥ 3". |
| Curtains | **not a hideable widget** | The "curtain" is **not** a separate object hidden after the raise. It is the **animated Y of two always-present host panels**: top Y = −offset, bottom Y = offset + 326. They slide off-canvas / are overpainted, but their visible flag stays set across all states. The prior "visible when ≤ 2" describes when the panels are in their CLOSED position, not an object's visibility. |
| Interactive credential group (ID/PW textboxes, Save-ID checkbox, OK button) | **state 6 (interval [6, 28])** | Built hidden; hidden at states 3/4. **Shown entering state 6** (the validate-armed idle where the user types); **hidden entering 29** (validate hides it and raises the PIN), and re-hidden at 31 and 33 — see the §2.2 EDGE LADDER, which is authoritative here. In practice only state 6 is occupied in this interval. This **supersedes the earlier "≈ 5..33" envelope**: the ladder's explicit per-edge hide calls at 29/31/33 are the precise truth, so the credential group is NOT visible under the PIN modal (31/32) or during the server-list fetch (33+). **NOT 3..32** — the inputs do not appear at 3/4 (the curtain has opened but the credential inputs stay hidden until the user advances 4→5→6). |
| Login-form host strip | always present | A distinct object from the credential group; visible as an object from build through the whole scene (its Y animates). |
| Server-list submit plate | states 1..~34 | Shown at 1/2/3/4 (and on instant-open); hidden on the 34→35 edge. |
| Server-list CONTENT panel | **state 35..37** | Turned on at the **34→35 edge** (shown entering 35; records painted at 37). **NOT 33..37.** Object-identity nuance: state 33 only *starts* the fetch worker; state 35 is "fetching: show the content panel". What the prior spec called "server-list root, 33..37" was actually this content panel. The genuine root background panel is visible far earlier (from build). |
| Quit/help strip + help plate | state 35 onward | Hidden at 1/2/3 + instant-open; shown on the 34→35 edge (server-list phase). |
| PIN keypad | **states 31/32 (CONFIRMED)** | Built hidden; raised on the 29→31 edge; kept shown on 31→32; hidden on the 33→34 edge. Technically still flagged visible through state 33, but 33 is a one-tick fetch-start that immediately advances; a port gating on {31, 32} is faithful. |
| Notice panel | always hidden (by the flow) | Built hidden; explicitly hidden on the 1→2, 29→31, and instant-open edges; the sub-state machine never shows it (only a separate notice/tooltip action can). CONFIRMED. |
| PIN yes/no panel | always hidden (by the flow) | Built hidden; explicitly hidden on the 5→6 edge; never shown by the tick machine. CONFIRMED. |
| Confirm-A/B popups, Exit panel, Error panel | always hidden (by the flow) | Built hidden; raised only by action ids 102/112/113/114 or message popups, outside this machine. CONFIRMED. |

### 2.3 Curtain geometry

Two full-width host panels driven by one offset (start 0, +5/tick): **top** curtain Y = −offset;
**bottom** curtain Y = offset + 326. At offset > 200 snap the server-list submit plate to **(494, 469)**.
Stop at offset > 222 (→ sub-state 3). No alpha animation. These are not hideable curtain widgets — they
are always-present panels whose Y animates (see §2.2 bands). **Start positions: top Y = 0, bottom
Y = 326; end positions: top Y = −222, bottom Y = 548 (= 222 + 326); per-tick step = +5; total
extent = 222 px each way; direction = top up, bottom down** (element-level pass, 2026-06-19). The two
animated panels are the front curtain panel and the body strip panel. Curtain start SFX = 2D cue
**861010105** (category 2).

### 2.4 Version & length gates

- **game.ver:** parse `data/cursor/game.ver` and `game.ver`, compare **field index 5** (u32) for
  equality; mismatch → msg **2204** + quit. Runs only when the VFS is mounted. The OK / Login button
  (action 103) runs this gate **before** advancing to the validation sub-state (29).
- ID length **≥ 4** else msg **4025**; PW length **≠ 0** else msg **4026**; no servers → msg **4027**;
  fetch failed → msg **4028**.

### 2.5 Save-ID persistence

On build, read the saved account from the options store; if present and ≠ `"(null)"`, pre-fill the ID
textbox and move focus to the PW box (else focus stays on ID). On checkbox toggle (104): if checked,
store the current ID text; if unchecked, clear it. On validate success, store the ID text. The Save-ID
checkbox's initial checked/unchecked frame is seeded from this saved value: empty/null → unchecked
(off frame), otherwise checked (on frame). (Element-level pass: checkbox off/on source origins
`(717,398)` / `(730,398)`, 13×13, atlas A1.)

**Persistence target (CONFIRMED, static IDA, CYCLE 18 Phase A):** the saved login id is read/written via
the Win32 private-profile APIs (`GetPrivateProfileStringA` / `WritePrivateProfileStringA`) to:
- **file:** `DoOption.ini` (EXE-relative; **NOT** `option.ini`),
- **section (`lpAppName`):** `DO_OPTION` (the API argument is the bare name; the `[DO_OPTION]` bracket
  form is just the on-disk header),
- **key (`lpKeyName`):** `OPTION_ID`,
- **value:** the stored login id string; read default `(null)` (an empty / `(null)` value = no saved id).
- **buffer:** read buffer size **0x32**; stored length **< 0x10** chars.

The same options loader reads all other `OPTION_*` integer keys (display width/height, sound volumes,
brightness, …) from the same `DO_OPTION` section of the same `DoOption.ini`. The Save-ID checkbox is
**not** a separate boolean key — presence/absence of the `OPTION_ID` string IS the toggle state.

The `[OPENNING] SKIP` Opening-skip flag is a **separate setting in a different file** (`option.ini`) —
see §5/§6. The selected server id persists to registry `HKLM\SOFTWARE\crspace\do : Lastserver`.

### 2.6 Credential hand-off (sub-state 40)

Build a single TAB-delimited string `"<account>\t<password>\t<PIN>\t<host> <port>"` (host/port from
the channel-endpoint fetch, space-separated). Field caps: account < 20, password = 17, PIN < 5
(≤4 digits). This feeds the secure-context builder → login packet **0x2B** (see `packets/login.yaml`,
`login_flow.md`). A 30 s connect timeout is armed.

### 2.7 Credential textbox construction & masking (element-level pass, 2026-06-19)

Both credential textboxes are built in the login-window construct routine (not in the later secondary
init), as 102 × 13 fields on atlas A1 sampling the same source origin `(615, 404)`. They differ only by
their dest X (ID at 390, PW at 568), their IME-mode field, and their mask flag:

- **Render is by a length/flags field.** Each textbox carries a length/flags byte. When its **mask bit
  (the high bit) is set**, the field draws the literal glyph `*` once per entered character, advancing
  **6 px per character**, in font slot 0. When the mask bit is clear, the field draws the stored string
  left-aligned (with horizontal scroll once the character count overflows the visible width).
  - **ID field:** mask bit clear → shown in clear; IME mode 16; the low bits of the length/flags field
    encode a small in-field length seed (the effective max length is enforced by the input handler, not
    by construction — **UNVERIFIED** exact cap).
  - **PW field:** mask bit set → masked; IME mode 12. Mask glyph = `*`, 6 px advance.
- **The IME mode (16 / 12) is NOT the mask switch.** Masking is purely the length/flags high bit; the
  IME mode only selects the IME conversion behavior of the field. (DIFF vs any reading that ties the PW
  mask to its IME mode — the binary masks on the field flag.)
- **Caret:** blinks on a ~500 ms cadence and is drawn only while the field has input focus.
- **Focus:** the construct routine focuses the ID textbox at show time (IME enabled on it).

## 3. PIN keypad (second password) — sub-states 31/32

> Element-level pass (2026-06-19): the keypad is a reusable scrambled-PIN widget (also used by the
> in-game gift/second-password modal); all coordinates below are panel-relative, the panel itself sited
> per §2.1 (dst ~(347,173)). Every key is a 1:1 atlas blit from `data/ui/password.dds`; the modal
> backdrop frame samples `data/ui/InventWindow.dds`.

- **Container panel:** screen dst **(347, 173)**, size **329 × 422**; digit keys + control buttons
  textured from **`data/ui/password.dds`**; the backdrop frame from **`data/ui/InventWindow.dds`**.
- **Digit positions: 10 cells, 5 columns × 2 rows, each cell 52 × 52.** Column X ∈ {28, 83, 138, 193,
  248} (= 55·col + 28); row Y ∈ {170, 230} (top row cells 0..4 at Y 170, bottom row cells 5..9 at Y 230).
- **Each cell is a stack of 10 digit-buttons (digits 0..9), 100 buttons total** — confirmed by the
  100-pointer button array zeroed at construction. The face of digit `d` samples the atlas at
  **source X = `d·52`** (columns 0,52,…,468); the **button state** selects the source **Y band**
  (per the NORMAL,PRESSED,HOVER builder order — §0.12):
  - **Normal (idle): srcY = 560.**
  - **Pressed: srcY = 664** (the 2nd source pair of the construct call).
  - **Hover: srcY = 612** (the 3rd source pair).
  - (This **corrects** the earlier "Pressed 612 / Hover 664" reading — the construct call passes the
    664 band as the PRESSED frame and the 612 band as HOVER, which the documented front-end 3-state
    argument order resolves unambiguously.) The scramble makes exactly **one** digit-button visible per
    cell. Each digit-button's action id = the digit value `d` (0..9).
- **Reset** button `(243, 133, 58, 30)` tag **11** (source origins on `password.dds` near X 663);
  **OK** `(90, 290, 154, 58)` tag **12** (source origins near X 330); **Cancel** `(90, 350, 154, 58)`
  tag **13** (source origins near X 486). **Tag roles (CONFIRMED, element-level pass): 11 = Reset/Clear
  (re-scramble + blank entry), 12 = OK / submit, 13 = Cancel.** Digit tags are **0..9** (not 1..10);
  tag 10 is unused.
- **Masked input field:** a text-only label (no atlas, font slot 0) at panel-relative
  `(81, 138, 150, 22)`, rendered as N literal `*` characters, one per entered digit (digits never
  drawn; no dot-sprite asset). **Max length 4.**
- **Backdrop / dragon frame** decoration: an `InventWindow.dds` panel sized **340 × 190**, source origin
  `(318, 647)`, **centered** in the parent; built then initially hidden (a structural backdrop element
  rather than a visible border in the shipped modal). No separate screen-dim quad is created by the
  keypad itself.
- **Draw order:** masked-entry label first, then the 100 digit-face buttons (cell 0 → cell 9), then
  Reset (11), OK (12), Cancel (13), then the (hidden) backdrop frame.
- **Scramble (CONFIRMED, static IDA, CYCLE 18 Phase A):**
  - **Seed:** `srand(time())` — the whole-second CRT wall-clock (`time()` family). Explicitly **NOT**
    `GetTickCount`, `timeGetTime`, `QueryPerformanceCounter`, or `GetSystemTimeAsFileTime` (those are
    imported but not used here). Two keypad opens within the same wall-clock second therefore seed the
    RNG identically — the scramble within a one-second window is reproducible (the original's
    seconds-granularity weakness; reproduce it faithfully via the same mechanism).
  - **Shuffle:** an **ASCENDING** uniform permutation of the digits 0..9 (the MSVC `std::random_shuffle`
    shape): a running bound `i` walks 2..10; each step swaps the new element with index
    `j = rand() mod i` (j ∈ [0, i−1]). This is **not** the descending textbook
    "for i = n−1 down to 1" form, but it produces an equivalent uniform 10-element permutation. (For a
    10-element array the MSVC large-range RAND_MAX guard never triggers; one `rand()` per step.)
  - **One digit per cell:** the 10-element permutation is mapped 1:1 onto the 10 cells; per cell, the
    single digit-button matching that cell's permuted value is shown and the other nine are hidden — so
    each of the ten digits 0..9 appears exactly once across the ten cells. Pressing a cell appends the
    digit of the face currently shown there (the visible button's tag IS its digit).
  - **Re-roll:** the scramble re-seeds and re-shuffles on **open (SetVisible-show)**, **Reset**, **OK**,
    and **Cancel**. On show the window also clears the typed-entry string and the submitted flag before
    scrambling.
  - **Reproduce the mechanism, not a value:** a port must perform a fresh whole-second-seeded ascending
    shuffle on each open — do not hard-code a permutation. (The runtime permutation value is emergent
    from the seed; only the *mechanism* is specified here, not a fixed sequence.)
- **Raise:** on the 29→31 edge (immediately when state becomes 31), after credential validation.
  **Submit:** OK (tag 12) copies up to 4 digits (formatted as ASCII, NUL-terminated, buffer of 5) to the
  login singleton (becomes field #3 of the credential string), sets the submitted flag, then re-scrambles.

## 4. Server-list display model — sub-states 35..37

> Element-level pass (2026-06-19): the server-list is a sub-view of the single LoginWindow, built once
> in the construct routine and re-laid each repaint by the list painter (a vtable method invoked by the
> pager). All plate/strip art is a 1:1 atlas blit; names/captions are msg.xdb text.

- **Outer list panel** (the page backdrop): dst **(270, 85)**, size **483 × 490**, atlas A2 source
  origin **(0, 490)**. A header/title image (atlas A1, dst (265,0) 494×113, source (0,469)) and a
  footer/agree caption strip (atlas A2, 70×17, source (0,980)) sit with it.
- **Page tabs:** 10 small 3-state strips across the top, each `(13 + 47·i, 66, 47 × 18)`, atlas A2,
  Normal `(596, 985)` / Hover `(643, 985)`, action **115 + i** (i = 0..9). These are **page selectors**
  (page = action − 115), not servers, and are re-skinned to a hidden source origin each repaint (they
  are a hidden hit-strip in the shipped list). (Two are also given alternate active faces in the build:
  index 1 → N`(690,985)`/H`(737,985)`, index 2 → N`(784,985)`/H`(831,985)`.)
- **Two detail plates** (the two servers on the current page — **2 visible per page, NOT 5**), built by
  a 2-column loop (i = 0,1) with X base 30, step 233, action base **400** (LEFT = 400, RIGHT = 401):
  - name label `(30 + 233·i, 390, 174 × 21)`, action 400+i, **left-aligned** (the authoritative list
    painter aligns the server-name label left; a duplicate painter variant centers it — prefer left).
  - icon / plate face image `(30 + 233·i + 47, 97, 100 × 372)`, atlas A4 src `(448 + 124·i, 6)`
  - select button3 `(30 + 233·i − 6, 97, 202 × 372)`, atlas A4 N`(9,6)`/H`(220,6)`, action 400+i
  - status/info label `(30 + 233·i, 410, 174 × 20)`
  - population/count label `(30 + 233·i, 430, 174 × 20)`, font slot 4, formatted `%4d / %4d`
- **Selection highlight strip** (drawn behind the selected plate): atlas A4, source origin `(700,18)`,
  46 × 168. **Status-color indicator quads ×3:** atlas A2, source origin `(500,786)`, 60 × 39, hidden by
  default and re-anchored around a special row (see the status==100 gate).
- **Server-name source (CONFIRMED, element-level pass):** server names come from `msg.xdb` via a
  table builder. **Name id = `5000 + server_id`** (server ids 1..40 → ids 5001..5040). Out-of-range
  ids fall back to caption id **5901** (formatted with the raw id). Column headers = msg **4029/4030/
  4031/4032**. (The msg table also pre-fetches parallel name banks 5101-5120 / 5201-5220 / 5301-5320 /
  5401-5440, but the server-list path returns from the 5001-5040 bank.)
- **Record decode** (8-byte LE record, in-memory list; see `packets/lobby.yaml`):
  `{server_id (+0), status_code (+2), load (+4), open_time/flags (+6)}`. Display index from a page =
  `record = (action − 400) + 2·page`. **On-screen row order is shuffled each repaint** (a per-render
  permutation is applied), so the visible row → record-byte-position mapping is **not** stable across
  renders (do not assume row 0 = the first record). The page stride is 2 records; page-jump via the
  115..124 strip is absolute (button i → page i), not relative ±1.
- **Commit guard:** `status_code == 0 && load < 2400` → write selected server_id, persist Lastserver,
  advance to channel-endpoint fetch (port 10000 + server_id). Selection is a **2D button hit**
  (OnEvent action 400/401), **not** a 3D ray-pick (that belongs to character-select — do not conflate).
- **Status / load coloring** (UI only; ARGB DWORDs re-confirmed 2026-06-18 and 2026-06-19): load > 1200
  → red (msg 6001, `0xFFFF0000`) · > 800 → orange (msg 6002, `0xFFED6806`) · > 500 → yellow (msg 6003,
  `0xFFFFFF00`) · **≤ 500 → green (`0xFFB5FF7A`), with NO msg id — the load is rendered as numeric
  `%4d / %4d` text, not a caption.** status_code 3 = scheduled-open: msg **6004 "preparing" only when
  `load (+4) == 24`**, otherwise msg **6005 HH:MM** built from `+4` (hour: /10, %10) and `+6`
  (minute: /10, %10). Record fields (re-confirmed, no swap): `+0` server_id, `+2` status, `+4` load,
  `+6` open_time.
- **status_code == 100 gate (CONFIRMED, element-level pass):** a record whose status is 100 is a
  **display-only special row** — the painter shows the 3 status-color indicator quads re-anchored around
  it (when a "show special" flag is set) instead of lighting the normal plate faces, and the commit
  guard's `status == 0` requirement makes the row **non-selectable**. **Quad anchoring (element-level
  pass, 2026-06-19):** the 3 quads are built at dst (0,0), 60×39, src (500,786), parked hidden; at repaint
  (gated by a one-byte "show special" flag) they are re-anchored to the special row's own plate-widget
  destination corner `(anchorX, anchorY)` (the plate's dst-X/dst-Y fields): quad 0 → `(anchorX−30,
  anchorY−13)`; quads 1 and 2 → `(anchorX+139, anchorY+13)` (the two right quads overlap exactly — a
  faithful duplicate, not a third distinct slot). Only the dst-X/Y are rewritten; size/source unchanged.
- **Default-selection highlight:** the painter compares each record's id against the remembered last
  server (registry `Lastserver`, read at boot) to draw the default highlight; the `NEW_SERVER_INDEX`
  value (the only `uiconfig.lua`-sourced number) marks the "new server" badge slot.
- **Persist:** committing a server writes registry `HKLM\SOFTWARE\crspace\do : Lastserver` (REG_DWORD
  = server id); the next launch reads it back to pre-highlight the previously selected server.

## 5. Loading window (scene state 2) — immediate-mode, NOT a widget tree

- Design resolution 1024 × 768, scaled to the backbuffer by `screenW/1024`, `screenH/768`.
- **Background quad:** full-screen `(0, 0, screenW, screenH)` blit of a randomly chosen DDS:
  `rand()%3` → `data/ui/loading.dds` | `data/ui/loading06.dds` | `data/ui/loading08.dds`.
  (Earlier `loading_01/02.dds` names are wrong for this build.)
- **Progress bar quad (CONFIRMED, static IDA, CYCLE 18 Phase A):** a second textured quad in design
  space. The destination corners are built from two screen-scale factors — `xScale = screenW · (1/1024)`
  and `yScale = screenH / 768` — both = 1.0 at the 1024×768 design resolution, so the design-space
  multipliers resolve to their literal values:

  | Edge | Design-space constant | Resolved @ 1024×768 |
  |---|---|---|
  | X-left | xScale · **−499.0** | −499.0 |
  | X-right | xScale · **−170.0** | −170.0 |
  | Y-top | yScale · **−363.0** | −363.0 |
  | Y-bottom | yScale · **−140.0** | −140.0 |
  | Z (depth) | per-vertex **1.0** (constant, all 4 verts) | 1.0 |

  X span = `|−170 − (−499)|` = **329** design units; Y span = `|−140 − (−363)|` = **223** design units.
  The vertex format is 5 floats `[X, Y, Z=1.0, U, V]`, four vertices. The bar samples a sub-rect of the
  art: U 443/1024..772/1024 (329 src px wide), V 576/768..744/768 (the bottom region). Drawn under an
  **orthographic screen-space projection with near = 0.0, far = 1.0** (identity world/view); the bar quad
  is drawn over the background quad only when load progress is non-zero.
  **CORRECTION:** the prior "depth 0.108" was wrong — there is **no 0.108** anywhere. Use **Z = 1.0**
  (ortho near/far 0.0/1.0), or simply rely on draw order over the background quad.
- **Fill (CONFIRMED, static IDA, CYCLE 18 Phase A):** progress from `VFS_GetProgress()` (0..100).
  `fill_px = clamp(223 · pct / 100, 0, 223)` (integer math; multiplier 223, divisor 100, upper clamp 223;
  the whole fill block is skipped at pct == 0 so the lower bound 0 is implicit). The texcoord delta =
  `fill_px · (1/1024)` (divisor 1024 = 0.0009765625), with an equivalent safety clamp of
  `223/1024 ≈ 0.2178`. The fill is driven on the **V (vertical)** axis of the bar sub-rect: one edge
  moves by `fill_px · yScale` from the Y-top base and the V texcoord of the two moving vertices shifts by
  the delta. No text label (percentage shown by fill only).
- **Completion / advance:** a "loading active" flag is cleared by the background loader thread after it
  finishes its corpus load + a 500 ms grace; the per-frame callback then ends the blocking scene loop,
  and the state machine advances to the destination chosen at case-2 entry (Opening or Select).
- **Skip-Opening gate (decided at case-2 entry):** `GetPrivateProfileInt("OPENNING", "SKIP", 0, ini)`
  (section spelled **OPENNING**, key **SKIP**, default 0), read from **`option.ini`** (NOT `DoOption.ini`
  — see §0.9). Non-zero → go to Select (4, skip Opening); zero → go to Opening (3).
- **Boot corpus** (loaded behind the bar, ordered): data tables incl. `system_control.scr`,
  `mapsetting.scr`, `items.scr`, `skills.scr`, `musajung.do`, `npcs.scr`, `mobs.scr`, `quests.scr`,
  `citems.scr`, the four `.xdb` (effectscale / creature_item / vehicle / buff_icon_position),
  `UiTex.txt`, char manifests, guild crests, + subsystem inits.
- **Audio:** looped 2D cue **920100100**, category 0 (single direct voice → cannot double-stack).

## 6. Opening scene (scene state 3) — ortho quads

> Element-level pass (2026-06-19): the slideshow background and the credit crawl are **concurrent
> layers** (the crawl scrolls over the crossfading backdrop), with the skip button on top throughout —
> not sequential states. Each TickStep advances the crawl first, then the slideshow FSM (or the
> finish-fade).

- **Backdrops:** 4 full-screen quads `openning_001.dds` .. `openning_004.dds` (1024 × 768). One phase
  index selects the active backdrop, drawn as a single full-screen textured quad (centered verts
  ±W/2, ±H/2) — **not** a child widget. The phase counter (init = 1 at window-build) is stepped 1→2→3→4
  by a banner slideshow FSM; each phase dwells **~17 500 ms** (timed off a shared millisecond timestamp)
  and ramps a single alpha byte **0 → 250 (0xFA)** by **+1 per tick**. The crossfade is a
  **single-texture alpha-over-(black-cleared)-back-buffer** modulation via the D3D texture-factor render
  state — **not** a two-texture blend; the next frame simply fades in from 0 as it replaces the prior.
  On each phase's dwell expiry, once that phase's alpha has reached 250, the FSM bumps the phase
  (1→2, 2→3, 3→4). Phase 4 is the last case — it does not bump further; it keeps running its own
  alpha ramp / dwell, and the scene is ended by SKIP or the finish-fade path.
- **Credit crawl:** texture `openning_scenario.dds`, built **1024 × 2048**, centered at
  X = `screenW/2 − 512`, starting Y = `screenH − 200`. After a **1000 ms** delay it translates the
  quad's destination Y at **30 units/second** (wall-clock, `dt_s · 30`) up to a bound of **~1843**, then
  sets a "crawl done" flag. It is a positional translate (not a UV offset). The code increments
  **+Y (DirectX Y-down)**; a **Godot Y-up port must invert the sign** so the crawl reads upward. The
  on-screen upward read is a property of the component's vertical-offset setter convention plus the
  bottom-anchored 2048-tall texture, NOT a negation inside the crawl math (the raw value increases
  0 → 1843). **Manual scrub (after the auto-crawl finishes):** action **1004** = rewind
  (−30·dt_s, floor 0), bound to **Page Up** (DIK_PRIOR); action **1005** = forward (+30·dt_s, ceil 1843),
  bound to **Page Down** (DIK_NEXT). These are FIXED keyboard bindings via the DirectInput DIK→app-code
  table (not configurable, not buttons/wheel-only — element-level pass, 2026-06-19). A separate mouse-wheel/drag scrub
  path steps a second crawl-Y by ±30 per event, clamped 30..1833.
- **Skip:** keyboard Enter (10) / ESC (27) / Space (32), or the 3-state skip button (action **100**) at
  dst `(screenW − 120, 10, 110 × 32)` on `mainwindow.dds`, source Normal/Hover `(761, 165)` / Pressed
  `(634, 165)`. Skipping persists `[OPENNING] SKIP = 1` (via `WritePrivateProfileStringA`, section
  spelled **OPENNING**, key **SKIP**, value **"1"**) to **`option.ini`** (the same file the Load scene
  reads; Opening then permanently skipped) and closes the window early, which unwinds the scene loop and
  advances to Select (4).
- **Auto-exit (CORRECTED — confirmed NOT load-bearing; static IDA, CYCLE 18 Phase A).** The WinMain
  state-3 case writes the next game-state = 4 (Select) **unconditionally and up-front**, before it builds
  the Opening window or runs the blocking scene loop. The fade does **not** transition the state; it only
  governs **when that blocking scene loop returns**. The Opening per-frame tick checks a one-byte
  **final-fade-armed flag**: when set, it treats this as the terminal fade-out, ramps the fade-alpha byte
  by +1 per frame up to its terminal value **250 (0xFA)**, then clears the engine run-flag — which makes
  the scene loop return and lets WinMain fall through into the pre-set Select (4) state. (On the next
  launch, WinMain's Load case reads the persisted `OPENNING`/`SKIP` flag and jumps straight to Select if
  set, bypassing Opening entirely.)
  - **Residual (MEDIUM, not load-bearing):** the exact instruction that first **SETS** the
    final-fade-armed flag after banner phase 4 (the *producer* of the armed state) was not isolated this
    pass — most plausibly a register/pointer-relative write or a constructor/phase-4-tail toggle rather
    than a literal immediate store. The *consumer* side (the armed-flag check, the alpha ramp to 250, and
    the run-flag clear that performs the exit) is fully confirmed. The port already advances Opening →
    Select on skip / completion and does not need this byte-exact arming site. **PENDING:** producer site
    only.
- **Audio:** looped 2D BGM **910061000**, started at scene build, stopped on teardown. (Distinct from the
  Loading-screen BGM 920100100 — the two scenes use different cues; do not assume a shared
  opening/loading track.)

## 7. Front-end audio cues (2D, category < 5 → `data/sound/2d/<id>.ogg`)

| Cue id | Where |
|---|---|
| 861010105 | login curtain intro (sub-state 1) |
| 920100100 | loading screen BGM (looped, category 0) |
| 910061000 | opening cinematic BGM (looped) |
| (UI click SFX per button) | login/server-list buttons |

## 7.10 Front-end config INI files (CONFIRMED, static IDA, CYCLE 18 Phase A)

The client builds **five distinct EXE-relative config INI files**, side by side in the EXE directory.
They are separate files — do not conflate them:

| File | Holds (front-end relevant) |
|---|---|
| `DoOption.ini` | the `[DO_OPTION]` options block — display width/height, sound volumes, brightness, and the saved login id (`OPTION_ID`); see §2.5 |
| `option.ini` | the Opening-skip flag `[OPENNING] SKIP` (read by Load case-2 entry, written by Opening skip); see §5/§6 |
| `panel.ini` | (HUD/panel layout — out of scope for this spec) |
| `combo.ini` | (combo settings — out of scope for this spec) |
| `TSIDX.ini` | (out of scope for this spec) |

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
