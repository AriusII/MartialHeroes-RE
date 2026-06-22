# Front-End Layout Tables — Login / PIN / Server List / Load / Opening (the build oracle)

```
verification: confirmed
ida_reverified: 2026-06-22
reconciled: 2026-06-22
anchor: 263bd994
promoted: 2026-06-22 (LOGIN-LAYOUT lanes A/B/C promoted; GU coordinate system section added — IDB SHA 263bd994)
evidence: [static-ida, debugger-confirmed-g2]
capture_verified: false
status: CODE-CONFIRMED (geometry literals + PIN scramble seed + load-bar rect + login visibility edges + opening fade mechanism, CYCLE 18 Phase A static IDA; element/asset/src-rect construction re-confirmed + deepened against the LoginWindow / PIN keypad / server-list / Opening construct routines, 2026-06-19 element-level pass — PIN digit-face state bands + credential mask mechanism + curtain extent + server-list plate/pager/status art all pinned); PIN second-password window CHROME CORRECTED 2026-06-19 chrome re-trace — the window backdrop blits password.dds (0,0)-(329,422), supplying the frame/title/red-warning/번호입력/field as baked art, superseding the earlier "no chrome" reading (see §0.7, §3); residual = opening final-fade armed-flag producer site only, and the exact pixel sub-layout inside the password.dds 329x422 backdrop (texture fact, VFS extract pending); SERVER-LIST NAME/STATUS/POPULATION RESOLVERS CORRECTED 2026-06-20 (see §4.1) — name bank is FLAT 5001+ServerId (the prior "5301-5440 banked resolver" is DROPPED; 5301/5101/5201/5401/5421 = discarded cache warm-up), status caption = 4029+StatusCode (4029..4032), population colour keyed on LoadCount (1200/800/500 → 6001 red / 6002 orange / 6003 yellow / green default, siblings 6004 maintenance + 6005 cur/max), 8-byte record {ServerId i16@+0, StatusCode i16@+2, LoadCount i16@+4, gate/flag i16@+6} with array ptr + count as OBJECT FIELDS (stale "+388" count offset DROPPED), StatusCode 100 = selectable sentinel (display-only); SERVER-LIST per-render SHUFFLE + Lastserver vs connected-id RESOLVED 2026-06-21 (visible plates are STABLE raw records [2i]/[2i+1]; the Fisher-Yates permutation hits a parallel id-vector whose only effect is the Lastserver value; the old "ServerId-vs-ServerId-1 off-by-one" was two different arrays, not an off-by-one; default-highlight key = NEW_SERVER_INDEX, not Lastserver); residual live-pending (6-D) = runtime StatusCode value semantics only; 2026-06-22 reconciled-dossier cross-check: existing content confirmed correct — substate ladder (1=intro+SFX, 2=curtain slide, 3/4/5 settle, 6=form), notice panel vs server-select grid distinction, PIN action range 0..99, ID textbox maxlen=16 (the dossier's "maxlen 6" was the old charset-mask reading, corrected here at §2.7 2026-06-21; spec wins); 2026-06-22 field-cap correction propagated (binary-won, IDB SHA 263bd994): ID per-keystroke cap=16 (GUTextbox +0xD0), charset mask=6 (+0xA4, NOT a length); PW per-keystroke cap=12 (GUTextbox +0xD0), charset mask=0x81 (+0xA4, NOT a length) — §2.1 PW row updated with maxlen 12; §2.7 already stated both correct values. LOGIN-VISUAL PROMOTE 2026-06-22 (IDB SHA 263bd994, counter-check reconciled): CONFLICT RESOLVED — sub-state 4→5 is AUTOMATIC (no Enter gate; binary-won, counter-check); form-plate snap corrected (200-threshold snaps the form decorative plate member +0x27C, not the ID textbox; binary-won); OnAction_Notice reset corrected (resets to CLOSED seam top=0/bottom=326, not "open"; binary-won); curtain quad atlas tuples promoted to §2.3 table; server-list painter-slot STATICALLY RESOLVED (direct-index path, CENTER name align); PIN Cancel-raises-ExitPanel and tag-11=re-scramble confirmed; load-colour dual-branch confirmed; pager re-arm geometry confirmed. G2 DEBUGGER-CONFIRMED PROMOTE 2026-06-22 (IDB SHA 263bd994, live doida.exe ?ext=dbg session): FULL-FRAME LOGIN MODEL at credential-freeze state promoted — (A) form-deco plate real dst CORRECTED to (265,548) 494×113 src(0,469) (supersedes the prior (494,469) value which confused the 494 width and 469 src-top for a destination; binary-won, G2 debugger-confirmed); (B) Connexion/Quitter control group real origin CORRECTED to X=356 (absolute group origin (356,531) 313×132; the prior (0,356) 531×313 panel coordinates were wrong — G2 debugger-confirmed, supersedes); curtain two-half model (top login_slice1.dds dstY=−222 / bottom login_slice1.dds dstY=+548) CONFIRMED ground-truth; the top curtain at −222 is the real animated curtain top half, NOT a port phantom — any prior note calling it a phantom is REVERSED. See §2.1 (Connexion/Quitter group) and §2.3 (form-deco snap dst).
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
7. **The PIN (second-password) window DOES have chrome — it is BAKED INTO `data/ui/password.dds`,
   not code-drawn** (chrome re-trace, 2026-06-19, supersedes the prior "no chrome" reading). The
   second-password container panel blits `password.dds` from source `(0,0)` sized `329×422` as its own
   full-panel **backdrop**, and that backdrop region of the texture contains the ornate frame, the title
   "2차 비밀번호 입력", the red multi-line warning, the "번호입력" caption and the input-field box. The
   keypad is 100 stacked digit-buttons (10 per position × 10 positions; scramble shows exactly one per
   position), drawn OVER that backdrop. The earlier conclusion missed that the keypad constructor
   overwrites the panel's backdrop-texture field with `password.dds` after the (texture-less) container
   ctor. There is no `msg.xdb` id and no ARGB color for the title/warning because they are pixels in the
   atlas, not labels — see §3.
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

## 0a. GU COORDINATE SYSTEM — implementers read this before placing any widget

> **This section is the single authoritative statement of the Diamond::GU widget coordinate system.**
> Every widget position in §2–§4 (and in `specs/login.md §7`) must be interpreted under these rules.
> Getting any one of these rules wrong causes a uniform mis-placement of every affected widget group —
> the "desaxage" / piled-up-form disorder reported on the login screen is a direct consequence of
> violating rule (3) below. Binary-confirmed from the recovered builder, transform, and render routines.
> (IDB SHA 263bd994, static IDA, promoted 2026-06-22.)

### 0a.1 Coordinate origin and axis direction

- **Origin: top-left corner of the back buffer, (0, 0).**
- **+Y is downward.** The UI orthographic projection maps pixel (0,0) to the top-left corner and the
  screen-height value to the bottom, so increasing Y moves a widget toward the bottom of the screen.
  This matches the standard DirectX screen-space convention.
- There is no centre-origin, no per-widget DPI scale, and no half-texel offset anywhere in the GU
  render path. One widget unit = one back-buffer pixel.

### 0a.2 Local vs absolute position — every coordinate in the tables is LOCAL

Every position value stored in a widget at construction is **LOCAL to its immediate parent's resolved
(absolute) origin**. The builder stores a widget's `localX` / `localY`; these are **NOT** direct
screen coordinates. The render system resolves absolute screen coordinates at draw time by the
additive cascade in §0a.4.

> **Critical implementation rule:** treat every `(x, y)` value in §2–§4 as the widget's offset
> within its named parent panel — NOT as the widget's absolute screen pixel.

### 0a.3 The root anchor — the centering offset applied to the entire login window

The `LoginWindow` object is itself a GU widget. At construction it is placed at:

```
rootLocalX = screenWidth  / 2 − 512
rootLocalY = screenHeight / 2 − 384
```

This centers a fixed **1024 × 768 logical canvas** inside the actual back buffer using integer
division. At the native 1024 × 768 resolution the anchor is (0, 0); at any larger resolution it is a
positive constant vector (for example, at 1920 × 1080 the anchor is (448, 156)).

Because every login widget is a transitive child of the `LoginWindow` root, **this anchor is added to
every login widget** through the parent cascade (§0a.4). A port that omits the anchor or applies it
with incorrect screen dimensions will shift every widget by the same constant vector — the classic
uniform desaxage.

### 0a.4 The parent cascade (computeTransform) — how absolute position is resolved

At draw time, each widget's absolute screen position is computed as:

```
absX = localX + parent.absX
absY = localY + parent.absY
```

For the root widget (no parent): `absX = localX`, `absY = localY`.

The cascade is applied recursively, parent-first: the root is resolved first, then each child uses the
already-resolved parent absolute. The resulting absolute (absX, absY) is the screen-pixel position of
the widget's top-left corner.

For any login widget the full expansion is:

```
absX = rootAnchorX + Σ(ancestor local x values) + widget.localX
absY = rootAnchorY + Σ(ancestor local y values) + widget.localY

rootAnchorX = screenWidth  / 2 − 512
rootAnchorY = screenHeight / 2 − 384
```

A widget's parent chain may include one or more intermediate panels. Each panel's own `localX` /
`localY` is included in the sum. A panel at `localX=0, localY=0` contributes nothing; a panel placed
at a nonzero local origin shifts all its children by that amount.

### 0a.5 Source-rect sampling — top-left origin, exact pixels, 1:1

- **Source-rect origin: top-left corner of the atlas texture, (0, 0).**
- The source rect for a widget is `(srcX, srcY, w, h)` — the rectangle from `(srcX, srcY)` inclusive
  to `(srcX+w, srcY+h)` exclusive. The builder stores the derived right and bottom edges directly.
- **No scaling, no UV inset, no half-texel offset.** The destination width/height equals the source
  width/height for every login widget — the blit is exactly 1:1.
- For 3-state buttons each state has its own `(srcX, srcY)` origin but shares the same `(w, h)`.

### 0a.6 Draw / z-order — insertion order, parent-first

- Draw order = AddChild order (first-added drawn behind later-added siblings).
- A parent draws itself first, then draws its children in insertion order.
- The per-widget depth field (default 3000) exists but is never reordered during the login build, so
  paint order equals build order throughout the login window.
- Visibility is governed by a per-widget boolean flag (`visibleFlag`). Invisible widgets and their
  children are skipped entirely.

### 0a.7 Common desaxage causes — diagnostic guide

| Symptom | Most likely cause |
|---|---|
| Every widget in a group shifted by the same constant vector | Root anchor omitted or computed with wrong screen dimensions (§0a.3) |
| A sub-group of widgets shifted uniformly | A panel's `localX`/`localY` wrong or the panel parented to the wrong container (§0a.4) |
| Individual widget in wrong position | The widget's own `(x, y)` treated as absolute instead of local (§0a.2) |
| Entire login canvas shifted from screen centre | Root anchor formula error — should be `(screenW/2−512, screenH/2−384)` |
| Atlas sprite wrong (correct position, wrong graphic) | `srcX`/`srcY` or atlas file wrong (§0a.5 / §1 atlas table) |

---

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
| PIN keypad panel | panel | 347 | 173 | 329 | 422 | 0 | 0 | password.dds | — | hidden | backdrop blits password.dds (0,0)-(329,422) = the ornate frame+title+warning+번호입력+field (baked); see §3 |
| Connexion/Quitter panel | panel | 356 | 531 | 313 | 132 | — | — | — | — | hidden (G2 debugger-confirmed 2026 / IDB 263bd994 — supersedes prior (0,356) 531×313 reading) |
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
| Form decorative plate | image | 265 | 0 | 494 | 113 | 0 | 469 | A1 | — | form-deco rod (decoration banner of the form strip, carried by the bottom curtain); rests at dst (265,0) local to the bottom-bar panel, resolves to abs (265,548) when the curtain is open — see §2.3 |
| ID label plate | image | 340 | 30 | 38 | 13 | 0 | 398 | A1 | — | "ID" caption graphic |
| PW label plate | image | 507 | 30 | 49 | 13 | 38 | 398 | A1 | — | "Password" caption graphic |
| Save-ID label plate | image | 619 | 86 | 67 | 13 | 87 | 398 | A1 | — | small notice/caption strip |
| **ID textbox** | textbox | 390 | 32 | 102 | 13 | 615 | 404 | A1 | 109 | IME mode 16; **maxlen 16** (GAP-4, §2.7); **unmasked** (mask bit clear); font slot 0 |
| **PW textbox** | textbox | 568 | 32 | 102 | 13 | 615 | 404 | A1 | 110 | IME mode 12; **maxlen 12** (GUTextbox +0xD0; binary-won 2026-06-22); charset mask 0x81 (+0xA4, allow-all, NOT a length); **masked** (mask bit set; `*` glyph, 6 px/char); font slot 0 — see §2.7 |
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

**Connexion/Quitter panel children** (the control group at abs origin (356,531) — G2 debugger-confirmed 2026 / IDB 263bd994):

> All `(x,y)` values below are **LOCAL to the panel origin (356,531)**. Absolute positions are the sum
> of the panel origin and the local offset. G2 live session confirms: strip abs (356,631) = panel+(0,100);
> Connexion abs (396,613) = panel+(40,82); Quitter abs (520,613) = panel+(164,82).

| Widget | type | x (local) | y (local) | w | h | srcX | srcY | atlas | action | abs dst (canvas) |
|---|---|---|---|---|---|---|---|---|---|---|
| Background strip | image | 0 | 100 | 313 | 32 | 289 | 437 | A1 | — | abs (356,631) |
| Connexion button | button3 | 40 | 82 | 110 | 38 | N520,492 | A2 | 111 | abs (396,613) — G2 confirmed |
| Quitter button | button3 | 164 | 82 | 110 | 38 | N750,492 | A2 | 112 | abs (520,613) — G2 confirmed |

> **Defect note (the prior port error):** the Connexion/Quitter group was previously placed too far RIGHT
> (against the OK-button X=456 or derived from a wrong origin). The real group origin is X=356 — not
> X≈456 or any other value derived from the OK button. The absolute destinations above (G2 debugger-confirmed)
> are the authoritative reference for the port. Supersedes all prior coordinates for this group.

**Confirm modals A/B** (single-OK message popups, msg 4023 / 4024):

| Widget | type | x | y | w | h | srcX | srcY | atlas | action |
|---|---|---|---|---|---|---|---|---|---|
| Confirm-A panel | panel | 342 | 289 | 340 | 190 | 318 | 647 | A3 | — |
| Confirm-A label (msg 4023) | label center | 10 | 100 | 330 | 20 | — | — | text | — |
| Confirm-A OK | button3 | 120 | 136 | 113 | 40 | N302,900 / P415,900 | A3 | 113 |
| Confirm-B panel | panel | 342 | 289 | 340 | 190 | 318 | 647 | A3 | — |
| Confirm-B label (msg 4024) | label | 10 | 100 | 330 | 20 | — | — | text | — |
| Confirm-B OK | button3 | 120 | 136 | 113 | 40 | N302,860 / P415,860 | A3 | 114 |

> **Confirm-A is the "connecting" popup.** The same Confirm-A object (the login-window's connecting-popup
> field) is what shows the **"서버에 접속중입니다…" (connecting to server)** caption (msg **4023**) during the
> server-join hand-off — it is raised at sub-state **40**, the instant before the secure-context/login-packet
> build (see §2.2). It is not a distinct object; "Confirm-A" and "the connecting popup" are the same panel.

### 2.1a Validation-error message box (the "확인 - N" countdown modal) — CONFIRMED (static IDA, 2026-06-19)

On a failed login submit the client shows a **dedicated error message-box panel** — a **distinct object**
from the Confirm-A/B popups above (it does **not** reuse action 113/114). It carries a single OK button
whose caption counts **down** ("확인 - 3" → "확인 - 2" → "확인 - 1") and the panel **auto-closes** when the
count reaches zero. Geometry matches the other login modals (atlas **A3** = `InventWindow.dds`).

| Widget | type | x | y | w | h | srcX | srcY | atlas | action |
|---|---|---|---|---|---|---|---|---|---|
| Error-msgbox panel | panel (modal) | 342 | 289 | 340 | 190 | 318 | 647 | A3 | — |
| Error message label | label center | 0 | 89 | 340 | 20 | — | — | text | 670 |
| Error OK (countdown) | button3 | 467 | 440 | 90 | 25 | N417,943 / H507,943 | A3 | 671 |
| (4 spare label lines) | label | — | — | — | — | — | — | text | — |

(The OK button rect is built panel-relative `(125, 151) 90×25`; the absolute dst shown above = panel
origin `(342,289)` + local `(125,151)`. The four spare labels are pre-built empty for multi-line bodies.)

- **Draw order:** panel frame → centered message label (action 670) → OK button (action 671) → four spare
  label lines.
- **Message id per failure edge** (each failure first resets the login flow back to sub-state **6**, then
  raises this modal — see §2.2 sub-state 29; the connect-result cases raise it from sub-state 35):
  - **ID empty OR ID length < 4 → msg 4025** (a single id covers both the "must enter an ID" and the
    "too short" cases; there is **no** separate empty-ID id).
  - **PW empty → msg 4026.**
  - **No servers returned → msg 4027** (generic); **fetch result −1 → msg 4028.**
- **Countdown behavior (CONFIRMED, the load-bearing fact):**
  - **Start N = 3.** The Show call passes a **3000 ms** budget; the displayed seconds = budget / 1000 = 3.
    (A screenshot catching "2" is mid-countdown; the true start is 3.)
  - **Tick source = a per-frame millisecond wall-clock delta** sampled while the panel draws, **throttled
    to at most one decrement per 1000 ms** (it subtracts a whole second only once ≥ 1000 ms have elapsed
    since the last decrement).
  - **Caption format = "<OK-caption> - <N>"**, where the OK caption is **msg 101** ("확인") and `N` is the
    remaining whole seconds — rebuilt on every decrement.
  - **Auto-close:** when the remaining budget expires (and the panel's no-auto-close flag is clear, which
    it is for validation errors), the panel **hides itself**. The login flow is already at sub-state 6
    (idle), so the screen simply returns to credential entry.
  - **Early dismiss:** clicking OK (action **671**) hides the panel immediately as well.
- **Confidence:** the panel geometry, the OK/message actions (670/671), the per-failure msg-id map, and
  the countdown timer logic (start 3000 ms, 1 Hz, "%s - %d", auto-close) are **CONFIRMED** (static IDA).
  The literal CP949 text of msg ids 101 / 4025 / 4026 / 4027 / 4028 lives in runtime `msg.xdb`, not in the
  binary → **UNVERIFIED** (debugger-confirmable). This **supersedes** any prior assumption that the
  validation error reused the Confirm-A/B popup: it is its own message-box object with a live countdown.

### 2.1b Credential form — consolidated per-widget table (the desaxage reference)

> This table is the single authoritative per-widget reference for the login credential form. Every
> column value was recovered from the binary builder (IDB SHA 263bd994, static IDA, promoted
> 2026-06-22 — LOGIN-LAYOUT lanes A/B/C). Implementers MUST read §0a before placing any widget:
> all `(x, y)` values are **LOCAL to the named parent panel** (§0a.2), not absolute screen pixels.
>
> **Prior value corrections (binary-won 2026, supersede prior login-visual values):**
> - ID textbox `maxlen` was previously recorded as **6** — corrected to **16** (binary-won 2026-06-22;
>   the value 6 was the charset-filter bitmask at a different field, not the length cap).
> - PW textbox `maxlen` was previously recorded as **129** — corrected to **12** (binary-won
>   2026-06-22; 129/0x81 was the allow-all charset mask, not the length cap).
> - Action on the "server-list toggle" / "select server" button (now correctly §2.1 action **102**)
>   was previously mis-labelled as "server-list open" in early notes — binary-won; the §2.1
>   action-semantics correction applies.

**Panel hierarchy for the credential form** (each level is LOCAL within its parent):

```
LoginWindow root  (absX = screenW/2−512,  absY = screenH/2−384)
  └─ Bottom login-bar panel (A)     localX=0, localY=326·screenH/768, w=1024, h=442
       └─ Credential sub-panel (B)  localX=0, localY=0, w=1024, h=100, tex=0 (no atlas)
            └─ [credential widgets — x/y below are relative to panel B]
```

**Credential sub-panel (B) itself** — tex=0, transparent, no atlas, no action:

| Widget | Parent | x | y | w | h | srcX | srcY | Atlas (.dds) | Action | Font slot | Notes |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Credential sub-panel | Bottom bar (A) | 0 | 0 | 1024 | 100 | 0 | 0 | none (tex=0) | — | — | Transparent layout container; opaque flag=0; hidden at build; origin LOCAL to the bottom bar |

**Widgets inside credential sub-panel (B)** — all `(x,y)` LOCAL to panel B:

| Widget | Parent | x | y | w | h | srcX | srcY | Atlas (.dds) | Action | Font slot | Notes |
|---|---|---|---|---|---|---|---|---|---|---|---|
| "ID" caption image | Credential sub-panel (B) | 340 | 30 | 38 | 13 | 0 | 398 | `login_slice1.dds` (A1) | — | — | Baked "ID" art; NOT a text label |
| "PW" caption image | Credential sub-panel (B) | 507 | 30 | 49 | 13 | 38 | 398 | `login_slice1.dds` (A1) | — | — | Baked password label art; NOT a text label |
| Save-ID hint image | Credential sub-panel (B) | 619 | 86 | 67 | 13 | 87 | 398 | `login_slice1.dds` (A1) | — | — | Small decorative caption strip |
| **ID / account textbox** | Credential sub-panel (B) | **390** | **32** | **102** | **13** | **615** | **404** | **`login_slice1.dds` (A1)** | **109** | 0 | IME mode 16; per-keystroke cap **16** chars (**binary-won 2026**, supersedes prior "6"); charset mask=6 (alphanumeric); unmasked |
| **Password textbox** | Credential sub-panel (B) | **568** | **32** | **102** | **13** | **615** | **404** | **`login_slice1.dds` (A1)** | **110** | 0 | IME mode 12; per-keystroke cap **12** chars (**binary-won 2026**, supersedes prior "129"); charset mask=0x81 (allow-all); masked — renders `*` per char |
| **Save-ID checkbox** | Credential sub-panel (B) | **694** | **86** | **13** | **13** | off **717**,**398** / on **730**,**398** | — | **`login_slice1.dds` (A1)** | **104** | — | Unchecked src (717,398,13,13); checked src (730,398,13,13); initial state seeded from saved-id |
| **OK / Login button** | Credential sub-panel (B) | **456** | **64** | **112** | **39** | N **266**,**398** / H **490**,**398** | — | **`login_slice1.dds` (A1)** | **103** | — | 3-state; pressed=normal; gated on game.ver index-5 check; builds AFTER the checkbox and saved-id branch |

> **All four credential input widgets (ID textbox, PW textbox, checkbox, OK button) bind exclusively
> to `login_slice1.dds` (atlas A1).** The ID and PW textboxes share the SAME atlas region
> `(615, 404, 102×13)`. Binding either field to `loginwindow.dds` (A2) or to any other atlas will
> produce a wrong background sprite — this is the single most common atlas-binding desaxage for
> credential fields.

**Bottom login-bar panel (A)** — children other than the credential sub-panel:

| Widget | Parent | x | y | w | h | srcX | srcY | Atlas (.dds) | Action | Notes |
|---|---|---|---|---|---|---|---|---|---|---|
| Server-list reveal button | Bar (A) | 456 | 166 | 112 | 39 | N 154,398 / H 378,398 | `login_slice1.dds` (A1) | **102** | Always visible at rest; opens quit-confirm ExitPanel (NOT the server list) |
| Form decorative plate | Bar (A) | 265 | 0 | 494 | 113 | 0 | 469 | `login_slice1.dds` (A1) | — | Snaps to **(265,548)** at curtain offset>200 (§2.3; G2 debugger-confirmed 2026 / IDB 263bd994 — supersedes prior "(494,469)" value) |

**Msg.xdb caption-id references for the credential form area:**

| Caption id | Widget | Role |
|---|---|---|
| 4001..4022 | 22 notice-panel body label rows | Notice/agreement text column (NOT server-list rows) |
| 4023 | Confirm-A label | Connecting dialog body |
| 4024 | Confirm-B label | Second confirm dialog body |
| 4025 | Error-msgbox | ID empty or length < 4 |
| 4026 | Error-msgbox | PW empty |
| 4027 | Error-msgbox | No servers returned |
| 4028 | Error-msgbox | Fetch error |

(All text is CP949. Caption text lives in `data/script/msg.xdb`. Reference captions by id only — see `formats/msg_xdb.md`.)

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
   at offset>200 snap the **form decorative plate** (credential-area chrome, member +0x27C) to dst
   **(265,548)** (G2 debugger-confirmed 2026 / IDB 263bd994 — supersedes the earlier "(494,469)" value,
   which mistook the 494 width and 469 src-top for a destination; see §2.3); at offset>222 → 3
3  curtain done: clear curtain offset 0; reposition curtains; show submit plate; hide notice; show the
   server-list ROOT panel; hide quit-strip/help-plate. Does NOT yet show the credential group.  → 4
4  curtain settled: **immediately and automatically → 5** (the tick's substate-4 case writes
   5 unconditionally, with no key check and no event guard — CONFIRMED binary-exact, counter-check
   2026-06-22; the earlier "Enter → 5 (event)" reading is CORRECTED). The opening is fully
   non-interactive from 1 through to 5.                                            → 5 (auto)
5  reveal form: child[161] hide, credential-group child[166] show(1), PIN modal hide → 6 (auto)
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
39 show the connecting popup (Confirm-A, msg 4023, single button action 113 = Cancel→34) + start join worker → 40
40 hand-off: build the TAB credential string, build secure context + login packet 0x2B, set the global
   game-state to the connect phase, arm 30 s connect timeout, leave the login scene.  → 41
41 post-hand-off idle (connect/SMSG path). SUCCESS = inbound 3/1 char-list tears the scene down → char-
   select (state 4); the connecting-popup button (113) or a fetch failure (msg 4027/4028) returns to the list.
```

> **CORRECTION (2026-06-19 — maintainer end-of-curtain capture oracle + IDA re-exam of `BuildScene`/
> `TickSubStateMachine`):** the curtain-settle is **auto-advancing** — `3 → 4 → 5 → 6` chains with **no
> user-input wait** — and the window **rests at sub-state 6** with the credential form (ID/PW label plates
> + textboxes, OK/확인 button, save-ID checkbox) **VISIBLE**. The earlier "4 = form idle that waits for
> Enter; credential hidden until the user advances 4→5→6" reading is superseded: the real client shows the
> credential form at end-of-curtain. Also: the two **curtain stone panels are ALWAYS PRESENT** (never
> hidden) and rest at **top Y = −222 / bottom Y = +548**, carrying the **frame + banner baked art**
> (디오 logo / `www.doonline.co.kr` URL / dragon ornament / 2 rings on the top panel; lower stone + the
> credential form host on the bottom panel). A port that hides them after the raise loses the entire
> frame/banner (the observed end-of-curtain bug). Rest-visible set: background + top frame + bottom curtain
> + curtain-header ornament + the credential form; only OK/Enter (→ 29) needs input.

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
| Interactive credential group (ID/PW textboxes, Save-ID checkbox, OK button) | **state 6 (interval [6, 28])** | Built hidden; hidden at states 3/4. **Shown entering state 6** (the validate-armed idle where the user types); **hidden entering 29** (validate hides it and raises the PIN), and re-hidden at 31 and 33 — see the §2.2 EDGE LADDER, which is authoritative here. In practice only state 6 is occupied in this interval. This **supersedes the earlier "≈ 5..33" envelope**: the ladder's explicit per-edge hide calls at 29/31/33 are the precise truth, so the credential group is NOT visible under the PIN modal (31/32) or during the server-list fetch (33+). **NOT 3..32** — the inputs do not appear at 3/4 (the curtain has opened but the credential inputs stay hidden; the transition 4→5→6 is fully automatic so the form becomes visible at end-of-curtain with no user input). |
| Login-form host strip | always present | A distinct object from the credential group; visible as an object from build through the whole scene (its Y animates). |
| Server-list submit plate | states 1..~34 | Shown at 1/2/3/4 (and on instant-open); hidden on the 34→35 edge. |
| Server-list CONTENT panel | **state 35..37** | Turned on at the **34→35 edge** (shown entering 35; records painted at 37). **NOT 33..37.** Object-identity nuance: state 33 only *starts* the fetch worker; state 35 is "fetching: show the content panel". What the prior spec called "server-list root, 33..37" was actually this content panel. The genuine root background panel is visible far earlier (from build). |
| Quit/help strip + help plate | state 35 onward | Hidden at 1/2/3 + instant-open; shown on the 34→35 edge (server-list phase). |
| PIN keypad | **states 31/32 (CONFIRMED)** | Built hidden; raised on the 29→31 edge; kept shown on 31→32; hidden on the 33→34 edge. Technically still flagged visible through state 33, but 33 is a one-tick fetch-start that immediately advances; a port gating on {31, 32} is faithful. |
| Notice panel | always hidden (by the flow) | Built hidden; explicitly hidden on the 1→2, 29→31, and instant-open edges; the sub-state machine never shows it (only a separate notice/tooltip action can). CONFIRMED. |
| Connexion/Quitter panel | always hidden (by the flow) | Built hidden; explicitly hidden on the 5→6 edge; never shown by the tick machine. CONFIRMED. (G2 debugger-confirmed panel origin (356,531) 313×132; see §2.1 children table.) |
| Confirm-A/B popups, Exit panel, Error panel | always hidden (by the flow) | Built hidden; raised only by action ids 102/112/113/114 or message popups, outside this machine. CONFIRMED. |

### 2.3 Curtain geometry

Two full-width host panels driven by one offset accumulator (start 0, +5/tick, no easing, no dt, no
clamp — frame-rate-dependent). **Top** curtain Y = −offset; **bottom** curtain Y = offset + 326.
At offset > 200 snap the **form decorative plate** (member +0x27C, atlas A1 src 0,469 size 494×113) to
canvas dst **(265, 548)** — the credential-area form-deco rod arriving near the end of the slide
(G2 debugger-confirmed 2026 / IDB 263bd994; supersedes the prior "(494, 469)" value, which confused
the 494-pixel width and 469 src-top for a destination coordinate — CORRECTED. The snap target is the
form plate, NOT the ID textbox; binary-won, counter-check 2026-06-22).
Stop at offset > 222 (→ sub-state 3 snap). At sub-state 3 the accumulator is **cleared to 0** and end
positions are hard-set: top = −222, bottom = 548 (not left at +5 overshoot). No alpha animation. The
four curtain/background quads are always-present panels (their visible flag stays set; Y animates).

**Full curtain quad inventory — G2 DEBUGGER-CONFIRMED (live doida.exe ?ext=dbg, IDB SHA 263bd994):**

> **CURTAIN TWO-HALF MODEL CONFIRMED, GROUND TRUTH.** Both curtain halves are real `login_slice1.dds`
> quads: the TOP half at rest dstY = −222 (slides upward off the top edge); the BOTTOM half at rest
> dstY = +548 (slides down off the bottom edge). This is the authoritative full-frame model read live
> from the running `doida.exe`. **Any prior note calling the top panel at −222 a "port phantom" is
> REVERSED — it is the real animated curtain top half, confirmed by the G2 live session.**

| Role | atlas (literal VFS path) | src (X,Y,W,H) | dst at rest (x,y,w,h) | animated quantity | build# |
|---|---|---|---|---|---|
| Background plate | `data/ui/loginwindow.dds` (A2) | 0,0,1024,490 | 0,110,1024,490 | static (not animated) | member +0x270 |
| **TOP curtain** | `data/ui/login_slice1.dds` (A1) | 0,0,1024,398 | 0,0,1024,398 | **dstY = −offset** (0 → −222, slides off top) — G2 confirmed, real animated curtain top half | member +0x274 |
| **BOTTOM curtain** (form host) | `data/ui/login_slice1.dds` (A1) | 0,582,1024,442 | 0,326,1024,442 | **dstY = offset+326** (326 → 548, slides off bottom); carries the form-deco rod — G2 confirmed | member +0x278 |
| Form decorative plate | `data/ui/login_slice1.dds` (A1) | 0,469,494,113 | 265,0,494,113 → **snaps to (265,548)** when offset>200 (G2 debugger-confirmed 2026 / IDB 263bd994; supersedes prior "(494,469)") | hidden until offset>200; snaps once | member +0x27C |

- All four quads are 1:1 atlas blits (dst w/h == src w/h, no UV scaling).
- `dstX` is held = 0 for both curtain panels every tick (X never animates).
- The background plate (A2, member +0x270) and the three A1 quads are loaded by LITERAL VFS path,
  NOT via UiTex.txt id.
- The form decorative plate is a child of the BOTTOM curtain host (member +0x278), not the window root.
- No action id is bound to any of these four curtain/background quads.
- **BOTTOM build-time dst-Y** = `326 × screenH / 768` (resolution-scaled at construction); the per-tick
  law overwrites dstY each frame with the flat literal `offset + 326`, so the scaled build value only
  governs the single pre-tick frame. (capture-pending: runtime confirmation; tick literal is authoritative
  for the animated portion.)

**Start/end state summary:**

| Quantity | Value |
|---|---|
| Accumulator start | 0 (seeded at sub-state 1) |
| Per-tick delta | +5 (linear, no dt, no easing, no clamp) |
| End threshold (→ sub-state 3) | offset > 222 (≈ 45 ticks) |
| End positions (hard-set at sub-state 3) | TOP dstY = −222; BOTTOM dstY = 548 |
| Mid-threshold (form-plate snap) | offset > 200 → form plate snapped to **(265,548)** (G2 debugger-confirmed 2026 / IDB 263bd994; supersedes prior "(494,469)") |
| Accumulator reset at sub-state 3 | yes, cleared to 0 |
| Cadence | once per render frame (frame-rate-dependent) |

**G2 DEBUGGER-CONFIRMED FULL-FRAME MODEL at credential-freeze (sub-state 6 rest) — back-to-front draw order (IDB SHA 263bd994, live doida.exe):**

> This is the authoritative layer stack for the LoginWindow at the credential-idle state (sub-state 6),
> read live from the running `doida.exe` via the `?ext=dbg` session. It supersedes any static guess
> about element order or position. The form-deco plate destination and the Connexion/Quitter origin
> are the two defects corrected by this session; all other layers are confirmed KEEP.

| # | Layer | Atlas / src | Dst (canvas abs) | Status |
|---|---|---|---|---|
| 1 (back) | STATIC backdrop — loginwindow.dds | src (0,0) | dst (0,110) 1024×490 | CONFIRMED KEEP |
| 2 | STATIC central banner frame — loginwindow.dds | src (0,490) | dst (270,85) 483×490 | CONFIRMED KEEP |
| 2a | — logo/dragon/rings child | loginwindow.dds src (70,980) | abs (477,129) 70×17 | CONFIRMED KEEP — must stay visible (carries baked art) |
| 3 | STATIC server-name plate — loginwindow.dds | src (0,490) | dst (270,85) 483×490 | CONFIRMED KEEP |
| 4 | ANIMATED curtain TOP half — login_slice1.dds | 1024×398 | rest dstY = **−222** | CONFIRMED KEEP (real animated top half — not a phantom) |
| 5 | ANIMATED curtain BOTTOM half — login_slice1.dds | rest dstY = **+548**; carries form-deco rod | dst (265,548) 494×113 src(0,469) | CONFIRMED KEEP |
| 6 | Credential form | ID textbox dst(390,~612 region) + PW textbox dst(568,…) | — | CONFIRMED KEEP (already correct) |
| 7 | OK/Login button (action 103) | login_slice1.dds src(266,398) | abs (456,612) 112×39 | CONFIRMED KEEP |
| 8 (front) | Connexion/Quitter control group, origin (356,531) | shared strip abs (356,631) 313×32 src(289,437) A1; Connexion (act 111) abs (396,613) 110×38 src(520,492) A2; Quitter (act 112) abs (520,613) 110×38 src(750,492) A2 | — | G2 CORRECTED — real group origin X=356, not against OK-button X=456 |

**DEFECTS corrected by this G2 session (the two active port errors):**
- **(A) Form-deco plate destination:** port placed it at canvas `(494,469)` — WRONG. Real dst = `(265,548)` (the port mistook the 494 width and 469 src-top for a destination). G2 debugger-confirmed supersedes.
- **(B) Connexion/Quitter group placement:** port placed the group too far RIGHT (against the OK-button X=456 or a wrong origin). Real group origin is X=356; Connexion abs X=396, Quitter abs X=520. G2 debugger-confirmed supersedes.

**Opening is FULLY AUTOMATIC (CONFIRMED, binary-exact, counter-check 2026-06-22):** sub-states 1→2→3→4→5→6
chain with no user input at any edge. The credential form goes live automatically at end-of-curtain settle.
No Enter key or any other input is required to advance past sub-state 4.

**OnAction_Notice reset path (CORRECTION 2026-06-22):** any hover/notice dispatch when not in a modal
state resets the two curtain panels to their **closed/seamed layout** (TOP dstY=0 / BOTTOM dstY=326) and
re-shows the page chrome. This path does **NOT** snap the curtain to its open positions (−222/548) — it
resets to CLOSED. An earlier spec wording "snaps fully OPEN" is incorrect; the binary writes the closed
positions. (This covers the "instant-open reset for any state ≠ 1" reference in §2.2 bands.)

Curtain start SFX = 2D cue **861010105** (category 2), one-shot, fired on the 1→2 edge only. No BGM in
the login window (no BGM/music start was found anywhere in the login tick or its state-enter branches).
See §7 for the full front-end audio cue table.

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
the channel-endpoint fetch, space-separated). **Hand-off buffer caps** (the downstream TAB-string copy,
NOT the per-keystroke input caps): account < 20, password = 17, PIN < 5 (≤4 digits). The **per-keystroke
input caps** enforced by the textboxes themselves are smaller — **ID = 16, password = 12** (see §2.7 /
GAP-4) — so these hand-off buffers never bind. This feeds the secure-context builder → login packet **0x2B** (see `packets/login.yaml`,
`login_flow.md`). A 30 s connect timeout is armed.

### 2.7 Credential textbox construction & masking (element-level pass, 2026-06-19)

Both credential textboxes are built in the login-window construct routine (not in the later secondary
init), as 102 × 13 fields on atlas A1 sampling the same source origin `(615, 404)`. They differ only by
their dest X (ID at 390, PW at 568), their IME-mode field, and their mask flag:

- **Render is by a length/flags field.** Each textbox carries a length/flags byte. When its **mask bit
  (the high bit) is set**, the field draws the literal glyph `*` once per entered character, advancing
  **6 px per character**, in font slot 0. When the mask bit is clear, the field draws the stored string
  left-aligned (with horizontal scroll once the character count overflows the visible width).
  - **ID field:** mask bit clear → shown in clear; IME mode 16. **Max length = 16 characters (CONFIRMED,
    static IDA, 2026-06-21 — GAP-4 resolved):** the textbox carries a per-field max-length cap set to 16 at
    login-window construction, enforced live by the per-keystroke and Ctrl+V paste input handlers (a typed
    or pasted character past 16 is rejected). The `6` quoted by an earlier reading was the field's
    **character-filter / charset mask**, not a length. The password field's equivalent max-length cap is
    **12**.
  - **PW field:** mask bit set → masked; IME mode 12. Mask glyph = `*`, 6 px advance.
- **The IME mode (16 / 12) is NOT the mask switch.** Masking is purely the length/flags high bit; the
  IME mode only selects the IME conversion behavior of the field. (DIFF vs any reading that ties the PW
  mask to its IME mode — the binary masks on the field flag.)
- **Caret:** blinks on a ~500 ms cadence and is drawn only while the field has input focus.
- **Focus:** the construct routine focuses the ID textbox at show time (IME enabled on it).

## 3. PIN keypad (second password) — sub-states 31/32

> Chrome re-trace (2026-06-19) — **CORRECTS the prior "no chrome" conclusion.** The login second-password
> window IS a full ornate window (frame, carved top, title "2차 비밀번호 입력", red multi-line warning,
> "번호입력" caption, masked input box, scrambled keypad, 확인/취소), exactly as the official client capture
> shows. The chrome is supplied by a **full-panel backdrop blit of `data/ui/password.dds`** (the prior pass
> traced only the keypad's child buttons and the container ctor's `tex = 0`, and missed that the keypad
> constructor then sets the panel's backdrop-texture field to `password.dds`). All coordinates below are
> panel-relative; the panel is sited per §2.1 at dst (347,173) size 329×422.

- **Container panel + chrome backdrop:** screen dst **(347, 173)**, size **329 × 422**. The container ctor
  builds it with `tex = 0`, but the keypad constructor then **assigns `data/ui/password.dds` as the panel's
  own backdrop texture** (writes it into the panel's backdrop-texture field). The panel draw step (the shared
  GUPanel onDraw → the alpha-fade image submit) blits that backdrop **before** the children, using the
  panel's own source rect — which the ctor left at source `(0,0)` extending to the panel size, i.e.
  **source `(0,0)-(329,422)` → destination `(347,173)` size `329×422`.**
- **The ornate chrome IS this backdrop region of `password.dds`** — the top-left `329×422` of the atlas
  contains the carved frame, the title **"2차 비밀번호 입력"**, the **red multi-line warning**
  ("잘못된 (2차 비밀번호)를 5회 이상 입력하게 되면 사용이 제한됩니다."), the **"번호입력"** caption, and the
  **input-field box** — all painted into the texture. They are therefore **not** a `msg.xdb` id and **not**
  a code-drawn label with an ARGB color: the keypad constructor makes **zero** message lookups and sets **no**
  color constant, precisely because this text is pixels in the art. The keypad's digit/control buttons and
  the masked-entry echo are drawn **over** this backdrop, sampled from other regions of the same atlas (the
  1:1 blit contract still holds per element — the backdrop's own element is `329×422`, the digit faces are
  `52×52`). **Consequence for the port:** blit `password.dds` `(0,0)-(329,422)` to `(347,173)` as the window
  background first, then draw the keypad children over it. Do not synthesize a frame or look up a title /
  warning string — render the texture region. (Extracting `data/ui/password.dds` from the VFS will show the
  exact pixel layout of title vs warning vs field within that 329×422 region; the code proves the whole
  region is the backdrop.)
- **Disambiguation:** the engine's `CountInputPanel` (a numeric *quantity* pad, e.g. split/drop count) is
  the widget that draws code labels with color constants (≈`0xFFFF15C0` / `0xFFFF0900`) and references the
  literal "초기화"/"+"/"-"/"00"; it is **not** the second-password window and the earlier "warning drawn as
  code labels, color 0xFFFFFF00" note conflated the two. The in-game/gift `GiftCharSecondPassword` is a
  *sibling* that reuses this same scrambled-keypad mechanism and submits the PIN over the network; it is not
  the login open path.
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
  tag **13** (source origins near X 486). **Tag roles (CONFIRMED, counter-check 2026-06-22): 11 = Reset
  (re-scramble + blank entry — NOT merely "clear"; re-randomises the keypad face layout), 12 = OK /
  submit, 13 = Cancel.** Digit tags are **0..9** (not 1..10); tag 10 is unused.
  - **Cancel (tag 13) raises the cancel-confirm ExitPanel** (CONFIRMED, counter-check 2026-06-22): the
    Cancel handler clears the entry string, re-scrambles, then explicitly calls `SetVisible(1)` on the
    reused ExitPanel child (see below) as a "really cancel?" confirm overlay. The ExitPanel is built
    hidden but is not dead chrome — Cancel shows it.
- **Masked input field:** a text-only label (no atlas, font slot 0) at panel-relative
  `(81, 138, 150, 22)`, rendered as N literal `*` characters, one per entered digit (digits never
  drawn; no dot-sprite asset). **Max length 4** (enforced by the per-digit append handler).
- **Hidden reused ExitPanel child:** the keypad constructor also builds an `InventWindow.dds` panel sized
  **340 × 190**, source origin `(318, 647)`, **centered** in the parent — a **reused quit-confirm
  ExitPanel** built then **kept hidden** (`SetVisible(false)`) at construction. It is raised by Cancel
  (tag 13 — see above) as the cancel-confirm. This is distinct from the **visible** `password.dds` window
  backdrop described above — do not confuse the two.
- **Draw order:** the `password.dds` window backdrop (the chrome, `(0,0)-(329,422)` → `(347,173)`) first,
  then the masked-entry echo label, then the 100 digit-face buttons (cell 0 → cell 9), then Reset (11),
  OK (12), Cancel (13). (The reused ExitPanel child stays hidden.)
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

> **Server name = small `msg.xdb` TEXT (slot-0 12 px); the big on-scroll calligraphy is BAKED decorative
> atlas art, NOT engine-rendered name text (CORRECTION, element-level pass 2026-06-19).** The painter
> resolves each server's name to a **string** via the message database keyed by the **server id**
> (`name_id = 5001 + ServerId`, flat base **5001**, no group/channel multiplier; out-of-range ServerId →
> fallback **5901** "unknown server #n" template — see §4.1) and feeds it to the **name label**
> (font slot 0, DotumChe 12 px, dst (…,390,174×21),
> centered, horizontal + ellipsis). It does **not** render a large or vertical font and does **not**
> re-point a source rect per server. Therefore the large black brush hanja/hangul (e.g. "羽化登仙") that
> fills the parchment scroll in the official client is **baked into the plate art on atlas A4
> (`loginwindow_02.dds`)** — a fixed **per-column** decoration (the 100×372 plate-face sub-rect
> `src (448 + 124·i, 6)`, i = 0/1), **not** a per-server image (there is **no** `server_<id>.dds`).
> **Z-order CONFIRMED (element-level pass 2026-06-19):** per-plate insertion order = paint order =
> (1) parchment select button → (2) name label → (3) **the 100×372 face quad** → (4) status caption →
> (5) count label, so the face is drawn **ON TOP** of the parchment (every plate widget is built with full
> alpha `0xFFFFFFFF`, no per-widget blend; insertion is a plain append). A port must draw the face
> **after/over** the parchment button (drawing it behind hides it under the opaque parchment — the observed
> "empty scroll" bug). **GAP (remaining):** only the .dds pixel content — whether the face crop at
> `src (448,6)/(572,6)` actually holds the brush calligraphy — cannot be read from the static binary;
> confirm against the atlas / visual oracle. The small slot-0 name text renders regardless.

> **Outer construction — the parchment/title/badge layers (element-level pass, 2026-06-19):**
> - **Backdrop is TWO layers:** a **full-screen** A2 (`loginwindow.dds`) image at **(0, 110) 1024 × 490**,
>   source **(0, 0)**, drawn **first**; then the list-box scroll panel (the next bullet) on top.
> - **Title "서버선택"** = a **baked atlas image** (not a msg string): atlas **A2**, dst **(207, 44) 70 × 17**,
>   source **(0, 980)**.
> - **EVENT badge** = a **baked image**: atlas **A1** (`login_slice1.dds`), dst **(407, −3) 210 × 70**,
>   source **(743, 398)**, sitting behind the refresh button.
> - **"Tabs" clarification:** the visible 하왕관 rows are the **two server PLATES per page** (actions
>   400 / 401) whose name labels carry the server-name text — they are **not** a tab strip. The ten
>   `115 + i` buttons are a **hidden page-jump strip** re-parked to a blank UV on each repaint.
> - **새로고침 (refresh)** = action **105** (the §2.1 "105 strip"): atlas **A1**, dst **(456, −3) 111 × 38**,
>   source N **(792, 398)** / H **(602, 416)**; **10-second-debounced** re-fetch (→ sub-state 34). The
>   back-to-login control is action **102** (the quit-confirm).
> - **Connecting popup "서버에 접속중입니다…"** = the Confirm-A modal (atlas **A3** `InventWindow.dds`,
>   panel src **(318, 647)**, dst **(342, 289) 340 × 190**, caption msg **4023** centered), raised at
>   **sub-state 39** (CORRECTION 2026-06-19: 39, not 40 — when the join/connect worker is spawned). It has a
>   **single 3-state button** (atlas-skinned, src y = 900, **caption baked into the .dds**) added with
>   **action 113**; clicking it **aborts the join and returns to the server list (sub-state 34)** — it is
>   **NOT** an OK button and **NOT** the credential-result feedback. The popup is never explicitly closed:
>   on a **successful** handshake the server sends the **3/1 character-list**, which tears the login scene
>   down and advances to **char-select (state 4)** (the popup dies with the scene). The **connect /
>   credential feedback** is the SEPARATE auto-counting-down message box (§2.1a): channel-fetch failures
>   raise msg **4027** (result 0) / **4028** (result −1) → back to the server list; a stale list raises msg
>   **4025** / **4026** → back to the credential form; a game.ver mismatch is a native MessageBox (msg
>   **2204**); a post-handshake rejection arrives over the wire (code-15 disconnect / 30 s timeout / server
>   popup opcode 4/500).

### 4.1 Server-record decode + name / status / population resolvers (CONFIRMED, static IDA, 2026-06-20)

> This subsection consolidates how each visible server plate's **name**, **status caption** and
> **population colour** are resolved from a packed in-memory record through the message database. All
> three resolvers read message ids out of `data/script/msg.xdb` (CP949); the literal CP949 text of each
> id lives in runtime `msg.xdb`, not in the binary, so the captions themselves are `live-pending (6-D)`.

**Server record = 8-byte packed structure** (little-endian; one per server in the in-memory list):

| Offset | Size | Field | Meaning |
|---|---|---|---|
| +0 | i16 | `ServerId` | 1-based server id, valid 1..40; also the name/status resolver key |
| +2 | i16 | `StatusCode` | server status code (drives the status caption + selectability) |
| +4 | i16 | `LoadCount` | population / crowd count (drives the colour branch) |
| +6 | i16 | load-valid flag / open-minute | **RESOLVED (static IDA, 2026-06-20):** when `StatusCode==0`, `+6 != 0` ⇒ `LoadCount` is a raw count read with the **threshold** ladder (1200/800/500); `+6 == 0` ⇒ `LoadCount` is a **discrete level** (4/3/2 → red/orange/yellow). When `StatusCode==3` it is the scheduled-open **minute** component. |

> The **record-array pointer** and the **record count** are **object fields** of the login/server-list
> object — they are read from named fields on that object, not from any fixed numeric offset. **DROP the
> stale "+388" count-offset note**: the record count lives at a *different* object field than that note
> claimed; do not cite a numeric address for it.

> **`ServerId (+0) == 100` is the special-row sentinel (CORRECTION, static IDA, 2026-06-20 — supersedes
> the earlier "StatusCode == 100" reading).** `Diamond_LoginWindow_PaintServerList` reads the
> sentinel from the **+0 server-id field** (`serverId = record[+0]`; the same value fed to the name resolver and
> the `1..40` range guard), **not** from `+2` status. A record whose `+0` id is 100 is **out of the 1..40
> name range** → its plate name falls back to msg **5901**, and the painter additionally lights the 3
> status-color indicator quads around it (see §4 "server_id == 100 gate"). It is **display-only** — it is
> NOT a selectability gate (the commit guard is `status (+2) == 0 && load (+4) < 2400`, confirmed in
> `LoginWindow_OnEvent`).

**Name resolver** — flat, no multiplier:

| Input | Resolved message id |
|---|---|
| ServerId 1..40 (in range) | `name_id = 5000 + ServerId` (msg bank **5001..5040** for ids 1..40; NO group/channel multiplier) |
| ServerId out of range | fallback **5901** ("unknown server #n" template, formatted with the id) |

> **Off-by-one (RESOLVED, static IDA, 2026-06-20):** `Server_GetNameString` builds `nameTable[1]=msg
> 5001 … nameTable[40]=msg 5040` and returns `nameTable[ServerId]`, so the formula is **`5000 + ServerId`** (ServerId 1 →
> 5001). The earlier "`5001 + ServerId`" wording was off by one; the C# painter already uses `5000 +
> ServerId`. This closes the documented "5000-vs-5001" nit.

> **DROP any "5301" base.** The 5301 block (and the sibling 5101 / 5201 / 5401 / 5421 blocks) are
> **cache warm-up that is DISCARDED** — they are never the name bank. The operative name bank is the flat
> **5001 + ServerId**.

**Status caption resolver** — four contiguous entries:

| StatusCode | Status-caption message id |
|---|---|
| 0 | 4029 |
| 1 | 4030 |
| 2 | 4031 |
| 3 | 4032 |

(i.e. `caption_id = 4029 + StatusCode`, StatusCode 0..3 → ids 4029..4032.)

**Population colour + crowd caption (`StatusCode == 0` only)** — TWO sub-branches selected by the `+6`
load-valid flag (CORRECTION, static IDA, 2026-06-20 — the painter has two colour ladders, not one):

*Branch A — `+6 != 0` (load-valid): `LoadCount` is a raw count, thresholded:*

| LoadCount band | Message id | Label colour (ARGB) |
|---|---|---|
| `> 1200` | 6001 | red `0xFFFF0000` |
| `801..1200` | 6002 | orange `0xFFED6806` |
| `501..800` | 6003 | yellow `0xFFFFFF00` |
| `<= 500` (default) | status caption `4029` reused | green `0xFFB5FF7A` |

*Branch B — `+6 == 0` (load-invalid): `LoadCount` is a DISCRETE level (exact equality, not a threshold):*

| LoadCount value | Message id | Label colour (ARGB) |
|---|---|---|
| `== 4` | 6001 | red `0xFFFF0000` |
| `== 3` | 6002 | orange `0xFFED6806` |
| `== 2` | 6003 | yellow `0xFFFFFF00` |
| else (incl. 0/1) | status caption `4029` reused | green `0xFFB5FF7A` |

- **Siblings of the crowd-caption bank:** message **6004** is a maintenance / check caption used for a
  sentinel load value (see §4 "status_code == 3" handling, where 6004 appears for `load == 24`); message
  **6005** is the current/max numeric caption (an `HH:MM`-style / `cur / max` formatted line). Both belong
  to the same caption family as 6001..6003 but are not part of the simple `> 1200 / > 800 / > 500` colour
  ladder above.

**`live-pending (6-D)` items for the server-list resolvers** (debugger-confirmable, see §4.2):
- ~~**ServerId vs ServerId-1 off-by-one** between call sites~~ — **RESOLVED (static IDA, 2026-06-21):**
  this was never an off-by-one. It is **two different arrays**. The **connected / committed** server id is
  read from the **raw record array** (record field `ServerId` at +0). The **`Lastserver` registry** value is
  read from a **separate, per-render-permuted parallel server-id vector** (indexed by the clicked record
  position, or element [0] for the single-server auto-select path). So the value persisted to `Lastserver`
  can legitimately differ from the displayed / connected id — they come from two distinct arrays, not from a
  one-off index slip. The visible plate order itself is **stable** (page i shows raw records [2i] and [2i+1]
  in order — see §4.2). No longer `live-pending`.
- The **runtime `StatusCode` value semantics** — which integer means "available" vs "full" (beyond the
  100 = selectable sentinel) — is `live-pending (6-D)`.
- ~~**When the +6 gate / flag field is nonzero**~~ — **RESOLVED (static IDA, 2026-06-20):** `+6` is the
  load-valid flag (nonzero → threshold colour ladder; zero → discrete 4/3/2 colour ladder) for
  `StatusCode==0`, and the scheduled-open minute for `StatusCode==3`. No longer `live-pending`.

### 4.2 Outer construction

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
  - **name label** `(30 + 233·i, 390, 174 × 21)`, action 400+i, **font slot 0** (DotumChe 12 px, weight 0),
    **center-aligned** (align mode 2), horizontal with mid-string ellipsis — **NOT** a large or vertical
    font (CORRECTION 2026-06-19: the authoritative painter centers; the earlier "left-aligned" reading is
    superseded, and the on-scroll calligraphy is baked atlas art, not this label — see the name note above).
  - **plate face image** `(30 + 233·i + 47, 97, 100 × 372)`, atlas A4 src `(448 + 124·i, 6)` — fixed
    **per-column** (i = 0,1), never re-pointed per server (candidate carrier of the baked calligraphy).
  - **select button3 (clickable parchment)** `(30 + 233·i − 6, 97, 202 × 372)`, atlas A4 N`(9,6)`/H`(220,6)`, action 400+i
  - **status caption label** `(30 + 233·i, 410, 174 × 20)`, **font slot 4** (DotumChe 12 px, weight 800),
    center-aligned, **colored per branch** (the "사용가능 / 준비중 / …" status text — see coloring below).
  - **count label** `(30 + 233·i, 430, 174 × 20)`, font slot 0, **set to an EMPTY string** by the painter.
    (CORRECTION 2026-06-19: the prior "%4d / %4d population count, font slot 4 at +430" was wrong — the
    slot-4 label is the STATUS caption at +410; +430 is left blank; the `%4d / %4d` and `i %d status %d
    count %d` lines are dead-debug stubs, never drawn.)
  - **Both the select button AND the name label carry action 400+i** (confirmed binary-exact, counter-check
    2026-06-22): `AddChildWithAction` is called for the name label with `v179` AND for the select button
    with `v179` — either clicking the parchment or clicking the name text fires the plate-pick.
- **Painter slot STATICALLY RESOLVED (counter-check 2026-06-22, IDB SHA 263bd994):** the derived-class
  vtable's last slot is the **direct-index painter** (`record[2·page + i]` — NO shuffle indirection).
  The base-class vtable's last slot is the **shuffle-order painter** (reads through the `+0x438` order
  array). The shipped `LoginWindow` construction uses the derived class, so the **live painter is the
  direct-index path** and **name alignment is CENTER** (the base variant's LEFT alignment is on the dead
  path). The `+0x438` render-order array is still built each repaint (by the Lastserver-aware or plain
  shuffle builders), but it is NOT consulted by the live painter. A port must draw plates in raw record
  order with **centered name labels**.
- **Pager re-arm geometry (CONFIRMED binary-exact, counter-check 2026-06-22):** each repaint all 10 pager
  strips are reset to a blank atlas region `N(500,792)/H(500,810)/P(500,810)`, then three of them get
  real art: strip[1] → `N(500,828)/H(500,846)`, strip[2] → `N(500,864)/H(605,985)`, strip[3] →
  `N(710,985)/H(815,985)`. The two strips that got alternate faces at build time keep those as the
  starting blank-art frame, overridden each repaint. Pager action arm range: **115..124** (10 strips),
  action = page index + 115; only strips for valid pages get real art; unused strips park on the blank
  origin.
- **Refresh debounce: 10 000 ms** (CONFIRMED binary-exact, counter-check 2026-06-22). Action 105 is a
  no-op if the current sub-state is 35 (already fetching) OR if the last fetch timestamp is less than
  10 000 ms ago. The throttle timestamp field is in the login-window object.
- **Selection highlight strip** (drawn behind the selected plate): atlas A4, source origin `(700,18)`,
  46 × 168. **Status-color indicator quads ×3:** atlas A2, source origin `(500,786)`, 60 × 39, hidden by
  default and re-anchored around a special row (see the status==100 gate).
- **Server-name source (CORRECTION, 2026-06-20 — supersedes the earlier "5301-5440 banked resolver"):**
  the painter resolves the display name from the message database with a **flat** base, `name_id = 5000 +
  ServerId` (ServerId 1-based, valid 1..40; msg bank 5001..5040), with **NO group/channel multiplier**. An out-of-range
  ServerId falls back to message id **5901** ("unknown server #n" template). **DROP the 5301 base** — the
  5301 (and the sibling 5101 / 5201 / 5401 / 5421) blocks are **cache warm-up that is DISCARDED**, never
  the name bank; the earlier "name resolver over banks 5301-5440" reading is superseded. (See §4.1 for
  the full name / status / population resolver tables.) **msg 4029..4032 are the STATUS CAPTIONS** (keyed
  by `status_code`, see coloring above), not column headers.
- **Record decode** (8-byte LE record, in-memory list; field table + resolvers in §4.1; see also
  `packets/lobby.yaml`):
  `{server_id (+0), status_code (+2), load (+4), open_time/flags (+6)}`. Display index from a page =
  `record = (action − 400) + 2·page`. **The on-screen plate order is STABLE (CORRECTION, static IDA,
  2026-06-21):** page i always shows raw records **[2i]** and **[2i+1]**, read sequentially from the raw
  record array — the visible row → record-byte-position mapping **is** stable across renders. A per-render
  Fisher-Yates permutation **is** performed each repaint, but it operates on a **separate parallel
  server-id vector**, NOT on the visible plates; its **only** observable effect is which server id is
  persisted to the `Lastserver` registry (see Persist below and the two-arrays resolution in the §4.2 live-pending list). So a
  port must draw the plates in raw record order and must NOT shuffle the displayed rows. (This supersedes
  the earlier "on-screen row order is shuffled each repaint / row 0 ≠ first record" note.) The page stride
  is 2 records; page-jump via the 115..124 strip is absolute (button i → page i), not relative ±1.
- **Commit guard:** `status_code == 0 && load < 2400` → write selected server_id, persist Lastserver,
  advance to channel-endpoint fetch (port 10000 + server_id). Selection is a **2D button hit**
  (OnEvent action 400/401), **not** a 3D ray-pick (that belongs to character-select — do not conflate).
- **Status / load coloring** (the slot-4 status caption at +410; ARGB DWORDs re-confirmed 2026-06-18 and
  2026-06-19). For `status_code == 0` with the load-valid flag (`+6`) set: load > 1200 → caption msg
  **6001**, `0xFFFF0000` (red) · > 800 → msg **6002**, `0xFFED6806` (orange) · > 500 → msg **6003**,
  `0xFFFFFF00` (yellow) · **≤ 500 → the status caption msg `4029..4032` (keyed by `status_code`),
  `0xFFB5FF7A` (green) — this is the "available" (사용가능) case.** (CORRECTION 2026-06-19: the prior
  "≤500 renders numeric `%4d / %4d`, no caption" was wrong — the available row draws the green status
  caption; the `%4d / %4d` count is dead debug.) **When the load-valid flag (`+6`) is ZERO (CORRECTION,
  2026-06-20),** the same `status_code == 0` path instead reads `load (+4)` as a **discrete level by exact
  equality**: `== 4` → msg 6001 red, `== 3` → msg 6002 orange, `== 2` → msg 6003 yellow, else → green
  status caption (same msgs/colours as the threshold ladder). `status_code == 3` = scheduled-open: msg **6004** only
  when `load (+4) == 24`, otherwise `snprintf(msg 6005, …)` = **HH:MM** from `+4`/`+6`. Other status codes
  draw the status-keyed caption `4029..4032` with no color override (color written to the GULabel color
  field +0x0C). Record fields (no swap): `+0` server_id (also the name-resolver key), `+2` status,
  `+4` load, `+6` open_time/load-valid flag.
- **server_id == 100 gate (CORRECTION, static IDA, 2026-06-20 — was "status_code == 100"):** the painter's
  sentinel test is on the **+0 server-id field** (`serverId = record[+0] == 100`), not `+2` status. A record whose
  **id** is 100 is a **display-only special row** — its name resolves to the out-of-range fallback msg 5901
  and the painter shows the 3 status-color indicator quads re-anchored around it (when a "show special"
  flag at `this+0x425` is set), and the commit guard (`status == 0 && load < 2400`) governs selectability
  independently (the special row is not made selectable by the id-100 test). **Quad anchoring (element-level
  pass, 2026-06-19):** the 3 quads are built at dst (0,0), 60×39, src (500,786), parked hidden; at repaint
  (gated by a one-byte "show special" flag) they are re-anchored to the special row's own plate-widget
  destination corner `(anchorX, anchorY)` (the plate's dst-X/dst-Y fields): quad 0 → `(anchorX−30,
  anchorY−13)`; quads 1 and 2 → `(anchorX+139, anchorY+13)` (the two right quads overlap exactly — a
  faithful duplicate, not a third distinct slot). Only the dst-X/Y are rewritten; size/source unchanged.
- **Default-selection highlight (CORRECTION, static IDA, 2026-06-21):** the authoritative list painter
  draws the default highlight by comparing each record's `ServerId` (+0) against **`NEW_SERVER_INDEX`**
  (the single `uiconfig.lua`-sourced value) — **not** against the `Lastserver` registry value. The earlier
  "compares against the remembered last server" wording is superseded: in this painter the default-highlight
  key is `NEW_SERVER_INDEX`. (`Lastserver` is **written** on commit — see Persist below; whether it is read
  back to pre-highlight a server is done elsewhere and was **not** re-confirmed this pass, so do not assert
  it as this painter's behaviour.)
- **Persist:** committing a server writes registry `HKLM\SOFTWARE\crspace\do : Lastserver` (REG_DWORD
  = server id); the next launch reads it back to pre-highlight the previously selected server.

### 4.3 Server-list window — full construction (binary-re-derived 2026, supersedes the prior partial §4.3)

> **Supersedes the prior partial §4.3; binary-re-derived 2026 (IDB SHA 263bd994); exhaustive
> from-scratch-rebuild reference.** This section was re-derived in full from the LoginWindow
> construction routine, its server-list paint routine, and its handshake sub-state machine, after the
> earlier §4.3 proved incomplete and produced a broken port (cluttered overlap, blank-white tabs,
> duplicated chrome). Every geometry/atlas/action/font/colour fact here is **static-confirmed** from
> hard-coded literals (the layout is **not** data-driven). Items that genuinely depend on runtime
> server data or on the atlas pixels are explicitly flagged **live-pending** / **texture-oracle**. A
> concurrent live `?ext=dbg` probe found **no live debuggee attached** (and in any case LoginWindow is
> torn down before the Loading scene), so the static binary is the authoritative — and, for a
> non-data-driven layout, conclusive — source. §4.1 (resolvers) and §4.2 (outer construction) remain
> correct and are cross-referenced rather than restated.

All builder argument orders are confirmed: an **image** is `(atlas, dstX, dstY, w, h, srcX, srcY)` and a
**1:1 atlas blit with NO scaling**; a **panel** adds an opaque/clip flag; a **3-state button** is
`(atlas, dstX, dstY, w, h, Normal srcX, Normal srcY, Hover srcX, Hover srcY, Pressed srcX, Pressed srcY)`.
Four atlases are preloaded, in this fixed order, and **no others** are used by the login window:
**A1** = `data/ui/login_slice1.dds`, **A2** = `data/ui/loginwindow.dds`, **A3** = `data/ui/InventWindow.dds`,
**A4** = `data/ui/loginwindow_02.dds`. There is **no** `server_<id>.dds`.

---

#### 4.3.0 The three near-overlapping backdrops — the double-draw resolution

The single LoginWindow builds **three distinct backdrops that occupy nearly the same screen region** but
belong to **three different, mutually-exclusive phases**. The broken port draws two or three of them at
once; the binary shows exactly **one** at a time. A correct port MUST treat these as one-of-N, never
stacked:

| Backdrop (role) | Phase it belongs to | dst | size | atlas / src | Visible when |
|---|---|---|---|---|---|
| **Notice / agreement panel** | the notice/EULA sub-view | (270, 85) | 483 × 490 | A2 src (0, 490) | Notice phase only; **hidden** across the curtain, PIN, fetch, and server-list sub-states |
| **Server-list content panel** | the server-list sub-view | (270, 85) | 483 × 490 | A2 src (0, 490) | **Server-list phase only** — shown at the fetch→show edge (sub-states 35..37) |
| **Login banner frame** | the credential-form phase | (265, 0) | 494 × 113 | A1 src (0, 469) | During the curtain/credential form; **hidden** at the fetch→show edge (so it is gone once the server list appears) |

> **Resolution of the double-draw the port suffers:** the notice panel and the server-list content panel
> are **two separate widgets built with the identical `(270, 85) 483 × 490, A2 src (0, 490)` blit.** They
> are never on screen together — the sub-state machine hides the notice panel before the server-list
> phase and only then shows the content panel. The login banner at `(265, 0)` is a **third** widget on the
> credential-form panel and is hidden once the list appears. **A port must render exactly one of these
> backdrops per phase; drawing the notice backdrop and the list backdrop over the same rectangle (or
> leaving the credential banner up under the list) is the observed clutter.** All three are built
> initially **hidden** and toggled by the sub-state ladder in §4.3.7.

---

#### 4.3.1 Server-list content panel — complete child inventory

**Static-confirmed.** All children below are parented to the **server-list content panel** (the
`(270, 85) 483 × 490` A2 backdrop of §4.3.0), are built **hidden** (visible-at-rest = no), and become
visible only when the panel itself is shown in the server-list phase. Coordinates are **local to the
content panel** unless noted. Two repeated systems (the two detail plates and the ten page tabs) are
detailed in §4.3.2 / §4.3.3.

| Widget | type | localX | localY | w | h | atlas | src tuple(s) | action | font | visible at rest |
|---|---|---|---|---|---|---|---|---|---|---|
| Title "서버선택" | image | 207 | 44 | 70 | 17 | A2 | src (0, 980) | — | — | hidden |
| Detail plate 0 (×5 widgets) | group | 30 | — | — | — | A4/text | see §4.3.2 | 400 | — | hidden |
| Detail plate 1 (×5 widgets) | group | 263 | — | — | — | A4/text | see §4.3.2 | 401 | — | hidden |
| Status-indicator quad ×3 | image | 0 | 0 | 60 | 39 | A2 | src (500, 786) | — | — | hidden |
| Selection-highlight strip | image | 0 | plate-0 parchment top + 8 | 46 | 168 | A4 | src (700, 18) | — | — | hidden |
| Page tab ×10 | button3 | 13 + 47·i | 66 | 47 | 18 | A2 | see §4.3.3 | 115 + i | — | **hidden** |

The **three status-indicator quads** (the `60 × 39` A2 `src (500, 786)` images) are shown and re-anchored
around the selected plate **only** when a record's `ServerId == 100` (the special-row sentinel) **and** the
content-panel's internal sentinel flag is set; they are otherwise hidden. The **selection-highlight strip**
is shown behind the plate whose `ServerId` equals the highlight key (§4.3.6). The **refresh / back controls
and the EVENT badge are NOT in this panel** — they live on the credential-form panel and are covered in
§4.3.4.

---

#### 4.3.2 The two detail plates — 2 slots per page, side-by-side

**Static-confirmed.** A 2-iteration loop builds **two plate slots** at X base `30 + 233 · i` — plate 0 at
local x = 30, plate 1 at local x = 263 (canvas x ≈ 300 and 533), X stride **233**. Each plate is a stack of
**five** widgets, all built **hidden**:

| Widget | type | localX | localY | w | h | atlas | src tuple(s) | action | font | notes |
|---|---|---|---|---|---|---|---|---|---|---|
| Name label | label (center) | 30 + 233·i | **390** | 174 | 21 | text | — | 400 + i | slot 0 | msg `5000 + ServerId`; center align; also carries the plate action |
| Plate-face image | image | 30 + 233·i + 47 | 97 | 100 | 372 | A4 | src (448 + 124·i, 6) | — | — | plate 0 src x = 448, plate 1 src x = 572; baked-calligraphy candidate; drawn **after / over** the select button |
| Select button (parchment) | button3 | 30 + 233·i − 6 | 97 | 202 | 372 | A4 | N (9, 6) / H (220, 6) / P (220, 6) | 400 + i | — | clickable parchment; **both** this and the name label carry action 400 + i (LEFT = 400, RIGHT = 401) |
| Status / load caption | label (center) | 30 + 233·i | **410** | 174 | 20 | text | — | — | **slot 4** | DotumChe 12 px weight 800; coloured per §4.3.5 |
| Spare label | label | 30 + 233·i | **430** | 174 | 20 | text | — | — | slot 0 | painter sets an **empty string** — never drawn; the old "class/count" reading is dead-debug |

**Z-order within a plate** (insertion = paint order): select button → name label → plate-face image →
status caption → spare. The face quad is drawn **on top of** the parchment button (drawing it behind would
hide it under the opaque parchment — the prior "empty scroll" bug). Text-line vertical stride = **20 px**
(390 → 410 → 430).

> **Scroll count / the "one centered scroll" oracle — resolution of the maintainer's count question:** the
> engine **always lays out two plate SLOTS per page**, but the painter populates and shows a slot **only if
> its record exists** (remaining-record guard, §4.3.3): with a single server on the page only **plate 0** is
> populated and shown, and plate 1 stays hidden. So the official client's single centered scroll is the
> **one-record case** (one plate rendered, the other hidden) — **not** a one-plate design. A port must build
> two slots and gate each on record-availability; it must **not** render an empty second parchment.
> **Texture-oracle item:** whether the single rendered plate should sit visually centered (vs. left at
> x = 30) is a presentation detail confirmed against the captures, not the binary.

---

#### 4.3.3 Name-strip page tabs — HIDDEN, not blank-art

**Static-confirmed.** Ten 3-state buttons are built by a loop while the running X stays inside the column:
local x = `13 + 47 · i` (13, 60, 107, … 436), local y **66**, size **47 × 18**, atlas **A2**, build-time face
Normal (596, 985) / Hover (643, 985) / Pressed (643, 985), action **115 + i** (page = action − 115). Two of
them receive alternate build-time faces — tab 1 → N (690, 985) / H (737, 985), tab 2 → N (784, 985) /
H (831, 985).

> **THE TAB VISIBILITY RULE — resolution of the maintainer's white-box question:** **every one of the ten
> tabs is built HIDDEN** (each gets a `SetVisible(false)` immediately after construction). They are
> **not** blank art that always draws. On each repaint the painter **re-skins** all ten to a blank source
> origin (Normal (500, 792) / Hover (500, 810) / Pressed (500, 810)) and then gives **only three** of them
> real pager art (see §4.3.4). **Re-skinning a hidden widget's source UV does NOT make it visible** —
> visibility is a separate flag the painter only sets for valid pages. **A port that renders all ten tabs
> as opaque white boxes (the blank (500, 792) crop) is wrong: unused tabs stay hidden; only the pager
> strips for pages that actually exist are shown.** The blank (500, 792) crop's pixel content is a
> **texture-oracle** item, but it is irrelevant when the rule "hidden unless a valid page" is honoured.

---

#### 4.3.4 Refresh, back, EVENT badge, and the pager

**Static-confirmed.** The **refresh** and **back** controls and the **EVENT badge** are children of the
**credential-form panel** (the same panel that carries the login banner of §4.3.0), **not** of the
content panel. They are shown at the fetch→show edge (server-list phase) and hidden during the
curtain/credential phase.

| Control | type | dst | size | atlas | src tuple(s) | action | role |
|---|---|---|---|---|---|---|---|
| **새로고침 / Refresh strip** | button3 | (456, −3) | 111 × 38 | A1 | N (792, 398) / H (602, 416) | **105** | re-fetch the list; **10 000 ms debounced** (no-op while already fetching or within 10 s of the last fetch) |
| **Back / quit-confirm strip** | button3 | (456, 166) | 112 × 39 | A1 | N (154, 398) / H (378, 398) | **102** | raises the quit-confirm panel / leaves the list; always present at rest in the form phase |
| **EVENT badge** | image | (407, −3) | 210 × 70 | A1 | src (743, 398) | — | decorative badge beside the refresh strip |

> **The single refresh — resolution of the maintainer's duplicate-button question:** there is **exactly
> one** refresh control (action **105**, at (456, −3)) and **exactly one** back/quit control (action **102**,
> at (456, 166)). They are distinct controls at distinct positions; a port must render **one** of each and
> never duplicate them. The visual oracle's single bottom-area refresh = the action-105 strip.

**Pager re-arm geometry** (per repaint): all ten tabs are first reset to the blank face
N (500, 792) / H (500, 810) / P (500, 810); then exactly three receive real pager art — tab 1 →
N (500, 828) / H (500, 846); tab 2 → N (500, 864) / H (605, 985); tab 3 → N (710, 985) / H (815, 985). Only
tabs that map to a **valid page** are made visible (§4.3.3); the remainder stay hidden on the blank face.
Action range **115..124** (page = action − 115). No "N servers available" count text is rendered — the
total count drives arithmetic only (remaining-plate bound, page math, §4.3.3).

---

#### 4.3.5 Record decode and the name / status / population resolvers

**Static-confirmed.** The in-memory list is an array of **8-byte little-endian records**; the record-array
pointer and the record count are **named object fields** of the server-list object (no fixed byte offset
is cited). The page cursor (§4.3.3) selects two records per page.

| offset | size | type | field | notes |
|---|---|---|---|---|
| +0 | 2 | i16 | `ServerId` | 1-based, valid **1..40**; name-lookup key and sentinel test; **== 100** = special display-only sentinel row (lights the three status quads of §4.3.1) |
| +2 | 2 | i16 | `StatusCode` | status caption + selectable gate (§4.3.6) |
| +4 | 2 | i16 | `LoadCount` | population; drives the colour ladder below |
| +6 | 2 | i16 | `OpenTimeFlag` | when `StatusCode == 0`: **nonzero** ⇒ `LoadCount` is a raw threshold count, **zero** ⇒ `LoadCount` is a discrete level (4/3/2); when `StatusCode == 3`: scheduled-open minute component |

> **Field order is binary-pinned: `+4` is the population `LoadCount` (the value compared to 1200 / 800 / 500)
> and `+6` is the validity gate / open-time.** Do **not** swap them.

**Name resolver (binary-confirmed):** `name_id = 5000 + ServerId` (ServerId 1 → msg 5001 … ServerId 40 →
msg 5040), flat, **no** group/channel multiplier. Out-of-range `ServerId` → fallback msg **5901** ("unknown
server #n", formatted with the id). The 5101 / 5201 / 5301 / 5401 / 5421 message blocks are **discarded
cache warm-up** (their lookups are thrown away; only the 5001-block table is indexed) — **drop any "banked"
or 5301-base name resolver**.

**Status-caption resolver:** `caption_id = 4029 + StatusCode` (StatusCode 0..3 → msg 4029..4032), loaded
into a 4-entry lookup at the top of the paint routine.

**Population colour ladder (`StatusCode == 0` only)** — two sub-branches selected by `OpenTimeFlag (+6)`:

*Branch A — `OpenTimeFlag != 0` (raw count, thresholded; tests `> 1200` first, then `<= 800`, then `<= 500`):*

| LoadCount | caption msg id | label colour (ARGB) |
|---|---|---|
| `> 1200` | **6001** | `0xFFFF0000` red |
| `801..1200` | **6002** | `0xFFED6806` orange |
| `501..800` | **6003** | `0xFFFFFF00` yellow |
| `<= 500` | `4029` (status caption reused) | `0xFFB5FF7A` green |

*Branch B — `OpenTimeFlag == 0` (discrete level, exact equality):*

| LoadCount | caption msg id | label colour (ARGB) |
|---|---|---|
| `== 4` | **6001** | `0xFFFF0000` red |
| `== 3` | **6002** | `0xFFED6806` orange |
| `== 2` | **6003** | `0xFFFFFF00` yellow |
| else (incl. 0/1) | `4029` (status caption reused) | `0xFFB5FF7A` green |

**`StatusCode == 3` special caption:** if `LoadCount == 24` → msg **6004** (maintenance / check); otherwise a
formatted open-time line via msg **6005** (a `cur / max`- or `HH:MM`-style string built from
`LoadCount / 10`, `LoadCount % 10`, `OpenTimeFlag / 10`, `OpenTimeFlag % 10`). The colour ladder is not
applied when `StatusCode != 0`. The literal CP949 text of every msg id lives in runtime `msg.xdb`, so the
captions themselves are **live-pending**; the ids, colours, and selection logic are static-confirmed.

---

#### 4.3.6 Selectable gate, default highlight, stable plate order

**Selectable gate (static-confirmed):** the paint routine renders a plate's select button and name label as
**interactive when `StatusCode == 0`** (the open state), regardless of load. The **click-handler commit
guard** additionally requires **`LoadCount < 2400`** before committing a selection. A port must apply
**both**: paint activates on `StatusCode == 0`; the commit gate is `StatusCode == 0 && LoadCount < 2400`.

**Default highlight (static-confirmed):** the painter highlights the plate whose `ServerId` equals the
highlight key `NEW_SERVER_INDEX` (a single integer read from `data/script/uiconfig.lua`). This is **not**
the `Lastserver` registry value (that is written only on commit). The highlight is drawn with the
selection-highlight strip of §4.3.1 (A4 src (700, 18), 46 × 168) behind the matching plate.

**Stable plate order (static-confirmed):** page i shows raw records **[2·page]** and **[2·page + 1]** in
order — the page cursor starts at `16 · page` bytes, consumes **2** records, and plate i reads
`16 · page + 8 · i`. The remaining-record guard `record_count − 2·page` bounds how many of the two slots are
populated (so a final odd record shows only plate 0). The live painter is the **direct-index path** (no
shuffle indirection) with **center-aligned** name labels. A per-render Fisher-Yates permutation exists but
operates on a **separate parallel id-vector** whose only effect is the `Lastserver` value — it does **not**
reorder the visible plates. **Draw plates in raw record order with centered names; do not shuffle the rows.**

---

#### 4.3.7 Sub-state visibility ladder (server-list relevant edges)

**Static-confirmed.** The handshake sub-state machine toggles the backdrops/controls. The server-list
relevant edges:

| Sub-state edge | Visibility changes |
|---|---|
| 33 → 34 | hide the PIN keypad modal; arm the fetch |
| 34 → 35 | **show the server-list content panel**; **hide the login banner**; **show the refresh/quit strip and the EVENT badge** |
| 35 | fetching — progress shown |
| 37 | plates shown — user picks a plate (action 400/401) or pages (action 115..124) |

The **notice panel** (the other `(270, 85)` backdrop) is hidden across sub-states 1 / 2 / 4 / 31 / 34; the
**server-list content panel** is shown only across 35..37. This is the precise mechanism behind §4.3.0: the
two same-rectangle backdrops are never co-visible.

---

#### 4.3.8 Connecting popup (sub-state 39)

**Static-confirmed.** When the join/connect worker is spawned (sub-state 39) a Confirm-style modal is
raised: atlas **A3** (`InventWindow.dds`), panel src **(318, 647)**, dst **(342, 289) 340 × 190**, caption
msg **4023** centered. It has a **single 3-state button** (action **113**, caption baked into the atlas);
clicking it **aborts the join and returns to the server list** (sub-state 34) — it is **not** an OK button.
On a successful handshake the character-list packet tears the login scene down (the popup dies with the
scene). Connect/credential feedback is the separate auto-counting message box (§2.1a).

---

#### 4.3.9 Implementation-readiness note and live-pending list

All geometry, atlas rects, action ids, font slots, colour literals, and record-field offsets in
§4.3.0–§4.3.8 are **static-confirmed** (hard-coded, non-data-driven) and are directly implementable for a
1:1 `ServerSelectSubView` rebuild. The non-negotiable rules a correct port MUST honour: (1) render **one**
of the three near-overlapping backdrops per phase (§4.3.0); (2) build **two** plate slots but show each only
if its record exists (§4.3.2); (3) keep the ten page tabs **hidden** unless they map to a valid page —
never draw the blank (500, 792) crop as a white box (§4.3.3); (4) render **one** refresh (action 105) and
**one** back (action 102), never duplicates (§4.3.4); (5) draw plates in raw record order with centered
names (§4.3.6).

**Live-pending / oracle items** (a concurrent live probe found **no debuggee attached**; LoginWindow is
torn down at Loading — a maintainer-driven re-launch parked at the server-list screen is required to settle
these):

- the **server-record array contents** and the runtime **`StatusCode` integer semantics** (which value the
  live server sends for "사용가능 / available" vs "full") — server-driven, **live-pending**;
- the literal CP949 caption **text** for every msg id (4029..4032, 6001..6005, 5001..5040, 5901) — lives in
  runtime `msg.xdb`, **live-pending**;
- the **atlas pixel content** of the blank tab crop (500, 792) and the plate-face crop (448, 6) / (572, 6)
  (whether the brush calligraphy is baked there) and the centered-single-plate presentation — **texture /
  visual-oracle** items, not debugger-decidable.

The future maintainer-driven session should breakpoint the server-list populate/paint site with the
list received from a replica, read the record-array base + count and walk the 8-byte records to bind each
runtime `StatusCode` integer to its on-screen available/full rendering.

---

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
