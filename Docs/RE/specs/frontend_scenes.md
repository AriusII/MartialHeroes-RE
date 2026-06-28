---
verification: confirmed against IDB SHA 263bd994; reconciled dossier promotion pass 2026-06-22 —
  cross-checked against Login and Loading reconciled dossiers; §1.5 substate ladder and §1.4c no-EULA
  reading confirmed as already correct (binary-agrees). LOGIN-VISUAL PROMOTE 2026-06-22: §1.5a "ID
  textbox snap" CORRECTED to "form decorative plate snap" (binary-won, counter-check IDB SHA 263bd994).
ida_reverified: 2026-06-27   # CYCLE 14 re-anchor (f61f66a9): confirmatory - subsystem cleanly relocated, 1 re-confirmed SAME, 0 corrected; prior 2026-06-22: 2026-06-21 dirty-note promotion confirmed §1/§3/§11.2–§11.5; 2026-06-22 reconciled-dossier cross-check: substate ladder (1=intro+SFX, 2=slide, 3=snap, 4=auto-advance, 5=show-form, 6=form) confirmed; §1.5a form-plate-snap CORRECTED (was "ID textbox snap" — binary-won per counter-check). CYCLE 12 Phase 2 (2026-06-22, IDB 263bd994): §7 enter-world handoff CORRECTED to the server round-trip ladder (1/7 mode-1 play-confirm -> 3/14 flag!=0 -> 1/9 emitted by the 3/14 handler -> 3/5); §8 send-map 1/7 mode bytes corrected (mode 1 = select-and-play, mode 0 = slot-lock; delete mode byte capture-pending); 1/9 self-checksum token clarified (MD5-hex of the client exe). CYCLE 11: added the scene-state-value vs case-index caveat (in-game = value 4 via case-index 5; cross-ref world_systems §13.1).
ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
readiness: IMPLEMENTATION-READY for the C# rebuild (control-flow-confirmed against IDB SHA 263bd994); items explicitly tagged debugger-pending / capture-pending / RD-* are NON-blocking runtime residuals to confirm later.
evidence: [static-ida]
conflicts: KF2..5 camera-keyframe arming (camera dolly) and the digit→slot PIN scramble seed/permutation remain debugger-pending; the EXE-window-close quit edge at Login is debugger-pending; which physical credential box is account vs password (join-string field #1 vs #2) is a static-hypothesis pending a capture; ActorVisualGlobal[3] high-tier threshold and catalog categoryBase[] values are data-pending
status: confirmed
sample_verified: partial   # game.ver version-token sample-verified (login_flow.md); all UI flow/constants are CODE-CONFIRMED; wire bytes capture-unverified
subsystems: [login_scene, server_select, character_select, enter_world, frontend_state_flow]
networked: partial         # the UI flow here is client-local; the wire shapes it triggers are owned by login_flow.md / opcodes.md / packets
encoding_note: All account, server, character-name, dialog and label text is CP949 (legacy MS949 code page), not UTF-8.
---

# Front-End Scenes — Clean-Room Specification

> Neutral, rewritten behavioural specification, promoted from dirty-room analyst notes under
> **EU Software Directive 2009/24/EC Art. 6** (decompilation permitted solely to achieve
> interoperability). It contains **no decompiler output, no pseudo-code, no legacy symbol names,
> and no binary virtual addresses**. Struct field offsets (`+0x..` inside an object/record) and
> file offsets are retained because they are interoperability facts, not code addresses. Every
> behaviour and constant below is re-expressed in this author's own words and tables.
>
> **Scope.** The complete front-end (out-of-world) scene flow at the **UI / control / flow level**:
> the login form, server selection, character selection / creation / deletion / rename, and the
> enter-world handoff. This is the connective tissue between the screens — what each widget does,
> what each local validation rule is, what message-catalogue dialog id each outcome shows, and
> which engine-state transition each step drives.
>
> **Cross-spec ownership — this spec never re-derives wire bytes.** Where a topic already has a
> committed spec, that spec is the authority and this file only summarises and cross-links:
> - **Wire packet shapes & opcodes** (login blob, char list, enter-game, create/select/rename/
>   delete, char-spawn): `opcodes.md` + `packets/*.yaml` + `specs/login_flow.md`. This spec cites
>   opcodes by their `major/minor` and canonical name but **defines none** and **edits neither**.
> - **The lobby mini-protocol** (synchronous port-10000 server-list & channel-endpoint fetch, the
>   8-byte frame wrapper, inbound LZ4, the 8-byte server record decode): `specs/login_flow.md`
>   §2. This spec references it as a black box (start → wait → consume) and does not re-decode it.
> - **The engine/scene state machine** (the **8 explicit scene states 0..7**, where **value 8 is the
>   loop-exit terminal shutdown sentinel — not a switch case**, see `specs/client_workflow.md` §4 — the
>   loop run-flag mechanism, the construct/destroy ledger per transition): `specs/client_runtime.md`
>   §7 + `specs/client_workflow.md` §4. **All scene-state numbers used here (0 Init, 1 Login, 2 Load,
>   3 Opening, 4 Select, 5 In-game, 6 Quit, 7 Error; 8 = exit sentinel) are defined there**; this spec
>   only states which transition each front-end action drives.
> - **The widget tree / atlas layout / fonts / IME charset**: `specs/ui_system.md` §2, §3, §6.
>   This spec adds the **action-dispatch and flow layer** that `ui_system.md` leaves open, and
>   **corrects** two of its sub-state labels (see the conflict note in §1.5 and §6).
> - **Camera for the select preview**: `specs/client_runtime.md` §7.3.
> - **Skin/bind/motion asset chains the preview reuses**: `specs/skinning.md`, `formats/mesh.md`.
> - **Sound ids**: `specs/sound_runtime.md` (referenced by id only).
>
> The message-catalogue (`msg.xdb`) **caption strings** are in the client VFS, are CP949, and are
> **not** reproduced here. Only the numeric message **ids** are recorded — they are
> interoperability facts; the localized text behind them must be supplied from a VFS extract.

---

## Status and verification banner

Per-claim confidence model (used inline below), matching the repo's vocabulary:

- **CODE-CONFIRMED** — read directly from the binary's control flow / immediate operands; the
  default tier for every flow rule, widget action, validation threshold, message id, state
  transition, and constant in this spec.
- **SAMPLE-VERIFIED** — additionally byte-checked against a real shipped sample. Only the
  enter-game version token (computed from the on-disk `data/cursor/game.ver`) reaches this tier
  here; see `specs/login_flow.md` §3.3.
- **CAPTURE-VERIFIED** — confirmed against a network capture. **No capture was available** to any
  source lane; every wire-direction claim below is therefore the cross-referenced static read from
  `login_flow.md`, **not** capture-verified.
- **PLAUSIBLE** — single-source inference; implement but keep tunable / behind a flag. Flagged
  inline and re-listed in **Open questions**.

> **Whole-spec caveat.** The numeric ids, lengths, thresholds, and transitions here are reliable as
> *values present in the legacy code*. The screen flow itself is fully recovered; the **packet bytes
> these screens trigger remain capture-unverified** and are owned by `login_flow.md`.

---

# 1. The login scene

**Engine state: 1 (Login)** — see `client_runtime.md` §7.1. A single window object (the
"login window") owns the login form, server selection, and the channel-resolve handoff, all
in-process. It is built once on entering state 1, from the UI config script
(`data/script/uiconfig.lua`), and runs until it hands off to the game connection or quits.

The login window carries **two independent internal counters** that must not be confused:

- a **UI page index** (which form/option page is visible), and
- a **flow sub-state** (the connect → animate → validate → PIN → server-list → endpoint → submit
  machine). The flow sub-state is the load-bearing one for this spec; its values are catalogued in
  §1.5. (Both are *below* the engine state and are never visible as an engine-state value — a point
  already reconciled in `client_runtime.md` §7.1 and `ui_system.md` §6.) A **third** init/idle value is
  seeded once by the constructor (a page-index / base counter — set to **5** at construction, distinct
  from both the MAIN page state and the TICK drive state; its concrete field offset belongs to the
  login-window struct map, not this behavioural spec).

## 1.0 The opening intro — a standalone scene *before* the login form (CODE-CONFIRMED)

> **Front-end engine-state model: 3 (Opening) → 4 (next front-end form).** The opening crawl is **not
> a phase of the login window.** It is its own dedicated **opening-window scene** that the top-level
> engine-state ladder constructs and runs at **engine state 3 (Opening)**, in its own main loop, and
> **tears down completely before the next interactive form's window is constructed at state 4.** The
> two windows never coexist. (Engine-state numbering is owned by `client_runtime.md` §7; this spec
> only records that the opening occupies state 3 and the interactive form follows at state 4.)
>
> This **supersedes** the older §11.6 reading that folded the opening into login sub-states 1–5 and
> attributed the intro SFX to it. The red-ribbon / banner-pan motion in login sub-states 1–5 (§1.5)
> is a **separate** intro animation belonging to the login window's own curtain; the four
> `openning_001..004` backdrops and the `openning_scenario` crawl are owned **exclusively** by the
> opening-window scene described here, and the opening-window cue is a **different** sound id from the
> login curtain SFX (§1.0.4 vs §1.5 sub-state 1).

> **Engine state-machine context — owned by `client_runtime.md` §7, summarised here (CODE-CONFIRMED).**
> The process entry point mounts the data archive **exactly once**, then runs a single
> `while(1) { switch(GameState) }` engine loop with states **0..8**; each case builds the front-end
> form for its phase. The states relevant to this spec are: **0 = cold bootstrap** (net handler + top
> window + display-mode select); **1 = Login** (the LoginWindow and its font table are constructed here
> — Login is the **state-1 init case**, not state 4); **2 = opening-or-skip gate** (reads
> `[OPENNING]`/`SKIP`, builds the LoadingWindow, routes to 3 or 4 per §1.0.0); **3 = Opening
> cinematic** (the opening-window scene of this section); **4 = character-select form
> specifically** (NOT login); **5 = in-game** (returns to **4**, never to login); **6 = Quit → 8**;
> **7 = net-engine guard → 8**; **8 = Exit**. (Earlier §1.0 wording omitted states **0** and **2** and
> conflated "login/char-select" — corrected here: Login is state 1, char-select is state 4.) Full
> ownership of the engine state ladder and the launcher gate stays in `client_runtime.md` §7; this is a
> cross-reference only.

> **Scene-state vs in-game caveat (CYCLE 11, binary-confirmed).** The app entry is a single `while(1) switch(game-state value)` where each case pre-writes the NEXT value on ENTRY before building its scene. The CHARACTER-SELECT scene is switch case-index 4 and writes value 5 on entry; the WORLD / IN-GAME scene is switch case-index 5 and writes value 4 on entry (the no-network default = return to character-select). So the in-game World scene runs at game-state VALUE 4 reached via CASE-INDEX 5 — do NOT read this document's front-end 'state 4' (the interactive front-end form) as the in-game scene; that 'state 4' is a front-end engine state, a different axis. The authoritative in-game scene-state mapping lives in `specs/world_systems.md §13.1`. An explicit leave-world/logout overrides the default value-4 with the quit-prep value, so a deliberate logout exits the client and never returns to character-select.

### 1.0.0 Launch gate — the opening can be skipped at boot (CODE-CONFIRMED)

Before choosing the entry state, the engine reads an integer option from the client options INI,
section **`[OPENNING]`**, key **`SKIP`**:

- **`SKIP != 0`** → jump straight to engine state **4** (the interactive form); the opening is not run.
- **`SKIP == 0`** → enter engine state **3** and run the opening scene below.

So a returning player who has already watched or skipped the intro never sees it again. The value is
written by the skip action (§1.0.5).

### 1.0.1 What the opening scene composites (CODE-CONFIRMED)

The opening scene draws **two overlaid layers** plus one skip control, all from `data/ui/`:

- a **4-frame cross-fading backdrop** — `openning_001.dds`, `openning_002.dds`, `openning_003.dds`,
  `openning_004.dds`, held in a 4-entry frame array and selected one-at-a-time by the fade phase
  (§1.0.2);
- a **vertical scrolling crawl** — `openning_scenario.dds`, a tall strip drawn as a quad centred
  horizontally (≈ screen-width/2 − 512) and scrolled upward by the scroll machine (§1.0.3);
- one **skip button** (action id **100**) placed near the lower-right of the canvas, sourced from
  `data/ui/mainwindow.dds`.

A per-frame callback drives the scene each frame: it runs the update step (which advances both the
fade machine and the scroll machine), draws the scenario panel (current backdrop frame + alpha +
scroll quad), then the standard view / input / texture-flush passes.

### 1.0.2 Fade machine — the dominant ~70 s dwell (CODE-CONFIRMED)

The backdrop is governed by a 4-phase fade machine, ticked from the per-frame update:

| Quantity | Value |
|---|---|
| Phases | **4** — phase index runs **1 → 2 → 3 → 4** |
| Backdrop per phase | phase **N** shows `openning_00N.dds` (frame array indexed by the phase) |
| Hold per phase | **17 500 ms** (a phase advances when `phase_start + 17 500 ms` is reached) |
| Per-phase alpha ramp | a per-phase alpha counter ramps **0 → 250** to fade the current frame in/out, one step per global pacing pulse |
| **Total dwell** | **4 × 17 500 ms = 70 000 ms = 70.0 s** |

The fade machine reaching the end of phase 4 is what drives the scene toward teardown and the
auto-transition (§1.0.5). The 70.0 s figure is exact (4 × 17.5 s).

### 1.0.3 Scroll crawl — a positional translate; screen-direction must be inverted to read UPWARD (CODE-CONFIRMED rate/bound/mechanism; UPWARD direction PLAUSIBLE / optional-live-confirm)

The `openning_scenario` crawl is an independent machine, also ticked every frame:

- a one-time **1 000 ms** start delay before scrolling begins;
- a **single scroll-position value** advances at **30.0 units per second** (wall-clock based — each
  frame adds `dt_seconds × 30.0`, so it is frame-rate independent; the position **increments**);
- it stops at a bound of **1843.0 units**, where a "scroll complete" flag is set; there is **no wrap**;
- after completion, two held-input events (a back / forward nudge pair) allow manual nudging of the
  scroll position at the same 30 u/s, clamped to roughly **0 … 1843**.

**Mechanism — how the position becomes motion (CODE-CONFIRMED).** The scenario texture is drawn as a
single tall (1024 × 2048) image quad, centred horizontally (left edge at `screen-width/2 − 512`). The
crawl is a **positional translate of that whole quad**: each active frame the scroll-position value is
written directly as the quad's **destination vertical placement** (the destination top-left Y) — it is
**not** a texture-UV / source-rect offset. The only animated quantity is the quad's screen position,
0 → 1843.

**Screen direction — the literal read is +Y-DOWN; it must be inverted to match the official UPWARD
crawl.** The UI sprite-batch this quad draws through uses a **top-left-origin, +Y-down** pixel
convention, so a naive port that copies the literal "position increases" rule translates the quad
**downward**. The official client's crawl reads **upward** (credits-style). A faithful port must drive
the same single monotonic parameter **0 → 1843 at 30 u/s** (1 s delay, latch at 1843, centred X), but
**invert the screen-direction sign** relative to the literal +Y-down reading so the visible texture
content **rises** on screen. Preserve the 30 u/s rate, the 1 s start delay, the 1843 bound, and the
centred X **exactly** — only the on-screen scroll direction must be UPWARD.

Derived run length ≈ **1843 / 30 ≈ 61 s** (plus the 1 s start delay). Because this is shorter than the
70 s fade dwell, the crawl finishes first and idles (or is hand-nudged) until the fade machine ends
the scene. The 30 u/s rate, the 1843 bound, and the positional-translate mechanism are **CODE-CONFIRMED**;
the human-readable seconds are arithmetic over a wall-clock timer (derived). The **UPWARD on-screen
direction is PLAUSIBLE** (observed-ground-truth-driven; the binary literal describes a +Y-down downward
translate) — one optional live-debugger read of the sprite's on-screen Y over a few seconds would
promote it to CODE-CONFIRMED (non-blocking).

### 1.0.4 Audio cue (CODE-CONFIRMED)

The opening scene starts **exactly one sound** at build time: a **looped 2D cue, id 910061000**, loaded
from `data/sound/2d/`. Being looped it doubles as the opening BGM; no separate streamed-music start
exists in the scene. This id is **distinct** from the login-curtain intro SFX **861010105** (§1.5
sub-state 1), which belongs to the separate login window — there is no evidence the opening fires
861010105.

### 1.0.5 Transition to the next form — auto-after-dwell or skip-on-input (CODE-CONFIRMED skip; auto-edge PLAUSIBLE)

- **Auto (completion).** The scene runs its own engine main loop; the loop ends when the engine
  run-flag is cleared, after which the engine tears down the opening window and advances engine
  state **3 → 4** (the interactive form). The fade machine completing phase 4 (after the ~70 s dwell)
  drives this teardown — so the **default behaviour is automatic transition after ~70 s.** The
  per-frame update is a **two-stage auto-exit**: an **exit-arm flag** (scene object +0x2C9) must be
  set, then a **final-fade alpha** counter (+0x2C8) ramps to **250**, after which the engine run-flag
  is cleared and the loop ends (state 3 → 4) — **the run-flag clear is CODE-CONFIRMED.** The 4-phase
  70 s fade machine itself **never** sets the exit-arm flag, so the **arming write comes from an
  indirect scene/scroll completion callback** static analysis cannot observe; that arming edge stays
  **PLAUSIBLE** (Open question 14). (The run-flag is also reachable event-driven via the generic
  scene event handler.)
- **Skip-on-input (CODE-CONFIRMED).** The opening's input handler short-circuits the dwell:

  | Input | Condition | Action |
  |---|---|---|
  | Keyboard | key code **10 (Enter) / 27 (ESC) / 32 (Space)** | persist skip + stop scene |
  | Mouse | left-click on the **skip button** (action id **100**) | persist skip + stop scene |
  | Scroll / arrow | — | manual nudge of the crawl position (§1.0.3) |

  The "persist skip + stop" action writes **`[OPENNING] SKIP = 1`** into the client options INI, sets
  an internal skip latch, and issues stop calls on the scroll/scene objects to end playback. So
  pressing **Enter / ESC / Space**, or clicking the skip button, **both immediately ends the opening
  and records SKIP = 1** so the intro is bypassed on the next launch (§1.0.0).

### 1.0.6 Crawl content is baked art — no message table (CODE-CONFIRMED)

The opening draws **only pre-rendered images**: the four cross-fade backdrops selected by the fade
phase, and the single scrolling texture moved by the scroll position. There is **no message-catalogue
lookup, no per-line text fetch, no message-id indexing, and no font-render loop** anywhere in the
scene's build / update / draw functions. Any "scrolling text" the player sees is **baked into
`openning_scenario.dds`** itself. A faithful rebuild must therefore render the crawl as image art and
must **not** wire it to `msg.xdb`. (A dynamic text crawl would be a new addition, not a fidelity match.)

> **Godot-fidelity contract.** Reproduce the opening as a standalone scene that runs before the
> login form: four full-screen backdrops cross-fading at **17.5 s each (70 s total, phase N →
> `openning_00N`)**, a vertical scroll of `openning_scenario.dds` at **30 u/s to bound 1843 (~61 s)**,
> a single looped 2D BGM **910061000**, a skip on **Enter / ESC / Space / skip-button (action 100)**,
> and a persisted "seen intro" flag (`[OPENNING] SKIP=1`) that is checked at boot to bypass the intro.

## 1.1 Widget action dispatch — how a click reaches behaviour (CODE-CONFIRMED)

Each interactive widget is registered with a small **numeric action id** when it is parented into
the window. On a click that lands on an enabled widget and is released inside its bounds, the
window's command handler walks the active panel's child list, finds the hit child, and reads its
action id into a window-local field. The window's input handler then dispatches on that id.

A load-bearing legacy quirk: **the action id is the ASCII code of a letter**, and the input handler
switches on it *as a character*. The numbers below therefore look like ASCII codes; a fresh
implementation may use any enum and need not preserve the literal values. They are listed so an
engineer can map the legacy `uiconfig.lua` ids to behaviour.

The input handler also classifies events by a leading **event-class byte**:

| Event class | Meaning |
|---|---|
| 1 | keyboard / system action (id 9 = swap focused textbox; id 10 = "confirm/advance", i.e. Enter) |
| 6 | widget click-release (dispatch on the action-id character, per §1.2) |
| 7 | list pick (server-name strip selection) |
| 13 | engine event id `10001` = loading-complete signal → break the engine main loop |

## 1.2 Login widget → action table (CODE-CONFIRMED)

Widget geometry and atlas sources are owned by `ui_system.md`; reproduced minimally here only where
needed to identify a control. "Action id" is the legacy numeric id (= ASCII char shown in
parentheses). Decorative widgets (labels, banner slices, option backdrops) carry no action.

| Widget (role) | Action id | Behaviour on activate |
|---|---|---|
| **ID / account textbox** | input only | editable; first credential token (see §1.3) |
| **Password textbox** | input only | editable, masked; second credential token (see §1.3) |
| **OK / Login button** | 103 (`g`) | run the `game.ver` version gate (§1.4), then advance flow sub-state to **29** (credential validation). **Sends no packet itself.** |
| **Server-list button** | 102 (`f`) | reveal the server-list panel |
| **Server-list scroll-up arrow** | 106 (`j`) | scroll the server listbox up (server-listbox container control, §11.2a). *Not* an EULA control — the earlier "EULA scroll/accept" reading is dropped (§1.4c) |
| **Server-list scroll-down arrow** | 107 (`k`) | scroll the server listbox down (§11.2a) |
| **Server-list scrollbar thumb** | 108 (`l`) | the scrollbar thumb / drag dot of the server listbox (§11.2a) |
| **Save-ID checkbox** | 104 (`h`) | toggle; persist/clear the saved id (§1.6) |
| **Focus ID box** | 109 (`m`) | focus the ID box, clear PW focus (mutually exclusive) |
| **Focus PW box** | 110 (`n`) | focus the PW box, clear ID focus |
| **Quit-confirm "Yes" #1** | 113 (`q`) | hide quit popup; (re)start the server-list path (advance to sub-state 34) |
| **Quit-confirm "Yes" #2** | 114 (`r`) | hide quit popup; same as above |
| **Help button** | 105 (`i`) | throttled (~10 s) server-list re-fetch path (advance to sub-state 34) |
| **Option strip tab 1** | 111 (`o`) | option-page select |
| **Option strip tab 2** | 112 (`p`) | option-page select / sub-panel toggle |
| ~~**Server name-strip buttons ×5** (115..119)~~ | — | **SUPERSEDED** — there are no 5 server name-strip selectors. In the login window, **115..124 are the server-list PAGER buttons** (active only at sub-state 37; page = action − 115; they re-paint the 2-plate view and never carry a server record or commit a selection — §11.4). The actual server selectors are the **two parchment plates, actions 400/401** (§2.4 / §11.4). |
| ~~**Help-strip range** (115..124)~~ | — | **SUPERSEDED** — not a help strip; these are the server-list pager buttons (see the superseded row above and §11.4). |
| **Server plates ×2** | 400 / 401 | **the two selectable SERVERS** (1 plate = 1 server, max 2 per page); active only when the server list is shown (sub-state 37). Action **400 = LEFT** = record `2·page`; **401 = RIGHT** = record `2·page + 1`. Commits the selection (§2.4 / §2.5 / §11.4). |

Keyboard/system class (event-class byte = 1): id **9** swaps the focused textbox (ID ↔ PW); id
**10** on the login form page runs the same logic as the OK button (version gate → sub-state 29),
i.e. **Enter = Login**; id **10** on the option page advances the option page. **The action ids
`106` / `107` / `108` are the server-listbox scroll controls** (scroll-up arrow / scroll-down arrow /
scrollbar thumb dot built into the server-listbox container — §11.2a), **not** intro-banner no-ops
and **not** EULA scroll/accept controls (the earlier "EULA / intro-banner" readings are both
superseded — §1.4c, construct walk).

## 1.2a Resting login chrome — nothing top-level paints in the bottom-left quadrant (CODE-CONFIRMED build-time visibility; absolute pixel attribution DEBUGGER-PENDING)


The login window's top-level child panels were mapped back to their construction sites to settle the
earlier UNVERIFIED question of what (if anything) renders in the **bottom-left** of the resting login
form. At the **resting login-form state** (tick/drive substate **6**):

- The **option-page panel** (destination `(356, 531, 313, 132)`, atlas `loginwindow.dds`, host of the
  option tabs at actions **111 / 112**) is **HIDDEN at rest** — the login code only ever calls
  *SetVisible(0)* on it (during the intro transitions); nothing shows it at rest. The option tab
  coordinates `(40,82)` / `(164,82)` are **relative to this hidden panel**, not absolute bottom-left
  screen positions.
- The **server-name-strip / banner container** and the **PIN modal** are also hidden at rest.
- The resting-visible top-level panels are the **central login-form container** (≈ `265,113`) and a
  **full-width top panel** (`y = 0`).

**Resolution (CODE-CONFIRMED).** **No top-level child whose destination rect lands in the bottom-left
quadrant is shown at rest** — the bottom-left element is a **NON-resting / opt-in element** (the
option page and/or the server-list overlay) that must **NOT** render on the base login form. A
faithful base-login render shows only the central form container + the top panel; the option page and
the server list appear only when their actions / sub-states arm them. The **absolute pixel attribution
of the runtime Draw walk** (exactly which widget paints in that quadrant once an opt-in element is
armed) stays **DEBUGGER-PENDING**, but the build-time visibility proves nothing resting-visible lands
bottom-left.

## 1.3 The ID and password editboxes (CODE-CONFIRMED)

Two text-entry boxes, both routed through the Korean IME composition (CP949):

| Box | IME composition slot | Max length | Render | Validation field |
|---|---|---|---|---|
| ID / account | 16 | **16** characters | plain | length read for the ID-length rule |
| Password | 12 | **12** characters | masked | length read for the empty-password rule |

- Focus is **mutually exclusive**: focusing one clears the other (actions `m`/`n`, or keyboard
  id 9). Korean composition routes to whichever box holds focus.
- The ID box max length is **16** and the PW box max length is **12** (CORRECTED, static IDA,
  2026-06-21 — GAP-4 resolved). Both are read from the per-textbox max-length field set at login-window
  construction (the GUTextbox length cap), honoured live by the per-keystroke and paste input handlers.
  **The earlier "6 / 129" reading is dropped:** the ID "6" was the textbox's **character-filter / charset
  mask** misread as a length, and the PW "129" was likewise not the length field. They are recorded as the
  original caps; **a revival may relax both** (validation only requires ID length ≥ 4 and password length
  ≥ 1, §1.4). The 20-byte account hand-off buffer at credential assembly (§2.6 of `frontend_layout_tables.md`)
  is a **separate, looser** downstream copy bound, not the input cap — since the field already caps at 16
  (< 20), that buffer never binds.
- On scene build, if a saved id is present (§1.6) and is not the literal `"(null)"`, the ID box is
  pre-filled with it (the Save-ID round-trip).

> **Wire-side capacity note (cross-reference, owned by `login_flow.md` §4.2).** These caps are the
> *UI editbox* limits, not the protocol limits. The login blob's runtime-confirmed capacities are
> account **< 20**, password **< 17** (staged in an exactly-17-byte zero-padded buffer), and the
> second-password / PIN **< 5** (≤ 4 chars). They live in `login_flow.md`; do not re-derive them
> here.

## 1.4 The Login click — local version gate then validation (CODE-CONFIRMED)

The OK/Login button (and the Enter key on the form page) run this exact sequence. **None of it is a
network send to the game server**; the only sends in the whole login scene are the lobby fetches and
the post-handoff handshake (§1.7).

1. **Version gate (local, runs first) — a client-up-to-date GATE, not a displayed label.** If the VFS
   is mounted, the client compares the version file inside the VFS (`data/cursor/game.ver`) against the
   external client-root on-disk `game.ver`. This is a **gate on the login action only**; the scene draws
   **no on-screen version / build text** (there is no version-label widget — §1.4b).
   - **File format & compare granularity (CODE-CONFIRMED gate; other-field semantics owned elsewhere).**
     `game.ver` is a **binary 28-byte** blob = **7 × u32 little-endian** (no ASCII string, no NUL
     terminator, no magic). The gate **requires the file to hold ≥ 7 u32s** (it loads the full 28-byte /
     7×u32 blob) but **does NOT compare the whole blob**: it extracts **only u32 INDEX 5 (file byte
     offset 20)** from each copy and runs a **single integer equality** `client[5] == vfs[5]`. A
     mismatch on that one field fails the gate; the other six u32s are not compared. **This REFUTES the
     earlier "full 28-byte blob compare" model and resolves the prior "compare granularity UNVERIFIED"
     item: the granularity is a single u32 field (index 5 / byte offset 20), CODE-CONFIRMED.** The
     field semantics of the other six u32s (which is build vs patch vs revision) remain owned by
     `formats/game_ver.md`. On mismatch the gate raises the msg-2204 error box and quits; the substate
     never advances to 29.
   - On **mismatch**: a Win32 modal error box is shown using message id **2204**; pressing OK runs
     the quit-from-load path (plays SFX **861010106**, writes engine state **6 / substate 2**, i.e.
     quits the client). See §1.8.
   - On **match** (or if the VFS is not mounted): continue.
2. **Persist Save-ID** if the checkbox is set (§1.6).
3. Advance the flow sub-state to **29**.
4. **Next tick, sub-state 29 = local credential validation:**
   - If **ID length < 4** → show the in-window timed popup with message id **4025**, return to
     sub-state 6 (stay on the form). **No network send.**
   - Else if **password length < 1** (empty) → show message id **4026**, return to sub-state 6.
     **No network send.**
   - Else → persist Save-ID again, advance the drive sequence toward the server-list / channel-endpoint fetch chain (§1.5). MAIN substate **6** is the resting login form (there is **no** EULA panel — §1.4c), and the PIN child panel is raised on the **TICK workflow substate 31 → 32 edge** after the credential gate (§1.4a / §1.5 rows 31/32).
5. The server-list → channel-endpoint → submit chain then runs along the **tick/drive substate**
   (advancing through {34..41}, §1.5). The actual account-login wire send and the cryptographic reply
   happen only at the **tail** of that chain (drive sub-state 40), on the main game connection — owned
   by `login_flow.md`. The **second-password / PIN** child panel (§1.4a) is collected as part of this
   tail, before the login blob is built.

The version check is **inline in the Login handler only** — there is no separate startup version
popup, and no background patch dialog (patching is the external launcher's job; see
`client_runtime.md` §6 on the `-Start`/launcher gate).

## 1.4a The second-password / PIN modal (RUNTIME-CONFIRMED via `login_flow.md`)

After the primary account + password submit succeeds the credential validation (§1.4, sub-state 29),
and **before** the account-login blob is built and sent (sub-state 40), the client raises a dedicated
**second-password / PIN** modal — the legacy "secondary password" dialog. It collects a short,
typically numeric, PIN.

- **What it is.** A first-class third login input, separate from the account id and the account
  password. The client models the PIN as its own input concept (an input/name object carries an
  explicit "is-PIN" flag distinguishing it from an ordinary text field), which is why it is a
  distinct modal rather than a third box on the main form.
- **Where it sits in the flow.** It is shown **after** the primary login submit and **before** the
  credential-submit / handshake join point — i.e. between §1.4 (validation passes) and the §1.5
  sub-state-40 join handoff. Conceptually it is the same boundary `login_flow.md` §1 step 1a places
  the PIN at.
- **Capacity.** The PIN is **≤ 4 characters** (the login blob validates it as length `< 5`, i.e.
  ≤ 4 chars plus a NUL — `login_flow.md` §4.2 / §7). It is numeric in practice.
- **Where its value goes.** The PIN's value becomes the **optional length-prefixed field of the login
  credential blob** — the `[0x2B][u32len account\0]([u32len PIN\0])` structure that is the **RSA
  pre-image of the `1/4`** secure auth reply (§1.7 / §4.5; **`1/6` is character-create only**, not the
  login send). This is the field a prior reading called the "optional auxiliary string"; a runtime
  read of the live client identifies it as the PIN / second-password. **The PIN is *not* the account
  password** — the account password rides inside the same `1/4` RSA ciphertext separately and the PIN
  is the optional trailing field of the blob. The byte layout, the u32-LE NUL-inclusive length prefix, and the field capacities are all
  owned by `login_flow.md` §4.2; this spec owns only the UI modal and its place in the flow.
- **Asset.** The dialog uses the secondary-password art catalogued in `formats/ui_manifests.md`
  (`data/ui/password.dds`, 1024×1024 DXT3 — listed there as "Secondary password dialog"). The
  caption strings are CP949 in the VFS and are not reproduced here.
- **Show-trigger — the TICK workflow substate 31 → 32 edge (CODE-CONFIRMED, CONFLICT C1 resolved).**
  The PIN keypad child window is constructed during the login-scene build (its own 2-atlas texture
  list, separate from the main login form's). Its visibility is set on the **TICK / drive workflow
  substate 31 → 32 edge**, reachable **only after the substate-29 credential gate** (account-id length
  ≥ 4, password present, + an account/save flag). **31 = raise the PIN modal; 32 = poll the PIN modal
  `(visible && submitted)` → 33** (§1.5 rows 31/32). **This CORRECTS the earlier "NOT a numbered
  sub-state" wording**: the *FORM/PAGE* (MAIN) state indeed never holds 31/32 (that is the field the
  campaign-4 reading examined), but the **TICK workflow state DOES** — two independent witnesses agree
  (this re-walk + `ui_system.md` §11.3). The PIN panel is routed **solely** by the keypad
  builder's **own internal tags `{11 = Reset / reshuffle, 12 = OK / submit, 13 = Cancel}`** (plus
  0-9 -> append): the panel's own dispatcher has **no handling of any window-level action**. **The
  LoginWindow actions `111 / 112` are the login form's option/tab buttons — they do NOT route the PIN
  child panel** (an earlier version of this spec wrongly cross-bound 111 = confirm / 112 = cancel to the
  PIN panel and called "both scopes real"; the binary does not support that cross-binding — CODE-
  CONFIRMED, independent two-witness). The entered PIN rides as **field #3 of the credential pre-image**
  built at the join handoff (byte layout owned by `login_flow.md`).
- **Digit→slot scramble — a clock-seeded shuffle (MECHANISM CODE-CONFIRMED; seed + permutation
  DEBUGGER-PENDING).**
  The keypad is **not** a static table and **not** a constant-seeded shuffle. The routine **seeds the
  C runtime RNG from the system clock**, then runs a textbook **Fisher–Yates** shuffle of the 10-digit
  pool using the standard MSVC **15-bit-RNG range-extension** (`(prev << 15) | (next & 0x7FFF)`), so
  tile position `p` shows `perm[p]`. It **re-rolls on open, on Reset (tag 11), and after OK-submit
  (tag 12)**. The **MECHANISM is now CODE-CONFIRMED**; only the **runtime seed value and the resulting
  permutation** remain **DEBUGGER-PENDING** (clock-seeded — not a code immediate; do not invent them).
- **Keypad layout literals (CODE-CONFIRMED).**
  Tiles are **52 × 52** at **X = 55·(p % 5) + 28** (X ∈ {28, 83, 138, 193, 248}), **Y = 170** (top
  row) / **Y = 230** (bottom row). **Reset (tag 11) @ `(243, 133, 58, 30)`; OK (tag 12) @
  `(90, 290, 154, 58)`; Cancel (tag 13) @ `(90, 350, 154, 58)`.** Dragon frame quad `(318, 647, 340,
  190)`. **PIN cap = 4** digits. (Full tile-glyph source rects are in §11.3.)
- **is-PIN modelling (CODE-CONFIRMED).** The "this is a secret input" flag is a **masked-secret bit
  on the textbox flag word** (renders the content as `*`), and the PIN is additionally its own
  **dedicated second-password widget class**. The value lands as **field #3 of the `1/4` credential
  pre-image** (byte layout owned by `login_flow.md`). **CAUTION:** the IDB's `DName::isPin*` /
  `setIsPin*` symbols are **MSVC C++ name-demangler runtime** (`__based`/`__pin` pointer modelling),
  **unrelated** to the login PIN — do **not** propagate them as a login concept.

> **Confidence.** The PIN's existence, its first-class "is-PIN" input modelling, its ≤ 4-char
> capacity, and the fact that its value lands in the optional login-blob field are **RUNTIME-
> CONFIRMED** against the live client (read from the client's process at login time; no addresses). The
> panel's **structure** — shown on the **TICK workflow substate 31 → 32 edge** (31 = raise, 32 =
> poll-to-33), routed by the keypad's own internal tags **{11 = Reset, 12 = OK, 13 = Cancel}** (the
> window-level actions **111/112** are the login option tabs, **not** PIN routing), the
> clock-seeded Fisher–Yates scramble **mechanism**, and the keypad layout literals — is **CODE-
> CONFIRMED** (two witnesses: this re-walk + `ui_system.md` §11.3). The **digit→slot scramble seed
> value + the resulting permutation** are **DEBUGGER-PENDING** (clock-seeded, not code immediates), as
> is the exact **account/save flag** that gates entry into substate 31.

## 1.4b The version check is a GATE, not a label; 3-state button source mapping (CODE-CONFIRMED)

Two presentation facts that affect a faithful rebuild of the login form:

- **No on-screen version text.** The version check (§1.4) is a **client-up-to-date GATE on the login
  action** — it does **not** draw a version / build label anywhere on the login screen. A reimplementation
  must **not** add a visible version string to the login scene; the only version artefact is the binary
  `game.ver` consulted at OK-click time, and a mismatch surfaces only as the msg-2204 error modal.
- **3-state button source-rect mapping is INVERTED vs the obvious call order (CODE-CONFIRMED).** The
  generic 3-state button builder stores the **HOVER** and **PRESSED** source sub-rects in two different
  widget fields, and the naive builder call-order maps them the **opposite** way to the obvious labelling:
  the **2nd source (srcX,srcY) pair supplied is the PRESSED frame**, and the **3rd source pair is the
  HOVER frame**. (So the order is NORMAL, then PRESSED, then HOVER — not NORMAL/HOVER/PRESSED.) This is
  **moot for every login button** because their hover and pressed art are identical, but a rebuild that
  ever uses **distinct** hover-vs-pressed art must apply this corrected mapping or the two states will
  render swapped.

## 1.4c There is NO EULA / terms panel — the 4001..4022 labels are a static stacked notice/agreement text column (CODE-CONFIRMED, SUPERSEDES the earlier "EULA EXISTS" reading)

> **CORRECTION (CODE-CONFIRMED, element-by-element construct walk).** An earlier draft of this section
> asserted the login window "constructs a full EULA / terms panel" at MAIN substate 6, built from
> message ids **4001..4022**. **A full walk of the login window's scene builder (every one of its 73
> widgets, in build order) refutes that reading: no terms/agreement panel is constructed anywhere in
> the login build.** The auto-labelling that produced "EULA body labels" was a decompiler heuristic on
> a label-build loop; the loop's 22 labels are in fact a **static stacked notice/agreement text column**
> on the login notice panel (a tall left-aligned paragraph block, NOT one-line server rows), and have
> **no scroll/accept/agree gating**. The **actual** server rows are a separate 13-button loop
> (action `115 + i`, runtime-filled text, no msg id — §11.2a). There is no EULA in the login flow.

What the construct walk actually builds in this area, and what each former "EULA" claim really is:

- **The 22 labels (message ids `4001..4022`) are a static stacked notice/agreement text COLUMN on the
  login notice panel, not server-list row captions and not EULA body text.** They are built in a loop of
  22 iterations (caption id = `4001 + i`, `i = 0..21`); each label seat is `(X = 50, Y = 100 + 18·i,
  W = 383, H = 50)`. Because each label is 50 px tall but the vertical stride is only 18 px, the labels
  **overlap vertically** to form a single tall left-aligned multi-line paragraph block — that geometry is
  decisive: it is a paragraph column, not a list of one-line rows. (The **actual** server rows are the
  separate 13-button loop at action `115 + i`, runtime-filled, with no msg id.) The CP949 caption text
  lives in `msg.xdb` and is not reproduced here. This matches `ui_system.md §10` ("static label
  captions"), which is the correct reading.
- **Actions `106 / 107 / 108` are the server-listbox SCROLL controls, not EULA scroll/accept.**
  `106 = list scroll-UP arrow`, `107 = list scroll-DOWN arrow`, `108 = scrollbar thumb dot` — three
  small atlas-B button sprites built into the server-listbox container (rects in §11.2a / §11.4). They
  scroll the server list; they do **not** scroll or accept any terms panel.
- **The only modal panels the login build constructs** are: two shared notice/error dialogs (bodies
  `4023 / 4024`, OK actions `113 / 114`), the **quit-confirm `ExitPanel`** and a generic **ErrorPanel**
  (both added last, topmost, on the shared dragon-frame quad), the **PIN / second-password modal**
  (§1.4a), and a small **option sub-panel** (actions `111 / 112`). **None of these is an EULA panel.**
- **MAIN substate 6 is simply the resting login form** — not an "EULA-read" state. A faithful client
  must **not** build a terms/agreement panel into the login scene, and must **not** gate the form on
  one. The campaign-4 "NO EULA" reading is therefore **vindicated**; the intervening "EULA EXISTS"
  reading was an auto-label artefact and is dropped here.

## 1.5 The login flow sub-state machine — ONE `+0x238` field, three writer classes (CODE-CONFIRMED — corrected CAMPAIGN 16)

> **CORRECTION (CAMPAIGN 16) — there is ONE physical login sub-state field, not two.** A zero-trust
> re-confrontation proved the login window drives a **single** sub-state value at object offset
> **`+0x238`**. (The same cell is addressed as `+0x17C` from code that holds the embedded
> CommonLoginWindow sub-object pointer at base `+0xBC`: `0xBC + 0x17C = 0x238` — proven where the
> action router recovers the base by subtracting `0xBC` from the held sub-object pointer before writing
> `Lastserver`.) The earlier
> "two distinct fields (MAIN form/page vs TICK drive)" model is **drift**, and its clean "written only
> by X" partition is false. One field, **three writer classes**, all writing `+0x238`:
> 1. **per-frame tick** — the primary driver; advances the full range **1..41** (seeded to 1 by the
>    constructor) as the intro/curtain/drive/fetch sequence plays (it also writes 4/6/34/35/37/38);
> 2. **input/action (click) router** — writes **{29, 34, 38}** (OK/Enter credential-validate, reveal
>    server-list, plate-pick commit) plus the computed second-password value, and **reads** the field
>    as an input gate (e.g. the plate-pick only fires when the field equals 37);
> 3. **the two lobby fetch worker threads** — deposit completion sentinels (**35, 39**) for the tick.
>
> So the value sets the old model split across "MAIN {4,5,6,29,34,35,37,38}" and "TICK 1..41" are all
> values of the **one** `+0x238` field; the PIN values **31/32** (raise/poll) and server-list **37** are
> real values of it too. The concrete offset belongs to the login-window struct map; the behaviour
> lives here. The table below enumerates the field's full 1..41 progression. It **supersedes two
> CODE-CONFIRMED-but-wrong labels in `ui_system.md` §6.3** (sub-state 29 and 31) — see the conflict note
> at the end of this section and in §6. (The "(MAIN substate)"/"(TICK substate)" parentheticals in the
> rows below are retained only as a *which-writer-touched-it* hint, not as two physical fields.)

| Sub-state | Meaning | Notes |
|---|---|---|
| 1 | **Intro start** — seed the curtain/letterbox animation; play login-enter SFX **861010105** | scene entry; the field's initial value. **861010105 is a sound id, not a VFX id**, fired from the tick at the 1→2 edge |
| 2 | Curtain / letterbox **opening** animation; reset banner Y | the "carved-stone-window / red-ribbon" intro motion is a **widget-reposition / letterbox-curtain animation** (two banner/curtain widgets advance per frame), **NOT** a spawned particle effect |
| 3, 4, 5 | Intro reposition → settle → **reveal** the login-form widgets | banner pan; also the option-page select target. State 5 stops the intro anim and shows the ID/PW boxes + buttons |
| 6 | **Login form active** — the resting state, waiting for user input | **NO EULA panel exists** (§1.4c, SUPERSEDES the earlier "EULA-read" reading). The construct walk builds no terms/agreement panel; the labels once read as "EULA body" (ids **4001..4022**) are a **static stacked notice/agreement text column** on the login notice panel (not server-list row captions), and actions **106/107/108** are the server-list **scroll** controls (up/down/thumb), not EULA scroll/accept. See §1.4c / §11.2a |
| **29** | **OK-button credential validation** (MAIN substate) | game.ver gate verified (mismatch → msg 2204 abort, §1.4); ID len ≥ 4 (else raise the ID-too-short notice → 6); PW len ≥ 1 (else raise the empty-password notice → 6); persist Save-ID; then advance the drive sequence toward the server/channel fetch chain (34..41). (The notices are cached-notice sourced; their literal msg ids at the call site are UNCONFIRMED.) **(corrects `ui_system.md`: NOT "server-list trigger"; substate 6 is the resting form — there is NO EULA panel, §1.4c — and the PIN is a post-submit child panel raised on the TICK 31→32 edge, NOT a single "31" step)** |
| ~~30~~ | **DEAD / UNREACHABLE in this build** | A `substate 30 → engine 6/8` (quit) branch is *consumed* by the tick, but **nothing in the whole binary ever writes value 30** (CODE-CONFIRMED exhaustive writer scan), so the branch is unreachable here. The genuine login quit-confirm is the shared ExitPanel, whose Yes is **inert at Login** (no GameState-1 case) — §1.8.  |
| **31** | **Raise the PIN modal** (TICK/drive substate) | On the **31 → 32 edge** the second-password / PIN child panel's visibility is set, reachable **only after the substate-29 credential gate** (account-id length ≥ 4, password present, + an account/save flag). **CORRECTS the earlier "NOT a login sub-state" wording**: the *FORM/PAGE* (MAIN) state never holds 31/32 (the campaign-4 reading was about the form/page state), but the **TICK workflow substate DOES** hold 31 and 32.  |
| **32** | **Poll the PIN modal → 33** (TICK/drive substate) | Substate 32 polls `(panel visible && submitted)` and advances to **33** on submit. The keypad is routed **only** by its own internal tags `{11 = Reset, 12 = OK / submit, 13 = Cancel}`; **the window-level actions 111/112 are the login option tabs and do NOT route the PIN panel** (the earlier "both scopes are real" 111/112->PIN cross-binding is dropped — the binary does not support it). CODE-CONFIRMED (independent two-witness: this re-walk + `ui_system.md` §11.3). |
| 33 | Press-OK transition → begin server-list fetch | sets up the fetch (advance to 34) |
| 34 | Start the server-list fetch thread (lobby port 10000) | a **blocking worker thread** separate from the main overlapped connection; see `login_flow.md` §2.1 |
| 35 | Wait for server-list reply | thread sets 36 on completion (or signals an error count) |
| 36 | Consume server list (§2.3 / §2.4) | empty → raise the empty-server-list notice → 6; connect-fail → raise the connect-fail notice → 6; else render and go 37. (The notices are cached-notice sourced; their literal msg ids at the call site are UNCONFIRMED.) |
| 37 | Server selected | entry click commits the selection + persists `Lastserver` (§2.5) |
| 38 | Start the channel-endpoint fetch thread (lobby port `10000 + selected id`) | second blocking worker thread; see `login_flow.md` §2.2 |
| 39 | Wait for endpoint reply | thread copies a 30-byte `"host port"` string into the window, sets 40 |
| 40 | **Join handoff** | collect the second-password / PIN (§1.4a) if not already entered; build the TAB credential string, rebuild the secure context, start the overlapped net engine, set engine state **7** as a guard, **queue transition effect 10001 scheduled 30000 ms ahead**; advance to 41. The login window then exits. |
| 41 | Transition complete; login window exits → loading | — |

> **✅ RESOLVED (rows 31/32) — the PIN IS the TICK substate 31→32 edge; there is NO EULA panel
> (CODE-CONFIRMED, CONFLICT C1).**
> A fresh independent re-walk resolves the long-standing 31/32 dispute: **31 and 32 are real values of
> the single `+0x238` sub-state field** (§1.5 headline, corrected CAMPAIGN 16) — they are the PIN
> raise/poll values, NOT a separate "page" state and NOT "31 = Help screen". Concretely:
> - **No EULA panel (SUPERSEDES the earlier "EULA at MAIN substate 6" reading).** A full element-by-
>   element walk of the login scene builder constructs **no terms/agreement panel**. The 22 labels once
>   read as "EULA body" (message ids **4001..4022**) are a **static stacked notice/agreement text
>   column** on the login notice panel (not server-list row captions), and actions **106/107/108** are
>   the server-list **scroll**
>   controls (scroll-up / scroll-down / scrollbar-thumb), not EULA scroll/accept. The campaign-4
>   "NO EULA" reading is **CONFIRMED**; MAIN substate 6 is the plain resting login form (§1.4c).
> - **Second-password / PIN panel** — driven by the **TICK workflow substate**: **31 = raise the PIN
>   modal** (the child panel is shown on the **31 → 32 edge**, after the substate-29 credential gate),
>   **32 = poll the PIN modal `(visible && submitted)` → 33**. The keypad is routed **only** by its own
>   internal tags `{11 = Reset, 12 = OK / submit, 13 = Cancel}`; **the window-level actions 111/112 are
>   the login option tabs, NOT PIN routing** (the earlier 111->confirm / 112->cancel cross-binding is
>   dropped — the binary does not support it). Its value rides as field #3 of the credential blob built
>   at the join handoff. (Independent two-witness: this re-walk + `ui_system.md` §11.3.)
>
> A faithful client builds **no EULA panel**, and drives the PIN modal off the
> **TICK substate 31→32 edge** (raise at 31, poll-to-33 at 32), routed by the keypad's own {11/12/13}
> tags (the window-level {111/112} actions are the login option tabs, not PIN routing).

> **Sub-state 40 detail.** The TAB string is `<first credential box>⟨TAB⟩<second credential box>⟨TAB⟩
> PIN⟨TAB⟩"host port"`. The first two fields are read **from the two credential edit boxes** (the
> handler reads the two box fields directly), so the field identity is **box-keyed, not label-keyed**:
> field #1 = the first credential box, field #2 = the second. The conventional reading is
> account-first / password-second, but **which physical box is account vs password is a
> static-hypothesis** (the binary names neither box) pending a capture — keep the order pinned to the
> boxes. The third field carries the **second-password / PIN** (§1.4a) and the 4th field is the
> channel endpoint text obtained by the channel-endpoint fetch (§2.6). The capacity bounds the
> secure-context builder applies are account `< 20`, password `< 17`, PIN `< 5` (owned by
> `login_flow.md` §4.2). The secure-context rebuild
> stages the credential session on the main (overlapped) game connection; from there the **login
> handshake `0/0 → 1/4`** proceeds (inbound `0/0` key exchange → reactive `1/4` credential reply;
> **`1/6` is character-create only, not part of the login handshake** — §1.7 / §4.5). The byte layout
> of all of those is owned by `login_flow.md` §3–§4 and `crypto.md`; this spec only marks the boundary.
> (The sub-state-39
> endpoint wait has **no in-tick failure toast** — the worker simply advances 39 → 40 on completion;
> there is no msg-4029 endpoint-error path here. Open question 1 → REFUTED/closed; see §1.9.)

> **Who drives the tick (CODE-CONFIRMED).** The login window is advanced each frame by a **generic
> per-window frame callback** (registered at scene build) that pumps input/messages, sets 2D/alpha
> render state, updates the IME and the hardware cursor sprite, calls the window's own per-frame
> update method, and flushes the texture manager. The login-specific work is that update method — a
> **switch on the tick / drive substate** (the 1..41 field of §1.5; the concrete field offset is in the
> login-window struct map, not here). The intro animation, the fetch-wait-consume net sequences, the
> credential submit, and the loading transition all live in that tick — **not** in the scene builder
> and **not** in the click/action handler (which only *sets* the MAIN workflow substate that gates input).

> **Two blocking worker threads, not the main connection (CODE-CONFIRMED).** The server-list
> (34→35→36) and channel-endpoint (38→39→40) fetches each run on their **own blocking worker thread**
> connecting to port `10000(+offset)`, performing a blocking receive, and posting a result sub-state
> back to the tick. The tick only **starts** the thread and **consumes** the posted result-state. The
> lobby mini-protocol bytes (8-byte wrapper + LZ4 payload, the 8-byte server record, the 30-byte
> endpoint string) are owned by `login_flow.md` §2.

> **Server-list widgets are built ONCE, toggled by visibility (CODE-CONFIRMED).** The server-list
> overlay (states 35/36/37) is not a separate scene — all its widgets (the **two server plates**,
> actions **400/401**, which **are the selectable servers**, LEFT plate = first record; the **ten pager
> buttons**, actions **115..124**, which **page the 2-plate view** (page = action − 115) and are reset
> to a blank UV by the painter — **not** server rows; scroll arrows; refresh button action 105; and the
> two shared notice/error dialogs) are constructed by the single login window builder at scene build and
> merely shown/hidden by the tick. A clicked **plate** (action 400/401) at state **37** commits the
> selection, persists `Lastserver`, and derives the channel-endpoint port as `10000 + selected server id`
> for the state-**38** fetch — consistent with the corrected §11.4 layout.
>
> **No login BGM (CODE-CONFIRMED absence).** No music/stream start was found anywhere in the login
> tick or in any of its state-enter branches: the tick plays the intro **SFX 861010105** and queues
> the transition **effect 10001**, and starts the net engine, but **issues no BGM/music start**. If a
> login BGM exists at all it is started outside the login window (engine/scene level), not by login
> code. A faithful rebuild should not start a login-scene music track from the login flow.

> **CONFLICT with `specs/ui_system.md` §6.3 (I do not own that file).** That table marks sub-state
> **29 = "Server-list trigger point"** and **31 = "Help screen"** as CODE-CONFIRMED. The
> login-scene lane shows both are wrong: **29 = OK-button credential validation** (ID/PW length
> checks → msg 4025/4026), and **31 is the PIN-modal RAISE step on the TICK workflow substate** (31 →
> 32 = poll-to-33), **not** "Help screen" (§1.5 rows 31/32 / §1.4a) — MAIN substate 6 is the resting
> login form, **not** an EULA panel (the construct walk builds none — §1.4c); the help button is a
> *separate* control (action id 105 `i`),
> not a sub-state. **An orchestrator/owner should correct `ui_system.md` §6.3 row 29** (= credential
> validation, not "server-list trigger") and confirm row 31 = "raise PIN modal" (drop any
> "31 = Help screen" labelling). Recorded here, not edited there.

## 1.5a Login-window curtain — the two-edge letterbox OPEN geometry (CODE-CONFIRMED)

> This is the exact geometry behind the §1.5 sub-states **1–5** intro curtain (the row-2 "carved-stone
> window / red-ribbon" motion). It is the **login window's own curtain**, driven by the login per-frame
> **sub-state** machine — **NOT** the opening-window scene's 4-phase fade machine of §1.0.2, and **NOT**
> a spawned particle effect. (This subsection resolves the open-direction/magnitude question raised as
> the "§1.0.2 NEW letterbox" deep-pass delta; the content belongs to the login curtain, so it is
> recorded here with §1.5.)

The curtain is a **two-panel vertical slide that OPENS OUTWARD** to uncover the form. Exactly **two**
horizontal panel widgets move, driven together by a **global offset accumulator** that resets to 0 and
advances by **+5 pixels per tick** (no easing, no frame-delta — a **frame-locked** step) up to a bound
of **222**. The two panels are placed at the SAME offset each tick from this one shared accumulator:

| Panel | Start Y | End Y | Motion |
|---|---|---|---|
| **TOP** panel | **0** | **−222** | slides **up**, off the top of the canvas (Y = −offset) |
| **BOTTOM** panel | **326** | **548** | slides **down**, off the bottom of the canvas (Y = offset + 326) |

- The per-tick offset `C` (0, **+5/tick**) drives **TOP Y = −C** and **BOTTOM Y = C + 326**; the curtain
  **completes when the offset exceeds 222** (≈ **45 ticks** at +5/tick). At completion the end positions
  are TOP Y = −222, BOTTOM Y = 548.
- **Form decorative plate snap seat.** Once the offset exceeds **200**, the **form decorative plate**
  (the credential-area chrome image, atlas A1 src 0,469 size 494×113) **snaps to screen position
  (494, 469)** — a single reposition late in the sweep, not a per-tick slide. (CORRECTION 2026-06-22:
  the snap target is the form plate, NOT the ID textbox. The ID textbox is not built or visible until
  the credential group reveal at sub-state 5/6. Binary-won, counter-check IDB SHA 263bd994.)
- **Sub-state binding (tick/drive substate, §1.5):** drive state **1** seeds the closed/start positions
  **and plays the login-enter SFX id 861010105** (a sound id, not a VFX id); state **2** runs the slide
  until offset > 222; states **3/4** settle; state **5** reveals the ID/PW boxes + buttons and **stops**
  the animation; state **6** is the resting form.
- The step is **frame-locked** (+5/tick, not wall-clock), so the original open duration is frame-rate
  dependent; a faithful port may keep the **222 sweep** but pick a fixed wall-clock duration for
  determinism. The canvas convention is **top-left-origin, +Y-down, 1024 × 768**.

> **DEBUGGER-PENDING — flat-vs-scaled curtain base.** The bottom panel's base **326** is a **768p
> literal** read inside the per-tick law, but the build-time placement of that panel scales the same
> base by screen height (`326 × scrH / 768`). Whether the running client uses the flat 326 (the tick
> literal) or the resolution-scaled value at runtime is **not statically determinable** and must be
> confirmed on the live client. The endpoints in the table above are the tick-literal (768p) values.

### 1.5a.1 Per-widget alpha fade (CODE-CONFIRMED)

Each login widget fades its own alpha independently of the curtain slide:

- Alpha steps by **±64 per tick** toward the target — **0** when the widget is hidden, **255** when shown
  — **clamped to [0, 255]** (so a full fade-in/out takes ≈ **4 ticks**).
- A **forced-alpha override byte** exists per widget: when set, it **PINS the alpha to a fixed value**
  and **overrides** the ±64/tick fade entirely (the fade does not run while the override is engaged).

**Confidence:** CODE-CONFIRMED (the two panels, the 0 → −222 / 326 → 548 endpoints, the shared
**+5/tick** global accumulator bound 222 with **completion at offset > 222** (≈ 45 ticks), the
**form-decorative-plate** snap to (494, 469) at offset > 200 (CORRECTED from "ID-box snap" 2026-06-22 —
binary-won, counter-check IDB SHA 263bd994), the open-outward motion, the **±64/tick alpha fade with
forced-alpha override**, and the drive-state 1 → 5 binding — all fully automatic with no user input at
any edge). The flat-vs-scaled 326 base is DEBUGGER-PENDING (above).

## 1.6 Save-ID persistence (CODE-CONFIRMED)

Toggling the Save-ID checkbox (action `h`) and a successful credential validation persist the
account id to a loose client INI file:

- **File:** `DoOption.ini`, section `[DO_OPTION]`, key `OPTION_ID` (an **INI file** string).
- **Exact round-trip (CODE-CONFIRMED).**
  - **READ on scene build:** the Win32 private-profile-string read pulls `[DO_OPTION] OPTION_ID` into a
    short buffer with the **default `"(null)"`**; the builder compares the value against the literal
    `"(null)"` and **skips the pre-fill** when it equals that default. The id is copied into the ID box
    (and the Save-ID box is ticked) **only when it is non-empty and shorter than 16 characters**.
  - **WRITE on the Login action:** the private-profile-string write stores the current id back to
    `[DO_OPTION] OPTION_ID`.
- `DoOption.ini` is **EXE-relative and Hidden** (its path is built by stripping the module path). It is
  one of **FIVE Hidden client INIs** the client maintains: **`DoOption.ini`, `option.ini`, `panel.ini`,
  `combo.ini`, `TSIDX.ini`**.
- Clearing the checkbox clears/over-writes the key.

This is a **client-local convenience only** — never sent on the wire.

> **Save-ID (INI) vs remembered-server (registry) — two SEPARATE persistences; do not conflate
> (CODE-CONFIRMED).** The Save-ID **account string** is persisted to the loose **`DoOption.ini`**
> (`[DO_OPTION] OPTION_ID`, a string). The **remembered server id** is a different persistence entirely:
> it is written to the **registry** value **`Lastserver`** (a `u32` under `HKLM\software\crspace\do`) at
> server-selection commit (sub-state 37, §2.5). Save-ID **never** touches the registry, and `Lastserver`
> **never** holds the account id. (An earlier dirty note loosely described the Save-ID write as a
> registry write — that conflated the two; the account id is the INI write, the server id is the
> registry write.)
>
> **A SECOND registry value lives beside `Lastserver` (CODE-CONFIRMED).** Under the same key
> `HKLM\software\crspace\do`, alongside the `u32` **`Lastserver`**, the client also persists a
> registry **string** value **`servername`** (read back on launch). §1.6 previously listed only
> `Lastserver`; both registry values share that key.

## 1.7 What the Login click sends (cross-reference, not owned here)

For an engineer's mental model only; the byte shapes are owned by `login_flow.md`:

- The **OK/Login button sends nothing directly to the game server.**
- The only client→server traffic in the entire login scene is:
  1. the two **lobby fetches** (synchronous, port 10000 then `10000 + selected id`; no
     `major/minor` opcode — an 8-byte frame wrapper + LZ4 payload), at sub-states 34 and 38, and
  2. the **game-socket handshake** at sub-state 40: the cryptographic key exchange (inbound `0/0`)
     and the reactive reply — the **account-login credential send is `1/4`** (CODE-CONFIRMED: the
     reactive reply to the inbound `0/0` key exchange), whose RSA pre-image is the
     `[0x2B][u32len account\0]([u32len PIN\0])` blob (account name + second-password / PIN, §1.4a).
     The account password rides inside that same `1/4` RSA ciphertext. **`1/6` is character-create
     only** (§4.5), **not** the login credential send.
- **No login widget emits a `(major:minor)` packet; the only outbound game opcode in the login scene
  is the reactive `1/4` auth reply.** There is no dedicated "login" opcode fired by the button click.

## 1.8 The quit paths (CODE-CONFIRMED)

Two distinct quit triggers, both terminating at engine state **6 (Quit)** → **8 (Exit)** (see
`client_runtime.md` §3):

1. **Version-mismatch quit.** OK/Enter with a `game.ver` mismatch → Win32 modal error box (msg
   **2204**) → on OK, the quit-from-load path: SFX **861010106**, a ~1000 ms UI fade, then engine
   state **6 / substate 2**.
2. **User quit-confirm — the shared ExitPanel (CODE-CONFIRMED; login Yes is INERT).**
   The genuine quit-confirm is a **shared `ExitPanel`** (caption **msg 2007**, **Yes = action 50**,
   **No = action 51**). Its "Yes" runs a **GameState-keyed dispatcher** that quits for **Load
   (engine 6/2)**, **In-game (engine 6/8)**, and **Select (net logout 2/137)** — but it has **NO case
   for GameState 1 (Login)**. Therefore at the login scene the ExitPanel's "Yes" is **inert** (it
   resolves to no quit action). The actual user-facing quit at the login scene is consequently an
   **OS-level window-close / Alt-F4 path outside the widget tree** — **DEBUGGER-PENDING** to confirm
   the exact Win32 close edge.
   - **The substate-30 → engine 6/8 branch is DEAD/UNREACHABLE in this build (CODE-CONFIRMED).** The
     tick *does* consume a `substate 30 → engine 6/8` branch, but an exhaustive scan of **every writer**
     of the tick/drive substate field across the whole binary found **NOTHING ever writes value 30**.
     The prior "writer of sub-state 30 is static-pending" item is therefore resolved to **no writer
     exists in this build** — the row is dead (see §1.5 row 30).

> **Third loop-exit edge (CODE-CONFIRMED).** Distinct from the two quit terminators above, the login
> dispatcher **action 101** clears the engine run-flag **directly** (an immediate cancel/back edge,
> CODE-CONFIRMED), and the loading-complete engine event (id **10001**) clears it as the normal
> handoff. Neither writes engine state 6 — they are run-flag clears, not quit terminators. **Actions
> 113/114 are NOT quit either**: they hide their popup and **write tick substate 34 (server-list
> re-fetch)** — CODE-CONFIRMED.

> **⚠️ 113/114 are NOT the client-quit terminator (reconcile with §1.2).** Actions **113 / 114**
> (captions msg ids **4023 / 4024**) hide the popup and **(re)start the server-list path (advance to
> tick substate 34)** — they are a **server-list re-fetch confirm**, consistent with §1.2's
> description of 113/114 (CODE-CONFIRMED). The genuine client-quit confirm is the shared **ExitPanel**
> (Yes = action 50 / No = action 51), whose Yes is **inert at Login** because the GameState-keyed
> dispatcher has no Login case; the previously-cited "writes sub-state 30 from a separate dialog
> action" path is **DEAD in this build** (no writer of value 30 — §1.5 row 30 / §1.8). Do **not**
> conflate 113/114 with the quit terminator.

The session never returns to the login scene after the first visit — post-in-game logout returns to
**character select (state 4)**, not login (`client_runtime.md` §4).

## 1.9 The login-scene message-catalogue id table (CODE-CONFIRMED ids; captions VFS-only)

All ids resolve through the message-catalogue lookup against `data/script/msg.xdb` (CP949). The
**caption text is in the VFS and is not reproduced** — record the ids, supply text from an extract.

| Message id | Used at | Meaning | Confidence |
|---|---|---|---|
| **2204** | version gate | `game.ver` mismatch / wrong client version (Win32 error box) | CODE-CONFIRMED |
| **4001–4022** | login notice panel | a **static stacked notice/agreement text column** — 22 labels built in a loop (`4001 + i`, `i = 0..21`) at `(X = 50, Y = 100 + 18·i, W = 383, H = 50)`, overlapping vertically into one tall left-aligned paragraph block on the login notice panel; **not** server-list row captions, **not** ID/PW/option form labels, and **not** an EULA terms body (§1.4c) | CODE-CONFIRMED (ids) |
| **4023** | quit-confirm popup #1 | quit-confirm prompt | CODE-CONFIRMED |
| **4024** | quit-confirm popup #2 | second quit-confirm prompt | CODE-CONFIRMED |
| **4025** | sub-state 29 | ID / account too-short notice (length < 4). **Cached-notice sourced** (boot-time fill); the literal id at the call site is UNCONFIRMED | UNCONFIRMED (literal id) |
| **4026** | sub-state 29 | empty-password notice (length < 1). **Cached-notice sourced**; literal id at the call site UNCONFIRMED | UNCONFIRMED (literal id) |
| **4027** | sub-state 36 | empty-server-list notice. **Cached-notice sourced**; literal id at the call site UNCONFIRMED | UNCONFIRMED (literal id) |
| **4028** | sub-state 36 | connect-fail notice. **Cached-notice sourced**; literal id at the call site UNCONFIRMED | UNCONFIRMED (literal id) |
| **4029** | server-row painter | **server-list column-header caption** (the list headers are 4029 / 4030 / 4031 / 4032, loaded once by the server-row painter) — **NOT** an endpoint-fetch error; not referenced by the login tick | CODE-CONFIRMED (REFUTES the earlier endpoint-error reading) |
| **101** | every timed in-window popup | the "OK / seconds remaining" countdown suffix (`%s - %d`) | CODE-CONFIRMED |

So the family is: **4001–4022 = the static stacked notice/agreement text column** on the login notice
panel (NOT server-list row captions, NOT login-form labels, NOT EULA body — §1.4c), **4023–4024 = the
two shared notice/quit-confirm prompts**, **4025–4028 = the inline login notices** (ID-too-short /
empty-password / empty-server-list / connect-fail — **cached-notice sourced via a boot-time fill;
the literal ids at the call site are UNCONFIRMED**, do not assert them as the inline error ids),
**4029–4032 = server-list column-header captions** (loaded by the server-row painter, not error toasts;
see §11.4), **2204 = the version-mismatch error box (the only inline advisory id, CONFIRMED)**,
**101 = the timed-popup suffix**.

---

# 2. Server selection

Server selection is owned by the **same login window** (still engine state 1); it is not a separate
scene. The selectable list is a **runtime network query**, not a VFS file and not a hardcoded array
— the on-the-wire decode is owned by `login_flow.md` §2. This section specifies the **presentation
and selection flow** only.

## 2.1 Two renderers, one decode (CODE-CONFIRMED)

There are **two visual variants** of the server-list renderer:

- a **classic** renderer, and
- a **NEW_SERVER** variant (which adds a "NEW" badge — §2.7).

Which one is active depends on which login-window subclass/atlas configuration is instantiated.
**Both decode the identical 8-byte server record and apply the identical status/load/open-time
presentation rules** — there is no behavioural difference except the badge. A reimplementation needs
one decode path and a presentation toggle.

## 2.2 The server record (decode owned by `login_flow.md` §2.1)

Each server entry is an **8-byte little-endian record** fetched over the lobby socket. Reproduced
here **only** so the presentation rules below are self-contained; the authoritative byte spec and
its packet YAML are owned by `login_flow.md` / `packets`:

| Offset | Size | Field | Used by presentation as |
|---|---|---|---|
| +0 | 2 (u16) | `server_id` | index **1..40** into the client-local localized-name table (§2.8) |
| +2 | 2 (i16) | `status_code` | availability; special values in §2.3 |
| +4 | 2 (i16) | `load` | population gauge; color thresholds in §2.3; also HH source for scheduled-open |
| +6 | 2 (i16) | `open_time` | only meaningful when `status_code == 3`; MM source for scheduled-open |

A `server_id` outside 1..40 is treated as an error/invalid entry.

> **UI-side 8-byte record decode (CODE-CONFIRMED) — note for the renderer.** The login window's
> own server-list painter reads each 8-byte record with a slightly different field decomposition than
> the presentation table above (which is carried verbatim from `login_flow.md`): the UI decode is
> **server id (i16) @ +0, name-key (i16) @ +2, status (u8) | load (u8) packed @ +4, flags (u8) @ +6**.
> In the UI decode `status` and `load` are a **single packed `u8|u8` pair** at +4 (not two separate
> i16 fields), and +2 is a **name-key** index (into the localized-name table, §2.8) rather than a
> status field. The byte *total* is the same 8 bytes and the wire authority remains `login_flow.md`;
> this note records the UI painter's exact view so a faithful renderer extracts status/load/name-key
> from the right bytes. (Where the two decompositions disagree on field widths, the UI painter's
> packed `u8|u8` status/load is what drives the §2.3 colour rules on screen.) **The 8-byte UI record
> is NOT the 16-byte char-spawn record** — any prior "16-byte server record" reference is wrong for
> the server list.
>
> **The presentation painter's own field view is FOUR u16s (CODE-CONFIRMED — authoritative for
> on-screen presentation).**
> The §11.4 server-list painter decodes each record as **four 16-bit fields**: **`+0` id (u16), `+2`
> status (u16), `+4` load (u16), `+6` open-time (u16)** — and the §2.3 status/load/clock rules are
> evaluated against *these* fields. This differs from the packed `u8|u8`-status/load model in the note
> just above; **for the on-screen presentation rules the painter's four-u16 view is authoritative**
> (whether an earlier decode expands packed wire bytes into these u16 slots is debugger-pending). The
> wire byte authority remains `login_flow.md`.

## 2.3 Status & load presentation rules (CODE-CONFIRMED; wire bytes capture-unverified)

These rules are **UI-only** — they color and label an entry; they are not part of any codec.

**Load colour thresholds (CODE-CONFIRMED, EXACT)** (population gauge). Each bucket has an exact display
colour and an exact message-catalogue id. The recovered opaque-ARGB display values (CAMPAIGN 9b,
two-witness IDA) are:

| `load` value | Bucket | Display colour (opaque ARGB) | Message id |
|---|---|---|---|
| > 1200 | highest / "full" tier | **red** `0xFFFF0000` | **6001** |
| > 800 | high tier | **orange** `0xFFED6806` | **6002** |
| > 500 | medium tier | **yellow** `0xFFFFFF00` | **6003** |
| ≤ 500 | default tier | **green** `0xFFB5FF7A` | (default, no special msg) |

The thresholds are **strict greater-than** comparisons evaluated top-down (so `load == 1200` is the
high tier, not the full tier). The four ARGB values are **literal in-code immediates** written into the
label's tint slot — the earlier "colours come from a data table" suspicion is **REFUTED** (CODE-
CONFIRMED). These are UI-only colour/label rules and are not part of any codec.

**Status field is a multi-field if-ladder, NOT a flat status-code switch (CODE-CONFIRMED — CORRECTS the
old "status_code special values" table).** The painter is a **nested if-ladder keyed on the record's
STATUS field** with only **two real cases**, plus a default:

| Branch | Condition (on the record fields) | Presentation |
|---|---|---|
| **active** | `status == 0` | normal/open — runs the load-colour path (red/orange/yellow/green per the table above) |
| **scheduled-open** | `status == 3` | scheduled-open path: if `load == 24` → show the "preparing" caption (msg **6004**); else render a scheduled clock `HH:MM` via the format string msg **6005** (§2.4) |
| **default** | any other status value | **index-selects one of the 4 column-header captions** (msg **4029..4032**) |

So the **CODE's handled status set is `{0, 3}`** only. The values `{2, 4, 24, 100}` that an earlier
reading attributed to `status_code` actually belong to **other record fields**:
- **2 / 4 / 24** are comparisons on the **LOAD** field (not status) — `load == 24` is the
  "preparing" diversion inside the scheduled branch; the 2/4 comparisons are other load gates.
- **100** is a comparison on the **SERVER-ID** field (not status).

**Status message ids (CODE-CONFIRMED roles, CORRECTS the prior labels).** The ids are **table-resident
DWORD slots** (read-only, from a contiguous global block holding `{5901, 6001, 6002, 6003, 6004,
6005}`) — **not** in-code immediates and **not** base+offset:
- **6001 / 6002 / 6003** = the load buckets (full / high / medium), as in the colour table above.
- **6004** = the **"preparing" / scheduled caption** (NOT "maintenance" — corrects the prior label).
- **6005** = the **scheduled-clock FORMAT STRING** (NOT `"%4d / %4d"` — corrects the prior label).
- The `"%4d / %4d"`-style **load read-out** uses a **separate in-code literal**, not a message id.
- **5901** = the **unknown-id / out-of-range fallback** caption.

> **Capture-bound open question (Open question 3).** What the **server actually emits** as the status
> enum is distinct from what the **code branches on**: the code only handles `{0, 3}` on the status
> field, but the wire may carry other status values. The full server-emitted status enum is unknown
> without a capture.

## 2.4 Scheduled-open clock packing (CODE-CONFIRMED math; capture-unverified semantics)

The scheduled clock is gated by **`status == 3 && load != 24`** (a `load == 24` value diverts to the
"preparing" caption, msg **6004**, instead of a clock). When shown, the entry renders a four-digit
`HH:MM` clock into the **msg-6005 format string**, where each half is a two-digit decimal produced by a
`/10`, `%10` split:

- **HH** (hours) = two digits from the **`load`** field (`load/10`, `load%10`),
- **MM** (minutes) = two digits from the **`open_time`** field (`open_time/10`, `open_time%10`).

So for `status == 3`, the `load` field is repurposed as the hours of the scheduled open time and
`open_time` as the minutes. The `load == 24` case (a 24-hours sentinel) instead shows the
"preparing" label (msg 6004). *(The render math is firm; whether the server truly packs HH into the load field
this way is `PLAUSIBLE` and capture-unverified — Open question in `login_flow.md`; carried here as
the presentation rule.)*

## 2.5 Selection commit and `Lastserver` (CODE-CONFIRMED)

> **Display model — two PLATES are the servers; the ten 115..124 buttons are PAGERS (CODE-CONFIRMED;
> supersedes the old "10 server-rows" reading).** The server list is presented as **two parchment
> plates** (UI actions **400/401**), and **each plate is one server** — 1 plate = 1 server, **max 2
> shown per page**. **Action 400 = LEFT plate = record `2·page`; action 401 = RIGHT plate = record
> `2·page + 1`.** The 8-byte server record (§2.2) is painted **onto a plate** (localized name → plate
> header label; status/load → plate label line). The ten widgets at **actions 115..124 are PAGER
> buttons**, not server rows and not selectable servers: clicking one sets **page = action − 115** and
> **re-paints the same 2-plate view** at that 2-record offset; the painter **resets all ten to a blank
> UV** (which is why they never show server content). With ≤ 2 records only the left plate is live; the
> pagers exist as capacity to step through a list longer than two. (This corrects the earlier model that
> read the ten buttons as server rows and the two plates as channel toggles — see §11.4 and the
> superseded §1.2 rows.)

A server is committed by a **plate click** (flow sub-state 37, or auto-selected when the list has
exactly one entry):

- The clicked **plate** (action 400/401), **gated to sub-state 37** and guarded by **`status_code == 0
  && load < 2400`**, resolves the record index `(action − 400) + 2·page` and writes that record's
  **`server_id`** into the window's **selected-server field** (it is later added to 10000 to derive the
  channel-fetch port — §2.6).
- The selected id is persisted to the registry value **`Lastserver`** under
  `HKLM\software\crspace\do` (a `u32`).
- The commit advances the flow **37 → 38** and fetches the per-server channel endpoint on port
  **`10000 + server_id`** — **there is no intermediate channel-picker step** (channel resolution is
  entirely post-selection on that fetch).
- On the **next** visit, if `Lastserver` is present, the list is shown in a **randomized display
  order that pins the remembered server** (§2.7); otherwise a plain sequential order is used.

A **server-id comparison against 100** (NOT a status value — the 100 literal is a comparison on the
record's **server-id** field, §2.3) plus an auto-advance flag drives the **single-server auto-connect**
path: when the list has one entry that the client treats as "current", it forces the (left) plate
visible, persists `Lastserver`, and advances straight to the channel-endpoint fetch (sub-state 38)
without a manual click.

## 2.6 Channel-endpoint resolve → join (CODE-CONFIRMED; payload capture-unverified)

After a server is committed, a **second** lobby fetch (the channel-endpoint fetch, sub-state 38)
connects to port **`10000 + selected_server_id`** and returns the chosen game server's endpoint as a
**fixed 30-byte NUL-padded ASCII string** of the form `"host port"` (decode owned by `login_flow.md`
§2.2). This endpoint becomes the **4th TAB field** of the join string built at sub-state 40 (§1.5),
and the secure-context rebuild parses `host` and `port` out of it. After this, normal
`major/minor` traffic begins on the game socket.

> The server_id (1..40) is added directly to 10000 as the channel port — i.e. the server provisions
> lobby ports `10001..10040` per server. Capture-unverified (Open question, carried from
> `login_flow.md`).

## 2.7 Randomized display order & the `NEW_SERVER_INDEX` badge (CODE-CONFIRMED)

- **Randomized order.** Both renderers map screen slot → record through a **display-order index
  array** built per refresh. When `Lastserver` is present the order is **shuffled (seeded from the
  clock) with the remembered server re-anchored** to a stable slot; otherwise it is sequential. The
  intent is load-spreading across servers. A reimplementation should treat the *presentation* order
  as decoupled from the *record* order.
- **`NEW_SERVER_INDEX` badge.** A Lua global of that name in `data/script/uiconfig.lua` (value 5 in
  the sampled client) names **which `server_id` to flag as "NEW"**. It is read at scene build and
  stored on the window. In the NEW renderer, the record whose `server_id` equals this value gets an
  extra **"NEW" badge widget** drawn beside it. It is **not** a renderer toggle and **not** an
  address of any server — purely a presentation flag. *(This answers the long-standing "what is
  NEW_SERVER_INDEX" question from the data-census lane.)*

## 2.8 Localized server names — client-local, never on the wire (CODE-CONFIRMED)

The wire carries only the numeric `server_id` (1..40). The **display name** is resolved entirely
client-side through a **41-entry name table** (indices 0..40) built from UI **string-resource
banks**, looked up via the same message-catalogue accessor as everything else:

- The active bank is string ids **5001..5040**.
- Parallel **per-region/locale** banks are pre-touched: 5101–5120, 5201–5220, 5301–5320,
  5401–5440 (and an overlapping 5421–5440). The active locale selects which bank's names show.

**Implication for the revival:** a fresh implementation must supply its own `server_id → name` map;
the names are localized resources, not protocol data. (Corroborated by `login_flow.md` §2.1.)

> **Lobby host discovery (where the *lobby* socket connects), for completeness.** Before any list
> query, the lobby connect helper resolves its host in strict priority: (1) a loose `ip.txt` token
> in the working dir (truncated to 19 chars), else (2) a record from a loose `list.dat` file keyed
> by the registry value `HKLM\SOFTWARE\crspace\do\servername` (each `list.dat` record is 768 bytes:
> a CP949 name match key at +0, the host string at +0x100; file = `u32 count` then `count × 768`),
> else (3) the hardcoded fallback host. This resolves the **lobby** host only; the **game** server
> host comes from the channel-endpoint fetch (§2.6). The `list.dat` byte layout is
> `static`-derived and on-disk-unverified (Open question 5). `do.ini` is **not** referenced by the
> server-selection path on the available evidence.

---

# 2L. The loading screen (Diamond_LoadingWindow) — CODE-CONFIRMED

**Engine state: 2 (Loading).** A full-screen progress screen shown **between server-select/login and
char-select** on the boot path, and again — reusing the same window class — on the **enter-world**
path. One window class and one preload worker serve both entries; they differ only in the game-state
value left when the loop exits, which routes the next scene.

## 2L.1 Composition

- **Background.** One full-screen background image is chosen **uniformly at random** (`rand()%3`,
  re-rolled each time the window is built) from three DDS files: `data/ui/loading.dds`,
  `data/ui/loading06.dds`, `data/ui/loading08.dds`. It is drawn as a **single full-screen quad sized
  to the live backbuffer** (centred, `±liveW/2` × `±liveH/2`), under an orthographic projection over
  the live width/height, sampling **U[0, 1] and V[0, 0.75]** of the DDS (the texture's top three
  quarters in height; the V=0.75 crop is CONFIRMED, the exact DDS dimensions are asset-side and not
  load-bearing).
- **Progress bar.** Laid out at a **1024×768 design resolution** and stretched to the live window by
  **liveW/1024 on X and liveH/768 on Y**. The track rect in design space is
  **x ∈ [−499, −170] (width 329), y ∈ [−363, −140] (height 223)** (lower-left of centre), i.e. canvas
  **(13, 524, 329, 223)** at 1024×768. Two layers are drawn: a static **track** quad (always) and a dynamic
  **fill** quad (only when percent > 0). The fill **grows VERTICALLY, bottom-anchored** (it does NOT widen
  left-to-right): full width **329**, height **h = ⌊223·percent/100⌋**, anchored at the bottom (design
  y = −363, canvas y = 747) and growing upward. It samples a sub-rect of the **same loading DDS** with
  **U fixed at [443/1024, 772/1024] = [0.4326, 0.7539]** and **V bottom-anchored at [(992−h)/1024, 992/1024]**
  (so at full height V = [769/1024, 992/1024] = [0.7510, 0.9688]). The bar art strip lies BELOW the
  background art in the one texture (no separate progress-bar DDS).
- **BGM.** A looping track, sound id **920100100**, on **music category 0** (the single direct voice
  slot — a new category-0 sound frees the prior one). Started on scene enter; it stops implicitly
  when the next scene (char-select / world) takes the category-0 slot. *(Whether an explicit Stop
  also runs on teardown is debugger-pending.)*
- **Pacing.** The render/update tick runs at roughly **10 fps** (≈100 ms per frame).

## 2L.2 The preload worker & progress

A background worker thread bulk-loads the **global data-table corpus** from the VFS — roughly **47
global tables** (system/control, items, skills, mobs, npcs/npc, quests, the char manifest batch,
skin list, emoticon/textcommand tables, the `.xdb` tables, etc.) read sequentially. This is the
**static/global** corpus the client needs before either char-select or the world; **per-area zone
geometry** (terrain / `.sod` / `.arr`) is **not** loaded here. The same worker runs on both entry
paths.

Progress is reported as a **cumulative-bytes-loaded ÷ fixed-total-bytes** ratio yielding an
**integer 0..100 percent** — NOT an N-of-M file count. The denominator is a tuned constant total
(asset-side; a revival can drive its own 0..1 float from its own preload byte/step count and need not
reproduce the exact denominator). The bar reads this percent each frame to size the fill.

## 2L.3 Advance trigger — loading-done + 500 ms grace, NOT bar == 100% (CONFIRMED)

The loading window runs inside the engine modal scene loop (pump-events → tick → render while the
engine run flag is set). The scene advances when that run flag is cleared, by **either** of two
paths, both leading to the same handoff:

1. **Worker-done flag + 500 ms grace (primary).** When the worker finishes all loads it **sleeps
   500 ms** (a deliberate grace delay) and then clears its "worker running" flag. The next render
   frame observes the cleared flag and ends the modal loop. The transition is therefore gated on
   **loading-complete + a 500 ms grace**, **not** on the progress bar reaching 100% (the percent only
   drives the fill geometry and is never compared for the transition).
2. **Engine loading-complete event (id 10001).** The window's command/event handler also ends the
   scene on receipt of the engine event with id **10001** (the "loading-complete" signal, see §1.1).
   Which of the two paths fires first per entry is debugger-pending.

The **grace constant is 500 ms**; the per-frame pace is ≈100 ms (~10 fps).

## 2L.4 Entry points & routing

The loading window is reached when the engine scene state is set to **2 (Loading)** in two cases:

- **Entry A — post-login (boot path).** The login scene sets the state to 2 on completion; after its
  loop returns, the state machine builds the loading window. It runs the boot data-table preload,
  then (after the grace) reads the opening skip flag and routes onward to the opening movie or
  char-select.
- **Entry B — enter-world ack (`3/5`).** The enter-game ack handler sets the engine scene state to 2
  and breaks the currently-running scene loop, so the state machine rebuilds the loading window for
  the **enter-world** transition. The same global-corpus worker runs; after completion the state
  machine proceeds into world-scene init (downstream of this scene). *(Any additional per-zone
  preload on the 3/5 path would be in world-scene init, outside this scene — UNVERIFIED here.)*

Both entries use the **same** loading-window class and the **same** global-corpus worker; only the
post-loop game-state value differs, which selects the next scene. The "loads a different corpus per
entry" idea is **not** supported — both load the same global tables.

> **For the revival Loading scene:** insert it between ServerList and CharScene; pick the background
> by `rand()%3` over the three DDS as a full-screen quad cropped to V[0, 0.75]; lay the progress bar
> out at 1024×768 design and stretch by liveW/1024 × liveH/768 (fill = 223·pct/100, clamped, growing
> left-to-right); drive the percent from the client's own preload (a 0..1 float is fine); loop BGM
> **920100100**; and **advance on preload-done + ~500 ms grace, not at bar == 100%**.

---

# 3. Character selection

**Engine state: 4 (Select)** — entered when the character-list packet (`3/1 SmsgCharacterList`)
arrives. The select scene is owned by a single 6280-byte (`0x1888`) **select window** object
(`SelectWindow`). Its widget tree and the 3D preview actors are built once, at packet arrival; the
object does not exist before that point.

## 3.0 Build and initialisation call sequence (CODE-CONFIRMED)

> **The 3D preview is not a 2D sub-viewport.** `SelectWindow` builds one full-screen 3D scene
> (named `"select"`, on the real `map000` area) behind the 2D chrome. There is no
> `GUCanvas3D`-style rect-anchored viewport in the char-select window; placement is world-space
> throughout. The `GUCanvas3D` widget class exists in the binary but none of its construction
> sites belong to `SelectWindow` — all are shop/trade/info panels.

The char-select window is realised in this fixed call sequence (CODE-CONFIRMED from build
`263bd994`):

1. **`SelectWindow_ctor`** — allocates the 6280-byte (`0x1888`) object; installs the vtable;
   runs a 5-iteration initialisation loop over the five 880-byte per-slot spawn descriptors
   at object offset `+0x238` (stride `0x370`); zeroes the 5 × 96-byte stat-block array at
   `+0x1368`, the 5-byte per-slot flag array at `+0x1548`, and all selection-state and 3D-scene
   pointer fields. No widgets, no textures, no 3D objects are built here.

2. **`SelectWindow_BuildAndInit`** (the window's **build/init virtual — vtable slot 14**, invoked once
   from the engine state-4 dispatch right after construction) — runs in this order:
   a. Snapshot-copies the server character list from the net-handler object:
      **5 × 880 bytes** of spawn descriptors, **5 × 96 bytes** of stat blocks, and **5 bytes**
      of per-slot state/lock flags (index-preserving block moves, slot k → slot k).
      Initialises the selected-slot index to `0` and the mode/visible byte to `1`.
   b. Loads **8 texture-atlas handles** (7 distinct `.dds` files; `InventWindow.dds` is
      registered twice for two sub-panels): `loginwindow.dds`, `mainwindow.dds`,
      `InventWindow.dds`, `CarrierPigeonPerson.dds`, `CarrierPigeonAll.dds`,
      `tradekeepwindow.dds`, `blacksheet.dds`.
   c. Builds the complete **2D widget tree** in a single straight-line construct pass —
      **279 GUI-builder calls** producing **127 heap-allocated widgets**, attached via
      **113 parent-add operations** across ~14 parent panels (the build order = paint / Z
      order). This is the load-bearing count: **279 build calls / 127 widgets / 113 adds**.
      Two of those widgets are dedicated child sub-panels — an **ErrorPanel** (the error/notice
      host, also reused by the tick as the tooltip host) and an **ExitPanel** (the exit/quit
      confirm dialog) — plus one **Descriptor** object; their own internal widgets are built by
      the panels' own builders (the ExitPanel and ErrorPanel inventories are enumerated in §11.5).
      See §11.5 for the full ordered widget inventory and the complete button set.
   d. Broadcasts `SetChildrenVisible(0)` (the show/hide virtual, vtable slot 1) over **exactly 12
      child widgets** — a **4-pointer contiguous block** plus **8 individually-referenced**
      chrome/dialog widgets — so these are built but start hidden. (The `GUCanvas3D` widget class
      exists in the binary but **no `GUCanvas3D` construction site belongs to this window**; the
      char-select 3D preview is a full-screen world scene, not a rect-anchored sub-viewport.)
   e. Calls `SelectWindow_RefreshSlotCountLabel` — sets the char-count label (msg 2209).
   f. As its penultimate call, invokes `SelectWindow_BuildScene` — the 3D scene
      (§3.5–§3.7): env load, terrain cold-start, camera, scene root "select", ambient effect.
   g. As its last call, starts the char-select BGM track **920100200** on the single
      category-0 music slot, looped. **The 2D widget tree is built before the 3D scene,
      the BGM starts after both.**

3. **Per-frame `SelectWindow_Tick`** runs every frame. On **frame 5** (a frame-counter field
   on the window object) it performs, in order:
   a. `SelectWindow_ResetScene` → `SelectWindow_SpawnPreviewLineup` — populates the
      5-slot roster row (§3.3) and arms camera keyframe 1 (§3.5.2), starting the
      KF0→KF1 entry dolly (2000 ms blend duration).
   From frame 6 onward the tick drives the camera boom/zoom accumulator, the
   selected-slot yaw turntable, and the per-frame sound tick.

4. **Roster refresh.** `SelectWindow_ResetScene` (and therefore `SelectWindow_SpawnPreviewLineup`)
   is also re-invoked from `SelectWindow_WriteSlotRecord` (new slot record from server),
   specific `SelectWindow_HandleCommand` cases, and `SmsgCharManageResult_Handler`.

> **Realised sequence:** ctor → 2D widget tree (8 atlas handles → 279 builder calls / 127 widgets /
> 113 parent-adds) → `BuildScene` (env/terrain/camera/effect) → BGM start → [frame 5] → ResetScene
> (lineup + KF1 arm) → [frames 6+] → per-frame tick.

> **The char-select scene is a full 3D world, not a 2D screen.** The select window builds a
> named 3D scene `"select"` on the real game world `data/map000`, frozen at afternoon (14:30),
> with up to five live, animated 3D character models on a stage in front of a perspective
> camera. The 2D chrome (§11.5) only dresses that 3D scene; selection itself is performed via
> 3D ray-pick (§3.3.3). The 3D composition is specified in §3.7; environment/lighting in §3.6;
> camera in §3.5; per-slot placement/pose in §3.3.

> **96-byte stat block is NOT consumed on the select screen.** The info row reads name /
> level / position from the spawn descriptor only (§3.2). The 96-byte block is carried
> through and copied into in-game live-stat globals only on Enter. A select-screen stat grid
> driven from that block is a port fabrication; the only per-slot text on select is the three
> labels of §3.2.

## 3.1 Where the slots come from & the per-slot record (CODE-CONFIRMED)

The inbound `3/1 SmsgCharacterList` (S2C, byte shape owned by `login_flow.md` §3.2 / `opcodes.md`)
is the message that **forces the select scene** (it writes engine state 4 / substate 8). Its body is
a **3-byte header** — `[server, channel, 5-bit slot mask]` — followed, **for each set bit**, by one
per-slot record read in four parts:

| Part | Size | Role |
|---|---|---|
| **Spawn descriptor** | **880 bytes** (`0x370`) | the full character record (§3.2) |
| **Stats block** | **96 bytes** (`0x60`) | per-slot stat block |
| **Slot flag** | **1 byte** | per-slot availability/relation flag |
| **Timing value** | **4 bytes** (u32) | per-slot timing (e.g. a cooldown/relation timestamp) |

> **Reconciliation with `login_flow.md` §1 / `opcodes.md`.** `login_flow.md` records the char-list
> per-slot stride as **981 bytes**. That figure is the **sum of these four parts** (880 + 96 + 1 +
> 4 = 981). They describe the **same record** at two granularities: `login_flow.md` owns the wire
> stride; this spec documents how the select scene splits it into descriptor / stats / flag /
> timing arrays. **No conflict** — recorded so the two specs read consistently.

There are at most **5 slots**. On entering the scene the select window **copies** these four arrays
into its own storage (a straight index-preserving block move — slot k in the inbound scratch becomes
slot k in the window), then builds the 124-widget UI (owned by `ui_system.md` §2.2) and the 5 preview
actors.

> **Slot placement is by BIT-POSITION, not sequential fill (CODE-CONFIRMED).** The handler first
> **zero-clears all five slots** (the 5×880-byte descriptor block, the 5×96-byte stats block, the
> 5-byte flag array, and the 5-entry timing array), then runs a **fixed 5-iteration loop** over the
> mask bits. The four destination cursors advance **every iteration** (descriptor +880, stats +96,
> flag +1, timing +4), but a record is **only read** when mask **bit k is set** — so a record for set
> bit k lands in **slot k**, and an **unset bit leaves slot k empty** (already blanked by the
> zero-clear). The operational rule: **slot k is occupied iff mask bit k is set** — records are *not*
> packed into the first N slots. A mask with bits `{0, 2}` set therefore populates slots 0 and 2 and
> leaves slots 1, 3, 4 empty. This is corroborated downstream: the enter path addresses the chosen
> slot **directly by index** (`880·slot` / `96·slot`), which would be meaningless under sequential
> packing. (Empty slots surface to the consumer as the **`"@BLANK@"`** / zero-name sentinel; §3.2.)


> **Each slot's preview appearance is DESCRIPTOR-DRIVEN, not hardcoded (CODE-CONFIRMED).** An
> existing slot shows the *real* character because its rendered skin/skeleton/outfit come entirely
> from the server-supplied 880-byte descriptor (§3.2), exactly as the in-world actor factory builds
> it — the create-scene's hardcoded carousel gids (§4.3) are used only for the *create* class
> preview, never for a list slot. The chain: read `internal_class` (desc +0x34, `{1..4}`) and
> `appearance_variant` (desc +0x2C), derive `model_class_id = 5·(internal_class + 4·variant) − 24`
> ∈ `{1, 11, 16, 26}` (the IdB), select the catalog skeleton for that IdB (one of g1..g4 via the
> visual catalog — no literal `g{n}.bnd` filename), then layer the visible-gear overlays from the
> equipment table at desc +0x58 (overlay slots `{3, 4, 6, 2, 11, 14}`, each leading gid →
> `data/char/skin/g{gid}.skn`). This reuses the §3.3 skin/bind/idle-motion chain; the appearance
> resolution itself is owned by `specs/skinning.md` and the skeleton-resolution chain. So Godot
> must render slot k from slot k's descriptor record, not from a placeholder.


## 3.2 The 880-byte spawn descriptor — fields the select scene uses (CODE-CONFIRMED)

Field offsets are **inside the 880-byte record** (interoperability facts; the full struct table is
owned by the struct cartographer). Only the fields the front-end touches are listed:

| Offset | Field | Meaning at select |
|---|---|---|
| +0x00 | `name` (CP949, ≤17 bytes incl. terminator) | character name; the sentinel **`"@BLANK@"`** marks an **empty slot** |
| +0x2C | `sex` (u8) | gender (also written by the create form, §4.2) |
| +0x2E | `faceA` (u16) | appearance param A (face). **Nonzero ⇒ the slot is occupied** (the preview occupancy test) |
| +0x30 | `faceB` (u16) | appearance param B (hair / second appearance seed — exact meaning unresolved, Open question 4) |
| +0x34 | `class` (u16) | internal class id (1..4) |
| +0x3A | `level` (u16) | shown on the slot info line ("0" if zero) |
| +0x58 | `equipment table` | **20 × 16-byte** worn-gear/visual slots; the **first dword of each** is an actor/equipment id resolved by the preview (§3.3) |
| +0xA0..0xA8 | `position` (two floats) | the character's **last in-world X/Z**, shown on the slot info line as `"X , Y"` |
| +0x88 / +0x98 / +0x108 / +0xB8 | starter-equipment id slots | seeded on **create** per class (§4.3); empty on existing characters |

The slot info line shows, for the active slot: **name**, **level**, and **position** (the two
floats above). Toggling the enter/create/delete buttons depends on the slot's lock flag.

> **Two occupancy tests, two jobs (CODE-CONFIRMED).** The slot-occupancy decision uses **two distinct
> reads** for two distinct purposes:
> - the **render** path (whether to build a preview actor for the slot) tests **`faceA` @ +0x2E
>   nonzero**;
> - the **enter / route-to-create** path (whether confirming the slot enters the world or opens the
>   create form) tests **`name` @ +0x00 == `"@BLANK@"`**.
>
> They are two separate reads, but they **agree on well-formed data** (an empty slot has both a zero
> `faceA` and the `"@BLANK@"` name).

> **THIRD occupancy witness — the 2D info-row gates on `sex` @ +0x2C (binary-won, counter-check IDB
> SHA 263bd994, static-only).** The per-slot info-row writer (`SelectWindow_RefreshSlotInfoRow`) draws
> the name / level / position labels **only when the descriptor byte @ +0x2C (the `sex`/`variant`
> marker) is nonzero**. This is a third distinct occupancy read, alongside the render gate (`faceA`
> @ +0x2E) and the enter gate (`name == "@BLANK@"`); it agrees with the §3.3 model-input byte and with
> the other two on well-formed data. Read the "two occupancy tests" note above as **three witnesses**:
> render → `faceA` @ +0x2E; info-row → `sex` @ +0x2C; enter → `name` @ +0x00.

> **RECONCILE — the info-line "X , Y" text reads two floats at descriptor `+0x4C / +0x50`, not
> `+0xA0 / +0xA8` (binary-won, counter-check IDB SHA 263bd994, static-only).** Re-derived three ways
> from the info-row writer (slot stride 880; the active slot's descriptor base is `window + 880·slot`;
> name at base +0x00; the two displayed position floats land at float indices 161/162 = byte offsets
> **+0x4C / +0x50**). The `+0xA0 / +0xA8` pair in the table above describes a **different** descriptor
> position field (e.g. the spawn coordinate fed to the in-world actor); the **2D slot info-line "X , Y"
> shown on the select screen reads +0x4C / +0x50.** Flagged for the struct cartographer to reconcile
> which float pair is the *displayed last-location* vs the *spawn coordinate* — until then a port that
> populates the info line must read **+0x4C / +0x50**.

> **Preview model inputs are `class` @ +0x34 and `variant` @ +0x2C — re-confirmed CYCLE 9 Phase 3.1
> (static IDA, IDB SHA `263bd994`, HIGH confidence).** The model-class formula that selects the
> preview skeleton/model takes its `class` argument from the descriptor `class` **u16 @ +0x34**
> (PC value in `{1,2,3,4}`, read sign-extended) and its `variant` argument from the descriptor
> `sex`/`variant` **u8 @ +0x2C** (read zero-extended; the same byte doubles as the slot-row
> occupied/body marker). Both offsets are confirmed at **four independent read sites**, and the
> in-world spawn path feeds the **same** offsets to the **same** actor factory. Keep them distinct from
> the render-occupancy gate `faceA` @ **+0x2E** (a separate u16) — that gate decides *whether* to build
> a preview actor, it is **not** a model input.
>
> **Port action item (alignment bug, not a layout error).** A decoder that reads `class == 0` at
> +0x34 for a real (non-empty) slot is reading from a **misaligned descriptor base**, not from a wrong
> offset: the tell is that the `level` u16 @ +0x3A in the **same** descriptor copy decodes correctly
> (the real character levels appear). Fix the decoder so the per-slot descriptor base points at the
> first byte of the slot record's 880-byte descriptor block; then u16 @ +0x34 yields the `{1,2,3,4}`
> class and `model_class_id = 5*(class + 4*variant) - 24` resolves. The live wire VALUE byte-proof
> stays **debugger-pending**; the *offset* is static-confirmed. spec: Docs/RE/structs/spawn_descriptor.md

## 3.3 The live 3D preview actors — placement, facing, pose (CODE-CONFIRMED)

The select scene renders each occupied slot as a **live, animated 3D character** standing in a **row**
on the 3D stage (§3.7) — **not** a 2D portrait, and **not** a separate asset path. The preview reuses
the exact in-world player-actor build path, so skin / bind / idle-motion resolve through the normal
`.skn` / `.bnd` / `.mot` chains (owned by `specs/skinning.md`, `formats/mesh.md`); char-select adds
**no new mesh/skin asset loading**, though hover/select swaps to a **distinct second motion clip**
(the select / turn-to-front `.mot`, §3.3.4 / §3.7.5). The preview-character
asset set for the four starter classes is catalogued in **§3.7.5**.

### 3.3.1 Per-slot world placement (CODE-CONFIRMED)

Each preview actor's world position is the **stage origin** (§3.7.2; world `(2048, 0, −6144)`) plus a
**baked per-slot offset**. Each offset is a **negative ΔX added to the base stage X (2048)**: the five
slots' ΔX run `{−1560, −1548, −1536, −1524, −1512}` — negative offsets ascending by a **+12 step**
(slot 0 is the most-negative offset, slot 4 the least), placing world X in the range ≈ 488..536. The Z
component arcs very slightly toward the camera at the centre slot (a shallow ~1.5-unit bow). **Y is
exactly 0.0 for every slot** — the actors stand on the stage-origin plane; Y is a **hard `0.0`
immediate** (stage-origin Y 0.0 + slot ΔY 0.0), with **no terrain sample and no per-slot ground lookup**
on the placement path.

| Slot | ΔX (offset from base X 2048) | ΔY | ΔZ | World X | World Y | World Z | Confidence |
|---|---|---|---|---|---|---|---|
| 0 | −1560.0 | 0.0 | −3593.0  | 488.0 | 0.0 | −9737.0 | CONFIRMED |
| 1 | −1548.0 | 0.0 | −3594.0  | 500.0 | 0.0 | −9738.0 | CONFIRMED |
| 2 | −1536.0 | 0.0 | −3594.5  | 512.0 | 0.0 | −9738.5 | CONFIRMED |
| 3 | −1524.0 | 0.0 | −3594.0  | 524.0 | 0.0 | −9738.0 | CONFIRMED |
| 4 | −1512.0 | 0.0 | −3593.0  | 536.0 | 0.0 | −9737.0 | CONFIRMED |

- **Placement is a separate post-build step.** The static scene builder does **not** place the actors
  and contains no 5-iteration actor loop; the up-to-5 preview actors are placed by a **separate
  post-build slot-actor step**, reached from the char-select orchestrator after the static scene is
  built. A faithful rebuild sequences the static stage first, then populates the preview row as a
  distinct step driven by the character list.
- **Offset table is a code immediate.** The five ΔX/ΔY/ΔZ triples are a **5-row code-immediate table**
  (a pooled float-constant block consumed by a per-slot branch chain), **exactly 5 rows**, **not** read
  from any data file. The loop covers slots 0..4.
- **X spacing:** adjacent slots are **12 world units** apart (the +12 ΔX step); with the **×3.0 preview
  scale** (below) the on-screen separation is **36 units**.
- **Z arc:** the Z offsets `{−3593, −3594, −3594.5, −3594, −3593}` dip to the centre slot, bowing the
  row very slightly toward the camera. (This refines the earlier "Z ≈ −3593" approximation.)
- **Scale (LEGACY units — reconcile before porting).** The legacy code's per-slot scale literal is
  ≈ **70** (the create-preview literal is ≈ **81**, §4.2). **These are LEGACY-space values, NOT a
  ready-to-use Godot multiplier** — the Godot-space equivalent must be unit-reconciled against the
  importer's mesh scale. The empirical **Godot × 3.0** the current port uses may already be the
  correct reconciliation, but treat the **70 (lineup) / 81 (create)** legacy literals as the source of
  truth and verify the Godot factor against them rather than hard-coding 3.0. (Flagged for fidelity
  reconciliation, not a binary unknown.)
- **Idle animation rate.** The lineup actors play their idle clip at rate **× 3.0**; the single
  create-preview actor plays at rate **× 12** (§4.2).
- **Spin:** the slot previews do **not** auto-rotate. (The separate single **create-preview** actor at
  §4.2 *does* idle-spin under player control.)

### 3.3.2 Facing — pure yaw, fixed at build (CODE-CONFIRMED; yaw 0 = FRONT on the binary side)

Orientation is a **pure-yaw quaternion** (rotation about the world up axis Y only; no pitch, no roll),
built once at actor creation and not changed by hover. The yaw is chosen from the slot's lock flag:

| Slot lock flag | Yaw | Meaning |
|---|---|---|
| set (locked / new / creating slot) | **π** (180°) | faces **away** from the camera (back to viewer) |
| clear (existing, occupied, playable slot) | **0** | faces **front** (toward the camera) |

- The front yaw is **literally 0.0** (not a camera-relative offset); the camera (§3.5) is posed so
  that yaw-0 shows the character's front.
- **yaw 0 = FRONT (CODE-CONFIRMED on the binary side).** Basis: the engine defines facing as
  `yaw = atan2(Δx, Δz)`, so forward = **+Z** at yaw 0; the live camera (keyframe 1 at Z = −9652) sits
  at **greater Z** than the row (≈ −9737) and looks in **−Z**; and the binary's `.skn` loader applies
  **no X-negation** (that negation is a *Godot-importer* convention, not in the binary). A yaw-0
  actor's +Z forward therefore points **at the camera** ⇒ **front**. **Open question 13 → closed on
  the binary side.**
- **Godot-port note (fidelity-check item, not a binary unknown).** The importer's mesh-local-X
  negation (`formats/mesh.md`) **+** the world Z-negation (`Helpers/WorldCoordinates`) must be verified
  to **compose** to "front toward camera"; if the port shows the back, add 180° yaw / mirror
  consistently. (Open question 13 is reframed as this port-composition check, not a binary unknown.)

### 3.3.3 Selection is the 3D row itself (CODE-CONFIRMED)

There is **no 2D slot widget that drives selection** — the row of 3D models *is* the clickable
selector. On each mouse move the cursor is unprojected and, for each preview actor, a screen-space
**axis-aligned bounding box** is built around the actor's projected position (`X ± 6`, `Z ± 6`, with a
**Y band from 70.0 to 92.0** = the standing-height range) and tested against the cursor. A hit sets the
hovered/selected slot. **The actor transform is never re-written on hover** — neither its position nor
its yaw changes; the visible "turn" is an animation-clip swap only (§3.3.4). The 2D slot frame / dim
chrome (§11.5) runs in parallel as cosmetic dressing.

### 3.3.4 Pose / motion — idle vs select-turn clip swap (CODE-CONFIRMED)

The preview uses the standard in-world animation pipeline; **there is no hardcoded "select stand"
motion id.** The **only on-actor change on hover/select is an animation-clip swap** — the actor plays a
different motion clip. **No** position, quaternion, scale, tint, glow, flash, or brightness change is
applied to the actor on hover/select; the visible "turn to face the player" is **baked into the select
clip**, not a transform change.

- **Idle clip** plays at scene entry for **every** occupied slot.
- **Select clip** is a **distinct second motion clip** — not a re-pose of the idle clip and not a
  play-mode flag. The idle clip and the select clip each resolve to a **separate** animation through the
  per-class animation catalogue, so a faithful rebuild needs a genuine **second `.mot`** for the
  turn-to-front pose.

On hover of a slot, that slot's actor plays the **select** clip; **every other occupied slot is forced
back to idle** the same frame. De-hover reverts the actor to the **idle** clip — the select pose is
**not sticky** (the hit-test runs every frame, and an explicit mouse-leave path forces all slots idle).
At scene entry **all** actors start in the idle clip (slot 0 is only the info-panel default, §3.3.5 —
it is **not** auto-played into the select clip).

The concrete `g{id}.mot` that each clip resolves to is owned by `specs/skinning.md` + the
animation-catalogue struct, via the same data-table chain as idle: **`data/char/actormotion.txt`
(col2 == IdB) → `data/char/mot/g{id}.mot`**. For the starter classes the idle clip is `g111100010.mot`
("peace", 30 frames @ 10 fps; §3.7.5). The **select** clip is the sibling entry in the **same** visual
record. The two clips resolve from **distinct slots** of the actor's `actormotion.txt` row. The **idle / stand**
clip is **`motion_ids_a[1]` = column 16 (record field `+0x44`)** — this is the slot the loader actually
reads, re-confirmed operand-for-operand against the IDB loader on build `263bd994` (see
`formats/actormotion.md` §Per-record layout and `formats/animation.md`). The adjacent slot
`motion_ids_a[0]` = column 15 (record field `+0x40`) has **zero static read-sites** — it is dead in the
loader — so any earlier reading that idle reads column 15 / `+0x40` is **wrong and superseded**: the live
client port reads column 16 / `+0x44`. The **select / turn-to-front** clip
reads visual-record field **+0x58** (= `motion_ids_a[6]` = `actormotion.txt` **column 21** under the same
0-based slot scheme), resolved through the standard motion-id chain. The **path is no longer unknown** —
but the **literal id** is a VFS/data read of column 21 (NEEDS-VFS-HARNESS); still **do not invent the
filename** (carried in §3.7.5 / Open questions).

| State | 3D-actor effect |
|---|---|
| Unselected occupied slot | idle clip; yaw 0 (or π if locked); scale ×3 |
| Selected / hovered slot | select / "turn-to-front" clip (a distinct second `.mot`); **same transform** (no move, no extra rotation, no glow/flash/tint/scale) |
| De-hovered / mouse-leave | reverts to idle clip (select pose is **not** sticky) |
| Locked / new / creating slot | yaw π (faces away); otherwise idle handling |
<!-- pending data-table: exact IdA=1 select .mot LITERAL id (path pinned = actormotion.txt col 21 via visual-record field +0x58 = motion_ids_a[6]; idle = col 16 via +0x44 = motion_ids_a[1] per formats/actormotion.md, col 15/+0x40 = motion_ids_a[0] is statically dead; literal id needs a VFS harness — do not invent) -->

### 3.3.5 Worn gear & default highlight (CODE-CONFIRMED)

- **Worn gear** is overlaid by scanning the descriptor's 20 × 16-byte equipment table at +0x58; each
  slot's first dword is resolved to a visual id and attached (gear/visual sub-mesh channels are
  re-armed after the build), gated by a class/sex check.
- After building, **slot 0 is the default committed selection** — its info line (name / level /
  position, §3.2) and slot-frame chrome are shown by default. This is an **info-panel default only**:
  **all** preview actors start in the **idle** clip, and slot 0's actor is **not** auto-played into the
  select / turn-to-front clip (the select clip appears only on actual mouse hover; §3.3.4). The
  default-highlight source is **CODE-CONFIRMED** (upgraded from MEDIUM).

> **Coordinate convention reminder (for the Godot bridge).** These are raw legacy stage-world
> coordinates with up = Y and the row along +X. Apply the project's world-to-engine convention when
> porting — `Helpers/WorldCoordinates.ToGodot` negates Z `(x,y,z) → (x,y,−z)` (after which the row Z
> becomes +9737..+9738.5). The 12-unit X spacing and the ×3.0 scale are convention-neutral. The
> mesh-local `.skn` X-negation is internal to skin building and is the source of the §3.3.2 front/back
> caveat.

### 3.3.6 Shared actor factory — list slots and the create preview build the same way (CODE-CONFIRMED)

Both the up-to-5 **list-slot previews** and the single **create preview** (§4.2) are built through the
**same actor factory** — the same path the in-world player actor uses — so skeleton selection, the
six-slot overlay attach, texture binding, and idle-motion resolution are identical across all three
contexts. The composition model is owned by `specs/skinning.md` §3.5: one shared skeleton (selected by
`model_class_id` / the skin's `id_b`) carrying up to six overlay `.skn` parts `{3, 4, 6, 2, 11, 14}`,
with the **body as overlay slot 3** (the `202`/"b" family) — there is no separate base mesh.

- **List-slot previews are fully descriptor-driven.** Each slot's appearance comes entirely from the
  server 880-byte spawn descriptor (§3.2): class at +0x34, variant at +0x2C, and the per-slot overlay
  gids in the equipment table at +0x58 (§3.3.5). No appearance values are hardcoded for the slots.
- **The create preview seeds a synthetic descriptor.** For a fresh create there is no server
  descriptor, so the create builder seeds the body-family overlay gids for the chosen class — the four
  layer families `202` / `203` / `206` / `209` (slots 3 / 4 / 6 / 2) — plus the variant, then calls the
  same factory. Slots 11 (head) and 14 (weapon) are empty for a fresh create, so the create carousel
  shows no head overlay and no weapon (the iterate-all-six loop still runs; empty slots resolve to no
  node).
- **Idle motion is actormotion-driven by `id_b`** for both forms: the actor's idle clip is selected
  from `data/char/actormotion.txt` keyed by the actor's `id_b` (col2), then resolved through the motion
  id registry (`formats/animation.md`). The four starter classes have **distinct** IdB values
  (1/26/11/16) and therefore key **distinct** `actormotion.txt` rows; whether all four rows resolve
  to the same "peace" idle clip (`g111100010.mot`) is sample-unverified — confirmed only for the
  Musa / IdB=1 row (§3.7.5). Higher-tier appearances carry class-distinct idles. No
  char-select-specific idle asset exists.
- **Rotation differs by form, not by factory.** The list slots do **not** auto-rotate (§3.3.1). The
  single create preview *does* turn so the player can inspect the would-be character — the concrete
  rotation mechanism (a press-and-hold turntable, superseding any earlier "auto-spin" description) is
  owned by **§4.2** and is the authority for that behaviour.


### 3.3.7 Per-part appearance resolution math -- the edge the port still omits (CODE-CONFIRMED)

The list-slot previews are descriptor-driven (3.3.6), but the port's resolver only reproduces the
**starter classes at variant 0** for both screens. CYCLE 6b recovered, statically, the full per-part
key composition the existing-character lineup uses, so the real (non-starter, equipped) character can
be rendered. `// confirmed: static IDA 2026-06-20`

- **Base body / skeleton key (already in the port):** `appearance_key = 5*(class + 4*variant) - 24`
  with `class` = descriptor +0x34 and `variant` = descriptor +0x2C (the PC branch; `variant == 3`
  yields the 0 sentinel = invisible). For players there is **no categoryBase term** -- the formula is
  pure, so the port's "categoryBase[] pending" note is the **wrong blocker for the player path**. The
  key selects the visual-catalog record (carrying the bind-pose / skeleton handle and the base skin).
- **Per-part overlay build:** the factory builds parts for overlay slots **{3, 4, 6, 2, 11, 14}**. For
  each slot it composes a single 64-bit catalogue key and looks it up to get the part's skin handle:
  `key64 = gid + 1_000_000_000 * (slot + 100 * appearance_key)`.
  - **Slot 14 (body / face / visible base):** the gid is rebuilt from the appearance bytes --
    `gid = 1000 * (d + 10 * (a + 10 * (b + 10 * (partId / 1_000_000))))`, where `d` = descriptor +0x22
    and `a`, `b` are the two appearance bytes (descriptor +0x2C / +0x34). The face/visible-base folds
    into these digits. (The build-part routine labels the two bytes opposite to the key formula; the
    byte SOURCES are pinned, the human label of which digit is "class" vs "variant" is debugger-pending.)
  - **Other slots {3, 4, 6, 2, 11}:** `gid = 10000 * (partId / 10000) + partId % 100`, where `partId`
    is the worn-item id taken from the descriptor's equipment table (descriptor +0x58, 20 entries x 16
    bytes, each entry's leading dword = a worn-item id).
- **Skin load:** each resolved part skin loads from **`data/char/skin/g{gid}.skn`** (inverse-bind baked,
  per `specs/skinning.md`) and binds to the shared pose. **Weapons take a separate rigid path:** the
  hand-weapon worn-item id resolves to a static item-skin attached to the hand bone (NOT a `g{gid}.skn`
  deform skin), dual-weapon aware.

> **Port gap (CharSelectScene3D / ClassAppearanceResolver).** The lineup must (a) read the equipment
> table at descriptor +0x58 and attach the worn-gear overlays, (b) rebuild the slot-14 body gid for
> non-starter / non-variant-0 appearances via the formula above, (c) compose the 64-bit per-part key
> and do the multi-slot catalogue lookup (the port currently loads a single base `.skn`), and (d)
> attach the rigid weapon skin. Face/head folds into the slot-14 gid digits; a distinct dye/color
> channel and a distinct gender field were NOT isolated statically -- candidates (per-equip-entry tail
> bytes; descriptor low byte at +0x14) are debugger-pending.

**Overlay slot order and the lineup-vs-in-world split (CODE-CONFIRMED — two independent driver walks).**
Three sibling driver routines build overlay parts; all share the same fixed slot order and the same
high-tier collapse guard:

- **Lineup actor driver** (called from `SelectWindow_SpawnPreviewLineup`): builds slots
  **`{3, 4, 6, 2, 11}`** then calls separate weapon/hand sub-routines. **Slot 14 (weapon) is NOT
  in the lineup's overlay loop** — it is invoked through a separate hand-attach path after the loop.
- **In-world / shared rebind drivers** (`ActorVisual_RebindLocalPlayerParts` and its sibling): build
  slots **`{3, 4, 6, 2, 11, 14}`** in that order, then the weapon-node attach.

Slot family mapping: slot **3** = BODY (`202`/"b"), slot **4** = `203`/"p", slot **6** = `206`/"s",
slot **2** = `209`/"a", slot **11** = head/hair/face, slot **14** = WEAPON. There is **no separate
base body mesh** — the body is overlay slot 3.

**High-tier collapse guard (CODE-CONFIRMED — both drivers share this entry check).** Every driver
opens with: `if (appearance_key > ActorVisualGlobal[3])` → build **only slot 3** (body); skip
`{4, 6, 2, 11, 14}`. Above the registry threshold `ActorVisualGlobal[3]` the actor is reduced to a
**body-only** high-tier mesh — no other overlays are applied. `ActorVisualGlobal[3]` is a data-driven
boot value (read from the visual-catalog singleton; its concrete integer value is not static-settleable
here — do not invent it). This guard fires for appearances whose `model_class_id` exceeds the
threshold, which can produce a body-only preview for non-starter / high-tier slots.

### 3.3.8 Scenery prop actor — the static decorative prop (CODE-CONFIRMED)

The char-select scene includes one **static decorative prop actor** distinct from the five preview
lineup actors and from the single create-preview actor. It is spawned at **tick-frame 5** (after the
3D scene graph is built) by `SelectWindow_SpawnSceneryActor`, using spawn mode **2 (static scenery)**:

| Property | Value | Confidence |
|---|---|---|
| Spawn mode | **2** (static scenery, not a player/mob actor) | CODE-CONFIRMED |
| World position (X, Y, Z) | **(512.0, 0.0, −9738.5)** (= stage anchor 2048,0,−6144 + Δ −1536.0,0,−3594.5) | CODE-CONFIRMED |
| Scale | **50.0** (legacy scale literal, same reconciliation note as §3.3.1) | CODE-CONFIRMED |
| Orientation | identity (Euler 0, 0, 0) | CODE-CONFIRMED |
| Role | decorative scene prop; no per-slot logic, no idle-motion swap, no hover/select behaviour | CODE-CONFIRMED |

> **CORRECTION — scenery prop Z (binary-won, counter-check IDB SHA 263bd994, static-only).** A static
> re-walk of the scenery routine read the raw local offset added to the stage anchor as
> **(−1536.0, 0.0, −3594.5)**, giving the anchored world position **(512.0, 0.0, −9738.5)**. The prior
> figures **(511.5, 0.0, −9684.0)** / raw Δ **(−1536.5, 0, −3540)** are **superseded** (Z was off by
> ~54.5 units, X by 0.5). The downstream "~50 units nearer the camera than the preview row" framing is
> consequently **wrong**: at Z = −9738.5 the prop sits essentially **inside** the preview-row Z band
> (≈ −9737 to −9738.5), not in front of it.

The scenery prop sits at world Z ≈ −9738.5, which places it **in the same Z band as the preview row**
(Z ≈ −9737 to −9738.5) rather than nearer the camera. It is registered to the scenery/world-manager
node and is destroyed and rebuilt only when `SelectWindow_ResetScene` tears down the whole scene.

### 3.3.9 Appearance-debt diagnostic — why a slot renders the wrong mesh (CODE-CONFIRMED)

Every mesh a slot shows is a **pure function of the bytes at the slot's 880-byte descriptor**
(`window + descriptor_base + 880·slot`) fed through the shared actor factory (§3.3.6 / §3.3.7).
No per-slot appearance value is hardcoded in the scene builder. A wrong or missing mesh on a specific
slot can only come from one of five causes, listed in diagnostic order:

| # | Cause | Tell | Fix |
|---|---|---|---|
| 1 | **Misaligned descriptor base** — the factory reads `class` @ descriptor +0x34 and `variant` @ +0x2C from the wrong byte offset because the per-slot base is computed with a wrong stride or block origin. | `class == 0` / garbage while `level` @ +0x3A decodes correctly (the level and class are nearby in the same block; if only class is wrong, the base is misaligned). | Ensure each slot's descriptor base = `descriptor_block_start + 880·k` (index-preserving; confirmed stride is 880 bytes; see §3.2). |
| 2 | **Non-zero `variant` or `variant == 3` sentinel** — `appearance_key = 5·(class + 4·variant) − 24`; `variant == 3` yields key 0 (the invisible-actor sentinel, §3.3.7), and a non-zero `variant` legitimately resolves to a key outside the starter set (e.g. `{11, 16, 26, …}`), which may trip the high-tier collapse guard (§3.3.7). | Slot renders nothing when it should, or renders only a body-only (slot 3) mesh for a non-starter character. | Faithfully decode `variant` @ descriptor +0x2C; treat `variant == 3` as no-render (invisible); pass `variant` to the formula; implement the high-tier collapse guard against `ActorVisualGlobal[3]`. |
| 3 | **Equip-table overlays skipped** — the visible outfit comes from the 20×16-byte equipment table at descriptor +0x58 via the per-part 64-bit key lookup (§3.3.7). A port that loads only the base body `.skn` and skips the `{3, 4, 6, 2, 11}` overlay loop shows only the bare skeleton body for any equipped character. | Slot renders a naked/default body instead of the character's worn gear. | Implement the per-part `key64 = gid + 1e9·(slot + 100·appearance_key)` lookup for all overlay slots `{3, 4, 6, 2, 11}`, plus the separate rigid weapon attach for slot 14 (§3.3.7). |
| 4 | **Occupancy-gate mismatch** — the render path builds an actor only when `faceA` (u16 @ descriptor +0x2E) is nonzero (§3.2); the enter/create-route path tests `name != "@BLANK@"` (§3.1). If the two fields disagree (stale or garbage data) a preview is built for a logically-empty slot, or omitted for a real character. | Actor appears at an empty slot, or a real slot is dark/empty. | Keep both occupancy signals in sync with the server-supplied data; render iff `faceA != 0`. |
| 5 | **Catalog miss or collapse fallthrough** — if `AnimCatalog_LookupByKey` (skeleton/visual record) or `AnimCatalog_FindSkinByKey64` (per-part skin) misses for the resolved key, the actor or the part is destroyed / silently skipped, surfacing as a missing or substitute mesh. The two data-driven values that govern these lookups — `ActorVisualGlobal[3]` (the high-tier threshold) and the concrete `model_class_id → bind-pose handle` map — are recovered in mechanism but their runtime values are data-pending; do not invent them. | A specific class or outfit variant is invisible or shows a substitute mesh while the formula and equip table are otherwise correct. | Feed the correct `model_class_id` and verify the per-part `key64`; the catalog contents are VFS-data (asset-analyst lane, see `specs/skinning.md`). |

**The slot-k wrong-mesh problem is always an *input* problem** (causes 1–4) or a *catalog-miss* (cause 5),
not a skinning-math error — the deform chain is settled (see `specs/skinning.md` §0). The diagnostic
sequence: confirm the descriptor base is aligned → confirm `class`/`variant` reads → confirm overlay
slots are iterated → confirm `faceA` gate → confirm catalog entries are populated.

## 3.4 Slot availability vs lock flags (CODE-CONFIRMED byte source)

> **HEADLINE CORRECTION — there are NO 2D slot-select tabs; slot selection is 3D ray-pick
> (binary-won, counter-check IDB SHA 263bd994, static-only).** A static walk of the command-handler
> dispatch settled what the three top-strip buttons actually do, and how a slot is picked:
> - **Actions 1 / 2 / 3 are the three top-strip COMMAND buttons**, NOT slot-1/2/3 tabs:
>   **action 1 = create-new-character** (reveals the create sub-tree), **action 2 = enter-game**
>   (gated on `*(detail+0x1BC) == *(detail+0x1B8)`, then sends the enter-game packet),
>   **action 3 = sub-panel / class-info toggle** (reveals `this+5816`, plays the UI click SFX).
> - **There are no 2D slot-select tab widgets at all.** Selection is done **entirely by 3D ray-pick**
>   over the five preview actors (§3.3.3 / §3.5.5): the command handler's mouse branch
>   (event type 4, non-command subtype) unprojects the click pixel to a world ray, places each actor
>   at its baked stage offset, builds a per-actor AABB (±6 X/Z, world-Y band [70, 92]), and ray-tests;
>   the first hit writes the picked slot. The picked index → committed-slot field → the `1/7` packet
>   byte 0 (§3.1 / §4 of the roster packet spec).
> - **Supersede:** any earlier "Slot 1/2/3 tabs (slots 4–5 not yet enumerated)" framing of actions
>   1/2/3 is **wrong and retired** — the 5-slot model has **no** 2D tabs, so there is no missing 4th/5th
>   tab to enumerate. The 5-slot count comes from the descriptor block / 3D row, not from tab art
>   (§3.1 / §3.3). The §11.5b "slot tab" labelling of actions 1/2/3 is corrected by this note; treat
>   the 113×40 art rows there as the **top-strip command-button art**, not per-slot frame tabs.

Two per-slot flag arrays gate enter/render, now pinned to their byte source on the select-window
object:

- the **lock / creating** flag = select-window field **`+0x1548 + slot`** — drives the yaw-π facing
  (§3.3.2) **and** blocks enter;
- the **occupied / selectable** flag = select-window field **`+0x148C + slot`** — the
  **server-supplied per-slot flag**; gates the `1/7` select/manage click.

**Selectable-for-enter = lock clear AND occupied; creating/locked = lock flag set.** (The exact
**wire** semantics of the server slot-flag — delete-pending vs rename-cooldown — remain
capture-pending; Open question 6.)

**The `+0x1548` per-slot byte also gates the per-slot info-row overlay (CODE-CONFIRMED mechanism).**
The per-slot roster-row refresh (`SelectWindow_RefreshSlotInfoRow`) reads `this + 0x1548 + slotIndex`
and swaps a small **mutually-exclusive overlay set** accordingly:

- **byte nonzero** → the conditional **action-61 overlay button** (§11.5c) is **hidden** (`SetVisible 0`),
  and the sibling triad takes one state arrangement (one sibling off, two on);
- **byte zero** → the action-61 button is **shown** (`SetVisible 1`), and the sibling triad takes the
  opposite arrangement.

Just before this branch the same refresh unconditionally **shows the three per-slot name / level /
position labels** in a 3-iteration loop. The flag is written by the slot-management routines
(slot-record write sets it to 0 when a character is populated; create / delete / reset paths also
mutate it), so the show/hide decision is **genuinely runtime-data-driven** off the live roster, not a
compile-time constant. **OPEN@DEBUGGER:** the exact semantic of the `+0x1548` byte (occupied vs locked
vs selected vs deletion-pending) and the action-61 button's on-screen role/click handler — both owe a
live-debugger confirm (no debugger this campaign).

## 3.5 The character-select preview camera — ENTRY DOLLY KF0→KF1 (CODE-CONFIRMED)

The select window builds a dedicated preview camera as **two distinct objects**: a bare **projection
camera** (holding only FOV / near / far) and a **separate camera-PATH rig** (holding the keyframe
table, the per-keyframe angle channels, the live keyframe index, and the per-frame interpolation). The
rig is ticked every frame and is what actually drives the on-screen view; the projection camera only
supplies the lens. The realised motion is an **entry dolly**: on scene-enter the rig blends from
keyframe 0 to keyframe 1 over ~2.0 s (position-lerp + orientation-slerp), then holds keyframe 1 for the
rest of the screen, responding only to the player's manual boom/yaw input overlay.

> **HEADLINE CORRECTION — FREE-LOOK keyframed camera, NO look-at point (CODE-CONFIRMED values;
> KF2..5 arming DEBUGGER-PENDING).** The rig is a **free-look keyframed camera**, **not** a target-orbit
> rig: the orbit / pivot offset is baked to **(0, 0, 0)**, so there is **no look-at point** and no
> separate target node. Each keyframe carries an absolute eye position **and** an explicit
> **orientation as Euler (yaw, pitch)** — the view direction comes from the per-keyframe Euler angles,
> not from aiming at a world point. This supersedes the earlier "look-at orbit point" framing in
> §3.5.4 (the boom/orbit-point language below is retained only as a redirect; read it as: eye =
> keyframe position, orientation = keyframe Euler, manual zoom is a small forward/back dolly on the
> view axis). The exact values:
>
> - **KF0 (entry-dolly start) = world (515.549, 137.266, −9397.710)** — EXACT (this was only
>   approximate in the prior reading). KF0 faces **yaw 2.40° / pitch −6.00°**.
> - **KF1 (entry-dolly end / resting pose) = world (512, 87, −9652)**, faces **yaw 0.785° /
>   pitch −2.67°**.
> - **KF2 = (343, 104, −9734); KF3 = (471, 115, −9812); KF4 = (622, 75, −9802.5);
>   KF5 = (662, 130, −9746)** (absolute).
> - **Entry blend** = a single **2.0 s KF0→KF1** transition: position **lerp** + orientation **slerp**,
>   with blend progress `dt = elapsed × 0.0005` (clamped to 1.0 → 2000 ms). The entry leg is a
>   **plain lerp/slerp — NO parabolic mid-arc bow**; the parabolic-bow weight only activates when **both**
>   keyframe indices are ≥ 2, which never happens on the KF0→KF1 entry.
> - **Arming:** only index **0** (rig constructor) and index **1** (scene reset) are armed in char-select.
>   **KF2..KF5 have no static arm site in this scene and are likely dead here (DEBUGGER-PENDING).**
> - **Projection (EXACT):** vertical **FOV 50°**, **near 5.0**, **far 15000.0**.

> **CYCLE 6b RECONCILIATION (2026-06-20) -- the manual boom is the input overlay, NOT a replacement
> for the entry dolly.** A CYCLE 6b camera re-walk independently re-confirmed the projection (FOV 50,
> near 5.0, far 15000) and the framed point near (508, 70, -9758), and found the per-frame **manual
> boom**: two keys add/subtract a boom-distance scalar on the scene-graph node at +/-10.0 units per
> second, no clamp, no easing. That pass did **not** chase the separate camera-PATH rig and therefore
> could not see the KF0->KF1 dolly -- the same omission the HEADLINE CORRECTION above already warns
> about (the prior "single static camera" reading made the identical mistake). So the boom is the
> **manual zoom overlay** layered on top of the entry dolly's resting pose, exactly as this section
> states ("responding only to the player's manual boom/yaw input overlay"); it does **not** refute the
> keyframed entry dolly. The yaw turntable on the selected create/zoom preview is +/-2.0 rad/s on the
> Y axis, input-driven while held, the accumulator zeroed on slot change -- consistent with 4.2.
> `// confirmed: static IDA 2026-06-20`

> **CORRECTION — UN-REFUTE the camera path (CODE-CONFIRMED, exhaustive static re-walk).** A prior
> reading concluded "single static camera; the orbit is REFUTED — no keyframes, no index, no
> lerp/slerp/ease." **That reading analysed only the bare projection camera (FOV/near/far/anchor) and
> never chased the separate path rig** — it is wrong. The 6 position keyframes, the 12 π-scaled
> yaw/pitch angle channels, the live keyframe index, and the position-lerp + orientation-slerp + ease
> blend over ~2.0 s **all exist and are CONFIRMED** in this build; they were simply on the object the
> prior pass missed. The corrected truth is an **entry dolly KF0→KF1** — which is **neither a full
> orbit nor a static camera**. Trig also exists: the quaternion builder uses half-angle trig (it was
> hidden behind mislabelled library routines). What genuinely does NOT happen in *this* scene's
> realised motion is a **full multi-waypoint orbit**, any **auto-advance through keyframes 2–5**, and
> any **select-focus camera retarget** — see §3.5.2 (only indices 0 and 1 are ever armed) and §3.5.5.

This **supersedes** the earlier approximate "7-waypoint" reading referenced from
`client_runtime.md §7.3`: the keyframe table is **6 keyframes, not 7**; the rig is **constructed at
index 0** and the entry scene-reset arms **index 1**, so the player sees the **KF0→KF1 dolly** then a
hold at index 1 (§3.5.2).

### 3.5.1 Scene & projection (CODE-CONFIRMED)

> **CORRECTION (CODE-CONFIRMED) — the char-select is a 3D GScene built on `map000`, NOT
> "map area 52200".** Earlier readings of this spec recorded the active map area as **52200** with a
> sub-area of **0x30 (48)**. A re-read of the scene builder shows those two values are **not** a map
> area id: the scene activates **area code 0**, which is rendered into the three-digit map-folder
> string **`"000"`** → the world folder **`data/map000`**. The triple that earlier readings mistook
> for "area 52200 / sub 0x30" is the **game-clock / weather** argument:
> - **52200** is a **time-of-day** value — **52200 seconds = 14:30** (afternoon) — fed to the world
>   clock setter, which validates it against the seconds-in-a-day bound (86400).
> - **48** is a **time / weather sub-index** (a discrete value bounded at 48), not a map id.
>
> So the char-select scene **reuses the real in-world environment, frozen at 14:30**, on `map000` — a
> full 3D world backdrop, not a flat 2D screen. The afternoon clock is why the backdrop is lit and
> sunny. The 3D scene composition / world coordinates / cells / assets are specified in the new
> **§3.7**; the environment & lighting in **§3.6**. The struck-through "52200 / 0x30 = area" row below
> is retained only as a redirect.

| Property | Value | Notes |
|---|---|---|
| Scene name | `"select"` | the named char-select 3D scene root |
| Base world | **`data/map000`** (area code **0** → folder string `"000"`) | the real world map, reused as the backdrop — see §3.7 |
| ~~Active map area 52200 / sub 0x30~~ | **NOT a map area** | superseded: `52200` = time-of-day clock (14:30), `48` = weather sub-index — see the correction above and §3.6 |
| Camera type | perspective | a perspective camera node |
| Vertical FOV | **50°** | `π · 50 / 180`, then **divided by the aspect ratio** (screen width / screen height) before being set as the projection field-of-view |
| Near clip | **5.0** | |
| Far clip | **15000.0** | |

> The FOV-over-aspect form means the legacy projection FOV scales with the window aspect. A
> reimplementation that uses a standard "vertical FOV + aspect" projection should set vertical FOV =
> 50° and let the renderer apply aspect normally; the legacy `fov / aspect` is the same framing on a
> 4:3 reference canvas.

### 3.5.2 The keyframe table & the armed-index model — entry dolly only (CODE-CONFIRMED)

The path rig holds a table of **6 position keyframes**, each a 3-float `(x, y, z)` triple. A **base
anchor of `(+2048, 0, −6144)`** (= the stage origin, §3.7.2) is added to every raw keyframe to place
the path in stage-world space. The anchored keyframe positions:

| Keyframe | Anchored position (x, y, z) | Status in this scene |
|---|---|---|
| 0 | ≈ (516.55, 137.27, −9386.65) | **armed at construction** (dolly start) |
| **1** | **(512.00, 87.00, −9652.00)** | **armed by the entry scene-reset** (dolly end / held) |
| 2 | ≈ (341.00, 104.00, −9734.00) | present in the table, **never armed** (dormant) |
| 3 | (anchored from the table) | present, **never armed** (dormant) |
| 4 | (anchored from the table) | present, **never armed** (dormant) |
| 5 | (anchored from the table) | present, **never armed** (dormant) |

> **Only indices 0 and 1 are ever armed (CODE-CONFIRMED, exhaustive).** The keyframe index is written
> by exactly one routine (the keyframe-apply call), reached by exactly two constant-argument call
> sites: the rig **constructor arms index 0**, and the **entry scene-reset arms index 1** (it runs once
> at tick frame ~5, after the scenery actor is spawned). Because the per-frame update blends from the
> current transform toward the armed target over the ~2.0 s window (§3.5.4), the scene **opens at KF0
> and travels to KF1**, then holds. This index-writer set is **closed and complete** — no third caller,
> no indirect reach — so KF1 is the resting pose.
>
> **Keyframes 2–5 are dormant in this scene.** The per-frame update **never advances the index itself**
> (no auto-play sequencer), and the slot-select / command dispatcher **never touches the rig at all**
> (it changes the active preview actor / highlight, not the camera). So keyframes 2–5 exist in the
> rig's tables but are **never armed by any char-select code path**; the parabolic-ease branch that
> only fires for target indices ≥ 2 (§3.5.4) is therefore **dead code for this scene**. There is **no
> full multi-waypoint orbit and no select-focus camera move** — only the KF0→KF1 entry dolly plus the
> manual input overlay.

> **All six keyframes are now EXACT (CODE-CONFIRMED).** KF0 = `(515.549, 137.266, −9397.710)` (the
> entry-dolly start, previously only approximate); KF1 = `(512, 87, −9652)` (the resting pose the
> player holds at — the value an earlier pass captured and mislabelled "the static camera"); KF2 =
> `(343, 104, −9734)`, KF3 = `(471, 115, −9812)`, KF4 = `(622, 75, −9802.5)`, KF5 = `(662, 130,
> −9746)`. Only KF0 and KF1 are armed in char-select; **KF2..KF5 have no static arm site in this
> scene and are likely dead here (DEBUGGER-PENDING).** Per the §3.5 headline correction, each keyframe's
> orientation is an explicit **Euler (yaw, pitch)**, not an aim at a look-at point.

> **Coordinate convention reminder.** These are stage-world coordinates as the legacy client stores
> them. Apply the project's world-to-engine convention (world geometry negates Z — see
> `Helpers/WorldCoordinates`) when porting; do not silently re-sign them here.

### 3.5.3 The 12 PI-scaled angle multipliers (CODE-CONFIRMED values **and** yaw/pitch split)

The camera also holds **12 angle multipliers**, each multiplied by π to yield an angle in radians.
The split is now resolved (CODE-CONFIRMED against the manipulator's keyframe-apply path): the twelve
values are **6 pitch + 6 yaw**, indexed by keyframe — **indices 0..5 = PITCH** (elevation, a rotation
about the camera's local X axis), one per keyframe 0..5, and **indices 6..11 = YAW** (azimuth, a
rotation about the world-up Y axis), one per keyframe 0..5. The keyframe-apply step builds a pitch
quaternion from the 0..5 value and a yaw quaternion from the matching 6..11 value, multiplies them
into that keyframe's orientation, then blends that orientation against the previous keyframe per the
easing law (§3.5.4).

The per-keyframe **pitch** deltas are small (about ±6° to ±14°), refining the −30° base tilt; the
per-keyframe **yaw** deltas are large for the inner keyframes (keyframes 2/3/4 swing roughly
−37° / −80° / +74°), i.e. the table *would* slew mostly in azimuth between presets **if those
keyframes were ever armed** — but they are **not** in this scene (§3.5.2: only indices 0 and 1 are
armed). For the realised entry dolly the only two angle entries that matter are **keyframe 0** (start)
and **keyframe 1** (rest): for keyframe 1 the seed angles are **pitch ≈ −2.67°** and
**yaw ≈ +0.785°**. The keyframe 2–5 angle channels remain in the table but are dormant here.

| Index | Axis | Keyframe | Multiplier | × π (rad) | ≈ degrees |
|------:|:----:|:--------:|-----------:|----------:|----------:|
| 0 | PITCH | kf 0 | −0.03333334 | −0.104720 | −6.000 |
| 1 | PITCH | kf 1 | −0.01483333 | −0.046600 | −2.670 |
| 2 | PITCH | kf 2 |  0.00333333 |  0.010472 |  0.600 |
| 3 | PITCH | kf 3 | −0.01111111 | −0.034907 | −2.000 |
| 4 | PITCH | kf 4 |  0.04333333 |  0.136136 |  7.800 |
| 5 | PITCH | kf 5 | −0.07666667 | −0.240855 | −13.800 |
| 6 | YAW | kf 0 |  0.01333333 |  0.041888 |  2.400 |
| 7 | YAW | kf 1 |  0.00436111 |  0.013701 |  0.785 |
| 8 | YAW | kf 2 | −0.20333332 | −0.638790 | −36.600 |
| 9 | YAW | kf 3 | −0.44444445 | −1.396263 | −80.000 |
| 10 | YAW | kf 4 |  0.41276109 |  1.296727 |  74.297 |
| 11 | YAW | kf 5 |  0.29111111 |  0.914553 |  52.400 |



Other constructor scalars (CODE-CONFIRMED): a `1.0` and a `10.0` speed/rate scalar pair, identity
initial scale/orientation values, and a constructor-default active keyframe index of 0 (wired to
**1** at runtime — §3.5.2). The **`1.0`** scalar is a time→input-rate multiplier (it scales the
per-frame millisecond delta when damping manual-orbit input); the **`10.0`** scalar is the manual
zoom/yaw/pitch input-rate constant. **Neither drives the automatic keyframe framing** — the keyframe
tween uses its own normalizer (§3.5.4).

### 3.5.4 Framing law — look-at, eye, easing (CODE-CONFIRMED; eye = orbit point at zoom 0)

> **SUPERSEDED FRAMING — read this as a FREE-LOOK camera (see the §3.5 headline correction).** The
> "orbit point + look-at target" language in this subsection is an older model. The IDA-exact re-walk
> shows the rig is **free-look**: the orbit/pivot offset is baked to **(0,0,0)**, so the "orbit point"
> IS simply the keyframe's eye position, and there is **no look-at target** — the view direction is the
> keyframe's explicit **Euler (yaw, pitch)**. The boom/zoom detail below is still correct as a small
> forward/back dolly on the view axis (manual zoom), but wherever this subsection says "look-at target =
> orbit point", read it as "eye = keyframe position; orientation = keyframe Euler". The entry KF0→KF1
> blend is a **plain lerp/slerp with no parabolic bow** (the bow only applies between inner keyframes
> ≥ 2, which never occurs on entry).

The camera manipulator is a scene-graph node, not an explicit eye/target pair. Each frame it computes:

- a current **orbit point** (a keyframe-derived world position), and
- an **orientation** (yaw/pitch quaternion),

then sets the camera eye to **`eye = orbitPoint + Rotate(orientationQuat, boom)`**, where the boom
vector points from the target out to the eye and its length (the zoom distance) is hard-clamped on the
manual-zoom path.

> **CONFLICT C3 — boom-Z clamp literal = `26.0` (RESOLVED, static IDA, 2026-06-21).**
> A dedicated re-walk of the SelectCamera vtable handlers confirms the **static boom-Z clamp literal is
> `26.0`** (the boom/zoom accumulator is clamped to `[0, 27]` in the wheel-boom and per-frame tick
> handlers, with `26.0` as the boom-Z depth limit). The earlier `22` reading is **superseded and dropped**
> — use **`26.0`** as the single authoritative clamp magnitude everywhere in §3.5. Low impact: this only
> bounds how far the player can wheel-zoom (the boom seed for keyframe 1 is **0**, so the static eye sits
> on the orbit point regardless; the clamp only matters once the player drives the zoom). The realised cap
> on the running client is the only remaining DEBUGGER-PENDING nuance; the static literal is settled.

(The boom-Z clamp magnitude below is `26.0`; the earlier `22` reading is dropped — see C3.) **The
look-at target is the active orbit point** (≈ world `(512, 87, −9652)` over the row
pivot ≈ `(508, 70, −9759)`) — *not* the stage origin (the stage origin is only the anchor the
keyframes are added to). The base **pitch ≈ −30°** (downward), modulated by each keyframe's stored
yaw/pitch; so the camera looks slightly **down at the standing row from in front**. The **live
keyframe is 1** (§3.5.2). (Recall the per-slot facing rule of §3.3.2: yaw 0 faces the camera; yaw π is
locked / new / creating.)

- **Look-at target (CONFIRMED):** the active (keyframe-1) orbit point ≈ world **(512, 87, −9652)**,
  which sits essentially **over the actor-row pivot ≈ (508, 70, −9759)** (§3.6 / §3.7.2) — slightly
  above and ~100 units in front of the standing row.
- **Eye (CODE-CONFIRMED — eye = orbit point at zoom 0):** the boom (zoom vector) is seeded
  **(0,0,0)** with unit boom-direction **(0,0,1)** and the zoom accumulator at **0**; boom-Z is
  hard-clamped to **[0, 26.0]**. Therefore the **static eye at scene start equals the look-at orbit point
  exactly** (the camera sits on the orbit point, oriented by the −30° pitch / 0 yaw base) until the
  player applies wheel/key zoom (which grows boom-Z from 0 to ≤ 26.0). Keyframe 1 = **(512, 87, −9652)
  exactly**. The only runtime variable is the player's live zoom ∈ [0, 26.0] (a preference, not a stored
  value).
- **Easing (CODE-CONFIRMED constants and duration):** when the active keyframe changes, the orbit
  point is **linearly interpolated** and the orientation **spherically interpolated (slerp)** over a
  normalized progress `t`. For transitions among the inner keyframes (both indices ≥ 2) an extra
  **quadratic ease `(1 − t)·(2t)`** is layered over the linear blend ("linear-then-quadratic" /
  ease-in-out); the keyframe-0↔1 transition uses the plain linear blend only. The tween normalizer is
  the literal **`0.0005` (= 1/2000)** multiplying the **millisecond** elapsed since the keyframe switch
  (the engine ms clock), clamped at 1.0 → a full keyframe transition of **2.0 s** (1 / 0.0005 = 2000
  ms). The earlier "0.5 s" annotation was a stale comment and is **superseded**. **Open question 12 →
  CLOSED.**
- **Auto-advance (CONFIRMED — none):** no timer auto-cycles the keyframe index. The per-frame update
  law never re-applies a keyframe; it only reads the active/previous index to drive the blend. The
  index is changed only by an explicit keyframe-apply call — at construction (index 0) and once when
  the actor row is built/rebuilt (index 1, the live value); no clock or counter advances it.

#### Interactive camera — zoom is a dolly, slot interaction never re-aims (CODE-CONFIRMED)

The only camera response to user input on the Select screen is a **player-driven mouse-wheel dolly**.
The manipulator keeps a manual **zoom/dolly accumulator** (camera object field at offset +0x114),
which the mouse wheel feeds (and which two zoom keys can also drive, damped toward zero when no input
arrives). It is **clamped to ±4**. This accumulator is the field whose role was previously ambiguous
("zoom or pitch"): it is now **definitively zoom/dolly, not pitch.** Each frame it scales the unit
**boom direction** and is added into the **boom vector**, lengthening or shortening the boom; the
boom's depth (Z) component is then **hard-clamped to the range [0, 26.0]** before the boom is rotated by
the orientation and added to the orbit point to make the eye. Manual pitch and yaw are **separate**
accumulators (each clamped ±1.0) layered on the −30° pitch base / 0 yaw base; they are not the +0x114
field.

So the **final eye distance = the keyframe-stored boom-depth seed, extended or retracted by the ±4
wheel/key accumulator along the boom, hard-capped to [0, 26.0].** The **boom-depth seed for keyframe 1
is 0** (CODE-CONFIRMED: boom seeded (0,0,0), unit boom-direction (0,0,1), zoom accumulator 0), so the
static eye sits **on** the orbit point until the player zooms — see §3.5.4 (Eye) / §3.5.5.

- **Slot select / hover does NOT re-aim or zoom the camera (CODE-CONFIRMED).** There is no camera
  travelling, dolly-on-select, or focus-on-selected behaviour. The camera holds keyframe 1 for the
  entire Select screen, framing the **whole actor row**. Clicking or hovering a slot moves nothing on
  the camera — no keyframe switch, no orbit-point move, no boom/zoom change. Slot interaction only
  (a) highlights the hovered actor and plays its select/idle clip, and (b) fills the UI labels (name /
  level / position). All five slots share the single keyframe (1); there is no per-slot keyframe
  table. The camera eye/look-at delta from any slot or state change is therefore **zero** — the only
  possible camera motion is the player's own ±4 wheel dolly.
- **Create-mode moves the ACTOR, not the camera (CODE-CONFIRMED).** Entering character creation does
  not move the camera. Instead the single create-preview actor is placed **+56.5 units forward in Z
  (toward the camera)** relative to the centre of the select row, so the lone preview character fills
  the same frame the five-actor row occupied. The camera keyframe, orbit point and boom are identical
  between select and create. For a 1:1 port: keep one fixed camera for both modes, never tween the
  camera on slot click, and for create-mode move the single preview actor +56.5 in the forward
  (world −Z) direction instead of moving the camera.



### 3.5.5 Static-complete vs runtime-pending (explicit)

- **Static-complete (CODE-CONFIRMED):** all 6 keyframe positions, the `(+2048, 0, −6144)` anchor
  offset (= stage origin), the 12 π-scaled angle multipliers **and their axis split (0..5 pitch,
  6..11 yaw)**, FOV 50° / aspect-divided, near 5.0, far 15000.0, the `1.0` (input-time) / `10.0`
  (manual-zoom) scalars, scene name `"select"`, base world `map000`, the **live keyframe index = 1**,
  the look-at target = active orbit point, the boom-zoom clamp ≤ 26.0, the base pitch ≈ −30°, the
  lerp/slerp + inner-keyframe quadratic-ease law, the **+0x114 manual accumulator = zoom/dolly
  (±4 clamp), not pitch**, the **no-auto-advance** rule, the **no-re-aim-on-slot-select** rule, and
  the **create-mode +56.5 actor-Z offset** (camera unchanged).
- **NOW RESOLVED (previously runtime-pending, closed CODE-CONFIRMED this pass):**
  - **The yaw-vs-pitch assignment** of the angle indices → **0..5 = pitch, 6..11 = yaw** (§3.5.3).
  - **The +0x114 zoom-vs-pitch question** → **zoom/dolly** (§3.5.4).
  - **Whether slot selection auto-switches the keyframe** → **no** (no auto-advance; the index is only
    set by an explicit keyframe-apply at construction = 0 and row-build = 1; slot interaction never
    re-aims).
  - **The live boom-depth (zoom) seed for keyframe 1** → **seed = 0** (CODE-CONFIRMED: boom seeded
    (0,0,0), unit boom-direction (0,0,1), zoom accumulator 0; boom-Z clamp [0, 26.0]). The static eye
    therefore sits **on** the orbit point at zoom 0 (§3.5.4).
  - **The precise eye world coordinate at scene start** → **= the look-at orbit point** = keyframe 1
    = **(512, 87, −9652)** exactly (boom = 0 at start); the only runtime variable is the player's live
    zoom ∈ [0, 26.0] (a preference, not a stored value).
  - **The tween duration** → **2.0 s** (literal `0.0005` = 1/2000 over the ms clock, clamped at 1.0;
    the "0.5 s" annotation was stale). **Open question 12 → CLOSED** (§3.5.4).
- **NOW CODE-CONFIRMED (was runtime-pending residual 1) — KF2..KF5 are never armed.**
  The keyframe-apply method has **exactly two callers**: construction (constant index **0**) and the
  actor-row build / scene-reset (constant index **1**). **No slot-click, hover, or create path passes
  an index ≥ 2** (the only other write to the "to" index is the tween-end self-snap "to" = "from", not
  a new arm). **KF2..KF5 (and their inner yaw/pitch channels) are dead data in this scene** — no UI
  action ever applies a keyframe other than 1.
- **Slot ray-pick AABB (CODE-CONFIRMED — NEW).**
  The command handler's mouse branch unprojects the click pixel to a world ray, then tests each of the
  five actors against an **axis-aligned box** with **half-extents ±6 in X and ±6 in Z** and a **fixed
  world-Y band `[70.0, 92.0]`** (the Y band is not actor-centred). **First hit wins.** The ±6 X boxes
  tile edge-to-edge against the **12-unit** slot spacing.
- **Five slot world placements (CODE-CONFIRMED — NEW).**
  Anchor **(2048, 0, −6144)** + per-slot offset → absolute **X = {488, 500, 512, 524, 536}** at
  **Y = 0**, with a shallow Z bow **{−9737, −9738, −9738.5, −9738, −9737}**. **X spacing 12.0**,
  centred ≈ 512, **lineup scale 70.0**. **Facing yaw = π** if the per-slot facing byte is `1`, **else
  yaw = 0** (default front, +Z forward) — the default lineup faces front.
- **Double-music guard = category-0 single-voice replace (CODE-CONFIRMED).**
  The scene BGM is sound **category 0**, single-voice by construction: a second BGM start on a
  mismatched id **frees + replaces** the prior voice (a matching id does nothing) — it **overwrites,
  does not stack**. The guard is the category-0 single-slot replace semantics, not a window flag.
- **Manual input overlay — exact app-input codes, rates, decay & clamps (CODE-CONFIRMED — NEW).**
  The per-frame tick folds three small accumulators on top of the resting keyframe pose; `dt =
  frameMs × 0.001`. Each accumulator is driven by a held app-input code, decays when no input arrives,
  and is hard-clamped:
  - **Zoom** (codes **1028 = in / 1029 = out**): boom accumulator (camera field +0x114, byte +276)
    `+= / −= dt × 10.0`; decays `× 0.8` per idle frame; clamped to **[−4.0, +4.0]**.
  - **Yaw nudge** (codes **1003 / 1002**): yaw-input accumulator (+288) `−= / += dt × 0.1`; decays
    `× 0.5`; clamped to **[−1.0, +1.0]**.
  - **Pitch nudge** (codes **1000 / 1001**): pitch-input accumulator (+284) `−= / += dt × 0.1`; decays
    `× 0.8`; clamped to **[−1.0, +1.0]**.
  The live yaw/pitch chase the armed keyframe's yaw/pitch with these accumulators added, and the
  orientation quaternions are rebuilt each frame from the resulting live angles (tiny ~0.001 deadbands
  prevent residual drift). A separate two-key boom helper on the select window adds `dt × 10.0` to the
  same boom field on keyboard scancodes **72 / 73** (with no clamp in that helper — the [−4, +4] clamp
  is enforced by the tick).
- **Wheel boom-Z depth — gate 27.0, cap 26.0, floor 0.0, seed 10.0 (CODE-CONFIRMED — refines C3).**
  The mouse-wheel handler (message type 8) adds `wheelDelta × 1e-6` to the boom accumulator (+0x114).
  When that accumulator passes **+4.0** the separate boom-Z depth field (camera byte +572) steps
  **down by 2.0**, floored at **0.0**; when it passes **−4.0** the depth steps **up by 2.0**, capped so
  that once it would exceed the gate **27.0** it is written to **26.0** (the depth saturates at 26.0).
  The boom-Z depth field is **seeded to 10.0** in the rig constructor — so the "10.0 scalar" of §3.5.3
  is specifically this boom-Z depth SEED (camera byte +572), distinct from the manual-key/zoom boom
  rate (also magnitude 10.0). This refines §3.5.3 / C3: clamp value 26.0, gate 27.0, floor 0.0, seed
  10.0; the boom accumulator itself stays clamped to [−4, +4].
- **Runtime-pending (still NOT confirmed — debugger-pending, do not invent):** none remaining for the
  keyframe-arm question (it is now CODE-CONFIRMED above). The manual-zoom boom-Z realised cap is the
  only residual (§3.5.4 / C3 below).

An implementer should treat the orbit geometry, the live keyframe (1), the look-at target = orbit
point = (512, 87, −9652), the boom seed = 0 (eye on the orbit point at zoom 0), the yaw/pitch axis
split, the zoom-dolly law, the no-re-aim rule, the 2.0 s tween, the easing law, the **ray-pick AABB
(±6 X/Z, Y∈[70,92])**, the **five slot placements + per-slot yaw**, and the **category-0 single-voice
BGM guard** as authoritative.



## 3.6 Char-select environment, lighting & ambient FX (CODE-CONFIRMED structure; colours data-driven)

The select scene does **not** author a bespoke select-only sky or light rig. It activates the **real
area-0 world environment** (§3.5.1 correction) and **freezes the world clock at 14:30** (time-of-day
value 52200, weather sub-index 48). The sky, sun direction, fog and ambient colour are therefore
whatever the **area-0 map + the area-0 sky data + the 14:30 clock** produce — a **parametric sky**,
not a `.box` skybox file (no skybox file exists in the VFS for this scene; §3.7.4). The sky-parameter
files it reads are the **area-0 (`…0.bin`) family**, not the area-015 family — see the supersede in
§3.6.3.

### 3.5.6 Create-form close-up framing — ACTOR-ONLY in this build (CODE-CONFIRMED; boom-dolly DEMOTED, CONFLICT C2)


The character-CREATE view is a CLOSE-UP of one character — but it is the **SAME 3D scene with the SAME
fixed camera (KF1)**; the close-up is achieved by **moving the actor, NOT the camera**.

- **CODE-CONFIRMED mechanism (actor-only).** In this build (SHA 263bd994) an **exhaustive float-literal
  scan of the create paths found NO camera boom write** — no `−1.0`/`+15.0` pair, no FOV / near / far
  change on any create path. The single create-preview actor is placed **≈ 56.5 units nearer the lineup
  centre** (at anchor + `(−1536.5, 0, −3538.0)`) and **scaled 81.0** (vs the lineup's **70.0**; the
  **81/70** ratio is confirmed by both lanes). The camera keyframe, orbit point and boom are identical
  between select and create.
- **DEMOTED claim (CONFLICT C2).** The campaign-9c "create-open writes a camera **boom Y = −1.0,
  Z = +15.0**" reading is **NOT supported by this build's code** and is **DEMOTED to DEBUGGER-PENDING /
  possibly-stale-from-another-build**. (The realised sign of `eye = orbit + Rotate(quat, boom)` cannot
  be read statically; a live read could differ, but the static evidence in this build is actor-only.)
- **Engineer guidance (explicit).** Keep **one fixed camera (KF1)** for both select and create; **never
  tween the camera on create** (or on slot click). For create-mode, move the **single preview actor
  +56.5 in world −Z** (toward the camera) and **scale it 81.0** — that is what fills the frame.

Both the **81/70 scale ratio** and the **≈ 56.5u-nearer actor placement** are **CODE-CONFIRMED** (both
lanes agree); the boom-dolly literals are DEBUGGER-PENDING (C2). The KF2..KF5 dead-arm is now
CODE-CONFIRMED (§3.5.5).

### 3.6.1 Lighting rig (CODE-CONFIRMED structure; VALUES data-driven — read light0.bin keyframe 29)

The scene attaches the shared **sky/time manager** singleton's render node into the 3D scene as a
child (the same manager the main world uses). That manager builds the lighting rig:

- **≈ 5 positional lights** (the sun plus fill lights), each with a light range/radius of **≈ 1024**.
  These are the **achromatic** area-0 sky/time lights (white baseline, grey-tinted by the 14:30 data),
  **not** a warm torch/brazier point-light rig: the scene builder creates **no** brazier point-lights
  at all (§3.6.5). The warm character of the cavern is the **additive FIRE BILLBOARDS of the ambient
  effect**, not a light source — do not model the braziers as warm point-lights (§3.6.5 render model).
- A **white (1.0) colour-scale baseline** (identity colour multipliers) and a **black, full-alpha
  clear colour** baseline, both later tinted by the time-of-day-driven sky data.
- **Sun / light state is produced by a 48-keyframe-per-day light table (CODE-CONFIRMED structure).**
  The day is divided into **48 keyframes (one per 30 minutes = 1800 s)**, **linearly interpolated** by
  the time-of-day in seconds. Each keyframe is **51 floats = 3 light slots × 17 floats** (sun /
  secondary / ambient), read from **`data/sky/dat/light0.bin`** — **not** a clock→angle trig formula.
  At the frozen char-select clock **52200 s = 14:30 the index is keyframe 29 exactly (interpolation
  fraction 0)** — so the exact 14:30 sun direction / colour is precisely the **51 floats of
  light-keyframe 29** (no interpolation).
- **No hard-coded ambient colour literal** exists in the scene builder — the final on-screen sun /
  ambient / fog **colours are data-driven** through the sky/time manager and the area-0 sky data at
  the frozen 14:30 clock. (Light count, range ≈ 1024, white baseline, black clear, the **48-keyframe
  table structure** and the **14:30 → keyframe 29** index are CODE-CONFIRMED; the per-keyframe
  **colour/direction VALUES remain data-driven** — read `light0.bin` keyframe 29, an **asset-analyst
  lane**, not static-settleable.)

> **Env-loader code-level facts (binary-won, counter-check IDB SHA 263bd994, static-only).** For area 0
> at 14:30 the env loader sets a small block of dome/sky enables before loading the data-driven
> environment binaries: **stardome enabled = 1** and **clouddome enabled = 1** (the sky-dome system is
> on), with **four adjacent env flags also set to 1**; the sky is then initialised by the sky-system
> init from the area's sky data, **weather** from the area weather `.bin` (with the weather sub-index
> 48 selected), and **wind** from the area wind data. So the only code-level lighting facts are these
> enables plus the weather/wind init — the concrete **ambient / directional / fog RGB are read from the
> map000 env data evaluated at the 14:30 keyframe**, never from code literals. A faithful port reads the
> map000 env / `light0.bin` keyframe 29 with stardome + clouddome both on.

**CAMPAIGN 9c — the warm/bright recipe + the "too dark" cause (IDA two-witness).** The scene has ZERO
light objects; the dominant light is a near-WHITE DEVICE AMBIENT floor: `floor = (OPTION_BRIGHT/100) ×
255`, at default BRIGHT=100 → device ambient = white (1,1,1), committed to the D3D ambient render-state.
The per-keyframe ambient table is INERT (multiplier 0.0). The cell geometry carries NO baked vertex
colour (the `.bud` FVF lacks a diffuse channel; the `.ted` vertex block is uniformly neutral-white) — so
warmth is white ambient on neutral-white geometry at FULL luminance, plus the additive emissive
brazier/glow/waterfall sprites. The `data/effect/map/<cell>.bmp` is the 2D MINIMAP bitmap, NOT a runtime
lightmap (never sampled by the char-select builder). The waterfall's BLUE comes from per-particle BGRA
diffuse in the `.xeff` (the `waterfall-pie` textures are pure white) — NOT from the texture. THE "TOO
DARK" CAUSE in a Godot port: PBR ambient at energy 1.0 lands Lambert-attenuated, so the unit-white floor
reads darker than the original flat full-bright ambient — drive the WorldEnvironment ambient brighter
(energy parity) with sky-contribution 0 and neutral/unshaded materials; do NOT add scene point-lights
(the original has none).

<!-- source: campaign9c scene_lighting.md + scene_lighting_assets.md (IDA two-witness; white device ambient, no lightmap, neutral-white geometry, waterfall blue = per-particle diffuse) -->

### 3.6.2 Fog / sky (CODE-CONFIRMED field)

The select scene explicitly **zeroes the fog-blend OFFSET field** — the sky-param struct's **+8**
field, set via `base · factor · 1.4` with **factor 0**. **This is now CODE-CONFIRMED directly in the
select-scene build helper** (a deep re-walk found the builder calls the sky module's fog-blend-offset
setter with the literal argument **0.0**), **promoted from the earlier inferred reading**. The renderer consumes `colour0 − offset0`, so this is the **distance-fog blend offset**;
zeroing it turns **distance fog off** behind the preview row, so the row reads clearly. (The resulting
fog **colour** still depends on the area-0 light/material data — data-driven.)

### 3.6.3 Sky-data asset set for the char-select area (CODE-CONFIRMED — area 0, NOT area 015)

> **SUPERSEDE (CODE-CONFIRMED) — the char-select sky is the area-0 (`…0.bin`) family, NOT the
> area-015 family.** An earlier version of this section asserted the char-select scene reads the
> **area-015** sky index (`fog015.bin`, `light015.bin`, `map015.txt`, …). A re-read of the scene
> builder and of **every** sky/environment loader shows that is wrong: each loader builds its
> filename from the **raw active-area code 0**, so the char-select scene loads the **`…0.bin`**
> family (`fog0.bin`, `light0.bin`, `clouddome0.bin`, …) under `data/sky/dat/`. The `…015.*` family
> belongs to whatever in-game zone maps to area 15 and is **unrelated to char-select**. The earlier
> "area-015" reading was an existence-inference from on-disk file presence, contradicted by the
> binary. (The scene builder also derives an `area → region` mapped index, but **no** sky/effect path
> is ever built from that mapped index — only from the raw area code 0.)

The sky/environment system is a **per-area family** of binary `.bin` parameter files (with optional
human-readable `.txt` companions), **not** a single skybox file. Every member's filename is built
from the **raw active-area code** — char-select = **0** → index string `"0"` (and folder string
`"000"`, §3.5.1). The sky family the char-select scene actually reads:

| Role | VFS path (area-0) | Id source | Confidence |
|---|---|---|---|
| Sky render options | `data/sky/dat/map_option0.bin` | raw area code 0 | CODE-CONFIRMED |
| Fog parameters | `data/sky/dat/fog0.bin` | raw area code 0 | CODE-CONFIRMED |
| Wind direction / strength | `data/sky/dat/wind0.bin` | raw area code 0 | CODE-CONFIRMED |
| Cloud dome + cloud cycle | `data/sky/dat/clouddome0.bin`, `data/sky/dat/cloud_cycle0.bin` | raw area code 0 | CODE-CONFIRMED |
| Star dome | `data/sky/dat/stardome0.bin` | raw area code 0 | CODE-CONFIRMED |
| Day-cycle directional light | `data/sky/dat/light0.bin` | raw area code 0 | CODE-CONFIRMED |
| Sky material / colour table | `data/sky/dat/material0.bin` | raw area code 0 | CODE-CONFIRMED |

The per-area region / gather tables are likewise built from the area string `"000"`
(`map000.bin`, `regiontable000.bin`, `region000.bin`, `gathertable000.bin`). Per-area light/material
parameters are also embedded directly under `data/map000/` (`light0.bin` 5,312 B; `material0.bin`
9,792 B). Shared sky textures (cloud / sun / moon / star / lens-flare / precipitation) live under
`data/sky/texture/` and are global to all areas.

> **Two distinct path families — do not confuse them.** The runtime sky family is
> `data/sky/dat/…0.bin` (id from the raw area code). The separate `data/sky/map/…%d.*` directory
> (e.g. `data/sky/map/map%d.txt`, `fog015.bin`) is a **different** subsystem and is **not** the file
> family char-select loads. In particular the `data/sky/map/map%d.txt` template is referenced by
> **no live code path** in this build (a dead / editor-only slot) — it is **not** the char-select
> effect placement table. The char-select effect placement table, where one exists, is
> `data/effect/map%s.txt` with `%s = "000"` → `data/effect/map000.txt` (§3.6.5).

> **The char-select clock is 14:30, weather sub-index 48 (CODE-CONFIRMED).** The triple an earlier
> reading mistook for "area 52200 / sub 0x30" is the **game-clock / weather** argument, not an area
> id: `52200` = time-of-day in seconds (= 14:30, ≤ 86400 bounds-checked), `48` = a discrete weather
> sub-index (bounded at 48). The scene reuses the area-0 world environment frozen at 14:30; the
> sun / ambient / fog colours are whatever the **area-0** sky data produce at that clock
> (§3.6.1 / §3.6.2).

### 3.6.4 Per-cell lightmaps (CODE-CONFIRMED)

The backdrop cell carries a baked lightmap bitmap under `data/effect/map/` named by the cell coordinate
(`d000x{X}z{Z}.bmp`, e.g. `data/effect/map/d000x10000z9990.bmp`, 49,208 B = 128×128 24-bit BMP). These
are pre-baked ambient/occlusion lighting for the terrain. (~3,791 such lightmaps exist across all
areas; the char-select cell's is present.)

### 3.6.5 Ambient FX — one code-spawned effect + the placement engine (CODE-CONFIRMED placement; brazier/waterfall roles RESOLVED)

The char-select scene's fixed ambient FX come from **two distinct mechanisms**, both feeding the same
pooled map-effect spawner; the scene also pushes one ambient **sound** cue.

> **CAVEAT — `380003000` is CODE-CONFIRMED; the `char_select-u.xeff` FILENAME is data-pending
> (binary-won, counter-check IDB SHA 263bd994, static-only).** The effect **id 380003000** appears as a
> single code immediate in the scene builder and is settled. Its on-disk filename, however, is **not
> statically provable from code**: the id → file edge resolves through the xeffect list
> `data/effect/xeffect.lst` → a file under `data/effect/xeff/`. The `char_select-u.xeff` mapping below
> is a **plausible reading carried as DATA-PENDING**, to be confirmed by reading the `.lst` entry for
> 380003000 — do not treat the filename as code-confirmed.

**(a) The single code-spawned ambient effect (CODE-CONFIRMED id; filename data-pending — re-confirmed: sole spawn, no second).**
The scene builder spawns **exactly one** ambient map effect from a code immediate: effect id
**380003000**, which (per the data-pending mapping above) is read as **`char_select-u.xeff`** (the composite torch/brazier corona
effect, 68 sub-effects). It is spawned at world **(508.483, 69.887, −9758.569)** — the **centre of the
preview character row**, framed dead-centre by the camera (the same point as the terrain-init pivot,
§3.7.2) — with **identity orientation (0,0,0 / quaternion 0,0,0,1)**, **scale 1.0**, and **loop = 1** (a
standing looped effect). The builder **first clears the
active-effect list and resets the particle manager**, *then* inserts this one pooled effect — so it is
a **standing background effect for the whole select session**, not a one-shot, and starts from a known
empty state. This is the **builder's sole pooled-spawn call** — the **only** effect-id immediate
reachable from the scene builder, with **no second hidden spawn** (the whole-binary immediate count for
**380003000** is exactly one); the braziers are this **single composite `.xeff`**, not per-pillar
spawns and not `.bud`-baked. **There are ZERO other code-spawned effects** in the build path. The per-area ambient manifest path is
**`data/effect/map000.txt`** (area string "000" from area 0); that file is **absent** in the shipped
VFS (§(b) below), so no manifest records contribute.
<!-- pending render-time confirm: that ALL visible braziers trace to this single 380003000 spawn (live frame) -->

> **Effect-id → file resolution is RESOLVED.** Effect id **380003000 is the internal effect id of
> `char_select-u.xeff`** — the single composite front-end effect that carries the torch / brazier
> coronas (68 sub-effects, a yellow lens-flare family). The earlier caveat ("no `380003000.xeff` exists,
> resolution UNVERIFIED") and the `380003001.xeff` guess are both **corrected**: a prior hex→decimal
> mis-conversion produced the wrong decimal id; the byte id pins to `char_select-u.xeff`.
>
> Resolution is **by registry id, not by filename**: effect filenames are listed in the effect manifest
> `data/effect/xeff/xeffect.lst`, and each `.xeff`'s own embedded header descriptor carries the internal
> id that becomes its registry key. A spawn looks the effect up by that numeric id. So the **absence** of
> a literal `380003000.xeff` file (and of `data/effect/map000.txt`) is **expected and not a gap** — the
> braziers come from the registry-resolved `char_select-u.xeff`, joined by id 380003000. The effect-file
> byte format is owned by the effect catalogue / `formats/xeff.md`; this spec owns only the id→role join.

**(b) The per-area ambient-effect placement engine (CODE-CONFIRMED mechanism; no records for area 0).**
Beyond the single code-spawned effect, fixed-anchor ambient effects (braziers, waterfalls, portals in
*other* areas) are placed by a **per-frame, per-area** engine that, on area change, loads a text
manifest `data/effect/map<area>.txt` and then proximity- and time-of-day-gates each record's
spawn / despawn through the same pooled map-effect path. For char-select the active area is **0**, so
the manifest path is **`data/effect/map000.txt`**. The shared manifest format is documented in
`formats/effect_placement_map.md`: a leading record count, then tab-delimited records
`effect_id ⟨tab⟩ pos_x ⟨tab⟩ pos_y ⟨tab⟩ pos_z ⟨tab⟩ scale ⟨tab⟩ time_start_hour ⟨tab⟩ time_duration`.

> **`data/effect/map000.txt` is ABSENT from the shipped VFS (CONFIRMED-from-VFS).** The placement
> engine runs for area 0 but finds **no manifest file** — so it spawns **no** brazier / waterfall /
> portal records for char-select. A VFS census of all present `data/effect/map*.txt` (19 files, none
> for area 0) found **zero** records whose XZ anchor falls inside the char-select cell, and **none**
> of the three char-select-named effect files (`char_select-u.xeff`, `zone_sel_u.xeff`,
> `zone_sel2-u.xeff`) is referenced by any manifest. So for this build the **only manifest-or-code
> effect placed in the scene is the single code-spawned 380003000**; everything else visible is cell
> geometry, the cell water layer, or character-preview motion VFX.

**(c) Ambient sound cue (CODE-CONFIRMED).** The builder also pushes the map-cue id **924000001** on
the ambient sound channel (kind-3). This is a **sound** cue, not a visual — and in char-select it is
not even rendered audible, because the per-frame ambient-sound driver bails when there is no local
player (none exists in the select scene). It effectively registers / stops the ambient channel. (The
audible char-select music is the BGM cue **920100200** of §3.8, on the separate music slot.)

**Visual-source attribution for the cavern dressing (braziers / waterfall / portal):**

| Visual feature | Source | Confidence |
|---|---|---|
| Braziers / central ambient FX (row centre) | the **single composite `char_select-u.xeff`** (effect id **380003000**; 68 sub-effects = all torch coronas), spawned **once** by the scene builder at world **(508.48, 69.89, −9758.57)**, scale **1.0**, identity rotation — **not** per-pillar spawns, **not** `.bud`-baked | CODE-CONFIRMED (single composite `.xeff` spawn; id→file resolved) |
| Waterfall / blue surface behind the row | the cell's own **terrain water layer** (`.fx3` / `.fx5`; textures `_water_new01/03/04`), rendered by the terrain system independent of the effect engine, **gated by the water option** — **not** a spawned `.xeff` | CODE-CONFIRMED that it is the cell water layer (exact `.fx3`/`.fx5` texture binding is terrain-side) |
| `zone_sel_u.xeff` / `zone_sel2-u.xeff` (ids 380000000 / 380000001) | **World-only** zone-transition / teleport portals, spawned only by in-world movement handlers gated on the local player (absent in char-select) — **not** char-select dressing | CODE-CONFIRMED (never spawned in char-select) |

> So the cavern look = **the cell geometry + lighting + the cell water layers + the single
> code-spawned `char_select-u.xeff` (id 380003000)** — not a different cell and not a 907-entry
> placement overlay. A faithful rebuild should load `map000` cell `d000x10000z9990`, spawn the one
> composite ambient effect **`char_select-u.xeff` (id 380003000)** at world (508.48, 69.89, −9758.57),
> render the cell `.fx3`/`.fx5` water (water option), and **not** attempt to load a
> `data/effect/map000.txt` (absent) or any `data/sky/map/map%d.txt` placement table (dead path). The
> brazier source is **RESOLVED** to the single composite `.xeff` (not `.bud`-baked); the `zone_sel*`
> portal files are **World-only** and never enter char-select. The remaining render-time item is only
> whether **every** visible brazier traces to this one spawn (vs the effect's own sub-effects) — a
> live-frame count.

### 3.6.6 Ambient-effect render model — alpha-blended textured billboards; NO scene point-lights (CODE-CONFIRMED + VFS-VERIFIED)

This subsection pins **how** the single ambient effect `char_select-u.xeff` (id **380003000**) must be
rendered, and the **root cause** of the stray "flying blue / red pixels" a naive port produces.

**No scene point-lights (CODE-CONFIRMED).** The scene builder creates a camera, the terrain handle,
the environment (clock pinned to 14:30), the named "select" scene object, and **exactly one** ambient
XEffect — and **nothing else**. There is **no light-creation call** in the builder and the particle
build path creates no light. The warm brazier glow is the **additive fire texture on the sprites**,
not a light source. A faithful port must **not** add OmniLights for the braziers expecting the
original look (a tasteful warm light per brazier is a legitimate *enhancement* but is not what the
original does and is not required for fidelity).

**The effect is 68 textured-sprite sub-effects (VFS-VERIFIED).** `char_select-u.xeff` is 75,372 B and
carries **68 sub-effects**, every one animated (frame strides ≈ 59–160 ms per keyframe). Decode of the
real file shows the emitter-type distribution **6 billboard / 51 mesh-particle / 11 directional-
billboard**, and **all 68 carry `resource_id = 0`** — so their textures are resolved from each
sub-effect's own 64-byte name slots to `data/effect/texture/<name>.tga` (32-bit uncompressed TGA with
alpha), **not** from `particleEmitter.eff`. The 16 distinct textures group into families:

| Family | Texture(s) (`data/effect/texture/<name>.tga`) | Role |
|---|---|---|
| Brazier fire | `fire_4-01` … `fire_4-06`, `fire_piece1b-01` | torch / brazier flames + burning fragments (warm) |
| Waterfall | `waterfall-pie-01` … `waterfall-pie-04` | cascading water sprites (blue/white) |
| Smoke / dust | `hit-center13(dustl2)-03` … `-05` | rising smoke / dust puffs |
| Ember sparks | `imot-gu-tung06-01` | rising ember / heat-shimmer directional streams |
| Yellow corona | `lflare-l-yellow-01` | tight yellow lens-flare highlight coronas at brazier positions |

(The `fire_*` / `lflare-*` / `imot-*` / `waterfall-*` textures are 128×128; the `hit-center13*` dust
textures are 64×64. All 16 confirmed present in the VFS.)

**Two waterfall contributors (CODE-CONFIRMED split).** The waterfall has **two** sources a faithful
port must reproduce: **(1)** the cell's horizontal **terrain water plane** (`.fx3`/`.fx5`, textures
`_water_new01/03/04.dds`, 256² DXT3 with 4-bit alpha — a scrolling flat plane, §3.6.5 / §3.7.3), and
**(2)** the **XEffect's vertical water sprites** (the `waterfall-pie-*` sub-effects — the falling
sheet / spray). The scrolling-UV-over-time on the water texture gives the falling-water illusion.

**FLYING-PIXELS ROOT CAUSE (decisive).** The stray scattered specks are the fire and water particle
sprites being drawn **as bare points / without their alpha / opaque** instead of as alpha-blended
textured sprites: each particle collapses to a ~1-pixel dot at its own animated world position — **red
= the brazier-fire emitters, blue = the waterfall emitters**. The original expands each particle into
a screen-facing **textured sprite** (with the sprite's alpha and an additive/transparent blend), so it
reads as a soft glow rather than a hard speck.

> **Render-mechanism note (two witnesses diverge — DEBUGGER-PENDING).** The low-level expansion
> mechanism differs between the two recovery witnesses: a static reading describes a dual fixed-
> function dispatch (legacy point-sprite expansion for small sizes vs a billboard-quad path for large
> sizes), while a black-box decode of the actual file finds all 68 sub-effects on the mesh-particle /
> name-table path with emitter types 0/1/2. Do **not** over-assert a single fixed-function point-sprite
> path as the only mechanism — the port guidance below is correct under **both** readings; which exact
> expansion the original GPU takes (point-sprite vs billboard) is the implementation's free choice and
> is DEBUGGER-PENDING.

**PORT CONTRACT (correct under both witnesses).** For each particle:

1. **Billboard it.** Render each particle as a **camera-facing, alpha-blended, textured quad** sized by
   the emitter's sprite size — never as a bare point, never as an opaque quad.
2. **Bind the per-sub-effect texture.** Resolve the sub-effect's name-table entry to
   `data/effect/texture/<name>.tga` and bind it (with its alpha). A missing texture / ignored alpha is
   the other half of "bare pixels".
3. **Additive / transparent blend.** Fire and water sprites read as a glow — use additive/transparent
   blend, not opaque.
4. **Animate via the XEffect track.** Step keyframes by the per-sub-effect frame stride (≈ 59–160 ms)
   and apply the UV-scroll flags where set, so the water/fire animate rather than sit static.
5. **No scene point-lights** for the braziers — the original has none; the glow is the additive
   texture (optional enhancement only).

The single highest-impact fix is #1 + #2: expanding each particle into a textured, alpha-blended
camera-facing quad converts the scattered dots into the brazier flames and the waterfall sheet.

**DEBUGGER-PENDING residuals (live read only):** the exact per-emitter fire-vs-water label across all
68 sub-effects (read each element's resolved texture at runtime), the exact additive-blend factors,
and the two brazier elements' ±X offsets around the spawn pivot.

<!-- source: campaign9/wave2 ida effect-brazier-waterfall + vfs effect-assets (render model, no point-lights, 68 sub-effects, billboard port contract) -->

### 3.6.7 Ambient-effect BUILD RECIPE — per-emitter-type placement (CAMPAIGN 9c, two-witness: IDA spawn/render + black-box bytes)

CAMPAIGN 9c resolved the §3.6.6 debugger-pending residuals (the two brazier ±X offsets, the
emitter-type render model, and the per-emitter blend) STATICALLY, with a two-witness gate (IDA static
recovery of the spawn/render path + a black-box byte-parse of the real `char_select-u.xeff`) agreeing
with zero residual over all 68 sub-effects. This is the engineer build recipe.

**Single placement.** The effect is spawned ONCE at world (508.483, 69.887, −9758.569) → Godot
(508.483, 69.887, +9758.569), **IDENTITY orientation (no yaw)**, scale 1.0, looping, anchored to a
fixed world point that does NOT follow the camera. Because the instance orientation is identity and
scale 1.0, each sub-effect's local offset (the first track triplet — `velocity_x/y/z`, the §A.8 field
the parser exposes as `XeffKeyframe.Velocity*`) is **added directly to the anchor with no rotation**.
So each quad/mesh CENTER sits at `anchor + offset`; only a billboard's *facing* tracks the camera.
**The flying-pixels correction: do NOT collapse all 68 to one camera-tracking point — place each at
`anchor + its own offset`, identity-oriented.** (The braziers therefore separate along world ±X.)

**Render each emitter type DISTINCTLY (not all as camera-facing billboards):**
- `emitter_type == 0` (billboard): camera-facing flat quad, full size = `size_x·2 × size_y·2`
  (size = half-extents), at `anchor + offset`. (Use the per-axis size — NOT a square `max()`.)
- `emitter_type == 1` (mesh-particle): a real ORIENTED quad/mesh tile (the `.xobj` template, here a
  quad), oriented by the keyframe Euler rotation, scaled by size, at `anchor + offset` — **NOT a
  camera-facing billboard**. The 28 waterfall tiles are oriented quads forming a flat curtain sheet.
- `emitter_type == 2` (directional billboard): an oriented quad with an explicit **+90° Y
  pre-rotation** plus the keyframe Euler rotation (used here for the large `imot-gu-tung06-01`
  corona glows).

**Feature grouping (byte-confirmed local offsets; full per-row table is dirty-only):**
| Feature | Texture family | offset_x | offset_z |
|---|---|---|---|
| RIGHT foreground brazier | `fire_4-*` core + `lflare-l-yellow-01` + smoke | **+28.6** | +36.8 |
| LEFT foreground brazier | `fire_4-*` core + `lflare-l-yellow-01` + smoke | **−23.9** | +36.8 |
| Secondary/background braziers (flank both sides) | `fire_4-*` + `lflare` | ±46 / ±51 / ±119 / ±123 | −35 … +18 |
| WATERFALL curtain (wide sheet, 28 tiles) | `waterfall-pie-01..04` | −147 … +118 (centroid ≈ 0) | **−95 … −116** |
| Waterfall-foot / sparks (10) | `fire_piece1b-01` | −142 … +106 | −44 … −107 |
| Corona / glow overlays (11, directional) | `imot-gu-tung06-01` | −136 … +118 | −108 … +31 |

The two foreground braziers are **~52.5 world-units apart on X**, both ~+36.8 forward, flame height
~+16…+24. The waterfall is a **wide ~265-unit curtain** behind stage centre (z ≈ −105), curtain-top
tiles high (+Y 45…82) and splash tiles lower — NOT a narrow column.

**Blend (per sub-effect).** A per-sub-effect flag selects the blend pair: straight alpha
(SRC-ALPHA / INV-SRC-ALPHA) vs **additive** (SRC-ALPHA / ONE). The brazier fire, lens-flare, corona
and waterfall spray use the **additive** pair, so they ADD light onto the framebuffer (brightening
the dark stone temple). **None of the emitters is a real light source** — do NOT port as point-lights.

**Animation.** Each sub-effect cycles on its own `tex_count`/`anim_stride` (fire ≈ 18×67 ms,
waterfall 4-frame, sparks 9-frame, corona 21-frame); size/alpha lerp, the sprite frame steps, alpha
is inverted (in-memory = 1.0 − file). UV-scroll bit0=U / bit1=V over a 5000 ms period.

<!-- source: campaign9c effects_format.md + char_select_effect.md + char_select_effect_bytes.md (two-witness; resolves §3.6.6 ±X + emitter-type + blend residuals) -->

## 3.7 Char-select 3D scene composition — world, cell, stage, assets (CODE-CONFIRMED + black-box VFS)

This section is the implementable composition of the char-select 3D backdrop: the world, the single
backdrop cell and its textures, the stage coordinate frame, and the preview-character asset set. It is
what an engineer rebuilds the scene from as a **3D scene**, not a 2D screen.

### 3.7.1 Base world & backdrop cell (CODE-CONFIRMED world; black-box VFS for the cell/textures)

- **Base world:** `data/map000` (area code 0 → folder string `"000"`, §3.5.1). Textures under
  `map000` are global to the whole client.
- **Backdrop cell:** the scene seeds a **3×3 first terrain ring** around the centre cell, but `map000`
  is **sparse** — only the single cell **`d000x10000z9990`** exists; the engine requests all 9 ring
  cells and silently skips the 8 absent neighbours. The backdrop is therefore rendered from this one
  purpose-built cell. Its cell-list manifest records the same key twice (a pre-compute + render pass
  pair).
- **Cell addressing (CONFIRMED):** cells are **1024 world units** on a side; `1024 / 64 = 16` is the
  intra-cell vertex spacing on the 65×65 grid. The cell naming/key convention is:
  - `mapX = 10000 + cx`, `mapZ = 10000 + cz` (so cell `(cx=0, cz=−10)` → `mapX=10000, mapZ=9990`),
  - `cell_key = mapX · 100000 + mapZ`,
  - file stem `d000x{mapX}z{mapZ}`, cell world origin `(cx·1024, cz·1024)` = `(0, −10240)` for the
    backdrop cell.
- **Centre cell:** `(mapX=10000, mapZ=9990)` = world X ∈ [0, 1024], Z ∈ [−10240, −9216]; the row pivot
  (508, −9734) sits inside it.

The backdrop cell's component files (under `data/map000/dat/`), as black-box VFS observations:

| File | Role |
|---|---|
| `d000x10000z9990.map` | Cell manifest (ASCII text): origin, terrain/building/FX section pointers, per-cell texture-id table; ORIGIN `0.000, −10240.000` |
| `d000x10000z9990.ted` | Height-field (binary): 64×64, 16×16 patches |
| `d000x10000z9990.bud` | Building / prop geometry (binary): the decorative 3D props (walls, pillars, ornaments) |
| `d000x10000z9990.exd` | Extra terrain data (binary) |
| `d000x10000z9990.fx1` | Terrain layer FX1 (binary) |
| `d000x10000z9990.fx3` | Terrain layer FX3 — water/reflection layer (binary) |
| `d000x10000z9990.fx5` | Terrain layer FX5 — secondary water layer (binary) |
| `d000x10000z9990.sod` | Collision wall segments (2D XZ ray-parity; minimal) |

(No `.mud`/`.pre`/`.post`/`.fx2` etc. for this cell — it is a purpose-built backdrop, not a full play
cell. The terrain/building/water binary formats are owned by their own `formats/*.md`.)

### 3.7.2 Stage coordinate frame (CODE-CONFIRMED)

| Quantity | Value | Notes |
|---|---|---|
| Stage world origin (X, Y, Z) | **(2048.0, 0.0, −6144.0)** | the preview-stage origin; per-slot offsets (§3.3.1) and the camera keyframe anchor (§3.5.2) are added to this |
| Terrain-ring centre / row pivot (X, Z) | **(508.0, −9734.0)** | = stage origin minus the ring-centre constants (X−1540, Z−3590); the focal point of the backdrop |
| Ambient-FX / look-at anchor (X, Y, Z) | **(508.48, 69.89, −9758.57)** | row centre lifted ~70 in Y (§3.6.5); the camera look-at (§3.5.4) sits essentially over it |
| Cell stride | **1024** world units / cell / axis | |

> The stage origin `(2048, 0, −6144)` is the anchor; the camera keyframes (§3.5.2) and the per-slot
> placements (§3.3.1) are both expressed relative to it. The **row pivot (508, −9734)** is the visual
> focus and the centre of the standing row.

### 3.7.3 Backdrop textures (black-box VFS; CONFIRMED present)

The backdrop cell's textures resolve through the standard terrain chain (`.map` texture-id →
`data/map000/texture/bgtexture.txt[id]` rel-path → `data/map000/texture/<rel>.dds`). The 11 textures
this cell references (all confirmed present):

| Section | Rel path → VFS `.dds` |
|---|---|
| Terrain | `terrain/g3` |
| Buildings | `building/haha`, `building/suksang01`, `building/suksang02`, `building/suksang03`, `building/suksang04`, `building/walll04`, `building/walll04_2` |
| Water (FX3/FX5, animated) | `terrain/_water_new01`, `terrain/_water_new03`, `terrain/_water_new04` |

(The water rows carry the animated-texture flag in `bgtexture.txt`. The terrain/building/texture-chain
formats are owned by their own specs; this is the concrete asset list for the backdrop.)

### 3.7.4 No skybox file (black-box VFS)

There is **no `.box` / `skybox.bin` skybox file** anywhere relevant in the VFS (none under
`data/effect/`, `data/sky/`, or `data/map000/`). The sky is **parametric** — assembled at runtime from
the per-area sky-parameter `.bin` files (§3.6.3) and the frozen 14:30 clock — not a pre-baked cube/box
texture. A revival must render the sky parametrically (or substitute an equivalent), not look for a
skybox asset.

### 3.7.5 Preview-character assets — the four starter classes (RESOLVER CODE-CONFIRMED; body skinId SAMPLE-UNVERIFIED)

> **CORRECTED (binary wins, 2026-06-22).** The prior version of this section derived class tags
> and `.skn` paths from a production-parser VFS observation with no IDA cross-check. That observation
> was wrong in two ways: (1) the class tags (3/4/6/11) do not map to `InternalClass` 1/2/3/4; (2) the
> listed `.skn` paths are not the body-resolver output — they are outfit-family skin gids from the
> equipped-gear key path, which carries a non-zero part-id mantissa. The body resolver ignores the
> mantissa and keys exclusively on `(slot=3, IdB)`. This section is rewritten from the binary-confirmed
> resolver math; see `Docs/RE/_dirty/starter_body_resolution.md` for the full static analysis.

#### Body resolver — how the preview body `.skn` is selected (CODE-CONFIRMED, static IDA `263bd994`)

The char-select preview body mesh is **not** resolved through `skin.txt` (that chain delivers the
texture id, not the geometry). The body resolver keys an **AnimCatalog lookup on `(slot=3, IdB)`**
with the part-id mantissa zeroed, where:

```
IdB = 5 * (InternalClass + 4 * AppearanceVariant) − 24        (see specs/skinning.md §3.5.2)
bodyKey64 = 1_000_000_000 * (slot + 100 * IdB)                slot = 3 for the body part
```

The catalog payload yields a numeric **`skinId`**, and the body file loads from **`data/char/skin/g{skinId}.skn`**. The loaded `.skn` carries its own `id_b` header which selects the bind skeleton (`g{id_b}.bnd`) via the verbatim pool-key rule (`specs/skinning.md` §8(e)).

#### Per-class IdB and body key for the four starter classes (CODE-CONFIRMED)

The four starter classes have `InternalClass` 1..4 with appearance variants stamped by the client
(`SelectWindow_WriteSlotRecord`) as variant = {1, 2, 1, 1} for classes {1, 2, 3, 4} respectively:

| InternalClass | Class name | Starter variant | IdB | Body key64 | Body `.skn` path |
|---:|---|---:|---:|---:|---|
| 1 | Musa | 1 | 1 | 103 000 000 000 | `data/char/skin/g{skinId}.skn` — skinId **SAMPLE-UNVERIFIED** |
| 2 | Salsu | 2 | 26 | 2 603 000 000 000 | `data/char/skin/g{skinId}.skn` — skinId **SAMPLE-UNVERIFIED** |
| 3 | Dosa | 1 | 11 | 1 103 000 000 000 | `data/char/skin/g{skinId}.skn` — skinId **SAMPLE-UNVERIFIED** |
| 4 | Monk | 1 | 16 | 1 603 000 000 000 | `data/char/skin/g{skinId}.skn` — skinId **SAMPLE-UNVERIFIED** |

The four IdB values (1/26/11/16) are **distinct**, so a correct resolver returns four **different** body
skinIds and attaches four **different** body meshes to four **different** skeletons (`g1.bnd`,
`g26.bnd`, `g11.bnd`, `g16.bnd` respectively — selected by the loaded skin's `id_b`, per
`specs/skinning.md` §8(e)).

> **The concrete `g{N}.skn` per class is sample-unverified.** The resolver math and key64 values
> above are CODE-CONFIRMED from static IDA analysis. The numeric `skinId` the AnimCatalog returns for
> each key — and therefore the literal VFS path — requires a live read of the catalog source table or
> a debugger confirmation (breakpoint on the body resolver with a Dosa slot; read the returned skinId
> and verify `g{skinId}.skn` in the VFS). Do not invent these values.

#### Port bug diagnosis: "class-1 body for every class / flat Dosa sliver" (CODE-CONFIRMED root cause)

The symptom "every class renders with a class-1 body / Dosa shows a near-flat sliver" is a
**SPEC/RESOLVER bug in the port — wrong key**, NOT a VFS data gap:

- The correct resolver keys `(slot=3, IdB)` with **IdB = 5*(InternalClass + 4*variant) − 24**.
  Four distinct IdBs → four distinct body skinIds → four distinct body meshes.
- A port that resolves the body through `skin.txt` (the texture chain, keyed by IdA), or hardcodes a
  single body gid, or uses the part-id mantissa instead of zeroing it, collapses all classes onto
  IdB=1 (Musa body) — the class-1 slab symptom.
- The "flat Dosa sliver" is the signature of loading the Musa body (IdB=1 skinId) and deforming it
  against the Dosa skeleton (IdB=11 / `g11.bnd`): mesh–skeleton mismatch, not a missing VFS file.
  A missing VFS file would fault or blank, not produce a consistent class-1-shaped result.

Fix the port: resolve the body via `(slot=3, IdB = 5*(InternalClass + 4*variant) − 24)` → AnimCatalog
skinId → `g{skinId}.skn` (NOT via `skin.txt` / IdA). The variant sentinel guard (`Appearance_ResolveKey`
returns 0 when `variant == 3`) should also be mirrored in the port (`specs/skinning.md` §3.5.2).

#### Idle motion (VFS-confirmed for Musa / Musa-rig; other classes pending)

The idle clip `data/char/mot/g111100010.mot` ("peace", **30 frames @ 10 fps**, 3.0 s loop) is
VFS-confirmed present and is the class-1 / Musa-rig (IdB=1) standing idle. Whether Salsu/Dosa/Monk
(IdB 26/11/16) resolve the same "peace" clip from their respective `actormotion.txt` rows, or
distinct idles, is **sample-unverified** — the resolver path (`actormotion.txt` column 16 / field
`+0x44`, keyed by IdB=col2) is CODE-CONFIRMED (`specs/skinning.md` §8(e); §10 of this spec), but the
per-IdB row values are a VFS data read. Do not assume a shared idle across all four starter classes.

- **No dedicated char-select idle clip exists.** The preview plays the same in-world idle the actor
  would play (§3.3.4).
- The reference/bind-pose clip `g101100001.mot` (3 frames) is the rest-state anchor, not the visible idle.

The class → body → skeleton → texture → motion chain is the normal in-world chain (`specs/skinning.md`,
`formats/mesh.md`); char-select adds no new asset type.

### 3.7.6 Character-creation backdrop — the SAME cell as selection (VFS-VERIFIED)

**Character-CREATION reuses the IDENTICAL cell, stage, camera and environment as character-selection.**
There is **no separate creation stage anywhere in the VFS** — an exhaustive enumeration of all 43,347
entries found `data/map000` contains exactly one cell, **`d000x10000z9990`** (the same backdrop cell of
§3.7.1), and no `create`/`creation`/`portal`/`relief`/`temple`-named distinct 3D stage. There is also
only one `d000*` lightmap (`data/effect/map/d000x10000z9990.bmp`), confirming a single shared stage.

The carved stone-relief wall (`suksang01..04.dds`) and the bright portal/archway the player sees behind
the create character are **baked into that one cell's `.bud` building geometry** (the `suksang*` /
`walll04*` stone textures of §3.7.3), already present in the select view. What differs between select
and create is therefore **not the backdrop** but:

- **the camera framing** stays put (the entry dolly's rest pose, KF1 — the camera does not move,
  §3.5.4), and
- **a single create-preview actor** is placed **≈ 56 units nearer the camera** (a Z shift only, §3.5.4
  / §4.2) in place of the 5-slot row, plus the 2D chrome swaps to the create form.

So a 1:1 port must **not** load a second cell or different terrain for creation: load `map000` cell
`d000x10000z9990` once, keep the same camera and environment, tear down the 5-actor row, and build the
single forward-placed create-preview actor. (VFS-VERIFIED, exhaustive — agrees with the §3.5.4
CODE-CONFIRMED "create moves the actor, not the camera" verdict.)

<!-- source: campaign9/wave2 vfs charselect-creation-assets (no distinct creation cell; same d000x10000z9990; difference is actor Z, not backdrop) -->

## 3.8 Char-select sound, music & the "character count : N" caption (CODE-CONFIRMED)

### 3.8.1 BGM cue & the single-BGM contract — EXACTLY ONE looping voice (CODE-CONFIRMED)

The char-select **BGM** is sound cue **920100200**. It is started **exactly once per scene build**:
the final tail call of `SelectWindow_BuildAndInit` (the build/init virtual) invokes the sound manager's
create-and-play method with `category = 0`, `soundId = 920100200`, `loop = 1`. The arguments are
**compile-time immediates** in the instruction stream — the **loop flag is a hard-coded `1`**, so the
voice is **LOOPING**, not data-driven. The trigger is straight-line code (no loop, no conditional wraps
it), and the per-frame tick installed two instructions later does **not** re-issue it; it only pumps the
global sound manager each frame. The play return value is **discarded** — there is **no retained audio
handle field** on the select-window object; the BGM is owned by the process-global sound manager, not by
a window field. **The earlier "no audio on char-select" reading is WRONG — there is one looping
category-0 BGM voice.**

> **DOUBLE-BGM VERDICT — exactly ONE voice (CODE-CONFIRMED, no debugger needed).** A char-select entry
> yields **exactly one BGM voice**, and a second category-0 BGM **cannot** coexist. The create-and-play
> method's category-0 path enforces this with **two independent structural guards**, plus an enable gate:
>
> 1. **Single shared slot.** Categories 0 and 1 share **exactly one** voice-pointer field in the sound
>    manager. A subsequent category-0 start **reuses/overwrites that same pointer** (re-binding the id,
>    or tearing the slot down and recreating it) — it can never hold two voices simultaneously. There is
>    exactly **one** category-0/1 concurrent voice slot.
> 2. **Per-id dedup.** Before (re)using the slot, the `soundId` is looked up in the manager's tracking
>    map; if the id is **already present** (already playing/tracked), the call **returns without creating
>    any voice**. The same id therefore never stacks.
> 3. **Enable gate.** A category-0-enabled byte must be set, or no voice is created at all.
>
> The 4th argument's meaning is settled statically: it is the **DirectSound loop flag** (`0` = one-shot,
> `1` = looping) — proven by the two distinct error strings on the play path (the one-shot vs looping
> branches) — and it maps straight to the buffer-play loop parameter. At the char-select trigger it is
> the immediate `1`, so the BGM loops. This loop-bit question is **NOT** OPEN@DEBUGGER — it is a
> hard-coded immediate, fully resolved in the binary.
>
> **OPEN@DEBUGGER (not load-bearing for the single-voice verdict):** the once-per-entry firing cadence of
> `SelectWindow_BuildAndInit` and whether the enable byte is set in practice are structurally implied but
> only runtime-observable; neither is required to confirm the single-looping-voice verdict.

**Single-BGM contract for a faithful rebuild.** Treat **920100200** as **one owned, looping BGM voice on
one slot**: make PLAY **idempotent** (if 920100200 is already playing, do nothing — mirroring the
binary's per-id dedup), keep it on a single shared music channel that any new category-0 cue **replaces**
rather than overlays, and **stop it on char-select scene-exit**. The per-class create-form preview cues
**91006xxxx** (§4.1) play on the **same single category-0 slot** and therefore **replace** the scene BGM
rather than overlay it. The separate ambient cue **924000001** (§3.6.5c) and the scene-ambient VFX path
are not the BGM and do not contend with it. (The kind-3-channel ambient cue **924000001** is not
audible as BGM in char-select — do not conflate the two.)

### 3.8.2 The "character count : N" top caption (CODE-CONFIRMED)

The top-of-screen "character count : N" caption is built by a dedicated helper:

- **Template:** MessageDB template id **2209** — a `%d`-bearing format string in the message DB
  (`msg.xdb`, CP949). The id **2209** is a hardcoded immediate (not a config-resolved global), so it
  is unambiguous; the localized template text is VFS-only and not reproduced here.
- **Count source `N`:** the **BillingState character-count field** at **dword index 32 (byte offset
  `+0x80`)** of the BillingState singleton — read directly by the caption helper and formatted through
  MessageDB template **2209**. This is an **account-wide** field; it is **independent of the current
  server's slot mask** (§3.1) and is never computed from the mask popcount, so the caption number can
  legitimately differ from the count of filled slots shown for the selected server.
- **Format / inject:** a single `snprintf` of template 2209 with the one integer `N`, assigned to the
  caption widget.
- **Exactly four writers of the `+0x80` field (CODE-CONFIRMED).** The field has a small, closed writer
  set — and **no other code path mutates it** (the billing/subscription packets — billing-info,
  billing-balance, subscription-toggle/notice server-commands — touch the gold balance, the
  subscription flag, or the notice string, **never** this field):
  1. **Constructor init → 0.** The BillingState singleton's constructor zero-initializes the field, so
     an empty account renders "0" before any character list arrives.
  2. **Create-accept (`3/6`) → increment.** The create-result handler, on a success result, performs a
     `+1` on the field and repaints the caption.
  3. **Delete-accept → decrement (floored at 0).** The char-manage response handler, on a delete
     success, decrements the field only while it is `> 0`, then repaints. (The opcode carrying this
     result is the **8-byte char-manage result**; the dispatch-ladder verdict of §5 pins it to **`3/7`
     SmsgCharManageResult** — see the cross-spec ladder-correction note in §5. The decrement of this
     field is independent of the opcode-renumbering.)
  4. **Enter-game (`3/5 SmsgEnterGameAck`) → overwrite store.** The enter-game response reads a
     **trailing standalone `u32` CharacterCount** from the packet and stores it **directly** into this
     same `+0x80` field (a plain assignment, not an increment). So `3/5`'s trailing CharacterCount is
     **not a separate counter** — it **re-syncs (overwrites) the same account char-count field** with
     the server's authoritative value at world entry.
- **Refresh points:** built on the initial select-window build, and re-rendered **after create and
  after delete** (the count-changed paths re-read BillingState `+0x80` and re-format the caption).


> **SUPERSEDE** the earlier guesses that the count caption was id **48001 / 2206** (or 48003/48004/
> 48005). Those are **per-slot info-line / chrome label** ids (config-resolved; see §11.5d), **not**
> the count caption. The top "character count : N" caption is **MessageDB id 2209**, count-bound to
> **BillingState `+0x80`**. (CODE-CONFIRMED; the literal Korean template behind id 2209 is VFS-only.)

### 3.8.3 No-character → create branch — NOT auto-opened on a zero-character account (CODE-CONFIRMED)

**A zero-character account does NOT auto-open creation.** It shows the **normal char-select scene with
five BLANK slots**. There is no scene-entry slot-count test that forces the create form:

- The **`3/1` character-list handler** writes the **Select** state (engine state 4) for **any** slot
  mask — zero or non-zero. It zero-clears all five slot records, fills only the slots whose mask bit is
  set, and then unconditionally advances to Select. There is **no mask-value test, no slot-count
  compare, and no create-form call** in this handler. An empty (zero-character) account therefore lands
  in the same Select scene as a full one, just with all five slots blank and no preview actors spawned
  (the per-slot render gate fails for blank slots).
- The account **character-count caption** (§3.8.2) is **displayed but never used to route** to
  creation.

**Creation opens per-slot, on confirming an empty slot.** The empty slot is marked by the **`"@BLANK@"`**
sentinel (§3.2 / §4.1a.1). When the user confirms / enters on a highlighted slot, the enter-world path
tests the slot's name against `"@BLANK@"`:

- **name == `"@BLANK@"`** (empty slot) → instead of entering the world, it **registers and shows the
  create-character modal** (the create panel held on the select window) and sets that modal's
  **mode/message id to 262** — i.e. it raises the create-confirm dialog. Then it bails (no enter-game
  send).
- **name != `"@BLANK@"`** (a real character) → it builds and sends the enter-game request.

Create is an **in-place sub-form of the same select window** (no new scene, no new window — §4); the
**Create** button (UI action 4, §4) and a keyboard create-shortcut route through the same modal open
path. (CODE-CONFIRMED.)

<!-- source: campaign9/wave2 ida flow-and-nochar-branch (zero-char shows 5 blank slots; 3/1 writes Select for any mask; per-slot confirm-on-@BLANK@ raises modal id 262; count caption never routes) -->

---

# 4. Character creation

Triggered by the **Create** button (UI action **4**, CODE-CONFIRMED — see correction note below), which opens a create sub-form drawn over the
select window: a class/appearance picker plus a name-entry textbox. The empty-slot path (§3 sentinel
`"@BLANK@"`) also routes here (§7).

> **Correction (CODE-CONFIRMED, widget-atlas sweep).** Previous versions of this spec and of
> `ui_system.md` recorded the Create button action id as **413** and the Delete button as **531**.
> Those values are in fact the **atlas src-X coordinates** of the respective button HOVER frames
> (Create HOVER src-X = 354+59 = 413; Delete HOVER src-X = 472+59 = 531), not action ids.
> The actual `Panel_AddChildWithAction` ids recovered from the builder call sites are:
> **Create = 4**, **Delete = 5**, **Enter = 6**. `ui_system.md §8.2` has been corrected accordingly.

## 4.1 Class selection & the UI→internal class map (CODE-CONFIRMED)

The create sub-form offers **four classes**, chosen by a UI selection index `0..3`. The legacy code
maps the UI index to an **internal class id** and plays a per-class voice cue:

| UI index | Internal class id | Per-class voice SFX |
|---|---|---|
| 0 | 4 | 910065000 |
| 1 | 1 | 910062000 |
| 2 | 3 | 910064000 |
| 3 | 2 | 910063000 |

> The UI→internal mapping is **not the identity** — UI `{0,1,2,3}` → internal `{4,1,3,2}`. A
> reimplementation must preserve the *internal* id (1..4) when it seeds the descriptor and builds the
> create packet, regardless of how the four buttons are laid out in the UI.

Selecting a class also sets the class **label** from message ids **14003..14007**, shows the class
description strings, plays the voice cue, and rebuilds the create preview (§4.2). *(Human-readable
class names are CP949 in `msg.xdb`, not reproduced — Open question 7.)*

### 4.1.1 Class description & name — the two text sources (CONFIRMED, two-witness)

The create form's right-hand panel has two separate text sources:

- **Class NAME** (the name-entry modal's title / class caption): message database
  `data/text/msg.xdb`, ids **14003..14007** (selected by the internal class id; 14003 is the
  default). NOT taken from the class description table. See `formats/msg_xdb.md`.
- **Class DESCRIPTION** (the three-line archetype blurb in the right panel): keyed string table
  `data/script/npc.scr`, records **keys 1..4**. For the selected class's key, the form copies the
  three CP949 lines at record offsets **+0x14 / +0x54 / +0x94** (string fields 0/1/2) onto the three
  description labels, top to bottom. The trailing string fields (3/4/5) are empty for class records.

The key↔class mapping carries the same UI-slot vs internal-class crossover as the voice cue above
(npc.scr key → internal class): key 1 → Monk (4), key 2 → Musa (1), key 3 → Salsu (2),
key 4 → Dosa (3). The full record layout, the per-class BGM, and the 18-cell stat-grid key families
(`2·disc+{110,111,120,121,130,131,140,141}`, VERIFIED) are documented in
`formats/config_tables.md §2.17.3`. **REFUTED key formula:** the earlier `disc+{210..240}` "key"
family is **NOT** a stat-grid key formula — those `210xxxxxxx` values are full **equipment ITEM ids**
(weapon / armor / accessory / body ids written into the committed slot record), not keyed-string
lookups. The stat grid binds **only** via the `2·disc+{110..141}` keyed-string lookups. (Owned by the
config-tables spec; recorded here as a frontend-binding correction.) `npc.scr` is loaded once at boot and persists for the session
(no per-area reload), so keys 1..4 are always resolvable on the create form.

## 4.2 The create preview & appearance seeds (CODE-CONFIRMED)

A **single** create-preview actor (separate from the 5 slot previews) is built from a freshly zeroed
spawn descriptor seeded with the current create choices:

| Descriptor offset | Source | Meaning |
|---|---|---|
| +0x2C | sex selector | **sex/gender** — `1`, or `2` for internal class 2 (the female/alt-gender class) |
| +0x2E | face selector | **faceA** — face index, the `+`/`−` buttons clamp it to **[1, 7]**, default **1**; nonzero ⇒ slot occupied (CODE-CONFIRMED) |
| +0x30 | second appearance selector | **faceB** — **set-once (`= 1`) on create-open, NO `+`/`−` stepper in the create form** (CODE-CONFIRMED); packed into the descriptor / create blob but **read by no create widget** — its meaning is downstream (skin chain / server), consumer DEBUGGER-PENDING (Open question 4 resolved on the client side) |
| +0x34 | class selector | **internal class id (1..4)** |

The create preview is placed at the stage centre (X ≈ origin − 1536.5, Z ≈ origin − 3538 — i.e. between
slots 2 and 3 in X) so the player sees their would-be character before naming it. **The create-preview
nudge toward the camera is on the Z axis** — the create actor sits **≈ +55.5..+56.5 units in Z toward
the camera** versus the lineup row (NOT an X or Y offset). Its scale literal is ≈ **81** in LEGACY units
(versus the lineup's ≈ **70**, §3.3.1) — a legacy value to be **unit-reconciled** into Godot space, not a
ready-made multiplier. The idle clip plays at rate **× 12** (versus the lineup's × 3.0). The preview is
**mutually exclusive** with the 5-slot row (the slot actors are torn down before the single create actor
is built). **Rotation is under player control** — a press-and-hold turntable (≈±2 rad/s while a rotate
control is held over the preview), NOT a continuous auto-spin.

- **Face ± does NOT rebuild the 3D actor (CODE-CONFIRMED, CORRECTION).** The face `+`/`−` handlers only
  **increment / decrement the face index** (clamped **1..7**); they do **not** re-spawn or rebuild the
  preview mesh. **Only a class change rebuilds the preview actor** (via the apply-class-selection path,
  §4.1). An earlier reading that said "the face buttons rebuild the actor" is **superseded** — the visible
  3D character is unchanged by face stepping (the face value feeds the create descriptor / a 2D portrait,
  not a live mesh rebuild). There is **no functional sex toggle**.
- **PREVIEW gear ≠ COMMITTED slot gear (CODE-CONFIRMED).** The equipment the create **preview** displays
  is drawn from a **different table** than the gear written into the **committed** character slot record
  on create (§4.3). A faithful rebuild must not assume the preview's visual ids equal the slot record's
  starter-equipment ids — they are two separate id sets.

A per-class **stat preview** (six stat-label groups) is filled from the class template (pure display).

## 4.3 Per-class starter equipment (CODE-CONFIRMED)

On create, four starter-equipment/visual ids are seeded into the descriptor by internal class id.
These are visual/equipment ids in the same id space as the item catalogue:

| Internal class | desc +0x88 | desc +0x98 | desc +0x108 | desc +0xB8 |
|---|---|---|---|---|
| 1 | 202110003 | 203110002 | 206110002 | 209110001 |
| 2 | 202220003 | 203220002 | 206220002 | 209220001 |
| 3 | 202130003 | 203130002 | 206130002 | 209130001 |
| 4 | 202140003 | 203140002 | 206140002 | 209140001 |

The `202xxx / 203xxx / 206xxx / 209xxx` families are the default weapon/armor/etc visual ids; which
descriptor slot is which equipment category is for the asset/struct authors to pin.

## 4.4 Name validation (CODE-CONFIRMED)

On create-confirm (cmd 35, and on rename §6), the entered name is validated locally before any send.
Each failure surfaces a distinct message-catalogue toast (CODE-CONFIRMED ids):

| Check | Failure id | Notes |
|---|---|---|
| empty / `@BLANK@` | msg **2190** | a blank or sentinel name |
| banned word | msg **2075** | matched against the banned-word list |
| charset / length predicate | msg **12012** | the rule below |

- **Minimum length: 2** characters; an empty first character fails. **DEBUGGER-PENDING:** whether
  the "2" is measured in **bytes or in CP949 characters** is not statically settleable (a single
  double-byte Hangul syllable is 2 bytes but 1 character) — confirm the exact byte-vs-char semantics
  on the live client before relying on it.
- **Allowed characters only:**
  - ASCII **lowercase `a`–`z`** (0x61–0x7A),
  - ASCII **digits `0`–`9`** (0x30–0x39),
  - **CP949 double-byte Hangul** (a valid lead byte + valid trail byte; a lone lead byte with an
    out-of-range trail fails).
- **Rejected:** uppercase Latin, spaces, punctuation, and any other byte.
- **Length bound: 17 bytes including the NUL terminator** (16 payload bytes).

So legacy character names are **Korean Hangul and/or lowercase-alphanumeric only** (no uppercase, no
spaces). A revival should keep the same rule (or deliberately relax it) and surface the rejection as a
message-id toast. On a valid name the create-character send is **`major 1 / minor 6` (`1/6`)** — byte
layout owned by `login_flow.md` (§4.5).

## 4.5 Create send & result (cross-reference, not owned here)

On a valid name, the client copies the CP949 name into a **52-byte** create buffer, sets a net-busy
guard, plays click SFX **861010101**, and sends the **create** message: **`major 1 / minor 6`,
52-byte body** (`{name + class/appearance seed fields}`).

> **STATICALLY RESOLVED — there is no `1/6` collision (CODE-CONFIRMED, no capture needed; cross-spec
> note, I do not own `opcodes.md` / `packets`).** Earlier readings mapped **`1/6 = CmsgLoginRequest`**
> *and* the 52-byte character-create send, framed as a two-hypothesis collision. A static read of both
> send sites dissolves it: the **login credential send is `1/4`** (built by the secure auth-reply
> builder, header stamped major=1/minor=4 — §1.7), and the **character-create send is `1/6`** (built
> by a **separate** fixed-body sender, header stamped major=1/minor=6, **exactly 52-byte** body whose
> offset-0 is the CP949 name, never `0x2B`). **There is no shared opcode and therefore no collision;**
> the prior "`1/6` = login" attribution was a conflation (the confirmed answer is hypothesis (b)).
> This was proven by sweeping the whole major-1 fixed-body sender family: exactly one stamps minor=6
> (create); the others are 1/0, 1/7, 1/9, 1/13, 1/14; none stamps minor=4 — that is solely the secure
> path. So the create body does **not** start with `0x2B`, and `1/6` carries **create only**. The
> protocol author should split the catalog rows accordingly (`1/6 = CmsgCreateCharacter`, login
> credential = `1/4`). Recorded for `conflictsFlagged`.

There is **no dedicated `3/23` create-result message**: the create is acked by the manage-result
latch + **`3/7`** + a refreshed character list (`3/1`), driving a scene refresh and incrementing the
account character count; failure codes map to `msg.xdb` error strings in the ~200–212 id range. (`3/23`
itself is **`SmsgCharStatusBytesByName`**, a 28-byte by-name status/level patch — owned by
`character_creation.md` §5.1 / `login_flow.md`, **not** a create result.)

---

# 5. Character deletion

Triggered by the **Delete** button (UI action **5**, CODE-CONFIRMED — see correction note below) → a confirm popup whose **Yes** runs the
delete. Guards: a valid selected slot (0..4), an occupied-slot flag, and the net-busy flag clear. On
confirm it plays SFX **861010101**, sets net-busy, copies the slot to a pending field, and sends the
**delete** message: **`major 1 / minor 7`, 2-byte body** = `{ slot, mode }` with the **mode byte
fixed at 1** (`{slot, 1}`). Delete is therefore an overload of the same `1/7` char-manage send used
for slot select (§7): the **mode byte distinguishes them** — `1/7 {slot, 0}` = select / view,
`1/7 {slot, 1}` = delete. (The mode-byte value `1` for delete is the literal in the binary; the
earlier static-elimination alternative `2` is refuted.)

The delete result arrives on the inbound **8-byte char-manage result** message (committed
`opcodes.md` currently labels it `3/4 SmsgCharManageResult`, but the dispatch-ladder verdict in the
cross-spec note below **corrects this to `3/7`** — pending the protocol-author's renumbering; result /
subtype / ready-time):

- **result == 1, subtype == 2 ⇒ delete confirmed**: the account character count is **decremented**,
  the slot is cleared, and the preview row is rebuilt.
- **result == 0 with a future ready-time ⇒ delete cooldown**: a **"deletion forbidden today —
  `%d` hours `%d` minutes"** message is shown for ~5000 ms, where the remaining hours/minutes are
  computed from `(ready_time − now)` (a same-day delete lock). The CP949 format string is in the
  binary's data; the *id/string* is VFS-owned and not reproduced.

> **⚠️ CROSS-SPEC CONFLICT — the major-3 receive ladder is being CORRECTED (route to the
> protocol-author; I do not own `opcodes.md`).** A static read of the receive dispatch ladder
> **resolves** the `3/4`-vs-`3/7` delete carrier: the minor ladder routes minor **7 → the 8-byte
> char-manage / delete result** (the handler that decrements `+0x80`), and minor **4 → a different,
> variable-length scene-entity / char-slot update**. The committed `opcodes.md` / this section have
> **`3/4` and the 8-byte manage result SWAPPED**, and additionally mislabel the **16-byte spawn
> confirm** as "3/7" when it is actually **3/14**. The correct triple the protocol-author must
> reconcile (rows `0x30004` / `0x30007` / `0x3000E`):
> - **`3/4` = SceneEntityUpdate** (variable-length slot-scratch refill / scene-clear),
> - **`3/7` = SmsgCharManageResult** (8 bytes — the delete / select / rename manage result; **this is
>   the delete-result carrier**),
> - **`3/14` = CharSpawnResponse** (16 bytes — the enter-into-world bridge).
>
> **This is a genuine conflict for the protocol-author to reconcile, not a silent relabel** — flagged
> here and routed to **[protocol-spec-author]**. The opcode **renumbering itself is the
> protocol-author's job in `opcodes.md`**; this spec only flags the ladder correction. **Anchor to
> behaviour**: whichever minor the catalog lands on, this is the handler that decrements the account
> character count on `result == 1, subtype == 2`. Recorded for `conflictsFlagged`.


---

# 6. Character rename

A per-slot rename action opens the same name-entry textbox as create. On confirm the new name is
validated by the **same rule as §4.4** (min 2; lowercase a–z + digits + CP949 Hangul), copied to a
buffer, and sent as the **rename** message: **`major 1 / minor 13`, 18-byte body** = the new CP949
name (≤17 bytes + terminator).

The rename result is the inbound **`3/6 SmsgRenameCharResult`** (owned by `login_flow.md`): a nonzero
result is success (carrying the new name); a failure carries an error code that maps to a `msg.xdb`
string. The select screen also routes a rename outcome through the 8-byte char-manage result
(subtype 1) to refresh the displayed name.

> The char-management C2S sends share a small family. **`1/7` is a 2-byte `{slot, mode}` char-manage
> message** carrying **both select (`{slot, 0}`) and delete (`{slot, 1}`)** by its mode byte (§5 / §7);
> **`1/13` rename (18 bytes)**; and **`1/14` slot-move / "location" (1 byte = one slot index)** — a
> distinct send that the binary's debug text identifies as "is sending location", **not** delete.
> These are the select-side counterparts of the inbound results, and (per the char-select lane) are
> **new C2S opcodes** relative to the older `names.yaml`. Their catalog/YAML are owned by the protocol
> author; recorded here as a flag (§7 / §8 / `conflictsFlagged`).


---

# 7. Enter-world handoff

Triggered by confirming the highlighted slot (double-click the preview, or the enter/OK button).
This is the load-bearing transition out of the front-end.

**Empty-slot branch first.** If the confirmed slot's descriptor name **== `"@BLANK@"`**, the slot is
empty and the action instead **opens the character-creation form** (§4). So "enter on an empty slot"
== "create a character".

**For a real character**, world entry is a **SERVER ROUND-TRIP ladder**, not a single local send.
The corrected sequence (CODE-CONFIRMED, CYCLE 12 Phase 2; version token SAMPLE-VERIFIED) is:

1. Play the **enter SFX 920100200**.
2. **Confirm the occupied slot ("play / select this character").** This emits **`1/7
   CmsgSelectCharacterSlot` with the play-confirm mode byte = `1`** (the *select-and-play* sub-mode —
   **not** mode `0`, which is the slot-lock / pre-play step). The 2-byte body is `{slot, mode}`.
3. **Wait for the server's `3/14 SmsgCharSpawnResponse`.** This 16-byte reply carries a **leading flag
   byte**: when that flag is **non-zero (success)**, the client reads the slot and spawn coordinates
   and **re-enters its enter-builder**. When the flag is **zero**, the client arms a timeout and
   **does not** emit `1/9`.
4. **The `1/9 CmsgEnterGameRequest` is emitted from *inside* the `3/14` handler** (server-triggered),
   **not** directly from the Enter/OK button on the normal play path. The Enter button merely confirms
   the slot (step 2); the actual world-entry send is gated on the positive-flag `3/14`. Build the
   **40-byte** request: **slot index at +0**, a **33-byte self-checksum token** at +0x01 (the
   lowercase-hex **MD5 digest of the client’s own executable file** — 32 hex chars + NUL; a
   build-integrity token, **not** a launcher/login session token — see `login_flow.md` §7 and
   `cmsg_char_enter.yaml`), a 2-byte zero pad at +0x22, and a **client-version token** u32 at +0x24
   computed as **`10 × game.ver[field 5] + 9`** (for the sampled `game.ver` `field 5 = 2114` → token
   **21149**, SAMPLE-VERIFIED). No `1/9` field echoes any prior inbound packet.
5. **Cache the chosen slot locally for the world load (the only "preload" char-select performs):**
   - the **880-byte spawn descriptor** is copied to a global local-player descriptor, and
   - the **96-byte stats block** is copied to a global stats cache.
   Asset (skin/terrain) loading happens **later**, in the load/in-game states, fed by this cache.
6. Set the select window’s **confirm-enter flag**. On teardown, the select window writes engine
   state **5 (In-game) / substate 8**, and the 5 preview actors + select camera are destroyed
   (`client_runtime.md` §5).

> **THE ENTER LADDER (load-bearing — CYCLE 12 Phase 2, CODE-CONFIRMED):**
> `1/7 (mode 1, play-confirm) → 3/14 (flag ≠ 0, in) → 1/9 (out, emitted by the 3/14 handler) → 3/5 (in)`.
> A client that fires `1/9` *unilaterally* off the Enter button — without the **mode-1** `1/7`
> play-confirm and without waiting for the **positive-flag `3/14`** — sends `1/9` out of sequence;
> this is the leading static hypothesis for a genuine **`3/100` select-mode result code 23** server
> reject (notice id 1604; recoverable, not fatal — see `packets/3-100_char_action_result.yaml`). A
> secondary, purely client-gated direct route from the Enter button to the enter-builder also exists
> (it checks only: a modal byte clear, the slot is a real `0..4`, and the op-pending latch not set),
> but the **server-triggered `3/14` path is the normal play flow**.

**What the client waits for after the `1/9` send** (owned by `login_flow.md` / `client_runtime.md` §7.4):

- The select → In-game scene exit is **client-local** (the select-window teardown sets engine state
  5/8 on the confirm-enter flag), **not** opcode-driven.
- **`3/5 SmsgEnterGameAck`** independently sets **Load (state 2)** and re-syncs the account char count
  (its trailing `u32` overwrites BillingState `+0x80`, §3.8.2).
- **`4/1` is the opcode that actually creates the local player** (the `g_LocalPlayer == NULL` create
  path) **from the cached descriptor**, plus the first 3×3 terrain ring; the spawn X/Z arrive in this
  server world-state packet (`4/1`), Y forced to 0, and the 3×3 terrain ring streams around the spawn
  (owned by `client_runtime.md` §7.4).

> **⚠️ CROSS-SPEC note — spawn driver + ladder (route to the protocol-author for `opcodes.md`).** Per
> the dispatch-ladder verdict (§5): **`3/7` is the 8-byte char-manage result** (delete/select/rename),
> the **local-player spawn is driven by `4/1`**, and the 16-byte **`3/14 SmsgCharSpawnResponse`** is
> the server enter-confirm that **re-enters the select enter-builder and emits `1/9`** (the ladder
> above). The client enforces **NO fixed receive order** between `3/5` and `4/1`; the strict **wire**
> arrival order of `3/5` vs `4/1` is **server-determined** and remains **DEBUGGER-PENDING** — settle
> it by breakpointing the receive dispatcher and the `3/5` / `4/1` handlers (live pilot only; never
> `dbg_start`).

> **OPEN (capture/debugger-pending):** whether the server strictly requires the two-step
> `1/7 mode 0 (slot-lock) → 1/7 mode 1 (play)` ordering or only the mode-1 play-confirm; and whether
> the delete-confirm 1/7 uses mode `0` or `1` (the delete-context mode byte is **not** statically
> provable — see §8 and §5). These are the only enter-ladder unknowns left.

## 7.1 In-game vs char-select scene graphs — view-platform count (CODE-CONFIRMED, CYCLE 7)

The two scenes the front-end hands off between use **distinct, independent scene graphs**, and the
view-platform counts must not be confused.

- **In-game scene (state 5)** builds **exactly five view platforms** — five **unrolled**
  view-platform constructor calls (no loop, no array index above 4) — stored to five contiguous
  object slots at offsets `+0x50, +0x54, +0x58, +0x5C, +0x60` (i.e. +80..+96 within the in-game
  scene-builder object). It also builds **one scene-root node of a *different* class** (the GScene
  root), labelled `"charater scene"` (literal label; the typo is authentic), stored at object offset
  `+0x70` (+112).

- **Char-select scene (state 4)** is its own **parallel, independent** scene: it builds **one view
  platform** plus its own GScene root labelled `"select"` (the 3D backdrop scene described in §3.7).
  It does **not** add a sixth view platform to the in-game array.

> **There is NO sixth in-game view-platform slot.** The allocation in the in-game builder that an
> earlier reading mistook for a "reserved 6th view platform" is the **GScene scene-root object** at
> `+0x70` — a *different class* with a *different* vtable, the scene root, not a view platform. The
> long-standing "5 view platforms" reading is therefore **reaffirmed**; the "6th slot" idea is
> dispelled. This is the front-end-side summary of the in-game scene graph; the world-build detail is
> owned by `specs/client_workflow.md` §5.4.1 / §5.4.1a and `specs/client_runtime.md` §7. (CODE-CONFIRMED
> via the view-platform constructor's exhaustive static call-site set — re-confirmed against IDB SHA
> 263bd994, CYCLE 7.)

---

# 8. The select-scene C2S send map (cross-reference table, not owned here)

For an engineer's mental model. **Byte shapes, catalog rows and packet YAMLs are owned by the
protocol author** (`opcodes.md` / `packets` / `login_flow.md`); this is a flow summary only. Every
send is gated by a **net-busy flag** so the client never has two character operations in flight; the
matching inbound result clears it.

| Action | Message (`major/minor`) | Body size | Trigger |
|---|---|---|---|
| Create character | `1/6` (create-only — collision REFUTED, §4.5) | 52 bytes | Create form confirm (valid name) |
| Slot-lock / pre-play | `1/7` `{slot, 0}` | 2 bytes | slot-lock / pre-play confirm (mode byte = **0**; also stamps the chosen name into the HUD) |
| Play-confirm (select-and-play) | `1/7` `{slot, 1}` | 2 bytes | "play / select this character" occupied-slot confirm (mode byte = **1**; elicits the `3/14` enter-trigger — see §7) |
| Enter game | `1/9` | 40 bytes | emitted by the **`3/14` handler** on a positive flag (server-triggered), not by the Enter button directly (§7) |
| Rename character | `1/13` | 18 bytes | rename confirm (valid name) |
| Slot-move ("location") | `1/14` | 1 byte | move/commit slot (single slot index; "is sending location") |

> **`1/7` mode byte (CYCLE 12 Phase 2 correction):** the two static emit sites are **mode `1` =
> select-and-play** (the occupied-slot play-confirm that drives the enter ladder of §7) and **mode `0`
> = slot-lock / pre-play**. The **delete-confirm** action ALSO rides `1/7 {slot, mode}` (there is no
> dedicated major-1 delete opcode; delete *results* arrive on `3/7` subtype 2, §5), but **which mode
> byte the Delete-Yes button emits (`0` vs `1`) is NOT statically provable** — it depends on the
> confirm-popup → command-code wiring and is **capture/debugger-pending** (breakpoint the 1/7 builder
> on Delete-Yes and read the 2 bytes). The earlier "`0` = select, `1` = delete" reading is therefore
> **corrected**: mode `1` is the play-confirm, mode `0` the slot-lock, and the delete mode byte is
> open. **`1/14` is a separate slot-move / "location" send** (1-byte single slot index), **not**
> delete.


---

# 9. Consolidated SFX, message-id and texture constants (CODE-CONFIRMED)

For the presentation/Godot engineer. Sound ids resolve through `sound_runtime.md`.

**Sound effect ids:**

| Id | Where |
|---|---|
| 861010101 | generic click / confirm (select scene) |
| 861010105 | login-scene **intro SFX** (a sound, fired at login sub-state 1→2; §1.5) |
| 861010106 | quit cue (version-mismatch quit, logout) |
| 920100100 | loading-screen cue (Load state) |
| **920100200** | **char-select BGM** (kind-0 looping music slot; started by the select-window constructor — §3.8.1) **and** the enter-world cue (confirm slot, §7) |
| 924000001 | char-select ambient/map cue (kind-3 channel; NOT audible in char-select — no local player; §3.6.5c) |
| 910062000 / 910063000 / 910064000 / 910065000 | per-class create voice (classes 1 / 2 / 3 / 4 — see §4.1 map) |

**Message-catalogue (`msg.xdb`) ids** (captions VFS-only):

| Ids | Meaning |
|---|---|
| 2204 | version-mismatch error box (login) |
| 4001–4022 | static stacked notice/agreement text column on the login notice panel (NOT server-list row captions, NOT form labels, NOT EULA — §1.4c / §11.2a) |
| 4023 / 4024 | shared notice / quit-confirm prompts (login) |
| 4025 / 4026 / 4027 / 4028 | inline login notices (ID short / PW empty / no servers / connect fail) — **cached-notice sourced; literal ids at the call site UNCONFIRMED** (boot-fill trace pending) |
| 101 | timed-popup countdown suffix |
| 5001–5040 (+ locale banks) | localized server names |
| **2209** | char-select **"character count : N"** top caption template (N = BillingState char-count field; §3.8.2) |
| 14003–14007 | class labels (create form) |
| ~200–212 | character create/rename failure strings |

**Loading-screen textures** (chosen at random on entering the Load state): `loading.dds`,
`loading06.dds`, `loading08.dds`.

### 9.1 Loading screen — visual composition (CODE-CONFIRMED — VERIFIED by deep re-walk)

> **VERIFIED (deep re-walk, CODE-CONFIRMED).** A fresh static re-walk of the LOAD-state start/draw
> handlers re-confirms every constant in this section: the background is `rand() % 3` over exactly the
> three candidates `loading.dds` / `loading06.dds` / `loading08.dds` (no dedicated `srand` at the pick
> site — the process-global PRNG); the looping cue **920100100** plays on the **kind-0 music slot — the
> SAME music slot the char-select BGM (920100200, §3.8.1) later reuses** (so the loading cue and the
> char-select BGM contend for one slot); and the progress-bar frame rect is **X ∈ [−499, −170], Y ∈
> [−363, −140]** under the screen-scale model **scaleX = liveW/1024, scaleY = liveH/768**. §9.1 is
> accurate.

Cross-links the loading **mechanics** (engine state 2 LOAD — the VFS bulk-preload gate driven by a
worker loading ~50 global data tables, exited on the running-flag clear + grace, NOT on the bar; see
the state-2 LOAD node in §10 and `client_runtime.md` §7). This sub-block is the on-screen
**composition** so the Godot loading screen can be rebuilt 1:1. The per-frame draw handler emits
**exactly two textured quads and zero text calls.**

- **Canvas.** Authoring reference is **1024 × 768**, origin at screen center, +Y up. The draw handler
  builds an orthographic projection sized to the **live backbuffer**, and every authored layout
  constant is multiplied by a per-axis screen-scale factor so the layout stretches to any resolution:
  `scaleX = liveWidth ÷ 1024`, `scaleY = liveHeight ÷ 768`. At native 1024×768 both factors are 1.0
  and the authored constants are literal screen pixels.

- **Background.** On entering the LOAD state, **one of three** VFS paths is chosen by `rand() % 3`
  (0 → `data/ui/loading.dds`, 1 → `data/ui/loading06.dds`, 2 → `data/ui/loading08.dds`), loaded via
  the VFS-or-disk texture loader, and drawn **full-screen** (quad spans `[−W/2..+W/2] × [−H/2..+H/2]`
  for live screen size W×H). Background UV crop ≈ `u,v ∈ [0..0.75]` (the art appearing to occupy the
  top-left region of the DDS) — **PLAUSIBLE / sample-unverified.**

- **Progress bar (screen rect).** A lower-left rectangle, authored in 1024×768-reference center-origin
  pixels: x ∈ **[−499, −170]**, y ∈ **[−363, −140]** (negative Y is below center → lower third), each
  edge × the screen-scale factor. This ~329 px-wide slot is the bar's art frame.

- **Progress bar (fill).** The visible fill width = **`223 × percent/100` px**, clamped to ≤ 223,
  **left-anchored** (grows left-to-right inside the slot, max 223 px — narrower than the 329 px frame).
  The bar is drawn **only when percent ≠ 0**, so it is **invisible at 0%** and appears once preload
  starts. The fill art is a **sub-rect of the SAME loading DDS** (no separate progress-bar texture is
  ever bound) — sampled from a horizontal strip at `u ∈ [≈0.754, ≈0.969]`, `v ∈ [≈0.432, ≈0.75]`.
  Sampling-from-the-same-DDS is CODE-CONFIRMED; the **UV→pixel crop is PLAUSIBLE / sample-unverified.**

- **No caption, no spinner, no percent text.** The draw handler issues exactly the two textured quads
  above and makes **zero font/text/string render calls** — there is **no `msg.xdb` caption id** and no
  numeric percent overlay. Any "loading…" wording the player sees is **baked into the DDS art** itself.

- **SFX (a looping BGM, NOT stopped at teardown).** A **looping** cue `920100100` (a **category-0**
  music track, source dir `data/sound/2d/`, loop = 1) plays while the LOAD state is up, started on a
  **background loading-audio worker thread**, on the **kind-0 music slot — the same slot the char-select
  BGM 920100200 later reuses**. **It is NOT explicitly stopped** anywhere — its only release is the
  implicit free when the next scene seizes the category-0 slot, and the worker thread is **not joined**.
  This un-stopped, un-joined looping track is the source of the occasional **double BGM** at the
  loading→char-select boundary (§3.8.1). The abort/leave path plays `861010106`. (Earlier wording that
  said this cue was "stopped at teardown" is corrected — there is no such stop.)

- **Timing / advance edge.** The bar tracks the VFS bulk-preload counter (0..100, accumulated by the
  ~50-table worker). The LOAD state advances on the **loading-complete engine event (id 10001)** that
  clears the scene running-flag (plus a ~500 ms grace) — **NOT on the bar reaching 100%** — so the bar
  can finish visually slightly before the state exits, and conversely the state can exit on the event
  before the bar visually completes. No network signal directly advances the bar. On exit the engine
  routes to the next state (Opening/Select on the post-login path; the world build on the enter-world
  path).

> The UV→pixel crops (background `[0..0.75]`, bar fill strip) are derived from the renderer's f32 UV
> constants; **no original `loading*.dds` was available** to confirm the pixel mapping. The screen
> rects and the screen-scale model are CODE-CONFIRMED and hold regardless; only the UV→pixel mapping
> would shift if a real DDS's dimensions differ.



**Secondary-password dialog texture:** `data/ui/password.dds` (1024×1024 DXT3, full mips —
catalogued in `formats/ui_manifests.md` as "Secondary password dialog"); used by the
second-password / PIN modal (§1.4a).

**Other pinned constants:** ID-box max length **16**; PW-box max length **12**; IME slots ID **16**
/ PW **12**; face index range **1..7**; max slots **5**; preview stage X offsets
{−1560, −1548, −1536, −1524, −1512}, preview Z offsets {−3593, −3594, −3594.5, −3594, −3593},
preview scale **×3.0** (§3.3.1); preview yaw 0 = front / π = away (§3.3.2); stage origin
**(2048, 0, −6144)** (§3.7.2); row pivot / look-at anchor **(508, −9734)** / **(508.48, 69.89,
−9758.57)**; backdrop world **`data/map000`**, cell **`d000x10000z9990`** (§3.7.1); empty-slot
sentinel **`"@BLANK@"`**; version-token formula **`10 × game.ver[field 5] + 9`**; Save-ID INI
`DoOption.ini` `[DO_OPTION] OPTION_ID`; `Lastserver` & `servername` under registry
`HKLM\software\crspace\do`; `NEW_SERVER_INDEX` Lua global in `data/script/uiconfig.lua`;
second-password / PIN capacity **≤ 4 chars** (login-blob bound `< 5`, owned by `login_flow.md` §4.2).

---

# 10. End-to-end flow & engine-state map (CODE-CONFIRMED)

State numbers are from `client_runtime.md` §7. "→ state N" = an engine-state write that drives the
next scene.

```
[state 1: LOGIN]
  build login window from uiconfig.lua   (NO login BGM is started by login code)
  flow sub-state: 1 intro+SFX 861010105 → 2 curtain → (3,4,5 reveal) → 6 (form active)
  OK / Enter:
      version gate (msg 2204 on mismatch → quit: state 6/2)
      → sub-state 29 (validate ID≥4 / PW≥1; fail → msg 4025/4026 → sub-state 6)
      → drive substate advances (MAIN substate 6 = resting form, NO EULA; PIN modal = TICK substate 31→32) → 34 server-list fetch (lobby :10000)
      → 35 wait → 36 consume (empty → 4027; fail → 4028; else render)
  [SERVER SELECT, same window]
      → 37 server selected (persist Lastserver; randomized order; NEW badge)
      → 38 channel-endpoint fetch (lobby :10000+id) → 39 wait
      → second-password / PIN modal (≤4 chars; value → optional login-blob field)   [§1.4a]
      → 40 build TAB join string (account / PIN / host port) + secure context handoff
           (guard state 7); window exits
  game socket handshake: inbound 0/0 key exchange → reactive 1/4 auth reply (RSA pre-image = login blob [0x2B][account\0][PIN\0])  [owned by login_flow.md / crypto.md]
       (1/6 is CHARACTER-CREATE only, NOT the login send — §1.7/§4.5)
  on auth OK: server sends 3/5 EnterGameAck → write state 2

[state 2: LOAD] → (optional state 3 OPENING) → [state 4: SELECT] on 3/1 CharacterList

[state 4: SELECT]   (a 3D GScene "select" on data/map000 area 0, frozen at 14:30 — §3.5.1/§3.6/§3.7)
  build select window + 5 live 3D preview actors from the 3/1 char list
  start char-select BGM cue 920100200 on the single cat-0 slot (double-music = un-stopped loading loop 920100100 contends across the loading->select handoff; fix = stop 920100100 + join loading audio worker before play, §3.8.1)
  top caption "character count : N" = msg 2209, N = BillingState char-count (§3.8.2)
  one code-spawned ambient effect 380003000 at (508.48, 69.89, -9758.57); no map000.txt manifest (absent)
  per-slot pick = hit-test the 3D row (Y band 70..92)
  Create (action 4) → class 0..3 → internal {4,1,3,2}, face 1..7, sex, starter gear
                      → name validate (min 2; a-z/0-9/Hangul) → send 1/6 (52B) → acked via 3/7 manage-result + 3/1 char-list refresh (NO dedicated 3/23 create-result; 3/23 = SmsgCharStatusBytesByName by-name status patch)
  Delete (action 5) → confirm → send 1/7 {slot,1} (2B) → 8-byte char-manage result (committed 3/4 → corrected to 3/7 per §5 ladder verdict; subtype 2 = deleted; cooldown msg)
                      (1/14 is a separate slot-move/"location" send, 1B — NOT delete; §5/§8)
  Rename             → name validate → send 1/13 (18B) → 3/6 result
  Enter (confirm slot):
      empty slot ("@BLANK@") → open Create form
      real slot → SFX 920100200; send 1/9 (40B, slot@0, token 21149);
                  cache 880B descriptor + 96B stats; write state 5

[state 5: IN-GAME]  build world; 3/5 ack (sets Load, re-syncs char count) ; 4/1 creates local player from cache + world-state X/Z (Y=0)
                    (3/5-vs-4/1 wire order DEBUGGER-PENDING; spawn driver = 4/1, NOT 3/7 — §7; ladder 3/4·3/7·3/14 correction owned by opcodes.md)
  logout / disconnect → state 4 (Select), never back to login (state 1)
  explicit quit → state 6 → state 8 (Exit);   fatal error → state 7 → state 8
```

---

## Open questions

1. **Login message id 4029 — REFUTED/CLOSED.** Earlier read PLAUSIBLE as the channel-endpoint-fetch
   failure analogue (sub-state 39). A static re-read shows **4029 is a server-list column-header
   caption** (the list headers are 4029 / 4030 / 4031 / 4032, loaded once by the server-row painter;
   §1.9 / §11.4) — **not** an endpoint-fetch error, and **not** referenced by the login tick. The
   sub-state-39 endpoint wait has **no in-tick failure toast**. Resolved CODE-CONFIRMED; no capture
   needed.
2. **ID-box max length 16** (CORRECTED 2026-06-21 — GAP-4; the earlier "6" was the charset-filter mask, not a length). Validation only requires ≥ 4.
   Whether it reflects a legacy fixed-width account id, or is overwritten elsewhere, is unresolved.
   Wants a real `DoOption.ini` / capture. A revival may relax it.
3. **Full server-emitted status enum (capture-bound).** The painter's status field branches on only
   `{0, 3}` (active / scheduled-open); the `{2, 4, 24}` literals are LOAD-field gates and `100` is a
   SERVER-ID-field gate, not status values (§2.3, CORRECTED). What the **server actually emits** as the
   status enum is distinct from what the code branches on and is capture-only.
4. **`faceB` semantics — RESOLVED on the client side (consumer DEBUGGER-PENDING).** Descriptor +0x2E
   is **faceA**, the face index the `+`/`−` buttons clamp to **[1, 7]** (default 1). Descriptor +0x30
   is **faceB**, which the create form **sets once to `1` on open and never exposes a `+`/`−` stepper
   for**; it is packed into the descriptor / create blob but **read by no create widget**, so its
   meaning is **downstream** (skin chain / server). The on-client behaviour is CODE-CONFIRMED; the
   downstream **consumer** is DEBUGGER-PENDING.
5. **`list.dat` byte layout.** The lobby-host file's 768-byte record (name @ +0, host @ +0x100) is
   `static`-derived and on-disk-unverified; the gap between the name and +0x100 (padding? flags?
   port?) and whether +0x100 also carries a port are unknown without a real file.
6. **Slot availability flag vs lock flag — byte source CONFIRMED; wire semantics still open.** The
   two per-slot flag arrays are now pinned (§3.4): **lock / creating** = select-window field
   `+0x1548 + slot` (drives yaw-π facing + blocks enter); **occupied / selectable** = `+0x148C + slot`
   (server-supplied; gates the `1/7` select/manage click); selectable-for-enter = lock clear AND
   occupied. The only residual is the **wire** semantics of the server slot-flag (delete-pending vs
   rename-cooldown), which remains capture-pending.
7. **Class names / labels.** The UI→internal class map `{0,1,2,3} → {4,1,3,2}` is confirmed, but the
   human-readable class names (message ids 14003..14007, CP949, VFS-only) were not decoded. Needs a
   `msg.xdb` extract.
8. **`1/7 CmsgSelectCharacter` role.** The 2-byte select send sets the net-busy flag; whether it is
   a "lock this slot / fetch detail" pre-step that must precede every `1/9` enter, or only the first
   selection, is unclear without a capture or the inbound 2-byte select reply traced.
9. **EULA gating — CLOSED (NO EULA exists).** Resolved CODE-CONFIRMED by the element-by-element login
   construct walk: the login scene builds **no terms/agreement panel** (§1.4c). The labels once read as
   "EULA body" (ids **4001..4022**) are a **static stacked notice/agreement text column** on the login
   notice panel (not server-list row captions — §1.4c) and actions
   **106/107/108** are the server-list **scroll** controls — there is nothing to gate the form. MAIN
   substate 6 is the plain resting login form; the **PIN** modal is the separate TICK substate
   **31 → 32** edge (§1.4a / §1.5). No EULA-show guard to chase.
10. **Second-password / PIN modal show-trigger (NOW RESOLVED to the 31→32 edge).** The PIN's
    existence, its first-class "is-PIN" input modelling, its ≤4-char capacity, and the fact that its
    value becomes field #3 of the credential pre-image are RUNTIME-CONFIRMED (§1.4a); its **layout +
    the scramble mechanism** are CODE-CONFIRMED (§1.4a / §11.3: modal rect, the 2×5 clock-seeded
    Fisher–Yates keypad, the reset/OK/cancel tags and atlas source rects). The **show-trigger is the
    TICK workflow substate 31 → 32 edge** (31 = raise, 32 = poll-to-33), wired at the window level to
    LoginWindow actions **111/112** and to the keypad's own tags **11/12/13** (§1.4a / §1.5 rows
    31/32) — CONFLICT C1 resolved. What remains **DEBUGGER-PENDING** is (a) the **account/save flag**
    that gates entry into substate 31, and (b) the **digit→slot scramble seed value + the resulting
    permutation** (clock-seeded, not code immediates). Also open: whether the modal can be
    skipped/disabled per account. Its labels are baked atlas art (no caption ids).
11. **Char-select 2D class icon.** No standalone class-icon widget keyed by a class index exists in
    the char-select 2D builder; per-slot class is conveyed by the descriptor-driven 3D preview (§3.3)
    plus the slot frame art (§11.5b). If a 2D class badge is desired in the revival, it must be added
    fresh - there is no legacy class→source-rect lookup to reproduce.
12. **Char-select camera tween duration — CLOSED.** Resolved CODE-CONFIRMED: the normalizer is the
    literal `0.0005` (= 1/2000) over the millisecond clock, clamped at 1.0 → a **2.0 s** keyframe
    transition. The "0.5 s" tool annotation was a stale comment and is superseded (§3.5.4).
13. **Char-select preview front/back facing — CLOSED on the binary side; reframed as a port check.**
    Resolved CODE-CONFIRMED on the binary side: **yaw 0 = FRONT** (the engine facing is
    `yaw = atan2(Δx, Δz)` so forward = +Z at yaw 0; the live camera at Z = −9652 looks in −Z at the
    row ≈ −9737; the binary `.skn` loader applies **no** X-negation). What remains is a **Godot-port
    composition check** (not a binary unknown): verify the importer's mesh-local-X negation + the world
    Z-negation **compose** to "front toward camera"; if the port shows the back, add 180° yaw / mirror
    consistently (§3.3.2).

### Cross-spec conflicts recorded here (owners must resolve in their files)

- **`specs/ui_system.md` §6.3** marks sub-state **29 = "Server-list trigger"** and **31 = "Help
  screen"**. The **29 = "Server-list trigger"** label is wrong (**29 = OK-button credential
  validation**); the **31 = "Help screen"** label is wrong (**31 = raise the PIN modal** on the TICK
  workflow substate — 31 → 32 poll-to-33; CONFLICT C1 resolved in favour of ui_system §11.3's PIN
  reading, not the §6.3 "Help" label). **There is no EULA panel** (the construct walk builds none —
  §1.4c); MAIN substate 6 is the resting form, and the help button is the separate action `i` / id 105.
  The owner of `ui_system.md` should correct §6.3 row 29 and reconcile row 31 to "raise PIN modal."
  (§1.5 / §1.4a / §1.4c)
- **Opcode `1/6` — collision STATICALLY RESOLVED (no capture needed); protocol-author to split the
  rows** (`opcodes.md` / `names.yaml` / `login_flow.md`): there is **no shared opcode**. The login
  credential send is **`1/4`** (the secure auth reply to inbound `0/0`); **`1/6` is character-create
  only** (52-byte body, offset-0 = CP949 name, never `0x2B`). Proven by sweeping the major-1
  fixed-body sender family (exactly one stamps minor=6 = create; none stamps minor=4 — that is solely
  the secure path). The protocol author should set `1/6 = CmsgCreateCharacter` and the login credential
  to `1/4`. (§4.5 / §1.7)
- **New C2S char-management opcodes** `1/7` (char-manage `{slot, mode}`, 2B — `mode 0` select /
  `mode 1` delete; §5), `1/13` (rename, 18B), and `1/14` (slot-move / "location", 1B — **not** delete)
  may be absent from `names.yaml` / `opcodes.md`; the protocol author owns adding them. (§5, §6, §8)
- **Major-3 receive ladder correction (protocol-author owns the renumbering in `opcodes.md`):** the
  dispatch ladder resolves the carriers to **`3/4` = SceneEntityUpdate (variable)**, **`3/7` =
  SmsgCharManageResult (8 bytes — the delete / select / rename manage result)**, and **`3/14` =
  CharSpawnResponse (16 bytes — the enter bridge)**. The committed catalog has `3/4`/`3/7` swapped and
  mislabels the 16-byte spawn as `3/7`; the local-player spawn is driven by **`4/1`** (not `3/7`). This
  is a genuine conflict for the protocol-author, not a silent relabel. (§5 / §3.8.2 / §7)


---

# 11. Front-end scene layout — pixel-exact implementation tables (CODE-CONFIRMED)

> **What this section adds.** Sections 1-10 specify the front-end *flow* (state machine, validations,
> message ids, sends). This section adds the **layout/composition** layer an engineer rebuilds the
> screens from 1:1: the exact on-screen rectangles, the source sub-rectangles into each atlas DDS,
> the three-state (normal / hover / pressed) frame sources, and which texture each widget reads.
> Every rect below is a **literal layout constant read off the legacy scene builders** - neutral
> coordinate facts, not code. **No caption text is reproduced**; widgets that draw runtime text
> reference a numeric caption id from the section 1.9 / section 9 tables (resolved at runtime from
> `msg.xdb`). Korean labels that are **baked into the atlas art** are noted as "baked art" and carry
> no id.

## 11.0 Common composition model (CODE-CONFIRMED)

- **Design canvas:** `1024 x 768`, top-left anchored. The whole layout is **centered on screen**:
  the scene origin is set to `(screenWidth/2 - 512, screenHeight/2 - 384)` before any widget is
  placed, so all `(X, Y)` below are canvas-local. A handful of background bars are placed at a
  height-scaled Y (`Y = 326 * screenHeight / 768`); those are called out per row.
- **Widget construction convention.** Every widget is built with the same leading argument shape:
  `(textureId, X, Y, W, H, srcU, srcV, [hoverU, hoverV, pressedU, pressedV], zOrder)`. The literal
  `(X, Y, W, H)` is the on-screen rectangle; `(srcU, srcV)` is the top-left pixel of the source
  sub-rectangle in the referenced atlas DDS (its size equals the widget's `W x H` unless a frame is
  scaled). A three-state button carries three such source origins; a checkbox carries two
  (off / on). There is **no external rect table** - every rectangle is an inline construction
  argument, so the tables below are the complete layout source.
- **Widget kinds:** static image / sprite, container panel, single-frame button, three-state button,
  two-state checkbox, text label, and editable text box (the last two are the only runtime-text
  kinds). Dialog wrappers (quit-confirm, error) are panel subclasses.
- **Texture format.** The login / server-list atlases are loaded as DXT5 (the format selector passed
  to the loader is the FourCC `"DXT5"`); the char-select dim sheet uses an explicit
  raw/uncompressed format. Texture file formats and dimensions are catalogued in section 11.1.

## 11.1 Texture inventory - the front-end atlases (CODE-CONFIRMED paths; dims SAMPLE-VERIFIED)

All paths are **concrete VFS paths** (no id resolution). Dimensions/format were read from the
shipped DDS headers by a VFS harness (no pixel data extracted).

| Atlas (VFS path) | Dims | Format | Role |
|---|---|---|---|
| `data/ui/login_slice1.dds` | 1024x1024 | DXT2 | Login background art + stone chrome + **baked Korean label plates** (account / password / confirm / quit / save-id words) + the gold confirm-button face + the bottom bar |
| `data/ui/loginwindow.dds` | 1024x1024 | DXT5 | (byte-confirmed 1024², DXT5) Login panel chrome (main panel art, listbox frame, scroll arrows + thumb, server-row buttons, lower confirm/cancel buttons); **also the char-select frame atlas** (shared) |
| `data/ui/loginwindow_02.dds` | 1024x1024 | DXT2 | (byte-confirmed 1024², DXT2) Server-list parchment scroll panel + channel-selector tab blocks (the variant chrome) |
| `data/ui/InventWindow.dds` | 1024x1024 | DXT3 | Reused for the login notice / error / quit dialogs **and** the PIN modal's framed background quad (byte-confirmed 1024², DXT3) |
| `data/ui/password.dds` | 1024x1024 | DXT3 (11 mips) | (byte-confirmed 1024², DXT3, 11-level mip chain) PIN modal: all digit-tile glyph art and the reset / OK / cancel button art |
| `data/ui/openning_scenario.dds` | 1024x2048 | DXT5 | Intro vertical-panorama scenario strip (pre-login slideshow; the four `openning_00N.dds` 1024x768 frames are the opening slides) |
| `data/ui/characwindow.dds` | **512x512** | RAW BGRA8 | (byte-confirmed 512², uncompressed BGRA8 / `A8R8G8B8`) **exists in the VFS but is NOT bound by the catalogued create/select widgets** — likely the in-game character-info window, not the create sub-form atlas (see the CONFLICT note below and §4.6.7) |
| `data/ui/mainwindow.dds` | 1024x1024 | DXT3 | (byte-confirmed 1024², DXT3) Char-select composited chrome / conditional overlay button source; bound by the create sub-form builder (§4.6.7) |
| `data/ui/CarrierPigeonPerson.dds`, `CarrierPigeonAll.dds`, `tradekeepwindow.dds` | (HUD/sub-window atlases) | - | Char-select composited chrome (reused sub-window atlases) |
| `data/ui/blacksheet.dds` | - | raw (explicit fmt) | Char-select dim/overlay sheet (dims unhovered slots / fades) |
| `data/ui/server_icon.dds` | 128x128 | DXT2 | Per-server badge icon in the server list |
| `data/cursor/stand.dds` | 32x32 | DXT2 | Default arrow cursor (all front-end screens); the in-engine cursor is re-targeted to the OS cursor position each frame |

> **Char-select chrome sourcing note.** The char-select 2D builder loads the seven shared atlases
> above (`loginwindow`, `mainwindow`, `InventWindow`, `CarrierPigeonPerson`, `CarrierPigeonAll`,
> `tradekeepwindow`, `blacksheet`); its **slot-frame and button art are sub-rects of
> `loginwindow.dds`** (the same login chrome family - section 11.5). A standalone
> `data/ui/characwindow.dds` also exists in the VFS and is the dedicated char-select chrome atlas;
> the heavily-used builder handles are `loginwindow.dds` and `mainwindow.dds`. No bespoke per-scene
> login atlas is needed beyond this set.

> **`characwindow.dds` — file exists, but the create/select widgets do NOT bind it (CONFLICT
> logged).** A VFS read confirms `data/ui/characwindow.dds` **physically exists** (512², uncompressed
> BGRA8) — this resolves the earlier "the string `characwindow.dds` is absent from the binary"
> reading: the file is on disk but is loaded by a hard-coded path / table lookup rather than an
> embedded string literal, so the two findings are **not contradictory**. However, the catalogued
> create-sub-form widgets bind **`loginwindow.dds` / `mainwindow.dds` / `InventWindow.dds`**, **not**
> `characwindow.dds` (§4.6.7). The most likely role of `characwindow.dds` is the **in-game
> character-info window**, not the create/select atlas; do **not** promote it as the create atlas.
> **UV normalization for the Godot rebuild:** divide source-pixel rects by **1024** for the 1024²
> atlases (`loginwindow`, `loginwindow_02`, `mainwindow`, `InventWindow`, `password`,
> `login_slice1`), and by **512** for `characwindow.dds` if/when it is used.

> **Fonts.** No font files exist in the VFS. Runtime text widgets render with the OS Korean system
> font (HANGUL charset, code page 949); the specific typeface depends on the host OS's installed
> Korean fonts. A revival must supply a CP949-capable Korean font.

## 11.1a Front-end atlas DDS facts (byte-confirmed FourCC / mips + Godot import flags)

> These are the byte-confirmed pixel-format facts for the §11.1 atlases, read directly from the
> shipped DDS headers by a VFS harness (header bytes only; no pixel data extracted). They refine the
> §11.1 inventory table with the exact FourCC, mip count, and the Godot import flags an engineer
> needs. All four DDS dimensions below are **VERIFIED** at 1024x1024 (or as noted) from the header
> width/height fields and corroborated by a file-size reconciliation against the block-compression
> stride.

| Atlas (VFS path) | Dims | FourCC / format | Mips | Godot import note | Confidence |
|---|---|---|---|---|---|
| `data/ui/loginwindow.dds` | 1024x1024 | DXT5 (explicit per-texel alpha) | 1 (single level) | Standard DXT5 import; straight alpha | VERIFIED (dims + FourCC) |
| `data/ui/loginwindow_02.dds` | 1024x1024 | **DXT2 (premultiplied alpha)** | 1 (single level) | **Premultiplied-alpha source** - set the premultiplied-alpha import flag (or unpremultiply on import); compositing differs from DXT3/DXT5 straight alpha. This is the variant server-list / channel chrome. | VERIFIED (dims + FourCC); premultiplied-alpha flag is the key new detail |
| `data/ui/password.dds` | 1024x1024 | DXT3 (explicit alpha) | **11 mip levels** | Full mip chain present - keep mipmaps on import (atlas is sampled at multiple scales) | VERIFIED (dims + FourCC + mip count) |
| `data/ui/InventWindow.dds` | 1024x1024 | DXT3 (explicit alpha) | 1 (single level) | Standard DXT3 import; straight alpha | VERIFIED (dims + FourCC) |
| `data/ui/characwindow.dds` | **512x512** | **RAW BGRA8** (`A8R8G8B8`, uncompressed) | 1 (single level) | Uncompressed 32-bit BGRA; no block decode. UV-normalize by 512, not 1024 (see §11.1 note). | VERIFIED (dims + format) |

**Format-by-FourCC summary (the Godot-import-relevant distinction):** `loginwindow.dds` = DXT5;
`loginwindow_02.dds` = **DXT2 (premultiplied alpha)**; `password.dds` = DXT3 (11 mips);
`InventWindow.dds` = DXT3; `characwindow.dds` = uncompressed BGRA8. The DXT2-vs-DXT3/DXT5
distinction on `loginwindow_02.dds` is the load-bearing new fact: a revival must treat its alpha as
**premultiplied** when compositing, otherwise the variant chrome edges composite incorrectly.

### 11.1a-1 Sub-rect cross-check (CODE-CONFIRMED via the construction-call read)

Two of the §11.2 / §11.3 source rects, previously only sanity-checked against the byte-confirmed
1024x1024 canvas (PLAUSIBLE), are now **literal-operand CODE-CONFIRMED** from the construction-call
arguments. The "fit the canvas, not pixel-verified" caveat is **superseded** by the construction-call
read (a texture-peek of the `(615,404)` pixels remains a *content* question, not a *code* question):

| Sub-rect | Atlas | Region | Status |
|---|---|---|---|
| **Channel-tab plate (3-state)** | `loginwindow_02.dds` | src `(9,6)`, size `202x372`; hover/pressed src `(220,6)`; the plate draws **stretched** per column | CODE-CONFIRMED |
| PIN dragon-frame = **shared notice/error/quit/connecting frame** | `InventWindow.dds` | src `(318,647)`, size `340x190`; reused **stretched to 329×422** on-screen for the PIN modal | CODE-CONFIRMED |

- **Relabel.** The first rect was previously called "Server-row plate"; it is the **channel-tab plate
  (3-state)** drawn stretched per column. The actual 47×18 server-row buttons are a **separate** atlas-B
  sprite (`loginwindow.dds` src `596,985` / `643,985`; see §11.2c / §11.4) — do not conflate them.
- **PIN dragon-frame = the shared dialog frame.** The `(318,647) 340x190` rect is the **shared
  notice / error / quit / connecting** frame (§11.2d), reused **stretched to 329×422** for the PIN
  modal (§11.3) — not a PIN-specific texture.

These coordinates match the literals recorded in §11.2b (channel-tab plate `9,6` / `220,6`,
`202x372`), §11.4 (parchment plate normal `9,6` / hover-pressed `220,6`), and §11.2d / §11.3
(dialog/PIN frame `318,647 ... 340x190`).

## 11.2 Login scene - widget layout (CODE-CONFIRMED literals)

Atlas shorthand for this subsection: **A** = `login_slice1.dds`, **B** = `loginwindow.dds`,
**C** = `InventWindow.dds`, **D** = `loginwindow_02.dds`. Rect = `(X, Y, W, H)` on the 1024x768
canvas; "src" = `(U, V)` top-left into the named atlas. Three-state buttons list
`normal / hover / pressed` source origins. Action ids are the section 1.2 flow ids.

> **Construct-walk provenance (CODE-CONFIRMED).** A full element-by-element walk of the login window's
> scene builder — its **73 widgets, in build order** — re-confirms every rect / source-UV / action id in
> §11.2a–§11.2g, the **4-atlas preload order** (`login_slice1.dds` → `loginwindow.dds` →
> `InventWindow.dds` → `loginwindow_02.dds`, §11.1), the window's centred **1024×768** canvas
> (`x = screenW/2 − 512`, `y = screenH/2 − 384`), and the build contract: the builder is the window's
> primary-vtable build slot (invoked once after the constructor; the constructor builds **no** widgets
> and only seeds the init/idle field — §1.5). The **22 static notice/agreement labels** (ids 4001..4022,
> a stacked text column on the login notice panel — NOT server-list row captions, §1.4c)
> and the **server-list scroll controls** (106/107/108) are confirmed; the **actual** server rows are the
> separate 13-button loop (action 115 + i, runtime-filled, no msg id) — there is **no** EULA panel
> (§1.4c).

### 11.2a Upper window - main panel, server listbox, scroll controls

| Role | Atlas | Rect (X,Y,W,H) | Src (U,V) | Kind | States (N/H/P) | Action / caption |
|---|---|---|---|---|---|---|
| Main panel art | B | 0,110,1024,490 | 0,0 | image | - | - |
| Server dropdown / listbox container | B | 270,85,483,490 | 0,490 | panel | - | - |
| List scroll-up arrow | B | 467,86,13,10 | 483,490 | button | - | 106 |
| List scroll-down arrow | B | 467,455,13,10 | 505,490 | button | - | 107 |
| Scrollbar thumb | B | 469,98,9,9 | 496,490 | button | - | 108 |
| Listbox header / selection bar | B | 207,44,70,17 | 70,980 | image | - | - |
| 22 x server/channel row labels | (text) | X=50, Y=100..478 step 18, 383x50 | - | label | - | captions 4001..4022 |

### 11.2b Background + two channel-selector blocks

| Role | Atlas | Rect (X,Y,W,H) | Src (U,V) | Kind | Action |
|---|---|---|---|---|---|
| Full background art panel | A | 0,0,1024,398 | 0,0 | panel | - |
| Second main-panel layer | B | 270,85,483,490 | 0,490 | panel | - |
| Header strip | B | 207,44,70,17 | 0,980 | image | - |
| Channel block (loop x2): header | D | X,390,174,21 | per-block | image | - |
| Channel block: body | D | X+47,97,100,372 | srcV,6 | image | - |
| Channel block: 3-state toggle | D | X-6,97,202,372 | 9,6 / 220,6 / 220,6 | 3-state button | 400, 401 |
| Channel block: 2 text labels | (text) | X,410,174,20 and X,430,174,20 | - | label | - |

- **Channel-block loop:** two iterations; block X starts at **30**, step **+233**; the body source V
  starts at **448**, step **+124**. The two toggles carry actions **400** and **401**.

### 11.2c Decoration sprites + server-row select buttons

| Role | Atlas | Rect (X,Y,W,H) | Src (U,V) | Kind | States (N/H/P) | Action |
|---|---|---|---|---|---|---|
| 3 x small badges / arrows | B | 0,0,60,39 | 500,786 | image | - | - |
| Scrollbar thumb (dynamic Y) | D | 0,(runtime),46,168 | 700,18 | image | - | - |
| 8 x server-row select | B | X=13, Y=66, 47x18, X step +47 | 596,985 / 643,985 / 643,985 | 3-state button | 115 + index |
| Large "Refresh" action button | A | 456,-3,111,38 | 792,398 / 602,416 / 602,416 | 3-state button | **105** |
| Its caption/face image | A | 407,-3,210,70 | 743,398 | image | - |

### 11.2d Notice / error dialogs (shared dialog panel)

| Role | Atlas | Rect (X,Y,W,H) | Src (U,V) | Kind | States (N/H/P) | Action / caption |
|---|---|---|---|---|---|---|
| Dialog #1 panel (notice) | C | 342,289,340,190 | 318,647 | panel | - | - |
| Dialog #1 body text | (text) | 10,100,330,20 | - | label (center) | - | caption 4023 |
| Dialog #1 OK | C | 120,136,113,40 | 302,900 / 302,900 / 415,900 | 3-state button | 113 |
| Dialog #2 panel (error) | C | 342,289,340,190 | 318,647 | panel | - | - |
| Dialog #2 body text | (text) | 10,100,330,20 | - | label (left) | - | caption 4024 |
| Dialog #2 OK | C | 120,136,113,40 | 302,860 / 302,860 / 415,860 | 3-state button | 114 |

The dialog panel source `(318,647) 340x190` is the shared notice/error/quit frame; the quit-confirm
and generic-error dialogs reuse the same rect (see section 11.2f for the trailing quit/error panels).

### 11.2e Bottom login form (the ID/PW box) - core fidelity target

> **Action-id correction (CODE-CONFIRMED, supersedes the earlier confirm/notice swap).** The
> login-form action handler dispatches **103 = login OK / confirm** (submit the ID+PW, run the version
> gate then advance to credential validation, §1.4) and **102 = server-list reveal** (show the
> server-list / channel panel — the construct walk builds this as the "server-list reveal button", not
> a "notice / agreement" toggle, and there is no agreement panel — §1.4c). The two gold 3-state buttons
> below carry these ids exactly: the `login_slice1.dds` button at **src (456, 64, 112, 39)** is the
> **login-OK** (action **103**), and the button at **src (456, 166, 112, 39)** is the **server-list
> reveal** (action **102**). The login-OK button, the two edit fields (109/110), the save-ID checkbox
> (104), and the server-listbox scroll controls (106 scroll-up / 107 scroll-down / 108 thumb) are the
> load-bearing form controls.

| Role | Atlas | Rect (X,Y,W,H) | Src (U,V) | Kind | States / notes | Action |
|---|---|---|---|---|---|---|
| Bottom login-bar panel | A | 0, 326*H/768, 1024,442 | 0,582 | panel | Y scales with screen height | - |
| Login background plate image | A | 265,0,494,113 | 0,469 | image | the plate the ID/PW row sits on (Rect/Src were transposed in a prior pass — re-verified vs binary BuildScene 2026-06-17: dst=265,0,494,113 src=0,469, matching login.md row 53) | - |
| **Login / confirm button** (gold) | A | 456,64,112,39 (on-screen y=398) | 266,398 / 490,398 / 490,398 | 3-state button | submit ID+PW (version gate → validation, §1.4); word baked into art | **103** |
| **Server-list reveal button** (gold) | A | 456,166,112,39 (on-screen y=398) | 154,398 / 378,398 / 378,398 | 3-state button | reveal the server-list / channel panel (NOT a "notice / agreement" toggle — there is no agreement panel, §1.4c); word baked into art | **102** |
| Inner form box (layout only) | (none) | 0,0,1024,100 | - | panel | invisible container | - |
| Account-label caption art | A | 340,30,38,13 | 0,398 | image | **baked art** | - |
| Password-label caption art | A | 507,30,49,13 | 38,398 | image | **baked art** | - |
| Small decoration plate | A | 619,86,67,13 | 87,398 | image | **baked art** | - |
| **ID input field** | A | 390,32,102,13 | 615,404 | text box | plain text; max length 16 (UI cap, §1.3). Dest origin `(390,32)`, src `(615,404)`, size `102×13` | **109** |
| **Password input field** (masked) | A | 568,32,102,13 | 615,404 | text box | masked, max length 12; mask glyph = ASCII `*` (§11.2e mask note). Dest origin `(568,32)`, src `(615,404)`, size `102×13` | **110** |
| **Save-ID checkbox** | A | 694,86,13,13 | 717,398 (off) / 730,398 (on) | 3-state checkbox | pre-checked from saved-id (§1.6) | **104** |
| Server-list scroll-up arrow | B | 467,86,13,10 | 483,490 | button | scroll the server listbox up | **106** |
| Server-list scroll-down arrow | B | 467,455,13,10 | 505,490 | button | scroll the server listbox down | **107** |
| Server-list scrollbar thumb dot | B | 469,98,9,9 | 496,490 | button | scrollbar thumb / drag dot of the server listbox | **108** |

> **Atlas note for the edit fields (CORRECTED — CODE-CONFIRMED).** Both edit-field frames are
> sub-rects of **`login_slice1.dds`** (atlas **A**), sharing source **(615,404)**, size **102×13**.
> The `(390,32)` and `(568,32)` figures are the **DEST origins** on the 1024×768 canvas, **not** the
> source UV → dest `(390,32,102,13)` (ID) and `(568,32,102,13)` (PW). The earlier reading that called
> `loginwindow.dds` the edit-field source and `(390,32)/(568,32)` source rects is **superseded**: the
> CODE-CONFIRMED source is `login_slice1.dds` (A) at `(615,404)`. (Corroborated by the ctor tails —
> ID maxlen 16 / plain filter; PW maxlen 12 / masked filter 0x81.)

> **Password masking — one ASCII asterisk per character (CODE-CONFIRMED, corrects "round dot").** The
> password textbox renders **one ASCII asterisk `*` glyph per entered character** (a fixed-pitch run
> over the live character count), **not** a round bullet dot. The ID field is plain (unmasked). A
> faithful rebuild must mask the password as `*`-per-character. UI max lengths are **ID = 16**,
> **PW = 12** (legacy editbox caps; the protocol caps are owned by `login_flow.md`, §1.3).

> The account / password / confirm / save-id Korean words are **baked into `login_slice1.dds`** (the
> caption-art plates and the gold button faces) - they are **not** message-catalogue strings. Only
> the server-row labels (4001..4022) and the dialog bodies (4023/4024) are runtime text.

> **Default input focus is conditional on the saved id (CODE-CONFIRMED).** When the login form is
> built, focus is placed once, branching on whether a saved account id exists (the persisted saved-id
> string, recognised as absent by a `"(null)"` sentinel):
> - **No saved id** (fresh install / id not remembered): the ID field is left empty and **focus goes
>   to the ID field** — the caret blinks in the ID box.
> - **Saved id present** (Save-ID was checked previously): the ID field is **pre-filled** with the
>   saved account string, the Save-ID checkbox is shown checked, and **focus goes to the PW field** —
>   the caret blinks in the password box so the user types the password directly.
>
> A faithful rebuild must reproduce this conditional default (ID box by default, PW box when an id is
> remembered), tied to the Save-ID persistence of §1.6.

> **Caret behaviour (CODE-CONFIRMED).** The focused field — and only the focused field — draws a
> blinking **insertion caret**: a thin vertical insertion bar at the text insertion point (right edge
> of the text when the cursor is at the end, otherwise advanced one fixed glyph pitch per character).
> It is an insertion bar, **not** a block/box cursor. The unfocused field draws no caret. The blink is
> a **1 Hz square wave** — about **500 ms on / 500 ms off** (a constant 500 ms half-period) — driven
> off a single shared global blink phase, so all editboxes blink **in sync**. In the password field
> the entered text is still rendered as one `*` glyph per character (one fixed pitch per char, §11.2e
> masking note); the caret rides at the masked insertion point.



> **The field handles are object field offsets, not "widget index 170/171" (CODE-CONFIRMED;
> supersedes the prior index note).** An earlier reading referred to the ID/PW edit boxes by global
> widget-array slots "≈170/171". Those are **global widget-manager registration slots** (registration-
> order dependent), **not** the field handles on the login-window object. The authoritative handles are
> the login-window **object field offsets** for the two edit boxes; the global-array indices are
> **needs-debugger** to confirm and must not be used as the field identity. The small-string-optimized
> inline text buffer inside each textbox (chars stored inline below a small length, else via a pointer)
> is confirmed.

> **Login quit - there is NO dedicated bottom-bar quit sprite (CORRECTS any earlier assumption).**
> The login scene exposes two quit routes, neither of which is a stand-alone "quit" push-button face
> on the bottom bar:
> 1. **Keyboard accelerator.** A keyboard activation triggers an immediate engine shutdown. No widget
>    feeds this path - it is keyboard-only.
> 2. **Visible route via a strip button** that advances the login flow toward the quit-confirm gate,
>    whose modal box is the **shared dialog frame** - `InventWindow.dds` (`C`) source `(318,647) 340x190`
>    drawn at on-screen `342,289,340,190` (the same notice/error/quit frame, §11.2d / §11.2f). The
>    quit-confirm popup is a dialog *panel*, not a button.
> **The quit tab is register-staged — PLAUSIBLE.** The top server-tab / option strip builds its
> buttons in a loop with a **register-accumulated x position and a computed action id** (no single
> literal per button). The strip button whose computed id resolves to the click handler's **quit case
> (209 / 220)** is the quit tab, but **which loop index** is the quit slot, and its exact source / screen
> rect, are **register-staged → PLAUSIBLE / needs-debugger** (breakpoint the click handler against the
> live client to pin the quit slot). The tab-button rects follow the pattern `(13 + 47·n, 66, 47, 18)`
> with hover/pressed at the `985`-row sprite band.


### 11.2f Trailing controls + quit/error dialogs

| Role | Atlas | Rect (X,Y,W,H) | Src (U,V) | Kind | States (N/H/P) | Action |
|---|---|---|---|---|---|---|
| PIN modal sub-window mount | - | 347,173,329,422 | - | child window | initially hidden (see section 11.3) | - |
| Small sub-panel | (none) | 356,531,313,132 | - | panel | - | - |
| Image plate | A | 67,48,178,13 | 0,437 | image | - | - |
| Image plate | A | 0,100,313,32 | 289,437 | image | - | - |
| Button | B | 40,82,110,38 | 520,492 / 520,492 / 635,492 | 3-state button | 111 |
| Button | B | 164,82,110,38 | 750,492 / 750,492 / 865,492 | 3-state button | 112 |
| Quit-confirm dialog panel | C | 342,289,340,190 | 318,647 | panel | - | - |
| Generic error dialog panel | C | 342,289,340,190 | 318,647 | panel | - | - |

### 11.2g Draw order / z-order, show/hide fade, static chrome (CODE-CONFIRMED)

**Paint order is back-to-front in build order.** The login window paints its widgets in the order they
were added to the scene (the §11.2a–§11.2f row order), depth-first per panel: a panel paints, then its
whole subtree paints on top, before the next sibling. Only currently-visible widgets paint. The first
widget added is the **bottommost**, the last added is the **topmost**. Hit-testing walks the **same
order in reverse**, so the topmost widget under the cursor receives the click first — the inverse
relationship a correct z-order requires.

**Back-to-front z-order (1 = bottom, highest number = top):**

| z (1 = bottom) | layer |
|---:|---|
| 1 | full-screen background art + stone-frame / bezel chrome panel |
| 2 | central login-window main panel art |
| 3 | server listbox container panel (channel-row labels, scroll arrows, thumb, header bar) |
| 4 | the two server-channel selector blocks (header + body + toggle + labels) |
| 5 | badge / arrow decoration sprites + the dynamic scrollbar thumb |
| 6 | the server-row select buttons |
| 7 | the large top action button + its caption face plate |
| 8 | notice / success dialog panel #1 (+ body label + OK button) — hidden until shown |
| 9 | error dialog panel #2 (+ body label + OK button) — hidden until shown |
| 10 | the bottom login-bar container panel |
| 11 | the bottom confirm/login gold button + its label face plate |
| 12 | the inner form sub-panel (an invisible layout panel; its subtree, z 13–17, paints on top) |
| 13 | the ID / PW caption-art plates + small decoration plate |
| 14 | the ID input box (its glyph text + caret when focused) |
| 15 | the PW input box (masked `*` glyphs + caret when focused) |
| 16 | the Save-ID checkbox (off/on frame) |
| 17 | the second bottom button |
| 18 | the PIN / second-password keypad sub-window — built hidden; paints over the form when shown |
| 19 (topmost content) | the quit-confirm and generic-error modal dialogs — hidden until triggered; added last, so they always composite over the form |
| 20 (above everything) | the hardware mouse cursor sprite — a separate top-level node repositioned each frame, topmost of all |

Layers 8, 9, 18 and 19 are present in the tree but invisible in the steady login form; they paint only
when their sub-state shows them, and because they are added late they always composite **over** the
form when shown. The mouse cursor is a sibling top-level node, above all window content.

**Widget show/hide fade (CODE-CONFIRMED, with offsets).** Every GU widget runs a generic alpha fade
on show/hide: its alpha ramps **±64 per frame** toward **255** (show) / **0** (hide), clamped
`[0,255]` — about a **4-frame fade** (0 → 64 → 128 → 192 → 255). The widget fields: **alpha at +4**,
**visible flag at +0x8C**, **forced-alpha override at +0xF** (any value ≠ 0xFF **pins** the alpha and
**bypasses** the fade); the composited color = `(alpha<<24)|rgb`. The ramp is **frame-counted, not
millisecond-timed**, so the wall-clock duration scales with frame rate (≈ 67–83 ms at 60 fps). Per
transition the **login state machine flips the +0x8C visible flag** on the overlay panels, which is
what fires the fade. So the revealed login form, the **server-list overlay**, the notice/error/quit
dialogs and the **PIN keypad** **FADE IN** via this generic ramp (they do **not** snap), unless a
widget pins a forced alpha. This is a generic show/hide transition, **not** a continuous
pulse/breathing effect.

- **CODE-CONFIRMED** for the notice / error / quit panels.
- **PLAUSIBLE-HIGH** for the PIN sub-window — a forced-alpha pin was not exhaustively excluded (the
  one live-frame tiebreak; carried as residual debugger-pending).

**Hardware cursor (CONFIRMS §11.1).** The hardware cursor sprite is **repositioned to the OS pointer
every frame** (read the OS pointer, map to client space, drive the cursor widget to it, clamped to the
client rect) — now CODE-CONFIRMED.

**Login chrome is static art (CONFIRMS the §5/fidelity scan).** The bezel / stone-frame, the central
panel art, the dragon / hanging-ring / flag decoration sprites, the painting and caption-art plates
are all **static art** — drawn with a translation-only transform; no rotation, sway, pulse, gradient
fill or alpha-cycle on any login chrome element. The only animated behaviours on the login form are
(a) the ~4-frame show/hide alpha fade, (b) the 1 Hz caret blink (§11.2e), (c) the per-frame cursor
follow, and (d) the separate intro curtain (§1.0 / §1.5, out of scope here). The 3-state buttons and
checkbox swap frames by mouse state (normal/hover/down) — a state-driven sprite swap, not a continuous
animation.


### 11.2h The two baked-art backdrop panels (CODE-CONFIRMED — bezel / rings / flag / URL are NOT widgets)

The carved-iron **bezel frame** (top / left / right / bottom rails + corners), the **hanging rings /
chains**, the small **red flag** (top-left), and the **URL text** (top-right) are **not** separate
sprite widgets. They are all painted pixels **baked into two large background-art panels**, both
sub-rects of `login_slice1.dds` (atlas **A**). The earlier subsections fold these into the z-order as
"full-screen background art + bezel chrome" (z=1) and "bottom login-bar container panel" (z=10)
without their backdrop dest/src spelled out as explicit rows; they are spelled out here.

| Role | Atlas | Rect (X,Y,W,H) | Src (U,V,W,H) | Kind | Notes | z |
|---|---|---|---|---|---|---|
| **Upper backdrop** (carved bezel top/left/right rails + corners + hanging rings + red flag (top-left) + URL (top-right) + upper frame) | A | 0,0,1024,398 | 0,0,1024,398 | image (backdrop blit) | bottommost; the entire upper carved-iron frame is **one baked image**, not child sprites | 1 |
| **Lower backdrop** (bottom carved-metal plate holding the ID/PW rows + buttons) | A | 0, round(326*H/768), 1024,442 | 0,582,1024,442 | image (backdrop blit) | Y scales with screen height; src is bottom-aligned in the atlas (582+442 = 1024) | 10 |

> **Flag / rings / URL are baked ART, not runtime sprites (CODE-CONFIRMED).** A faithful 1:1 rebuild
> must **blit these two rects** rather than place individual flag / ring / chain / URL sprites — the
> legacy client never split them out as widgets. In particular the **URL text is image art baked into
> the upper backdrop, NOT a `msg.xdb` string**: do not render it as runtime text. The confirmed canvas
> is **1024 x 768, top-left anchored, centred on screen** (scene origin `(screenW/2 - 512,
> screenH/2 - 384)`).
>
> **`login_slice1.dds` vertical-band partition (1024² atlas):** rows **0..398** = upper backdrop / frame
> art (the upper-backdrop row above); rows **398..582** = the broken-out caption-art plates and gold
> button-face plates (the small sprites in §11.2c / §11.2e at srcV 398 / 404 / 437 / 469); rows
> **582..1024** = lower backdrop / bottom-panel art (the lower-backdrop row above). An engineer must
> slice the atlas along these three bands.

### 11.2i Login widget construction order (CODE-CONFIRMED — full 73-widget sequential build walk)

> A full element-by-element walk of the login scene builder confirms the §11.2a–§11.2h tables byte-for-
> byte and establishes the exact build sequence. Build order = z/paint order (first built = bottommost);
> the z-order column in §11.2g is the direct consequence of this sequence. All 73 widgets are built by
> the single builder; none is built by the constructor (the constructor only seeds the init/idle field).
>
> **4-atlas preload order** (loaded into the window's `GUTextureList` at the very top of the builder,
> before any widget is constructed, in this order):
>
> | Handle | VFS path |
> |---|---|
> | A | `data/ui/login_slice1.dds` |
> | B | `data/ui/loginwindow.dds` |
> | C | `data/ui/InventWindow.dds` |
> | D | `data/ui/loginwindow_02.dds` |
>
> **Grouped build sequence (73 widgets, in build order):**
>
> | Phase | Widgets | Description |
> |---|---|---|
> | 1 | 1–9 | Server-list listbox container (atlas B): main panel bg, listbox container panel, scroll-up arrow (action 106), scroll-down arrow (action 107), scrollbar thumb (action 108), header chrome strip; then 22 static notice/agreement text labels (msg ids 4001..4022, stacked at x=50, y=100..478 step 18). Parented into window; hidden at build. |
> | 2 | 10 | Full background art panel (atlas A, src 0,0,1024,398) — the resting login backdrop; shown at build. |
> | 3 | 11–35 | Server-list overlay panel (atlas B/D): overlay container, header strip, TWO-PLATE LOOP (2 iterations, actions 400/401): per plate — header label, parchment body image, 3-state plate button, caption label #1, caption label #2; parchment scrollbar thumb (dynamic Y); 3 status/indicator images; PAGER BUTTON LOOP (10 buttons, actions 115..124). Then: the refresh button (action 105, atlas A) and its gold label plate. |
> | 4 | 36–37 | Notice dialog #1 (action 113, atlas C) and Error dialog #2 (action 114, atlas C) — built hidden, each with body label and OK button. |
> | 5 | 38 | Bottom-bar band panel (atlas A, src 0,582,1024,442; Y scales with screen): hosts the server-list reveal button (action 102), the gold refresh label plate, and the refresh button (action 105). |
> | 6 | 39–47 | Top form / inner-form sub-panel (layout only, invisible): ID caption art (action −), PW caption art (−), small deco plate (−), ID textbox (action 109, IME 16, maxlen 16), PW textbox (action 110, IME 12, maxlen 12, masked), Save-ID checkbox (action 104), OK/Login button (action 103). Parented into bottom-bar panel. |
> | 7 | 48 | PIN / second-password keypad sub-window (dst 347,173,329,422, built hidden). |
> | 8 | 49–50 | Option-strip panel (dst 356,531,313,132, hidden): two art plates + two tab buttons (actions 111, 112). |
> | 9 | 51–52 | ExitPanel and ErrorPanel (shared quit/error dragon-frame dialogs, atlas C, built hidden). |
>
> **Inner form construction order (widgets 39–47, the credential form).** The exact sub-sequence
> within the inner-form sub-panel (widget 39) is, in build order:
> 1. Inner form sub-panel (invisible layout container, dst 0,0,1024,100) — parent of the form row.
> 2. ID caption art plate (atlas A, dst 340,30,38,13, src 0,398).
> 3. PW caption art plate (atlas A, dst 507,30,49,13, src 38,398).
> 4. Small deco plate (atlas A, dst 619,86,67,13, src 87,398).
> 5. **ID / account textbox** (atlas A, dst 390,32,102,13, src 615,404; IME 16; maxlen 16; action 109).
> 6. **Password textbox** (atlas A, dst 568,32,102,13, src 615,404; IME 12; maxlen 12; masked; action 110).
> 7. **Save-ID checkbox** (atlas A, dst 694,86,13,13; off src 717,398; on src 730,398; action 104).
> 8. Saved-id **prefill branch** — runs after the checkbox is built; reads the persisted saved id,
>    sets checkbox state and pre-fills the ID box if applicable, selects initial focus target (§1.3 /
>    §11.2e). No new widget built in this step.
> 9. **OK / Login button** (atlas A, dst 456,64,112,39; N src 266,398 / H+P src 490,398; action 103).
>    Built AFTER the checkbox and the prefill branch.
>
> **Parenting order into the inner form panel** (determines paint/z order within the form):
> ID-caption → PW-caption → deco → ID textbox (action 109) → PW textbox (action 110) →
> checkbox (action 104) → OK button (action 103). The inner form panel is then parented into the
> bottom-bar band panel as its last child.

## 11.3 PIN / second-password modal - layout & keypad behaviour (CODE-CONFIRMED)

The PIN modal (section 1.4a) is the second-password child window mounted over the login background.
It uses two atlases only: **`password.dds`** (all digit-tile and reset/OK/cancel button art) and the
shared dialog/frame atlas (`InventWindow.dds`) for the framed background quad - source rect
`(318, 647, 340, 190)` (`srcU=318, srcV=647, W=340, H=190`), the same notice/error/quit frame
(section 11.2d). This is the dragon-frame background quad described in the table below.

**Build call order (CODE-CONFIRMED).** The modal is constructed by the login-window builder as a
sub-object (an operator-new allocation of 696 bytes), in this exact sequence: (1) construct the
modal object and install its vtable; (2) call `BuildKeypad` — which loads the two own atlases
(`password.dds` → primary handle, `InventWindow.dds` → secondary handle) and builds **all keypad
children** in order: masked-echo label → 100 digit buttons (10 positions × 10 digit-glyph stacks)
→ Reset button (tag 11) → OK button (tag 12) → Cancel button (tag 13) → nested close panel; (3)
call the modal's vtable hide slot (argument 0) — the modal is **built hidden**; (4) parent the
modal into the LoginWindow. The `BuildKeypad` child-add order equals the paint order within the
modal; the masked-echo label is built and added first, behind the digit tiles and buttons.

- **Modal panel rect:** `347, 173, 329, 422` on the canvas. **Panel origin = `(347, 173)`**; every
  panel-local child rect below maps to the canvas as **`(347 + Xlocal, 173 + Ylocal)`** (the panel-
  local coordinates below are relative to this origin). The frame quad is centred inside the window
  from the window's own W/H, which lands it on this panel rect.
- **Dragon-frame background quad.** The modal background is the framed dragon quad - a sub-rect of
  `InventWindow.dds`, source `(318, 647, 340, 190)`, NOT the whole 1024x1024 texture. The source art
  is `340 x 190` but the on-screen panel is `329 x 422` (taller than the source), so the frame is
  drawn **stretched**: render it as a **NinePatch** (or equivalent corner-preserving stretch) from
  `(318, 647, 340, 190)` up to the `347, 173, 329, 422` panel rect. The keypad tiles and buttons
  below are NOT stretched - they are drawn at their native `password.dds` source sizes
  (52x52 / 154x58 / 58x30). The frame quad is constructed **set-invisible at build** and is **shown
  when the modal is raised**; it is the visual backdrop, so render it behind the keypad tiles/buttons.
- **No runtime text - the warning line is baked atlas art (CONFIRMED).** The number-entry caption,
  the **red warning line**, the button faces, and the modal title are all **baked into the atlas
  art** (the digit/button glyphs into `password.dds`; the title + warning line into the
  `InventWindow.dds` dragon-frame quad). The modal performs **no caption lookup at all** - there is
  no message-catalogue id for the warning line. A revival must therefore render the warning line as
  part of the dragon-frame sub-rect art and must NOT wire it to a `msg.xdb` / message-catalogue
  caption. (A dynamic warning string would be a NEW addition, not a fidelity match.) The entered PIN
  is held as an internal string (<= 4 chars) and shown as a masked `*`-per-digit string.
- **Masked-PIN echo label (passive widget).** There is no editable text-box widget, but the build
  function DOES construct a dedicated passive label that echoes the typed digits as `*` characters
  (one `*` per entered digit, up to the internal entry cap of 4). It is a plain text label (no atlas
  source rect), built first — before the keypad tiles — at **panel-local `(81, 138, 150, 22)`**
  (`X=81, Y=138, W=150, H=22`); it carries no tag and takes no input (the keypad buttons drive entry).
  Render it as the masked-echo display surface above the keypad.
- **Nested close ExitPanel inside the keypad (CODE-CONFIRMED, folded from the construct walk).** The
  keypad builder constructs **one additional** centred close/X panel **inside** the modal — its own
  `ExitPanel` on the shared dragon-frame sub-rect (`InventWindow.dds` src `(318,647)`, `340×190`),
  placed centred at `((W−340)/2, (H−190)/2, 340, 190)`. It is the modal's own close frame and is built
  in addition to the digit tiles + Reset/OK/Cancel buttons. A faithful rebuild includes this nested
  close panel as a distinct child of the PIN modal.

### 11.3a Keypad tile grid (2 rows x 5 columns)

| Property | Value |
|---|---|
| Column count | 5 (positions 0..9, row-major) |
| Tile X (panel-local) | `55 * (p mod 5) + 28` -> columns **28, 83, 138, 193, 248** |
| Tile Y (panel-local) | **170** for top row (p < 5), **230** for bottom row (p >= 5) |
| Tile size | **52 x 52** |
| Column spacing | 55 px |
| Row spacing | 60 px |

Equivalently (build-function form, panel-local): each tile position `p` is placed at
dest **X = `55 * (p % 5) + 28`**, **Y = `170`** for the top row (`p < 5`) / **`230`** for the bottom
row (`p >= 5`), size `52 x 52`. The OK/확인 (tag 12, dest `90,290`, `154x58`), Cancel/취소 (tag 13,
dest `90,350`, `154x58`) and Reset (tag 11, dest `243,133`, `58x30`) buttons in section 11.3d sit at
fixed panel-local dests; their source rects are listed there.

### 11.3b Scrambled-digit glyphs (sourced from `password.dds`)

The keypad does **not** build one button per position. For **each** of the 10 positions it builds a
stack of **10 overlapping digit-graphic buttons** (one per digit value 0..9) at the same tile rect -
**100 button widgets total**. Per position, exactly one digit graphic is made visible; the rest are
hidden. The visible digit at position `p` is `perm[p]` from the scramble (section 11.3c).

- **Digit glyph source (CORRECTED — the digit varies along U/x, the button state along V/y).**
  For digit value `d`, the source **column** is `U = d * 52` (columns `0, 52, ..., 468`). The three
  button states read from three source **rows**: **normal = 560, state1/hover = 664, state2/pressed
  = 612** (these are the `srcV` values), each tile `52 x 52`. So digit `d`'s normal-state glyph is
  `password.dds` source rect **`(d*52, 560, 52, 52)`** (`srcU = d*52`, `srcV = 560`). The digit
  changes the U (x) coordinate; the button state changes the V (y) coordinate. (A previous revision of
  this section recorded the rect transposed as `(560, d*52, 52, 52)`; the build function's actual
  source-rect arguments are `srcU = d*52`, `srcV = 560` — the axes were swapped.)

### 11.3c Keypad scramble (CODE-CONFIRMED; on-Reset re-roll CODE-CONFIRMED — CAMPAIGN 9b)

The digit->position mapping is produced **entirely client-side** - there is no server permutation and
no fixed local table:

1. On modal open (and on every Reset press), the C-runtime RNG is seeded from the **current local
   wall-clock time**.
2. A textbook **Fisher-Yates shuffle** permutes the digit pool `[0..9]` (each index draws a random
   value and swaps; the random range is extended past the 15-bit RNG limit).
3. For each position `p`, the digit-button matching `perm[p]` is set visible and the other nine
   hidden - so position `p` displays digit `perm[p]`.

Result: a **fresh random permutation of 0-9 every time the modal opens and every time Reset is
pressed**. Both the on-open re-roll AND the on-Reset re-roll are now **CODE-CONFIRMED** — the keypad
OnEvent tag-11 (Reset) handler re-invokes the scramble routine (CAMPAIGN 9b, IDA re-walk).

**Show-time ordering (CODE-CONFIRMED — load-bearing for a faithful port).** The modal's
`SetVisible(true)` call runs the following steps in strict order before the base `SetVisible` show:
1. Hide the nested close-panel child (sets its visible flag to false).
2. **Clear the entered-digit collection** (resets any previously typed PIN).
3. **Clear the submitted flag** (`submitted = 0`).
4. If the masked-echo label exists: run the **scramble** (re-randomise the layout).
5. Call the base widget `SetVisible(true)` to actually show the modal.

A port must **clear-then-scramble-then-show** in exactly that order. Scrambling before clearing the
entry collection would leave stale entry state; showing before scrambling would briefly display the
prior layout. The submitted flag clears before scramble so a double-show cannot carry a stale "already
submitted" state into the newly-shown modal.

- **Digit-key behaviour — the TAG is the true digit, the POSITION is scrambled.** The on-screen digit
  positions are re-rolled on open and on Reset, but each digit button's **tag is its true digit value
  `d` (0..9)** — set when the 10 overlapping digit-glyph buttons are built for each position (the inner
  build loop runs the tag counter 0..9). The keypad event handler reads the pressed button's **tag**,
  not its grid position, to know which digit was entered. So scrambling the visible layout never
  changes which digit a button means; the visible glyph and the tag always agree.

### 11.3d Reset / OK / Cancel buttons + key tags

Button **tags** are integer ids stored on each widget and read back by the keypad event handler.

| Role | Tag | Rect (panel-local, X,Y,W,H) | `password.dds` src (normal / hover / pressed) | Behaviour |
|---|---|---|---|---|
| Digit tiles 0-9 | 0..9 | per section 11.3a (52x52) | `(d*52, 560)` / `(d*52, 664)` / `(d*52, 612)` — digit `d` along U/x, state along V/y (section 11.3b) | append digit (cap 4), mask `*` |
| Reset (clear + re-shuffle) | 11 | 243,133,58,30 | 663,8 / 663,88 / 663,48 | re-run scramble (section 11.3c) |
| OK (submit) | 12 | 90,290,154,58 | 330,0 / 330,116 / 330,58 | submit second password (PIN -> login blob, section 1.4a) |
| Cancel (close/abort) | 13 | 90,350,154,58 | 486,0 / 486,116 / 486,58 | close / abort modal |

> The OK submit hands the PIN to the protocol layer (the second-password / PIN destination is the
> optional login-blob field - owned by `login_flow.md` section 4.2; the in-game gift-character
> variant of this modal routes its submit through the net handler with a separate constant). The
> modal fires no VFX and no PIN-specific SFX of its own.

> **DISCREPANCY RESOLVED (CAMPAIGN 9b, IDA — the keypad OnEvent switch is ground truth).** The handler
> maps **tag 11 = Reset (re-shuffle)**, **tag 12 = OK (submit)**, **tag 13 = Cancel** — i.e. THIS table
> is correct. The earlier campaign-9 wave-3 reading (11 = OK / 12 = Clear) was WRONG. The rects were
> never in dispute (the (243,133,58,30) small button re-runs the scramble; the (90,290,154,58) wide
> button submits). **The PIN panel is routed ONLY by these keypad-internal tags 11/12/13** — the
> LoginWindow actions **111/112 are the login option tabs, NOT PIN routing** (§1.4a; the earlier
> 111→confirm / 112→cancel cross-binding is dropped — the binary does not support it).

### 11.3e Two distinct numeric keypads — do NOT conflate them

The binary builds **two different** on-screen numeric keypads. Only the first is the §11.3 modal:

- **The §11.3 login PIN / second-password modal (this section).** Dragon/parchment-framed popup using
  **`password.dds` + `InventWindow.dds`** (the `(318,647,340,190)` frame). The SAME builder serves
  **two mount points** with an identical layout: the login-time second-password modal (section 1.4a),
  and the **in-game HUD second-password keypad** (storage / gift-character unlock). The in-game
  variant is the same widget layout, not a separate one — its submit routes through the net handler
  with a different constant (section 11.3d blockquote).
- **The `AutoCheckPanel` anti-bot / "auto-check" keypad (a SEPARATE class — NOT §11.3).** A distinct
  countdown challenge keypad that uses **`password.dds` only** (no dragon frame), with a **different
  tile grid**, two large buttons, **three yellow text labels**, and an **~18-second timer** with a
  periodic tick callback. Do not treat it as the §11.3 modal; it is an independent anti-bot challenge
  and would be documented separately if/when a spec covers it.

## 11.4 Server-list overlay - widget layout (CODE-CONFIRMED literals)

> **CORRECTED DISPLAY MODEL (CODE-CONFIRMED; supersedes the prior "10 rows + 2 channel plates"
> reading).** The two parchment **PLATES (actions 400/401) ARE the selectable servers** — 1 plate = 1
> server, max 2 shown per page, action **400 = LEFT** = record `2·page`, action **401 = RIGHT** = record
> `2·page + 1`; the 8-byte record (§2.2) paints **onto a plate**. The ten **115..124 widgets are PAGER
> buttons** — page = action − 115, re-paint the 2-plate view, reset to a blank UV by the painter — **NOT
> server rows and NOT selectable servers**. The old reading (ten server rows + two channel toggles) is
> **superseded**; the rows/notes below are corrected accordingly.

Server selection is a **visibility sub-state (35/36/37) of the same login window** (section 2): all of
its widgets are built ONCE at login-scene build by the same single window builder and toggled
visible/hidden by the per-frame tick (§1.5). There is **one builder, not two** — the "classic vs new"
server-list look (§2.1) is a render-time branch on a client config flag (a field index, baked-art
overlay), not a second build function. The set reuses the same four atlases loaded once at login-scene
build. Shorthand: **A**=`login_slice1.dds`, **B**=`loginwindow.dds`, **C**=`InventWindow.dds`,
**D**=`loginwindow_02.dds` (parchment, DXT5).

| Role | Atlas | Src rect (U,V,W,H) | Dst (X,Y,W,H) | Kind | Action / caption |
|---|---|---|---|---|---|
| Full background art panel | A | 0,0,1024,398 | 0,0,1024,398 | panel (full-bg) | - |
| Bottom-bar band | A | 0,582,1024,442 | 0,(runtime),1024,442 | panel (Y scales w/ screen) | - |
| Server PLATE (the SELECTABLE SERVER) - normal state | D | 9,6,202,372 | col0 dst 24,97,202,372 / col1 dst 257,97,202,372 | 3-state plate | **400** = LEFT (record `2·page`) / **401** = RIGHT (record `2·page+1`) — **one plate = one server, max 2/page** |
| Server PLATE - hover/pressed state | D | 220,6,202,372 | (same dst as normal) | 3-state plate | **400/401** (header label shares the plate's action) |
| Parchment scroll BODY - channel column 0 | D | 448,6,100,372 | dst 77,97,100,372 | image | - |
| Parchment scroll BODY - channel column 1 | D | 572,6,100,372 | dst 310,97,100,372 | image | - |
| Parchment scrollbar thumb | D | 700,18,46,168 | dst 0,(runtime),46,168 | image (dynamic Y) | - |
| Server-list PAGER buttons x10 (loop) | B | 596,985,47,18 / hover-pressed 643,985 | X=13+47n, 66,47,18 | 3-state button | **115..124** — **PAGER** (page = action − 115; re-paints the 2-plate view; reset to blank UV by the painter; **NOT** server rows, **NOT** selectable servers) |
| List scroll-UP arrow | B | 483,490,13,10 | 467,86,13,10 | 1-state button | - |
| List scroll-DOWN arrow | B | 505,490,13,10 | 467,455,13,10 | 1-state button | - |
| Scrollbar thumb / commit dot | B | 496,490,9,9 | 469,98,9,9 | 1-state button | - |
| **Server-list reveal button** (on bottom-bar) | A | 154,398,112,39 normal / 378,398 H+P | 456,166,112,39 | 3-state button | **102** — arms the server-list overlay (distinct from the refresh button below; also reachable via action 112 / 'p') |
| **Refresh button** | A | 792,398,111,38 | 456,-3,111,38 | button (+2nd UV rect) | **105** (10 s cooldown -> re-enter fetch) |
| Refresh-button label plate | A | 743,398,210,70 | 407,-3,210,70 | image (gold plate) | **baked art** |
| List column header labels | (text) | - | in scroll | label | captions 4029..4032 |
| Availability indicator (per row) | (text) | - | per row | label | population captions 6001..6005 |
| Notice dialog #1 FRAME | C | 318,647,340,190 (== shared notice panel) | 342,289,340,190 (centered) | panel (hidden) | runtime body caption (msg.xdb id) + OK button |
| Error dialog #2 FRAME | C | 318,647,340,190 (== shared notice panel) | 342,289,340,190 (centered) | panel (hidden) | runtime body caption (msg.xdb id) + OK button |
| Sword/arrow cursor | `data/cursor/stand.dds` | - | follows mouse | sprite | verified vs `data/cursor/game.ver` |

> **Server-list reveal vs refresh — two distinct widgets (CODE-CONFIRMED).** The **server-list reveal
> button** (action **102**, bottom-bar dst `456,166,112,39`, atlas A src normal `154,398` / hover+pressed
> `378,398`) is the control that **arms the overlay** (shows the parchment-plate server selector). The
> **refresh button** (action **105**, bottom-bar dst `456,−3,111,38`, atlas A src normal `792,398` /
> hover+pressed `602,416`) **re-fetches the server list** (throttled to ~10 s). They sit at very similar
> X positions (`456`) but at different Y values (`166` vs `−3`) on the bottom-bar panel, and carry
> different art and different handlers. Do not conflate them.

- **Pager-button count = exactly 10 (CODE-CONFIRMED).** The button loop runs X from 13 in steps of
  +47 while X < 483 → 10 iterations → X ∈ {13,60,107,154,201,248,295,342,389,436}, each at dst
  `(X,66,47,18)`. Each registers action id **115 + index** → the contiguous range **115..124**. **These
  ten are PAGER buttons, not server rows:** at sub-state 37 a click sets **page = action − 115** and
  re-invokes the server-record painter to re-paint the **two plates** at that 2-record page offset; the
  painter **resets all ten to a blank UV** (which is why they carry no server content), and a pager
  click **selects nothing and changes no sub-state** (pure paging). They are capacity for paging a list
  longer than the two plates ever show. These small sprites (`loginwindow.dds` src `596,985` normal /
  `643,985` hover-pressed, `47x18`) are DISTINCT from the server PLATE — see the plate note below.
- **Per-server-row record:** 8 bytes/entry, little-endian (decode owned by section 2.2 /
  `login_flow.md`): `+0` u16 server id (valid range 1..40), `+2` i16 status, `+4` i16 population /
  load code (color thresholds 500/800/1200), `+6` i16 open-time / extra flag. Row count from the
  window's row-count field. **Display names are NOT on the wire** — server id (1..40) indexes a
  client-local localized name table (string banks **5001..5040** + locale banks). Population captions
  **6001..6005**; column headers **4029..4032**; unknown-id fallback **5901**.
- **Plate click → channel-endpoint flow (cross-ref §1.5 / §2.5 / `login_flow.md` §2):** a clicked
  **plate** (action **400/401**), gated to sub-state **37** and guarded by **`status_code == 0 &&
  load < 2400`**, resolves the record index `(action − 400) + 2·page` (action 400 = LEFT = record
  `2·page`; action 401 = RIGHT = record `2·page + 1`) and sets the window's selected-server field to
  that record's `server_id`. The selected id is persisted to the client `Lastserver` setting and added
  to **10000** to derive the channel-endpoint fetch port (`10000 + server_id`) — the sub-state advance
  **37 → 38**. **The two parchment plates (actions 400/401) ARE the two selectable SERVERS, not channel
  toggles** — channel resolution is post-selection on the `10000 + server_id` fetch; there is **no
  intermediate channel picker**. The 8-byte record is painted **onto a plate** (name → plate header
  label, status/load → plate label line), **not** onto the 115..124 pager buttons.
- **Two-plate geometry (CODE-CONFIRMED loop, 2 iterations):** the two parchment **server plates** (the
  selectable servers, not channels) are built by a loop of count 2 — one "channel-block" group per plate
  (the legacy "channel-block" name describes the **parchment art only**, not a channel concept). Each
  group = a header label + parchment body image + 3-state PLATE button (actions 400/401) + 2 labels.
  Block X base starts **30** and steps **+233** → block X {30, 263}; the scroll-BODY source-U starts
  **448** and steps **+124** → src-U {448, 572} (the two columns already tabulated above). `srcV = 6` is
  fixed for both parchment quads; the PLATE source-UV is FIXED (normal `9,6` / hover-pressed `220,6`)
  and does NOT advance per plate.
- The Refresh and Cancel button **words** may be baked atlas art (gold plates) rather than caption ids
  - UNVERIFIED which; the rects (Refresh `456,-3,111x38`; Cancel = login action 111) are firm.
- **CORRECTION — list scroll arrows / thumb source (CODE-CONFIRMED).** Earlier drafts placed the list
  up/down arrows at `loginwindow.dds` src `690,985` / `784,985`. The real builder sources all three
  list controls from a DIFFERENT band of `loginwindow.dds` (B): scroll-UP at src **(483,490)** (dst
  `467,86,13,10`), scroll-DOWN at src **(505,490)** (dst `467,455,13,10`), and the scrollbar
  thumb / commit dot at src **(496,490)** (dst `469,98,9,9`). The `690,985` / `784,985` figures are
  WRONG for these controls — corrected in the table above.
- **Calligraphic title (武神再起), the per-server "NEW" badge, and `server_icon.dds` are render-time /
  baked art, NOT scene-build widgets.** The calligraphy title and parchment scroll art are baked into
  `loginwindow_02.dds`. The red "NEW" badge is driven by a client config flag (a server index) and is
  drawn beside the matching server record by the per-row render path — a presentation flag, not a
  server address and not a build-time widget. `data/ui/server_icon.dds` (128x128 per-server badge) is
  likewise bound on the per-row render path, not in this builder. **PENDING (live-debugger):** the
  exact per-row render-path draw of `server_icon` / the NEW badge, and the integer caption id of the
  calligraphy header, are not pinned here — they need a render-tick read / `msg.xdb` extract.
- **Server PLATE vs pager button (do not confuse).** The `202x372` server PLATE
  (`loginwindow_02.dds` src normal `9,6` / hover-pressed `220,6`, actions **400/401**) **IS the
  selectable server** (one plate = one server; the 8-byte record paints onto it) — it is DISTINCT from
  the small clickable **pager** button sprite (`loginwindow.dds` src `596,985` / `643,985`, `47x18`,
  actions **115..124** above), which only **pages** the 2-plate view and carries no server record. The
  plate's source-UV is FIXED (does not advance per plate). The `100x372` scroll BODY source-U advances
  **+124** per plate column (`448` -> `572`); `srcV = 6` is fixed for both parchment quads; only two
  plate columns are built (plate count = 2 = max servers per page). The parchment chrome lives entirely
  on `loginwindow_02.dds`.
- **Two notice/error dialogs are built (not one), both == the shared notice panel.** The server-list
  set instantiates the shared `InventWindow.dds` frame sub-rect `(318,647,340,190)` TWICE — a notice
  dialog #1 and an error dialog #2 — each drawn (hidden) at on-screen `342,289,340,190`, each with its
  own runtime body caption label (a `msg.xdb` caption id, not reproduced here) and its own OK button.
  The OK button's pressed state is at source-U **415** (same shared 3-state OK widget used elsewhere in
  §11.2d). There is no dedicated connecting-frame texture rect — only the runtime body caption differs.
  **Behavioral nuance (UNVERIFIED, static-only):** the server-list WAIT (state 35) raises the
  channel-tab parchment panel + refresh button, NOT a notice frame; the dialogs are raised on the
  empty-list / connect-fail / endpoint-wait branches (msg 4027/4028 etc., §1.5 states 36/39).

## 11.5 Char-select scene - widget layout (CODE-CONFIRMED literals)

The char-select 2D builder composites its chrome from the shared atlases (section 11.1 note). The
slot-frame and Create/Delete/Enter button art are **sub-rects of `loginwindow.dds`** (shorthand **T1**
below); one conditional overlay button is sourced from `mainwindow.dds` (**T2**). The 5 live 3D
preview actors and the preview camera are owned by sections 3.3-3.5 (not layout art).
Rect = `(X, Y, W, H)`.

### 11.5a Window chrome / root panels

| Role | Atlas | Rect (X,Y,W,H) | Src (U,V) | Kind |
|---|---|---|---|---|
| Root window frame panel | (none) | X=(W/2-288), 575, 244,187 | - | panel |
| Title/info chrome plate A | T1 | 0,12,200,46 | 608,793 | image |
| Title/info chrome plate B | T1 | 200,0,176,58 | 608,735 | image |
| Title/info chrome plate C | T1 | 376,12,201,46 | 608,689 | image |
| Centered char-info panel | (none) | X=(W-215)/2, 0, 244,187 | - | panel |
| Char-info background art | T1 | (centered),0,215,147 | 556,542 | image |

### 11.5b Character SLOT tabs (the per-slot frame art) - 113x40, all from `loginwindow.dds`

> **The slot model is FIVE slots (CODE-CONFIRMED).** The select-window object holds **exactly 5**
> per-slot spawn descriptors (`5 × 880 bytes`, stride `0x370`, built by a 5-iteration ctor loop;
> §3.0 / §3.1) and renders up to **5 live 3D preview actors** in a row (§3.3) — this is the
> authoritative **5-slot grid**. Slot occupancy, the per-slot 3D preview and the name / level / class
> display are descriptor-driven (§3.2–§3.3), not layout art. The 2D **slot-select buttons** below are
> the per-slot frame art; each slot-select button **is** the frame graphic (its normal-state source
> rect). The byte-exact tab rects recovered so far cover slots 1–3 (actions **1, 2, 3**); the
> slots 4–5 tab rects are not yet enumerated byte-exact in 2D, but the underlying slot count is 5.

| Slot tab | Action id | Rect (X,Y,W,H) | Normal (U,V) | Hover (U,V) | Pressed (U,V) |
|---|---|---|---|---|---|
| Slot 1 | 1 | 67,17,113,40 | 675,795 | 675,795 | 483,883 |
| Slot 2 | 2 | 232,7,113,40 | 640,742 | 640,742 | 483,923 |
| Slot 3 | 3 | 393,17,113,40 | 625,691 | 625,691 | 483,963 |

### 11.5c Create / Delete / Enter buttons - 59x20, all from `loginwindow.dds`

| Role | Action id | Rect (X,Y,W,H) | Normal (U,V) | Hover (U,V) | Pressed (U,V) |
|---|---|---|---|---|---|
| **Create** | **4** | 130,112,59,20 | 0,1004 | 0,1004 | 59,1004 |
| **Delete** | **5** | 42,112,59,20 | 118,1004 | 118,1004 | 177,1004 |
| **Enter** | **6** | 112,112,59,20 | 236,1004 | 236,1004 | 295,1004 |
| **Conditional overlay** | **61** | 20,112,95,20 | 405,466 | 405,466 | 500,466 |

All four buttons draw from the **same atlas row** for their src origin. Action 61 draws from
`data/ui/mainwindow.dds` (T2); the other three draw from `data/ui/loginwindow.dds` (T1, row V=1004).
All source rects in the action-61 row are byte-exact literals recovered from the builder call site in
`SelectWindow_BuildAndInit`; the prior placeholder "pressed V=500" was a mis-labelled column value —
the full origin pair for PRESSED is U=500, V=466 (same row as NORMAL/HOVER). NORMAL and HOVER share
origin (405,466); PRESSED alone shifts the atlas column to U=500. Width=95, height=20 for all states.

Prior build-order note item #33 (`dst(reg,500) wh(20,95) press(112,20)`) mis-paired the operands;
the corrected operand order is dstX=20, dstY=112, W=95, H=20.

Show/hide trigger (RESOLVED STATICALLY). The action-61 child is created **unconditionally** at build
time (only the standard allocation null-guard wraps it) and carries no static `SetVisible(false)`
call. Its **visibility is driven per slot** by `SelectWindow_RefreshSlotInfoRow`, which reads the
per-slot byte at select-window field **`+0x1548 + slotIndex`**: byte **nonzero → HIDE** action 61 (and
flips a sibling overlay triad to one arrangement); byte **zero → SHOW** action 61 (sibling triad to the
opposite arrangement). The byte is populated from the live character roster, so the toggle is genuinely
runtime-data-driven (full mechanism in §3.4). **OPEN@DEBUGGER:** the exact byte semantic and the
action-61 button's on-screen role / click handler.

The Create/Delete/Enter rects/actions agree with the section 4/section 5 correction (action ids
4/5/6, not atlas-X coordinates).

### 11.5d Per-slot info plates + number cells (chrome detail)

The per-slot info region draws chrome plates plus a grid of **placeholder number-glyph cells** whose
digits are substituted at runtime from the slot's stat/level values (the build-time source rects are
placeholders). All from `loginwindow.dds` unless noted.

| Role | Rect (X,Y,W,H) | Src (U,V) |
|---|---|---|
| Info plate | 0,142,215,147 | 556,542 |
| Info plate | 215,0,29,22 | 556,729 |
| Info plate | 0,352,29,40 | 556,689 |
| Number-glyph cell | 12,238,34,18 | 297,980 |
| Number-glyph cell | 12,262,34,18 | 331,980 |
| Number-glyph cell | 12,286,34,18 | 365,980 |
| Stat-number cell block (x7) | X=46/51, Y=193..286 step 24, 157x18 | 140,980 (placeholder) |

The per-slot info-line **caption labels** carry caption ids **48001, 48003, 48004, 48005** (the
name/level/position **slot-label** set, config-resolved); additional chrome captions are **46001,
46002, 14001, 14002, 2206, 63030** (all integer ids; text VFS-only, not reproduced). The **top
"character count : N" caption is a SEPARATE caption — MessageDB id 2209**, count-bound to the
BillingState char-count field (§3.8.2); it is **NOT** 48001 / 2206 (those are slot labels). The
scene-ambient VFX id is **380003000** (section 3.6.5 / effect catalogue); the char-select BGM is
SFX **920100200** (kind-0 music slot, §3.8.1), which is also the enter-world cue (section 9).

> **No standalone class-icon-by-index widget exists in the 2D builder.** Per-slot class is conveyed
> by the descriptor-driven 3D preview (section 3.3) and the slot frame art (section 11.5b); the
> inline-source cells in the info region are **number-glyph placeholders**, not class icons. A 2D
> class badge keyed by a class index is **UNVERIFIED / absent** in this builder (Open question 11).

### 11.5e Create-form construction refinements (CAMPAIGN 9b, IDA re-walk)

The character-CREATE sub-form (a sub-state of the same window, drawn over the same 3D temple cell,
§4 / §3.7.6) adds these CODE-CONFIRMED widgets, recovered byte-exact from the create-form builder.
Note this is the CREATE form — it is distinct from the select-screen info region (§11.5d), which
correctly has no 2D class badge.

- **Create sub-form chrome atlas = `data/ui/InventWindow.dds`** — a THIRD atlas beyond
  `loginwindow.dds` and `mainwindow.dds`, loaded into its own texture list for the create panel.
- **Class-select button strip** (the 4 class-picker buttons): source row **V = 1005** (distinct from
  the 1004 row used by Create/Delete/Enter), with a per-class source-X highlight matrix — idle
  `{590, 635, 680, 725}` <-> selected `{770, 815, 860, 905}` for UI classes `{0,1,2,3}`. Class-pick
  button **actions = 10 / 11 / 12 / 13** (UI index = action - 10).
- **Create / Delete / Enter buttons**: actions **4 / 5 / 6**, all at source row **V = 1004**. The
  conditional overlay button is action **61**.
- **Face appearance steppers**: actions **21 (face +) / 22 (face -)**, 2D-only — they re-page a 2D
  face index and do NOT rebuild the 3D preview actor (only a class change rebuilds it, §3.3).
- **Class NAME** = `msg.xdb` ids **14003..14007** keyed by internal class id (14003 default). **Class
  DESCRIPTION** = `npc.scr` keyed records, keys 1..4, three CP949 lines at record fields +0x14 /
  +0x54 / +0x94 (§4.1.1 / `config_tables.md` §2.17.3).
- **UI-index -> internal-class map**: UI `{0,1,2,3}` -> internal `{4,1,3,2}`; per-class create BGM
  `{910065000, 910062000, 910064000, 910063000}` for UI `{0,1,2,3}`.
- **Stat grid** binds the keyed-string map at `2·discipline + {110,111,120,121,130,131,140,141}`
  (the `discipline + {210..240}` alternative is REFUTED — those are equipment item ids). Stat values
  are **pure display** from the class template, NOT interactive point-buy.

### 11.5f Char-select construct walk — full element ledger (CODE-CONFIRMED, folds in the 279-element walk)

> A complete element-by-element walk of the char-select window's scene builder (every builder call in
> build order, ~279 elements) confirms the §11.5a–§11.5e tables byte-for-byte and adds the elements
> below, which the earlier draft did not enumerate. The select window is allocated as a 6280-byte
> object; its scene builder is invoked once from the engine state-4 case (see §1.0 / `client_runtime.md`
> §7). Build order = paint / Z order. All these are children of the same select window — the create
> form is an **in-place hidden sub-tree of the same window**, not a separate scene.

**Atlas texture-list (load order = handle slots; loaded into the window's own texture list, NO global
cache).** The earlier tables named only three (`loginwindow.dds`, `mainwindow.dds`, `InventWindow.dds`);
the construct loads **four more** for the bottom mail/pigeon/letter cluster:

| Slot | VFS path | Role |
|---|---|---|
| T1 | `data/ui/loginwindow.dds` | primary chrome / slot tabs / Create-Delete-Enter / info plates |
| T2 | `data/ui/mainwindow.dds` | conditional overlay button (action 61); pigeon / letter buttons |
| T3 | `data/ui/InventWindow.dds` | CREATE-form panels, class strip, name textbox, confirm popups |
| — | `data/ui/CarrierPigeonPerson.dds` | mail / pigeon cluster art |
| — | `data/ui/CarrierPigeonAll.dds` | mail / pigeon cluster art |
| — | `data/ui/tradekeepwindow.dds` | mail / pigeon cluster art |
| — | `data/ui/blacksheet.dds` | mail / pigeon cluster art |

**Char-list block-copy at build entry.** The builder's first act is an index-preserving copy of the
server char list from the net-handler object into the window: **5 × 880-byte spawn descriptors**,
**5 × 96-byte stat blocks**, and a **5-byte per-slot state/lock flag** array; the selected-slot index
is initialised to 0 and a mode/visible byte to 1. (Confirms §3.1 / §3.4.)

**Active-slot DETAIL plate (a SECOND, larger info region — distinct from the §11.5d per-slot info).**
A 474×244 panel holds the detailed stat readout: a multi-piece info background (T1/T2 plates), a wide
number-glyph bank (placeholder cells, src `(980,140)`, runtime-substituted digits), and **7 stat
caption labels** (config-resolved stat names). This is the active-slot detail panel; §11.5d's smaller
region is the per-slot tab info.

**Five distinct confirm / notice MODAL sub-panels (model them as panels, not loose labels).** The
caption ids §11.5d lists flat — `{48001, 48003, 48004, 48005, 46001, 46002, 14001, 14002, 2206,
63030}` — are in fact the bodies of **five separate modal sub-panels**, each on the shared dragon-frame
quad (`InventWindow.dds` src `(318,647)`, `340×190`, drawn centred at `(342,289,340,190)`, hidden until
raised), with these Yes/No action pairs:

| Panel role | Frame | Buttons (action ids) | Caption (msg.xdb id) |
|---|---|---|---|
| Generic 2-button confirm | 318,647,340,190 | Yes **62** / No **63** | 63030 |
| 1-button notice (centre-aligned body) | 318,647,340,190 | OK **74** | 48001 / 48003 |
| Confirm (single action) | 318,647,340,190 | **64** | 48004 / 48005 |
| **DELETE-confirm** popup | 318,647,340,190 | Yes **54** / No **55** | 14001 / 14002 |
| Second confirm | 318,647,340,190 | Yes **59** / No **60** | 46001 / 46002 |

These feed the create / delete / enter / rename senders (§4 / §5).

**Create-form appearance / stat sub-steppers — actions 25..36 (a 14-button matrix).** Beyond the face
steppers (21/22, §11.5e) the create form builds a 2-row matrix of small 16×24 increment/decrement
buttons (T1, source columns stepping `{0BF,0D7,0EF,107,11F}`, source-V `{1F4, 20C}`) bound to
**actions 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36**. Their exact create-form bindings are a
**static-hypothesis** (appearance / stat sub-controls). Two further class-detail +/- stepper pairs are
bound to **actions 66/67** (source col 388) and **68/69** (source col 576).

**Create-form name entry — TWO `GUTextbox` editors, both seeded with msg-db 14001.** An action-**65**
name-edit **host panel** (T2 mainwindow, `176×42` at `(100,430)`, src `(295,132)`) hosts the name
field. The build constructs **two** edit boxes, each seeded with `MessageDB(14001)` as the default /
placeholder caption: a smaller editor (the mail / letter input) and the visible **create-form
name-entry field** (`274` wide, at `(80,60)`). A class-strip overlay image at the create class-title
seat (T3, `(75,55,274,18)`, src `(1003,419)`) corroborates the create-form class-strip **src-Y 1005**
family (§11.5e / §9b).

**Bottom mail / pigeon / letter button cluster — actions 70..73 + a mail icon (NEW; role
debugger-pending).** Built from T2 + the four extra atlases above:

| Element | Action | Atlas | Rect (X,Y,W,H) | Notes |
|---|---|---|---|---|
| Pigeon button A | 70 | T2 | 50,?,60,80 | src col 334/3AC/370 |
| Pigeon button B | 71 | T2 | (same geom, alt src) | |
| Letter button A | 72 | T2 | ?,?,30,30 | src col 258/294/276 |
| Letter button B | 73 | T2 | (alt src) | |
| Mail icon | (75) | pigeon atlases | 3CB,262,23,23 | src `(610,971)` |

These appear to be the **carrier-pigeon / letter (mail)** shortcut cluster on the char-select screen;
the exact behaviour and when each is shown are **debugger-pending**.

**Build-tail captions, scene + sound.** The slot-count caption uses font slot **4** with text
`MessageDB(2206)`; a second label uses font slot **2**; the top **"character count : N" caption =
`MessageDB(2209)`**, the `N` bound to the billing-state char-count field (§3.8.2). The builder then
calls the **3D scene builder** (scene "select", map000 area 0, clock 52200 s = 14:30, perspective
camera, single ambient effect id **380003000** — §3.5 / §3.6.5 / §3.7) and starts the char-select BGM
on the kind-0 music slot (§3.8.1).

### 11.5g Child-panel inventories, total count, duplicate verdict & action gating (CODE-CONFIRMED)

The select window's top-level build pass attaches two dedicated child sub-panels — an **ExitPanel** and
an **ErrorPanel** — each of which builds its own widgets in its own builder. Both are enumerated
element-by-element below (static-confirmed, byte-exact build order).

**ExitPanel — exit/quit-confirm dialog (built by the panel's own button-builder).** A single
straight-line pass, no loops, no conditionals beyond the standard allocation null-guards:

| # | Kind | Action id | Rect (X,Y,W,H) | Normal (U,V) | Hover (U,V) | Pressed (U,V) | Text |
|---|---|---|---|---|---|---|---|
| 1 | Label (caption, no texture) | none (plain add) | — | — | — | — | msg.xdb **2007** |
| 2 | 3-state button | **50** | 55,136,113,40 | 302,860 | 302,860 | 415,860 | — |
| 3 | 3-state button | **51** | 174,136,113,40 | 302,900 | 302,900 | 415,900 | — |

ExitPanel total: **2 clickable buttons** (actions **50**, **51**) + **1 caption label** = 3 widgets.
Nothing built twice, nothing conditional, nothing unexplained.

**ErrorPanel — error/notice host (built by the panel's own build/init virtual, NOT its constructor).**
Note the base constructor builds **zero** widgets (it only installs the vtable and zero-inits ~10
fields); the actual widgets are created in the class's build/init virtual:

| # | Kind | Action id | Rect (X,Y,W,H) | Atlas origin(s) | Note |
|---|---|---|---|---|---|
| 1 | Multi-line label | **670** | 0,0, (panel-width/2 − 6), — | — | text set at runtime |
| 2 | 2-state button | **671** | 125,151,90,25 | 417,943 / 507,943 | OK / dismiss |
| 3–6 | Label ×4 (4-iteration loop) | none (plain add) | 0,0 | — | text set at runtime |

ErrorPanel total: **1 clickable button** (action **671**) + **5 text labels** (action **670** on the
first label; the 4 loop labels are actionless) = 6 widgets. Button **671 is the dismiss control** — its
input virtual hides the panel (`SetVisible 0`) on a click hitting action 671; an on-tick countdown can
also auto-dismiss.

> **Duplicate-bar / built-twice verdict (CODE-CONFIRMED): NOTHING is built twice.** Across the select
> window and its two child panels there is **no accidental duplicate widget**. The only repeated builds
> are **deliberate and explained**: ErrorPanel's **4-iteration label loop** (a `do/while` of exactly 4,
> the four runtime-filled body labels), and two benign ctor-side **double zero-clears** of internal
> field blocks (the 5-slot descriptor span is bulk-`memset` and then per-slot re-zeroed; the four scene
> pointers are zeroed by both the ctor and a near-end ctor loop before the 3D builder assigns them) —
> belt-and-braces, not duplicate widgets. There is **no duplicate slot bar / duplicate button row**.

**Total widget count (CODE-CONFIRMED).** The select window's own build pass = **279 GUI-builder calls →
127 heap-allocated widgets, attached via 113 parent-add operations across ~14 parent panels** (§3.0c).
Of those 127, the **`SetChildrenVisible(0)` broadcast hides exactly 12 child widgets at build** — a
**4-pointer contiguous block** plus **8 individually-referenced** chrome/dialog widgets — built then
hidden until raised. The ExitPanel (3 widgets) and ErrorPanel (6 widgets) inventories above are the
two named child sub-panels' own contents, built by their own builders. (The flat §11.5f ledger folds in
the broader 279-element walk; the §11.5a–§11.5f rect tables are confirmed byte-for-byte by it.)

**Complete top-level button / action set (with show/hide trigger).** Actions bound in the select window
and its create sub-tree, with their show/hide trigger where known:

| Action | Role | Show/hide trigger |
|---|---|---|
| **1, 2, 3** | Slot-select tabs (slots 1–3; model is 5 slots, §11.5b) | always shown |
| **4** | Create | shown on select screen |
| **5** | Delete | hidden at build; raised by selection state |
| **6** | Enter | hidden at build; raised by selection state |
| **10, 11, 12, 13** | Class-pick strip (create form; UI index = action − 10) | create sub-form only |
| **21, 22** | Face appearance steppers (create form, 2D-only) | create sub-form only |
| **25–36** | Appearance / stat sub-stepper matrix (create form; bindings static-hypothesis) | create sub-form only |
| **50, 51** | ExitPanel choice buttons | shown when ExitPanel raised |
| **54, 55** | DELETE-confirm Yes / No | shown when delete-confirm modal raised |
| **59, 60** | Second confirm Yes / No | shown when that modal raised |
| **61** | Conditional overlay button | per-slot: shown iff `+0x1548+slot` byte is **0**, hidden if nonzero (§3.4 / §11.5c) — **OPEN@DEBUGGER** for the byte semantic + the button's role |
| **62, 63** | Generic 2-button confirm Yes / No | shown when that modal raised |
| **64** | Single-action confirm | shown when that modal raised |
| **65** | Name-edit host panel (create form) | create sub-form only |
| **66, 67 · 68, 69** | Class-detail stepper pairs (create form) | create sub-form only |
| **70, 71, 72, 73** | Carrier-pigeon / letter (mail) cluster (role debugger-pending) | **OPEN@DEBUGGER** |
| **74** | 1-button notice OK | shown when notice modal raised |
| **670, 671** | ErrorPanel label / dismiss button | shown when ErrorPanel raised; 671 click dismisses |

**HandleCommand action → button gating (CODE-CONFIRMED routes; one-line roles).** UI events / button
actions are dispatched by the window's command handler (`SelectWindow_HandleCommand`, the secondary
event-handler vtable's command slot). Its top-level routes (from callee scan, not transcription):

- **Class-pick (10–13)** → apply the chosen class to the active slot (`SelectWindow_ApplyClassSelection`)
  and rebuild the create-preview actor (§3.3).
- **Slot-select (1–3)** → begin per-slot preview or confirm the selection
  (`SelectSlot_BeginPreviewOrConfirm`), refresh the slot info row and appearance strings, and re-run the
  per-slot overlay gating (action-61 show/hide, §3.4).
- **Create (4) / class chosen** → open the create / name-entry sub-form on demand
  (`SelectWindow_ShowCreateNameModalForClass`); the confirm path emits the create-character packet
  (`Cmsg_CreateCharacter_Send`).
- **Enter (6)** → proceed into the world (`SelectWindow_EnterGame`).
- **Delete path** → no dedicated delete-modal child is constructed in the builder; the delete-confirm
  (actions 54/55) and its full route are **OPEN@DEBUGGER** (deletion path not resolved this campaign).
- UI click / confirm SFX are issued by the sound manager's create-and-play method along these routes
  (category-0/1 single-slot path, §3.8.1).

> **Create / name modal is built ON DEMAND, not retained.** No persistent create-name-modal pointer
> field was observed on the window in the constructor or build pass — the name-entry sub-form is opened
> when a class is chosen via HandleCommand. Whether a pointer is retained vs. constructed-on-click is
> **OPEN** (static-only this campaign).

### 11.5h Char-select builder — complete element-by-element widget inventory (CODE-CONFIRMED immediates)

> A full call-by-call walk of the char-select window's open/init builder confirms and **refines** the
> §11.5a–§11.5g tables: it pins every builder call's literal arguments in build (= paint / Z) order,
> settles the per-call duplicate question at the **window level**, and pins the canonical clickable-button
> set. Where this subsection's per-call immediates and the rounded §11.5f/§11.5g counts differ, **this
> walk's literals are the precise reading**. The builder constructs **124 visual widgets** (the GU-ctor
> sites) via **268 GU/UI-construction calls** out of **489 total calls** in the routine; the §11.5g
> "279 → 127 widgets" framing additionally counts the 11 message-database string lookups as construction
> steps and folds in the ExitPanel/ErrorPanel/Descriptor child sub-panels (built by their own builders,
> §11.5g) — both framings are consistent, and the load-bearing figures are **124 in-routine widgets** and
> **489 total calls**.

**GU/UI-construction call breakdown (CODE-CONFIRMED counts).**

| Builder call | Count | Role |
|---|---|---|
| Panel ctor | 10 | GU panel widget |
| Image-component ctor | 37 | static 1:1 atlas-blit image widget |
| 3-state button ctor | 46 | normal / hover / pressed button widget |
| Label ctor | 29 | text-label widget |
| Textbox ctor | 2 | editable text-field widget |
| — visual-widget subtotal | **124** | (10 + 37 + 46 + 29 + 2) |
| Parent-add (no action) | 71 | parents a child; paint order = insertion order |
| Parent-add-with-action | 42 | parents a child **and** binds its click action id |
| Atlas texture load | 8 | per-window `.dds` atlas binds (§11.5f list) |
| Label text + align set | 15 | label text / alignment |
| Textbox caption set | 2 | textbox caption |
| Font-slot select | 2 | font-slot assignment |
| Visibility broadcast (terminal) | 1 | shows the roster set at open |
| Slot-count label refresh (terminal) | 1 | "x/5 characters" count |
| 3D scene build (terminal) | 1 | builds the 5-slot preview scene (§3.5) |
| Scene BGM start (terminal) | 1 | SFX 920100200 (§3.8.1) |
| — UI-construction total | **268** | |

**Argument contract (how to read the rect tables).** Builders take, after the implicit `this` widget:
Panel `(tex, dstX, dstY, w, h, srcX, srcY, opaqueFlag, color)`; Image-component
`(tex, dstX, dstY, w, h, srcX, srcY, color)` — a 1:1 atlas blit, so the source rect is `(srcX, srcY)`
with the **same** `w×h` as the destination (`srcRight = srcX+w`, `srcBottom = srcY+h`); 3-state button
`(tex, dstX, dstY, w, h, …)` with three atlas origins for normal / hover / pressed; Label and Textbox
`(tex, dstX, dstY, w, h, …)`. The active atlas is whichever the most recent texture load bound. A field
shown as **Reg** below is **computed at open from layout state — its exact pixel is [OPEN@DEBUGGER]**;
the constant fields beside it are CODE-CONFIRMED. Every `color` argument was a zeroed register at every
call site walked (default color); any non-default color is **[OPEN]** (none observed statically).

**Ordered widget table (build index `bi` = ordinal among the 124 widgets; dst = `(x,y,w,h)`; src =
`(x,y)` into the named atlas, decimal; act = action id bound by the matching parent-add-with-action,
`—` = plain parent-add / structural).**

*Group 1 — roster frame + top command-button strip (loginwindow / mainwindow atlases):*

| bi | class | dst (x,y,w,h) | src (atlas) | act | role |
|----|----|----|----|----|----|
| 1 | Panel | (Reg,Reg,577,58) | (Reg,Reg) login | — | top header bar (opaque=0) |
| 2 | Panel | (0,0,244,187) | (0,0) login | — | left roster panel 244×187 |
| 3 | Image | (0,12,200,46) | (608,793) login | — | header art L |
| 4 | Image | (200,0,176,58) | (608,735) login | — | header art C |
| 5 | Image | (376,12,201,46) | (608,689) login | — | header art R |
| 6 | Button3 | (67,17,113,40) | (483,883) login | **1** | command "create new character" |
| 7 | Button3 | (232,7,113,40) | (483,923) login | **2** | command "enter game" |
| 8 | Button3 | (393,17,113,40) | (483,963) login | **3** | command "create" (alt) |
| 9 | Image | (0,0,215,147) | (556,542) login | — | slot-info backing |
| 10 | Image | (215,0,29,22) | (556,729) login | — | corner art TR |
| 11 | Image | (0,147,29,40) | (556,689) login | — | corner art BL |
| 12 | Image | (20,33,34,18) | (771,542) login | — | info icon row 1 |
| 13 | Image | (20,57,34,18) | (771,560) login | — | info icon row 2 |
| 14 | Image | (20,81,34,18) | (771,578) login | — | info icon row 3 |
| 15 | Image | (50,33,157,18) | (140,980) login | — | value bar row 1 |
| 16 | Image | (50,57,157,18) | (140,980) login | — | value bar row 2 |
| 17 | Image | (50,81,157,18) | (140,980) login | — | value bar row 3 |
| 18 | Label | (60,37,70,12) | — login | — | info label 1 |
| 19 | Label | (60,61,70,12) | — login | — | info label 2 |
| 20 | Label | (60,85,70,12) | — login | — | info label 3 |

*Group 2 — create-class modal A (InventWindow atlas), one of the five 340×190 modals:*

| bi | class | dst | src | act | role |
|----|----|----|----|----|----|
| 21 | Panel | (Reg,Reg,340,190) | (318,647) opaque=1 | — | modal 340×190 #1 |
| 22 | Label | (35,60,12,12) | — | — | |
| 23 | Label | (10,100,12,12) | — | — | |
| 24 | Button3 | (55,136,113,40) | (415,Reg) | **62** | OK (create-class confirm) |
| 25 | Button3 | (174,136,113,40) | (415,Reg) | **63** | Cancel |

*Group 3 — caption-only notice modal B (InventWindow):*

| bi | class | dst | src | act | role |
|----|----|----|----|----|----|
| 26 | Panel | (Reg,Reg,340,190) | (318,647) op=1 | — | modal 340×190 #2 |
| 27 | Label | (0,0,340,190) | — | — | full-panel caption (message-db text) |
| 28 | Button3 | (230,150,113,40) | (415,Reg) | **74** | OK (re-arm all 5 slot actors) |

*Group 4 — delete-confirm-style modal C (InventWindow):*

| bi | class | dst | src | act | role |
|----|----|----|----|----|----|
| 29 | Panel | (Reg,Reg,340,190) | (318,647) op=1 | — | modal 340×190 #3 |
| 30 | Label | (24,60,12,12) | — | — | |
| 31 | Label | (20,100,12,12) | — | — | |
| 32 | Button3 | (120,133,113,40) | (415,Reg) | **64** | confirm |

*Group 5 — bottom roster action-button row (loginwindow):*

| bi | class | dst | src | act | role |
|----|----|----|----|----|----|
| 33 | Button3 | (20,112,95,20) | (500,Reg) | **61** | delete / conditional overlay (per-slot gated, §11.5c) |
| 34 | Button3 | (130,112,59,20) | (59,Reg) | **4** | bottom-row button |
| 35 | Button3 | (42,112,59,20) | (118,Reg) | **5** | bottom-row button (no-op pass-through) |
| 36 | Button3 | (112,112,59,20) | (295,Reg) | **6** | move / relocate select |

*Group 6 — right detail panel 244×474 + stat icon / value column (loginwindow; one image on mainwindow):*

| bi | class | dst | src | act | role |
|----|----|----|----|----|----|
| 37 | Panel | (0,0,244,474) | (Reg,Reg) login | — | right detail panel 244×474 |
| 38 | Image | (0,0,215,93) | (809,543) login | — | portrait backing |
| 39 | Image | (0,93,215,49) | (Reg,730) mainwindow | — | uses mainwindow atlas |
| 40 | Image | (0,142,215,210) | (809,768) login | — | body art |
| 41 | Image | (215,0,29,22) | (556,729) login | — | corner TR |
| 42 | Image | (0,352,29,40) | (556,689) login | — | corner BL |
| 43 | Image | (12,33,34,18) | (771,596) login | — | stat icon 1 |
| 44 | Image | (12,57,34,18) | (771,542) login | — | stat icon 2 |
| 45 | Image | (12,109,34,18) | (771,614) login | — | stat icon 3 |
| 46 | Image | (12,73,34,18) | (771,632) login | — | stat icon 4 |
| 47 | Image | (12,190,34,18) | (771,650) login | — | stat icon 5 |
| 48 | Image | (12,214,34,18) | (771,668) login | — | stat icon 6 |
| 49 | Image | (12,238,34,18) | (297,980) login | — | stat icon 7 |
| 50 | Image | (12,262,34,18) | (331,980) login | — | stat icon 8 |
| 51 | Image | (12,286,34,18) | (365,980) login | — | stat icon 9 |
| 52–58 | Image ×7 | (46, 33/57/190/214/238/262/286, 157,18) | (140,980) login | — | stat value bars 1–7 (placeholder) |
| 59–64 | Label ×6 | (51 or 118, 193/217/241/265/289/155, 35 or 26, 12) | — | — | stat values 1–6 (runtime-substituted) |
| 65 | Label | (51,36,26,12) | — | — | name / title label |

*Group 7 — per-slot mini controls (the appearance / slot buttons):*

| bi | class | dst | src | act | role |
|----|----|----|----|----|----|
| 66 | Button3 | (48,109,25,18) | (Reg,551) | **22** | face − |
| 67 | Button3 | (73,109,25,18) | (Reg,576) | **21** | face + |
| 68 | Button3 | (48,153,25,18) | (Reg,551) | **25** | spinner |
| 69 | Button3 | (73,153,25,18) | (Reg,576) | **26** | spinner |
| 70–74 | Button3 ×5 | (154, 191/215/239/263/287, 24,16) | (Reg,548) | **27,28,29,30,31** | column-A rows 1–5 |
| 75–79 | Button3 ×5 | (178, 191/215/239/263/287, 24,16) | (Reg,572) | **32,33,34,35,36** | column-B rows 1–5 |
| 80 | Button3 | (42,325,59,20) | (Reg,413) | — | bottom OK (plain parent-add) |
| 81 | Button3 | (112,325,59,20) | (Reg,531) | — | bottom Cancel (plain parent-add) |

*Group 8 — inner sub-panel group (the SECOND 244×187 panel: class-pick + appearance spinners):*

| bi | class | dst | src | act | role |
|----|----|----|----|----|----|
| 82 | Panel | (Reg,Reg,244,187) | (Reg,Reg) login | — | second 244×187 panel |
| 83 | Image | (Reg,0,215,147) | (556,542) login | — | mirror of bi#9 art |
| 84 | Image | (Reg,0,29,22) | (556,729) login | — | mirror of bi#10 |
| 85 | Image | (Reg,147,29,40) | (556,689) login | — | mirror of bi#11 |
| 86 | Button3 | (Reg,30,45,19) | (Reg,Reg) | **10** | class tab 1 |
| 87 | Button3 | (Reg,30,45,19) | (Reg,815) | **11** | class tab 2 |
| 88 | Button3 | (Reg,30,45,19) | (Reg,860) | **12** | class tab 3 |
| 89 | Button3 | (Reg,30,45,19) | (Reg,905) | **13** | class tab 4 |
| 90–92 | Label ×3 | (Reg, 72/86/100, 12,12) | — | — | stacked labels 1–3 |
| 93 | Button3 | (Reg,110,59,20) | (Reg,413) | — | OK pair (plain parent-add) |
| 94 | Button3 | (Reg,110,59,20) | (Reg,531) | — | Cancel pair (plain parent-add) |
| 95 | Label | (Reg,148,215,274) | (405,600) | — | big caption block |
| 96 | Label | (Reg,Reg,12,12) | — | — | caption value |
| 97 | Button3 | (Reg,388,27,18) | (Reg,674) | **66** | appearance spinner − |
| 98 | Button3 | (Reg,388,27,18) | (Reg,701) | **67** | appearance spinner + |
| 99 | Label | (Reg,410,190,Reg) | (320,790) | — | caption block 2 |
| 100 | Button3 | (Reg,576,27,18) | (Reg,674) | **68** | appearance spinner − |
| 101 | Button3 | (Reg,576,27,18) | (Reg,701) | **69** | appearance spinner + |
| 102 | Button3 | (Reg,Reg,80,60) | (Reg,820) | **70** | nav / sort-filter |
| 103 | Button3 | (Reg,Reg,Reg,60) | (Reg,820) | **71** | nav / sort-filter |
| 104 | Button3 | (Reg,Reg,30,30) | (Reg,600) | **72** | toggle / sort-filter |
| 105 | Button3 | (Reg,Reg,30,30) | (Reg,600) | **73** | toggle / sort-filter |

(The single in-builder visibility broadcast fires here, after Group 8 — it shows the roster set at open;
all per-modal show/hide thereafter is driven by the command handler.)

*Group 9 — close button + notice panel + textbox + exit / dim-sheet chrome (late atlas re-binds):*

| bi | class | dst | src | act | role |
|----|----|----|----|----|----|
| 106 | Button3 | (971,610,23,23) | (Reg,Reg) tradekeep | — | corner CLOSE (plain parent-add; **[OPEN]** action binding) |
| 107 | Panel | (430,100,176,42) | (132,295) op=1 | **65** | small notice panel |
| 108 | Textbox | (54,60,140,12) | — | — | editable field #1 (caption from message-db) |
| 109 | Label | (Reg,Reg,Reg,12) | — | — | exit-panel caption (font slot 4) |

(Group 9 also re-binds the CarrierPigeonPerson / CarrierPigeonAll / tradekeep / blacksheet atlases and
constructs three auxiliary child sub-panels by their own constructors — a descriptor panel, the ErrorPanel
and the ExitPanel — whose own widgets are **not** among the 124 in-routine widgets and are inventoried in
§11.5g. The exit-dialog button set is built inside the ExitPanel's own button-builder; its action ids are
in that callee — see §11.5g for the ExitPanel/ErrorPanel sets.)

*Group 10 — create-NAME modal D (InventWindow):*

| bi | class | dst | src | act | role |
|----|----|----|----|----|----|
| 110 | Panel | (Reg,Reg,340,190) | (318,647) op=1 | — | modal 340×190 #4 (create-NAME) |
| 111 | Label | (70,70,12,12) | — | — | |
| 112 | Button3 | (55,136,113,40) | (415,Reg) | **54** | OK (slot-select send) |
| 113 | Button3 | (174,136,113,40) | (415,Reg) | **55** | Cancel |
| 114 | Label | (Reg,100,12,12) | — | — | value label |
| 115 | Label | (Reg,100,12,12) | — | — | value label 2 |

*Group 11 — rename modal E (InventWindow):*

| bi | class | dst | src | act | role |
|----|----|----|----|----|----|
| 116 | Panel | (Reg,Reg,340,190) | (318,647) op=1 | — | modal 340×190 #5 (rename) |
| 117 | Button3 | (55,Reg,113,40) | (415,Reg) | **59** | OK (rename send) |
| 118 | Button3 | (174,Reg,113,40) | (415,Reg) | **60** | Cancel |
| 119 | Label | (Reg,Reg,Reg,12) | — | — | label |
| 120 | Label | (25,77,12,12) | — | — | label 2 |
| 121 | Image | (55,75,274,18) | (419,1003) InventWindow | — | name-field underline strip |
| 122 | Textbox | (60,80,274,18) | — | — | editable NAME field (caption from message-db) |
| 123 | Label | (Reg,20,300,20) | — | — | wide status line (font slot 2) |

*Terminal sequence (after all widgets):* the slot-count label refresh ("x/5 characters"), the **3D
5-slot preview scene build** (§3.5), and the **char-select BGM** start (SFX 920100200, kind-0 single
voice, §3.8.1).

**Window-level duplicate verdict (CODE-CONFIRMED at this granularity): no bar / strip / tab / panel is
built more than once as a redundant within-window duplicate.** Repeated *shapes* exist, but every repeat
is a **distinct widget** parented to a **distinct window field** and gated visible by a **distinct
action** — siblings sharing one atlas sprite, never the same bar drawn twice into the same parent:

- The **340×190 panel** from InventWindow src `(318,647)` is constructed **five** times (bi#21, #26, #29,
  #110, #116) — the five separate modal dialogs (create-class, caption-only notice, delete-confirm,
  create-NAME, rename), each parented into a different child field and bound to a different OK/Cancel
  action pair (62/63 · 74 · 64 · 54/55 · 59/60), with mutually exclusive visibility.
- The **244×187 panel** from loginwindow is built **twice** (bi#2 left roster panel src `(0,0)`, and bi#82
  the inner class-pick / appearance sub-panel). bi#82 also re-blits the same three corner-art sprites
  (bi#83–85, mirroring bi#9–11) — one sprite reused in a second, different panel. This is the most likely
  origin of any earlier "duplicate bar" observation: shared sprite art legitimately reused, not one bar
  drawn twice.
- The **113×40 OK/Cancel button** from InventWindow src `(415,*)` and the **157×18 value bar** from login
  src `(140,980)` likewise recur across sibling panels — one sprite reused per modal / per panel.

So the only "built more than once" facts are **shared atlas sprites reused across sibling panels**, all
with mutually-exclusive runtime visibility, by design. This agrees with the §11.5g "nothing built twice"
verdict at finer granularity.

**Complete clickable-button set — 42 action bindings (CODE-CONFIRMED).** Every parent-add-with-action
binding, with its widget build index and the role; a rebuild that wires exactly these renders zero
phantom buttons and zero orphaned bindings. The count is exactly the 42 parent-add-with-action calls.

| act | bi | role | gating / when shown |
|----|----|----|----|
| 1 | bi#6 | command "create new character" (top strip) | always shown while window open |
| 2 | bi#7 | command "enter game" (top strip) | always shown |
| 3 | bi#8 | command "create" alt (top strip) | always shown |
| 4 | bi#34 | bottom-row button | shown with roster |
| 5 | bi#35 | bottom-row button (no-op pass-through) | shown with roster |
| 6 | bi#36 | move / relocate select | shown with roster |
| 10,11,12,13 | bi#86–89 | class-pick tabs (UI index = act − 10) | create sub-form only |
| 21 | bi#67 | face + | create sub-form only |
| 22 | bi#66 | face − | create sub-form only |
| 25,26 | bi#68,69 | appearance spinners | create sub-form only |
| 27,28,29,30,31 | bi#70–74 | create appearance/stat sub-steppers (column A) | create sub-form only |
| 32,33,34,35 | bi#75–78 | create appearance/stat sub-steppers (column B) | create sub-form only |
| 36 | bi#79 | scene reset / re-arm | create sub-form only |
| 54 | bi#112 | create-NAME OK (slot-select send) | create-NAME modal raised |
| 55 | bi#113 | create-NAME Cancel | create-NAME modal raised |
| 59 | bi#117 | rename OK (rename send) | rename modal raised |
| 60 | bi#118 | rename Cancel | rename modal raised |
| 61 | bi#33 | delete / conditional overlay | per-slot: shown iff `+0x1548+slot` byte is 0 (§11.5c) |
| 62 | bi#24 | create-class OK | create-class modal raised |
| 63 | bi#25 | create-class Cancel | create-class modal raised |
| 64 | bi#32 | delete-confirm | delete-confirm modal raised |
| 65 | bi#107 | notice / debug panel | notice panel raised |
| 66,67 | bi#97,98 | appearance spinner −/+ | create sub-form only |
| 68,69 | bi#100,101 | appearance spinner −/+ | create sub-form only |
| 70,71,72,73 | bi#102–105 | nav / sort-filter set | create sub-form only |
| 74 | bi#28 | notice-modal OK (re-arm 5 slot actors) | notice modal raised |

That is **42** bindings — none missing, none duplicated. **Non-clickable buttons (plain parent-add, no
action bound at build):** bi#80/81 and bi#93/94 (the two inner-panel OK/Cancel pairs) — structural /
visual, their click handling (if any) routed by the parent panel's own logic; **[OPEN@DEBUGGER]** whether
they are wired live. The corner CLOSE button (bi#106) is also a plain parent-add — its action binding is
**[OPEN]** (not a parent-add-with-action site in this routine). The exit-dialog button set is built inside
the ExitPanel's own button-builder (its actions inventoried in §11.5g, actions 50/51).

**The per-slot 880-byte descriptor IS the SmsgCharacterList server record (CODE-CONFIRMED copy).** At
builder entry, three block-copies move the live server char-list out of the network handler into the
window object: a **5 × 880-byte** block (the per-slot character records), a **5 × 96-byte** block (the
per-slot secondary/stat blocks), and a **5 × 1-byte** block (the per-slot state / lock flags). The
5 × 880-byte block is the cached **`SmsgCharacterList`** payload — each 880-byte unit is the
`SpawnDescriptor` portion of that packet's 981-byte per-slot record (981 = 880 descriptor + 96 stat
block + 1 occupied/facing marker + 4 flags word). The copy source / size / stride are CODE-CONFIRMED
here; the per-byte field meanings **inside** each 880-byte record (name / class / level / appearance
offsets) are owned by the packet spec — cross-reference **`packets/3-1_character_list.yaml`** (and §3.1 /
§3.4 here). The exact `Reg`-computed modal X/Y values, the live wiring of the four plain-parent-add
OK/Cancel buttons, the corner-CLOSE action binding, and the 5 per-slot flag-byte semantics remain
**[OPEN@DEBUGGER]** (no live-debugger campaign on this routine yet).

> **Cross-ref — slot-23 secondary-parts double-build:** the per-slot static walk corroborates
> `skinning.md` §3.6 (the former OPEN-4, now **RESOLVED static**): the two slot-23-family secondary
> deform builders compute **distinct** catalogue keys via different formulas reading different actor
> fields, so they are a legitimate multi-part attach, **not** a redundant within-pass double-build.

## 11.6 Intro / opening sequence (CODE-CONFIRMED art; sequencing per §1.0 and §1.5)

> **Two distinct intros — do not conflate them (CODE-CONFIRMED).** There are **two** separate intro
> animations in the front-end, owned by different windows:
> 1. **The standalone opening scene (engine state 3)** — the `openning_001..004` cross-fade + the
>    `openning_scenario` crawl. This runs **before** the login form's window exists and is fully owned
>    by **§1.0** (4 phases × 17.5 s = 70 s dwell; crawl 30 u/s to 1843; looped 2D BGM **910061000**;
>    skip on Enter/ESC/Space/skip-button id 100; persists `[OPENNING] SKIP=1`).
> 2. **The login window's own curtain (login sub-states 1–5)** — a banner-pan / letterbox-curtain
>    reposition animation belonging to the login window, with the login-enter SFX **861010105** fired
>    at the 1→2 edge. This is **not** the opening scene and does **not** play `openning_*` art.

**Standalone opening scene art (state 3, owned by §1.0):**

- **Backdrops:** four 1024×768 opening frames (`data/ui/openning_001.dds`..`004.dds`), one per fade
  phase (phase N → `openning_00N`), 17.5 s each = 70 s total.
- **Crawl:** the tall `data/ui/openning_scenario.dds` strip, scrolled vertically at 30 u/s to bound
  1843 (~61 s), centred horizontally.
- **Skip control:** one skip button (action id **100**, from `data/ui/mainwindow.dds`).
- **Audio:** one looped 2D cue **910061000** from `data/sound/2d/` (doubles as the opening BGM).

**Login window curtain art (login sub-states 1–5, owned by §1.5):**

- **Banner pan:** two banner panels animate into place (their Y advances from off-canvas to a settled
  position) — pure procedural positional animation, no external asset.
- **Login-enter cue:** SFX **861010105** fires at the intro sub-state (resolves to `data/sound/2d/`
  per `sound_runtime.md` / §9). This cue belongs to the login curtain, **not** to the opening scene.
- **Loading transition:** on the credential-submit join (sub-state 40), transition effect ids
  **30000 / 10001** fire (fade into world-load).

> The standalone opening's timing/skip/transition is owned by **§1.0** (engine state 3 → 4); the login
> curtain's banner-pan timing is owned by the **§1.5** sub-state machine (states 1–5). §11.6 records
> only which art each step composites.


## 11.7 Layout known-unknowns (carried for the engineer)

> **Resolved (byte-confirmed, §11.1a):** atlas FourCC/format and mip counts are now
> byte-confirmed; `loginwindow_02.dds` is **DXT2 (premultiplied alpha)** - set the
> premultiplied-alpha import flag. The server-plate `(9,6) 202x372` and PIN/notice frame
> `(318,647) 340x190` sub-rects are PLAUSIBLE (fit the confirmed 1024x1024 canvas; not
> pixel-verified).

- **Refresh / Cancel server-list button text:** baked atlas art vs caption id - UNVERIFIED.
- **Server-list calligraphy header caption id:** an integer caption id, exact value needs a `msg.xdb`
  extract.
- **Char-select conditional overlay button (action 61):** source rects now confirmed — see §11.5c.
  Role (select vs play overlay) and exact runtime show/hide trigger remain UNVERIFIED (debugger-pending).
- **Char-select number-glyph runtime mapping:** the build-time cell source rects are placeholders;
  the per-digit atlas mapping at runtime (analogous to the PIN digit/row scheme) was not chased.
- **2D class-icon-by-index widget:** absent in the char-select 2D builder (descriptor-driven instead)
  - Open question 11.
- **Live PIN re-roll on Reset:** static-confirmed, debugger-testable, UNVERIFIED live.
- **`characwindow.dds` internal frame rects:** the dedicated char-select atlas's sub-rects were not
  individually catalogued (the builder primarily uses `loginwindow.dds` / `mainwindow.dds` sub-rects).

## 11.8 Front-end font table — the 15 slots (CODE-CONFIRMED literals)

The login entry builds a 15-slot font table once, immediately before the login scene loop, and every
front-end widget references a font by slot index. Each slot is created with an explicit **face**,
**size**, **advance width** (the per-glyph advance — always non-zero, so the faces are **fixed-advance**),
**cell height** (the pixel height handed to the font engine), and **weight**. The character set is
**HANGUL (code page 949)** for every slot. The three faces are the Korean Windows system fonts
**DotumChe** (fixed-pitch gothic), **Dotum** (gothic), and **BatangChe** (fixed-pitch serif).

| slot | face | size | advance | cell height | weight |
|------|------|------|---------|-------------|--------|
| 0  | DotumChe  | 12 | 6  | 12 | 0 (≈400) |
| 1  | Dotum     | 10 | 5  | 10 | 0 |
| 2  | DotumChe  | 32 | 16 | 32 | 800 |
| 3  | DotumChe  | 18 | 12 | 24 | 800 |
| 4  | DotumChe  | 12 | 6  | 12 | 800 |
| 5  | BatangChe | 12 | 6  | 12 | 0 |
| 6  | BatangChe | 18 | 12 | 24 | 700 |
| 7  | BatangChe | 12 | 6  | 12 | 700 |
| 8  | BatangChe | 12 | 6  | 12 | 700 |
| 9  | DotumChe  | 12 | 6  | 12 | 700 |
| 10 | Dotum     | 16 | 10 | 20 | 800 |
| 11 | DotumChe  | 10 | 5  | 10 | 400 |
| 12 | DotumChe  | 12 | 6  | 12 | 400 |
| 13 | DotumChe  | 14 | 7  | 14 | 400 |
| 14 | DotumChe  | 16 | 8  | 16 | 400 |

Slot **2** (32 px, weight 800) is the large title face; slot **4** (12 px, weight 800) is the emphasised
body face used by the build-tail captions (see §3.8); slots **0/1** are the default small UI faces. Because
the faces are fixed-advance, label glyphs are laid on a fixed advance grid — a proportional renderer will
mis-align multi-glyph captions versus the original.

## 11.9 Widget builder primitives — the rect contract (CODE-CONFIRMED)

Every front-end widget is built by one of a small set of primitive builders. The destination rect is
**(X, Y, W, H)** and the atlas origin is **(SrcX, SrcY)**; the **source sub-rect extent is always equal to
the destination W×H** — i.e. **every blit is 1:1 pixels and never scaled**. Default alpha is opaque (255);
a colour of −1 (white) means "no tint" (the common case). A capability flag distinguishes image / panel /
clickable-button. The primitives and their argument order:

1. **Image** — `(tex, X, Y, W, H, SrcX, SrcY, color)`.
2. **Panel** — `(tex, X, Y, W, H, SrcX, SrcY, modalFlag, color)`; the `modalFlag` marks an opaque /
   input-capturing panel.
3. **3-state button** — `(tex, X, Y, W, H, NormSrcX, NormSrcY, HovSrcX, HovSrcY, PrsSrcX, PrsSrcY, color)`;
   three atlas origins for the normal/hover/pressed frames, all sharing the one destination W×H.
4. **1-state button** — `(tex, X, Y, W, H, SrcX, SrcY, color)`; used for the small scroll/pager arrows.
5. **Label** — `(tex = none, X, Y, W, H, color)` then a font-slot assignment and a set-text-and-align call;
   a label is an image component with no texture whose glyphs come from its font slot.
6. **Textbox / editbox** — `(tex, X, Y, W, H, SrcX, SrcY, color)`; the IME mode, maximum length and the
   click action id are assigned by follow-up calls, not constructor arguments.
7. **Checkbox** — `(tex, X, Y, W, H, OffSrcX, OffSrcY, OnSrcX, OnSrcY, color)`; separate unchecked/checked
   atlas origins.
8. **Composition** — add-child (passive) and add-child-with-action (binds the child's click to an action
   id). Action ids are each scene's command vocabulary (e.g. login: 100/101 login/quit, 102 open server
   list, 104 save-id, 106–108 scroll, 109/110 id/password; PIN keypad: 0–99 digit faces, 11 ok, 12 clear,
   13 cancel; opening: 100 skip).

## 11.10 Boot, Init resolution & the front-end message/sound catalogue (CODE-CONFIRMED)

**Boot (before the scene loop).** The entry point keeps the display awake, loads `game.lua`, reads the
integer keys `vfsmode`, `launcher`, `debugmode`, sets the VFS mount mode, then enters the scene loop **only
if** `launcher == 0` **or** the command line is `-Start`; the VFS is mounted exactly once at that point.

**Init (state 0).** Builds no UI. It acquires the engine/network singletons, selects the screen
resolution, then advances straight to Login (state 1). Resolution selection: a video-config mode field
chooses between **native-desktop** (width = primary-screen width, height = primary-screen height, each
clamped to **1920×1200**) and a **configured** width×height (a 0 in either axis falls back to the desktop
metric; same 1920×1200 clamp). The authored reference canvas is **1024×768**; all front-end widget
coordinates are authored for 1024×768 (§11.0).

**Login entry (state 1).** Loads the message table `data/script/msg.xdb`, constructs the login window,
builds the 15-slot font table (§11.8), and reads `DISPLAY_GAME_ADDICTION_WARNING_CHECK_TIME` (×1000 ms)
for the Korean play-time warning timer.

**Front-end caption-id catalogue (`msg.xdb`).** The login scene resolves these caption ids:

- **4001–4022** — the 22 central notice / agreement body labels.
- **4023, 4024** — the two login modal-panel prompt texts.
- **4025, 4026** — credential-entry validation (id-too-short / password-empty).
- **4027, 4028** — login / network failure messages.
- **4029–4032** — server-list text labels.
- **5901** — formatted fallback caption for a server id outside the 1..40 range.
- **6001–6005** — server status / load captions (6001 red / 6002 orange / 6003 yellow population bands;
  6004/6005 load-threshold & capacity-format text).
- **2204** — version-mismatch error (shown when `data/cursor/game.ver` disagrees with the bundled
  `game.ver`).

The login build also reads `data/script/uiconfig.lua` key `NEW_SERVER_INDEX` to seed the highlighted
new-server entry.

**Per-scene sound ids.** Login intro stinger **861010105** (one-shot, no loop, fired as the login window
opens its intro curtain); Load BGM **920100100** (looping); Opening BGM **910061000** (looping).

## 11.11 Login window visibility — the sub-state FSM (CODE-CONFIRMED)

The login window runs a per-frame sub-state machine (the render callback calls the window's update method)
that drives which panels are visible. It is the load-bearing fact for a faithful login: **most panels are
hidden at build time and shown only in the sub-state that needs them.** Sub-state sequence and visibility:

- **Sub-state 1 (curtain start):** play SFX **861010105** (category 2, no loop). Top curtain at Y=0; the
  **form panel at Y=326** (closed). SHOW the backdrop + the confirm faceplate. **HIDE** the notice panel,
  the server-list panel, the quit/help strip + deco, the interactive header (ID/PW/login), and the PIN.
- **Sub-states 2–4 (curtain slide):** each tick the **top curtain slides up to Y=−222** and the **form panel
  slides down to Y=548** (open). The form widgets are children of the form panel and **ride it to Y=548** —
  the form's resting position is the **bottom band (Y≈548–753)**, NOT the mid-canvas. Notice / server-list /
  option panel stay hidden; the faceplate is shown (its size set to 494×469 once the slide passes ~200).
- **Sub-state 5:** **SHOW the interactive header** (the ID / password / login / save-id / server-list row);
  keep the PIN hidden. → at-rest sub-state 6.
- **At rest (sub-state 6):** visible = backdrop + form panel at Y=548 (faceplate + ID/PW/login/checkbox).
  **The notice/EULA panel is NEVER shown by the FSM — it is hidden at rest** (it is an overlay that shares
  the central rect with the server-list, only the server-list is raised on demand). The central area shows
  only the backdrop.
- **Server-list:** opening it (its sub-state) **hides the interactive header, the PIN and the notice**, and
  raises the server-list panel in the shared central rect; selecting a server / the FSM closes it.
- **PIN:** the PIN modal is raised in its sub-state (hides the notice); on submit the flow advances.

C# parity: the notice panel must be `Visible=false` at rest (never restored), and the form container must
**track the bottom curtain to Y=548** (not be frozen at Y=326). Freezing the form at 326 with the notice
shown was the cause of the "everything piled in the centre" disorder.

---

## Addendum — CYCLE 11 / Block A: char-select action dispatch, enter-world ladder & scene-state numbering (binary-confirmed, static)

> Verification refresh, IDB SHA **263bd994**, static-only (CYCLE 11 / Block A, 2026-06-22).
> Deepens the character-select section (§3) with the action-dispatch table, the create steppers, the
> delete semantics, the enter-world ladder, and an important scene-state numbering clarification.

### A.1 Action dispatch — the selector and the two handler groups

Character-select UI commands are dispatched by the **firing widget's bound action id** (each interactive
widget carries an action id; activating it routes through the window's command handler on that id). The
handler resolves the id through two grouped jump tables — a **create-form group** (face + stat steppers)
and a **roster/preview group** (select, delete, enter, preview-orbit). *([CONFIRMED]* the selector is
the widget action id, not a window state byte.)*

| action group | effect | packet / state |
|---|---|---|
| **face stepper** | adjusts the create-form face index, clamped **1..7** | none (local; staged into the create blob) |
| **stat steppers** | the point-buy +/- controls (see `character_creation.md §2.1`) | none (local) |
| **slot select** | commit a roster slot for preview/enter | `1/7` with the select-context flag |
| **delete-confirm** | confirm deletion of the committed slot | `1/7` with the **delete-context flag set** |
| **delete-cancel** | dismiss the delete-confirm modal | none |
| **preview-orbit** | latch a preview turntable axis direction | none (local) |
| **exit-preview** | leave the preview sub-mode; re-idle the five preview actors | none (local) |

### A.2 Delete uses `1/7` with a flag — not a dedicated delete opcode

Both an ordinary **slot select** and a **delete-confirm** send the **same `1/7`** character-management
message; they differ only by a **context flag byte** in the payload (select-context vs delete-context).
The delete path is gated on a busy latch being clear and on a slot being committed; the delete-cancel
action only hides the confirm modal and sends nothing. *([CONFIRMED]* the shared `1/7` opcode, the
flag-byte discriminator, and the confirm/cancel split.)* The flag-byte value meaning on the wire is a
runtime residual (capture-pending) — see `packets/cmsg_char_select.yaml`.

### A.3 Enter-world ladder (the reach into the in-world scene)

The transition from character-select into the world runs as an ordered ladder:

1. **slot pick** — a local screen-ray test against the five preview actors selects a slot;
2. **`1/7` select-and-play** — sent for the committed slot (sets the net-busy latch);
3. **server** replies; **`3/14`** (the lobby-bridge spawn confirm) advances the window's latch;
4. an **enter-ready gate** is satisfied;
5. **`1/9` enter-game request** is sent;
6. the character-select scene loop **exits** and the **in-world scene is built**.

The **local-player spawn happens later** (on the in-world spawn message `4/1`), **not** on `3/14` —
`3/14` is only the lobby bridge confirm. *([CONFIRMED]* the ordered sends and the loop-exit-driven
transition; the enter-ready gate origin and the enter token contents are runtime residuals.)*

### A.4 Scene-state numbering — the case index vs the game-state value (IMPORTANT for the port)

The engine scene-state machine's **switch case index** and the **running game-state value** it writes
are **not the same number** for these two scenes — a subtlety that will cause an off-by-one if the C#
scene state machine conflates them:

| switch case index | builds | sets running game-state value to |
|---|---|---|
| **4** | the **character-select** window | **5** |
| **5** | the **in-world** window | **4** |

So **character-select runs while the running game-state value == 5** (reached via case index 4), and the
in-world scene runs while the value == 4 (via case index 5). State both meanings explicitly in any port
of the scene state machine. *([CONFIRMED]* the case→game-state writes.)*

### A.5 Per-class description binding & credential order (cross-reference)

- The character-select / create class strip binds its three description labels from the
  **`data/script/npc.scr`** keyed-node table (record offsets +20/+84/+148), and plays per-class create
  BGM cues — see `character_creation.md §3.1`.
- The login credential field order (account first, then optional PIN, then password) remains the
  **static hypothesis** already recorded in §1.3/§1.4 of this spec — unchanged this cycle, still
  capture-pending for the on-wire bytes.

### A.6 Preview lineup placement (cross-reference)

The five preview actors are placed at per-slot X offsets **−1560, −1548, −1536, −1524, −1512** (step
**+12**) over the scene anchor, with a slight bowed Z (slot 2 deepest), **base scale 70.0 × runtime
scale 3.0**, and per-slot facing from the facing-flag byte (value 1 → faces away). The camera/material
details are in `rendering.md` (CYCLE 11 addendum); the facing flag is in `structs/spawn_descriptor.md`
(CYCLE 11 addendum). *([CONFIRMED]* the placement deltas and the scale chain.)*

> spec path: `// spec: Docs/RE/specs/frontend_scenes.md`
