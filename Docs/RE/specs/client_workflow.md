---
verification: confirmed            # the dominant tier for this doc's load-bearing facts (scene machine, frame loop, boot, login sub-states, world spawn re-derived from binary control-flow); wire-level opcode bytes are capture/debugger-pending
ida_reverified: 2026-06-16
ida_anchor: 263bd994
evidence: [static-ida]             # add 'vfs-sample' only where a real asset sample corroborated (UI dialog IDs via msg.xdb, area inventory); noted inline as SAMPLE-VERIFIED
conflicts: login credential wire opcode (key-string secure context vs literal C2S 2/1 byte form); 4/1-form arrival ordering; whether the display FRAMERATE config is truly inert vs the hardcoded 60 FPS cap — all capture/debugger-pending
status: confirmed
sample_verified: partial   # scene machine, frame loop, boot sequence CODE-CONFIRMED; network constants PLAUSIBLE; UI dialog IDs SAMPLE-VERIFIED via msg.xdb
subsystems: [boot, scene_machine, frame_loop, login, server_select, char_select, world, ui, effects, sound, network, resource_pipeline, environment, terrain, skinning, anticheat]
networked: true
encoding_note: All Korean in-game text, dialog strings, config keys, and player names are CP949 (MS-949 code page). Register CodePagesEncodingProvider before any string decode.
---

# Client Workflow — Master End-to-End Specification

> **Clean-room neutral spec.** Promoted from dirty-room analyst notes under EU Software Directive
> 2009/24/EC Art. 6. No decompiler pseudo-code, no binary virtual addresses (VA), no decompiler
> identifiers (sub_, loc_, dword_, __thiscall, _DWORD, mangled names). Struct field offsets (+0xNN
> within an object) and file byte offsets are neutral layout facts and are allowed.
>
> **Purpose.** This document is the single entry-point for understanding the entire legacy
> *Martial Heroes* client end-to-end — from OS process creation through every interactive screen to
> the live game world, including all modules and their interconnections. Deep detail lives in
> satellite specs; this document summarises and cross-links. Every implementation engineer must
> read this file first, then follow cross-links for the precise constants they need.
>
> **Evidence grades used throughout:**
> - **CODE-CONFIRMED** — re-derived from binary control-flow logic; value is in the binary.
> - **SAMPLE-VERIFIED** — additionally cross-checked against real shipped VFS/asset bytes.
> - **CAPTURE-VERIFIED** — confirmed against real network captures.
> - **PLAUSIBLE** — single-source inference or structural deduction; implement but keep tunable.

---

## Table of contents

1. [Executive overview and end-to-end flow](#1-executive-overview-and-end-to-end-flow)
2. [Boot and initialisation](#2-boot-and-initialisation)
3. [Frame loop anatomy](#3-frame-loop-anatomy)
4. [Scene state machine](#4-scene-state-machine)
5. [Scene chapters](#5-scene-chapters)
   - 5.1 Login
   - 5.2 Server Selection
   - 5.3 Character Selection
   - 5.4 World
6. [Module chapters](#6-module-chapters)
7. [Module interconnection matrix](#7-module-interconnection-matrix)
8. [Engine identity](#8-engine-identity)
9. [Open questions register](#9-open-questions-register)

---

## 1. Executive overview and end-to-end flow

The legacy client is a single-process Win32 application built on the proprietary "Diamond" engine.
All major screens share a single message-pump / render loop; what changes between screens is the
*scene handler* object registered with that loop. The main function itself *is* the scene state
machine — it constructs a handler for each state, drives the shared loop until the handler signals
completion, tears the handler down, and then falls into the next state's branch.

### 1.1 Full process flow (ASCII diagram)

```
OS process create
        |
        v
[CRT startup]
  (static-singleton construction order: Tier-A CRT statics → Tier-B lazy Meyers first-use)
        |
        v
[WinMain entry]
  read game.lua  ──>  {vfsmode, launcher, debugmode}   (all default true if absent)
  if launcher==true: enforce -Start flag / dostart.exe
        |
        v
[Init block — State 0]
  DoOption.ini parse  (display, sound, quality, ~30 keys)
  D3D9 device creation (HAL, HW-VP|MT, X8R8G8B8, D32→D24X8→D24S8→D16 depth fallback,
                        IMMEDIATE present)
  Korean font table (15 slots: DotumChe / Dotum / BatangChe, HANGUL_CHARSET=129)
  VFS mount: data.inf 24-byte header parsed (entry count at header +0x0C, the 4th dword),
             144-byte sorted TOC entries, opened FILE_FLAG_RANDOM_ACCESS;
             data.vfs kept open for the session
        |
        v
[State 1 — Login]
  LoginWindow constructed (~340 widgets from uiconfig.lua)
  lobby mini-protocol: port 10000 / server list
  user enters credentials → LoginWindow internal handshake sub-machine (field +0x238)
    credential validation → PIN / second-password modal (sub-state 31) → server-list fetch
  credential handoff = a TAB-SEPARATED KEY string (account \t password \t PIN \t "host port")
    consumed by the secure-context builder → join → branch:
    EnterGameAck (3/5) ─────────────────────────────> [State 2 — Load]
    version/auth fail / 30000 ms watchdog timeout ─> dialog ─> stay in State 1
  user quits ─> State 6 (Quit)

[State 2 — Load]   (also used before World; see §4)
  LoadHandler (~536 B) + async BulkAssetLoader_Thread: ~50 tables in fixed order
  loading screen: random DDS backgrounds (loading.dds, loading01–08.dds)
  progress denominator: 9,395,240 bytes (CODE-CONFIRMED, hardcoded)
  on complete:
    OPENNING/SKIP INI key set ─> [State 4 — Character Select]
    OPENNING/SKIP absent/false ─> [State 3 — Opening]

[State 3 — Opening]   (optional)
  plays intro sequence (openning_001.dds … openning_004.dds)
  on complete ────────────────────────────────> [State 4 — Character Select]

[State 4 — Character Select]
  SelectWindow constructed (~6280 B; 5 × 880-byte SpawnDescriptor slots)
  3D character preview via live Actor_Factory_Create (same pipeline as World)
  Select camera (multi-waypoint preview path, distinct from the five in-world manipulators)
  CharacterList (3/1) packet triggers builder; SmsgCharacterList → widget tree built
  user picks slot → C2S 1/7 select → C2S 1/9 enter
  SpawnDescriptor + 96-byte stats cached
  confirm-enter flag set → SelectWindow_End → State 5

[State 5 — In-game]
  MainHandler (~200 B) → BuildGameWorld → BuildSceneGraph
  camera: FOV 65°, near 5, far 15000; 5 manipulators + 1 reserved slot
  await S2C 4/1 GameStateTick → extract spawn position → Terrain_InitFirstRing_3x3
  per-frame world-update callbacks (six render passes A–F + UI)
  logout / disconnect ─> State 4 (Character Select), NOT back to Login

[State 6 — Quit]
  engine shutdown → writes state value 8 (exit)

[State 7 — Error]
  reason read from the GameState reason field → modal dialog; net connection closed
  → writes state value 8 (exit)

[Exit — state value 8, NOT a 9th case]
  the switch is bounds-checked (state <= 7, a jump table of 8 entries 0..7 plus a default);
  writing state value 8 falls into the switch DEFAULT branch: resource teardown in
  reverse-singleton order, then WinMain returns / process exit
```

> **Scene-state count (corrected):** the WinMain switch has **exactly 8 top-level cases, states 0..7**.
> The value **8 is a sub-state / exit sentinel**, not a 9th top-level case — it is what states 6 (Quit)
> and 7 (Error) write to drop into the switch default (teardown + return). Earlier "states 0..8 /
> nine-state lifecycle" phrasing is wrong: it is *states 0..7 (8 cases); 8 is the exit sentinel*.

> **Key non-obvious edge:** in-game (state 5) transitions **back to character select (state 4)**,
> not to login. Login is visited only once per process lifetime. The load/opening gate (state 2) is
> also visited only once, after the first successful login.

*Network-driven transitions* (S2C packets that force a state change) are documented in the
transition table in §4. *UI-driven transitions* (user button presses) are in §5.

---

## 2. Boot and initialisation

> **Detail owner:** `specs/client_runtime.md` (full singleton construction order, D3D9 device
> constants, DoOption.ini key table, five INI files, state-machine per-case details) and
> `specs/game_loop.md` (loop architecture).

### 2.1 Entry and configuration (CODE-CONFIRMED)

The process entry point is WinMain. Execution proceeds in three phases before the scene loop starts:

**Phase 1 — Lua config read.** The embedded Lua 5.1.2 VM is initialised early enough to load
`game.lua` from the VFS (or local path). Three boolean globals determine subsequent boot behaviour.
All three default to `true` if `game.lua` is absent or fails to load:

| Lua global   | Effect when true |
|-------------|-----------------|
| `vfsmode`   | Files are read from the VFS (data.inf + data.vfs); when false, raw filesystem fallback |
| `launcher`  | Process must have been launched with the `-Start` command-line flag or via `dostart.exe`; absent → immediate exit via `dostart.exe` `WinExec` |
| `debugmode` | Enables additional console/logging output; stored at engine-state struct byte +0x0C |

**Phase 2 — Option parsing.** Five INI files are located in the EXE directory and marked hidden
(`SetFileAttributesA`): `DoOption.ini`, `option.ini`, `panel.ini`, `combo.ini`, `TSIDX.ini`.
`DoOption.ini` section `[DO_OPTION]` is parsed into ~30 fields covering display resolution,
windowed/fullscreen (`OPTION_SCREENMODE`), texture quality, sound volume buses, and
network settings. All field indices and clamp ranges are in `specs/client_runtime.md §DoOption`.

**Phase 3 — Device and font setup.**

- D3D9 device: HAL adapter, hardware vertex processing + multi-threading flags, `X8R8G8B8`
  back-buffer, depth fallback chain `D32 → D24X8 → D24S8 → D16`, IMMEDIATE swap-chain present.
  SDK version `32` (decimal). (CODE-CONFIRMED)
- Korean font table: 15 slots using `DotumChe`, `Dotum`, and `BatangChe` faces with
  `HANGUL_CHARSET` (129). Text rendering uses `D3DXCreateFontA`; fixed-advance layout (charWidth
  per character). Full 15-slot table in `specs/ui_system.md §6`.
  (CODE-CONFIRMED — see also `specs/client_runtime.md §0.6`.)
- VFS mount: `data.inf` 24-byte header read; **entry count at header byte offset +0x0C (the 4th
  dword), not +0x08**; 144-byte sorted TOC entries (path at +0, i64 data-offset at +104, i64
  data-size at +112). The `.inf` is opened with `FILE_FLAG_RANDOM_ACCESS` (not `SEQUENTIAL_SCAN`);
  the `data.vfs` file handle is kept open for the process lifetime.
  (Byte-layout authority is `formats/pak.md`, to be byte-witnessed in the VFS pass — cite as
  CODE-CONFIRMED via control-flow, VFS byte-witness pending.)

### 2.2 Singleton construction order (CODE-CONFIRMED)

Tier A singletons are C++ static-storage objects whose constructors run before WinMain. The only
load-bearing Tier A object traced end-to-end is a billing/anti-cheat scheduler proxy that gates
state-1 entry (failure → `exit(1)`). Tier B singletons are lazy Meyers first-use objects. The
authoritative ordered list is in `specs/client_runtime.md §0.7` and `structs/runtime_singletons.md`.

---

## 3. Frame loop anatomy

> **Detail owner:** `specs/game_loop.md`, `specs/client_runtime.md §8`.

### 3.1 Four-phase body (CODE-CONFIRMED)

Every interactive screen (Login, Opening, Character Select, World) runs the same engine main loop.
The loop is **software frame-capped at a fixed 60 FPS** (NOT uncapped): a QueryPerformanceCounter-measured
limiter sleeps each iteration to hold the target frame period. The target rate lives in the engine
object's framerate field, seeded to `60.0f` in the engine constructor and never overwritten anywhere
in the traced control-flow. The display-config `FRAMERATE` value has no consumer that reaches the
throttle (statically inert) — so treat the cap as a hardcoded 60 FPS. The presentation interval is
still IMMEDIATE (the frame pacing comes from the QPC limiter, not from vsync).

> Whether the display-config `FRAMERATE` value is *truly* inert at runtime, or wired through a path
> not visible statically, is (capture/debugger-pending). Implement the 60 FPS hardcoded cap.

The loop has **four phases per iteration**, in order:

**Phase 1 — Message pump and deferred input dispatch.**
Win32 `PeekMessage` / `TranslateMessage` / `DispatchMessage` until the queue is empty, then
a double-buffered deferred-event list is swapped and each queued UI/input event is dispatched
through the input manager. A raw mouse/keyboard DirectInput thread is spawned once on the first
pump iteration and runs concurrently thereafter. The Korean IME (IMM32) is serviced here.

**Phase 2 — Scene render and present.**
The active scene handler's frame-step callback is invoked once per iteration. Inside, the order is:

1. Camera transform update (view matrix rebuild; frustum cull walk).
2. Scene-graph render pass (see §5.4.4 for the authoritative six-pass world order; reduced
   form for non-world screens).
3. Back-buffer present (`IDirect3DDevice9::Present`, IMMEDIATE).
4. Device-lost detection and recovery loop (sleep 1000 ms on `DEVICELOST`; three-step reset on
   `DEVICENOTRESET`: release default-pool resources → reset → recreate default-pool resources).

**Phase 3 — Round-robin logic-tick scheduler.**
A round-robin scheduler walks registered tick subscribers. The scheduler advances approximately
1.1% of registered subscribers per frame (floor of `count × 0.011`), spreading work across ~90
frames at a typical rate. No leftover-time carry — a missed tick cannot be caught up. Logic systems
that need guaranteed per-frame execution register as frame-step callbacks instead.

**Phase 4 — Frame throttle.**
The QPC-measured frame limiter computes the elapsed iteration time against the 60 FPS target period
and sleeps the remainder, holding the loop at the fixed 60 FPS cadence.

### 3.2 Clock (CODE-CONFIRMED)

- Wall clock source: `timeGetTime()`, millisecond resolution. This is the sole simulation clock.
  Neither `GetTickCount` nor `QueryPerformanceCounter` feeds logic.
- A global time-scale float (default 1.0) multiplies the raw `timeGetTime()` delta before passing
  it to most subsystems. Values below 1.0 produce slow-motion.
- Day/night cycle tick runs as a **pre-draw callback** inside Phase 2 render pass A, gated ≥ 50 ms
  wall-time per tick (shared gate with terrain water UV-scroll animation).
  (CODE-CONFIRMED — see `specs/client_runtime.md §8.4`.)

---

## 4. Scene state machine

### 4.1 Overview (CODE-CONFIRMED)

WinMain mounts the VFS once, then runs a `while(1)` switch/case over a single global engine-state
integer record (a 3-int record: `[state, sub-state, reason]`). The switch is **bounds-checked
(state ≤ 7) and dispatches through a jump table of exactly 8 entries (states 0..7) plus a default
branch**. There is no 9th top-level case — the value **8 is the exit sentinel** written by the
Quit (6) and Error (7) cases to fall into the switch default (teardown + WinMain return). See §4.2.

Each top-level case:
1. Writes the *next* engine state immediately (pre-loop intent).
2. Constructs the scene handler.
3. Calls the shared engine main loop (the four-phase loop from §3), which runs until a one-byte
   run-flag is cleared.
4. Destructs the handler.
5. Falls through to the outer `while(1)` which re-dispatches on the current engine-state value.

The loop-break mechanism is a dedicated function that clears the global run-flag. State transitions
are effected by writing the desired next-state integer and then calling this break function.
Network-received transitions and user-action transitions both use the same mechanism.

### 4.2 State enumeration

| State | Name (internal label) | Handler | Approx. size |
|------:|----------------------|---------|-------------|
| 0 | Init | Inline — device setup only | — |
| 1 | Login | `LoginWindow` | ~1368 B |
| 2 | Load | `LoadHandler` / loading-screen handler | ~536 B |
| 3 | Opening | `COpeningWindow` — intro sequence | ~720 B |
| 4 | Character Select | `SelectWindow` | ~6280 B |
| 5 | In-game | `MainHandler` + full scene graph | ~200 B base |
| 6 | Quit | Inline — engine shutdown path; writes state value 8 | — |
| 7 | Error | Inline — modal dialog (reason from the GameState reason field); writes state value 8 | — |
| 8 | *(exit sentinel — NOT a case)* | Switch **default** branch — teardown and WinMain return | — |

> **State count (CODE-CONFIRMED, corrected):** there are **8 top-level cases (states 0..7)**.
> The switch is bounds-checked (`state ≤ 7`) over a jump table of 8 entries plus a default branch.
> **Value 8 is the exit sentinel, not a 9th case** — it is what cases 6 and 7 write so the next
> `while(1)` iteration falls into the switch default (resource teardown + WinMain return). Earlier
> "states 0..8 / nine-state" phrasing is wrong.

> Note: a `SimpleLoadHandler` class exists in the binary with a complete constructor but zero
> callers. It is dead code from an earlier iteration. (CODE-CONFIRMED)

### 4.3 Transition table

| From state | Trigger | Target state | Sub-state |
|-----------|---------|-------------|----------|
| 0 Init | Always (one-time) | 1 Login | — |
| 1 Login | Pre-loop case body | 2 Load | — |
| 1 Login | Window config failure | 7 Error | 1 |
| 1 Login | Device/secondary init failure | 7 Error | 3 |
| 1 Login | Quit button / version mismatch confirmed | 6 Quit | 2 |
| 1 Login | EnterGameAck (3/5) received | 2 Load | — |
| 2 Load | `OPENNING/SKIP` INI = true | 4 Select | — |
| 2 Load | `OPENNING/SKIP` absent / false | 3 Opening | — |
| 2 Load | Quit hotkey | 6 Quit | 2 |
| 2 Load | Connection error during load | 7 Error | 2 |
| 3 Opening | Intro complete | 4 Select | — |
| 4 Select | CharacterList (3/1) received | 4 Select | 8 |
| 4 Select | Pre-loop case body (enter-game) | 5 In-game | 8 |
| 4 Select | Quit / exit command (action-result code 0) | 6 Quit | 8 |
| 4 Select | Action-result code 202/203/232 (S2C 3/100) | 2 Load | — |
| 4 Select | Action-result code 1..4 or 7 (S2C 3/100) | 7 Error | 5 |
| 5 In-game | Default on loop-exit | **4 Select** | — |
| 5 In-game | Quit / logout | 6 Quit | 8 |
| 5 In-game | 4/1 GameStateTick (form 1), actor create fail | 4 Select | — |
| 5 In-game | Action-result code ≠ 0, local player (S2C 3/100) | 7 Error | 8 |
| 6 Quit | Case body | writes value 8 (exit sentinel → default branch) | — |
| 7 Error | Case body | writes value 8 (exit sentinel → default branch) | — |
| Any | `WM_QUIT` | (run-flag cleared) | — |

> **Opcode attribution (CODE-CONFIRMED, corrected):** the result codes 202/203/232 and 1..4/7 above
> are handled by the **generic char/lobby action-result S2C 3/100**, NOT by the char-manage result
> S2C 3/7. The 3/7 handler handles only refresh / rename / delete subtypes and the same-day delete
> cooldown — see §5.3.2. The *transition targets* above are correct; only the earlier "CharMgmt"/3/7
> attribution was wrong. (Wire-level opcode bytes: capture/debugger-pending; the prose attribution
> here is static.)

> `OPENNING/SKIP`: the INI key typo (two N's) is authentic — `[OPENNING]` section, key `SKIP`.
> If absent by default in all shipping clients, state 2 → 4 is effectively the production path.
> (CODE-CONFIRMED — see `specs/client_runtime.md §7.3`.)

### 4.4 Scene-aware disconnect routing (CODE-CONFIRMED)

A scene-aware quit dispatcher reads the current engine state and picks the appropriate teardown:
- State 2 → write state 6 / sub-state 2 (quit during load); SFX 861010106; schedules a 1000 ms
  timed-engine-event (the loading-done bridge, see §4.5).
- State 4 → send a network leave message (no direct state write; the server reply drives the change).
- State 5 → write state 6 / sub-state 8 (logout cleanup); SFX 861010106; schedules a 1500 ms timed
  event; tears down world UI / actor slots.
- State 7 is the generic error path for unexpected disconnects from any state.

The in-world leave/logout teardown additionally disables the keepalive (clears the master-enable
global, see §6.4), emits SFX 861010106, and writes state 6 / sub-state 8.

Full network-driven transition table: `specs/client_runtime.md §7.5.2`.

### 4.5 Timed-engine-event bridge and the login watchdog (CODE-CONFIRMED)

The client uses a single **universal timed-engine-event mechanism** (a scheduled "fire after N ms"
engine event, internally identified as event id **10001**) to bridge asynchronous network replies
and teardown to deterministic state writes. Every error / quit / disconnect path enqueues a 10001
event with a delay; observed delays are **1000 / 1500 / 5000 / 10000 / 30000 ms**. When the timer
fires, its handler performs the deferred state write (advance or abort). This is the common spine
under §4.4 and the disconnect routing in §6.4.

A specific instance is the **30000 ms login watchdog**: at the end of the login join handshake
(§5.1.3) the client schedules a 30000 ms 10001 event; if the server never returns `EnterGameAck`
(3/5) within that window, the watchdog drives the error path. (Wire-level dependence on a live
server reply: capture/debugger-pending.)

### 4.6 Error-state reason field (CODE-CONFIRMED)

State 7 (Error) selects its dialog text from the **reason field of the GameState record** (the
third integer of the `[state, sub-state, reason]` record). When the sub-state is 8 and the reason
is non-zero, the handler formats a per-code message from the reason value; otherwise it maps the
sub-state to a generic message. The reason field is how a server-supplied char/lobby action-result
code (written by the S2C 3/100 handler, §5.3.2) reaches the error dialog. The handler then closes
the NetClient and shows the modal.

---

## 5. Scene chapters

### 5.1 Login

> **Detail owner:** `specs/login_flow.md`, `specs/frontend_scenes.md §login`,
> `specs/ui_system.md §8.1 and §11`.

#### 5.1.1 UI surface

The `LoginWindow` constructs approximately 340 widgets (from `data/script/uiconfig.lua`) at
fixed pixel coordinates on a 1024×768 canvas. The 21 explicit constructor sites are tabulated
in `specs/ui_system.md §8.1`. Key controls:

- ID textbox: max 6 characters, IME slot (widget index 16), screen position (390, 32), 102×13 px.
- PW textbox: max 129 characters, '*' mask at 6 px per char, IME slot (widget index 12),
  500 ms caret blink, screen position (568, 32), 102×13 px. (CODE-CONFIRMED)
- Five texture atlas files loaded at construction time (see §5.1.4).
- All on-screen button labels come from `msg.xdb` dialog IDs in the range 4001–4028.

**Action-ID dispatch:** button click events carry an integer `actionId` stored at field `+0x10`
on the widget object (NOT at +0x0C, which is the tint/colour field — a critical distinction
corrected in `specs/ui_system.md §1.2`). The window dispatcher routes the `actionId` on a
click-release event via the embedded command handler. Corrected action-id map:

| actionId | ASCII | Widget | Behaviour |
|---------|-------|--------|-----------|
| 102 | `f` | Server-list button | Reveal server-list panel |
| 103 | `g` | OK/Login button | Version gate (msg 2204 on fail → quit) → sub-state 29 |
| 104 | `h` | Save-ID checkbox | Persist account/ID into the persisted settings (Save-ID) |
| 105 | `i` | Re-fetch strip | 10000 ms throttled server-list re-fetch (→ sub-state 34) |
| 111 | `o` | Option page select | Sets page-state 5 (see §5.1.3 page-state machine) |
| 112 | `p` | Server-list button (2nd) | **Second server-list reveal — same handler as `f`**, NOT an option toggle |
| 113 | `q` | Panel hide #1 | **Hides a panel, then sets sub-state 34 (server-list RE-FETCH)** — NOT a quit-confirm |
| 114 | `r` | Panel hide #2 | Same as `q` (hide panel → sub-state 34 re-fetch) |
| 115–124 | — | Server name-strip / pager | Server entry pager: at sub-state 37, page = `actionId − 115` (repaint only, no commit) |

> **Action-id corrections (CODE-CONFIRMED):** earlier versions mapped 112 `p` to "Option/tab 2
> toggle" and 113/114 `q`/`r` to "Quit-confirm Yes → advance to quit". Both are wrong: `p` is a
> second server-list reveal sharing the `f` handler, and `q`/`r` hide a panel and re-enter the
> server-list fetch at sub-state 34. The actual server-plate *commit* is class-7 plate-pick
> (left/right pager actions 400/401 at sub-state 37) — see §5.2.2.

Clicking **Login** does NOT send a packet immediately; it runs a local validation gate (ID minimum
length ≥ 4, PW non-empty) and then advances the internal network/handshake sub-machine.

Dialog IDs used:

| msg.xdb ID | Displayed when |
|-----------|---------------|
| 2204 | Server-reported version mismatch |
| 4025 | Login ID too short |
| 4026 | Password empty |
| 4027 | No servers available |
| 4028 | Connection failed |

**Button atlas assignment (CODE-CONFIRMED — corrected):**
The OK/Login button, Server-list button, ID/PW textboxes, and Save-ID checkbox are bound to
**`data/ui/login_slice1.dds`** — not `loginwindow.dds` as stated in earlier drafts.
`loginwindow.dds` holds the option/tab buttons, server name-strip, and decorative elements.
Full per-widget atlas table: `specs/ui_system.md §8.1`.

#### 5.1.2 Network surface

Login uses a *lobby mini-protocol*: a separate synchronous blocking socket opened to port 10000,
distinct from the game socket (which is not yet open at this stage).

The login **credential handoff is a tab-separated KEY string**, not a literal `2/1` byte blob. At the
end of the handshake (§5.1.3) the client assembles a single string of four tab-separated fields —
`account` `\t` `password` `\t` `PIN` `\t` `"host port"` — from the ID and PW text-boxes, the PIN
captured by the second-password modal, and the resolved channel endpoint. Field caps are: account
< 20 chars, password < 17 chars, PIN < 5 chars. This string is consumed by the secure-context
builder, which establishes the authenticated connection. On success, `EnterGameAck` (opcode 3/5) is
received on the game socket, storing the session token and triggering the state transition to Load;
auth/version failure surfaces a dialog and the client stays in Login.

> The exact *wire form* of the credential submission — whether the secure-context builder ultimately
> emits a literal C2S **2/1** packet, or a different framing — is **(capture/debugger-pending)**; the
> static path shows only the key-string → secure-context construction, not a `2/1` opcode on this
> path. Treat the "2/1" opcode label as provisional. Full wire layout (once captured): `opcodes.md`
> and `packets/c2s_login.yaml`.

#### 5.1.3 Login: two parallel machines (CODE-CONFIRMED — corrected)

`LoginWindow` runs **two separate state fields** that earlier drafts conflated:

- **UI page-state** at object offset `+0x17C` — driven by the event handler. Values 4 / 5 / 6 select
  which login page is shown (e.g. action `o` sets page-state 5; pressing Enter while on page-state 6
  enters credential validation at handshake sub-state 29).
- **Network/handshake sub-state** at object offset `+0x238` — driven by the per-frame tick callback.
  This is the handshake/join machine (sub-states 2–41), documented in `specs/login_flow.md` and
  `specs/ui_system.md §11.3`.

Key handshake sub-states (corrected from earlier versions):

- Sub-state **1 → 2**: plays the login SFX 861010105 (2D, category 2) and advances.
- Sub-state **29** is *OK-button credential validation*: ID length `< 4` → dialog msg 4025; empty PW
  → dialog msg 4026; otherwise it seeds the account into the secure context and **opens the PIN /
  second-password modal**, setting sub-state 31. (It is **not** a server-list trigger.)
- Sub-state **31** is the **PIN / second-password modal** (a second-password entry overlay shown
  immediately after credential validation) — **not** an EULA / terms-of-service overlay and **not**
  a help screen. **There is no EULA / terms-accept path anywhere in the login machine.**
- Sub-state **32** polls the modal (visible + submitted) and advances to **33**.
- Sub-states **33 / 34 / 35** start the server-list fetch thread (port 10000); a no-servers reply
  shows dialog msg 4027, a connection failure shows msg 4028.
- Sub-states **37 / 38** are the server-plate pick (§5.2.2): on commit, the chosen server is persisted
  (Last-server) and the channel endpoint fetched.
- Sub-state **41** builds the credential **key string** and performs the join handoff (§5.1.2),
  then schedules the 30000 ms login watchdog (§4.5).

#### 5.1.4 Data dependencies

- `msg.xdb` for all dialog strings (IDs 2204, 4001–4028). (SAMPLE-VERIFIED)
- `data/script/uiconfig.lua` — widget layout (~340 widgets); contains `NEW_SERVER_INDEX` global.
- `game.lua` for the `launcher` and `vfsmode` flags.
- `DoOption.ini` for persisted last-server selection (key `OPTION_ID`).
- Atlas files: `data/ui/login_slice1.dds`, `data/ui/loginwindow.dds`,
  `data/ui/loginwindow_02.dds`, `data/ui/inventwindow.dds`.

#### 5.1.5 Curtain intro animation (CODE-CONFIRMED)

The login screen opens with a **vertical-slide curtain** animation driven by a global accumulator
(not a window member). The tick advances the accumulator by **+5 per tick** up to a threshold of
**222**; below a 200-unit sub-threshold it also re-anchors a child widget. The result is a curtain
that slides vertically into place as the login window appears. (Detail: `specs/frontend_scenes.md §login`.)

---

### 5.2 Server Selection

> **Detail owner:** `specs/frontend_scenes.md §server_select`, `specs/client_runtime.md §7`.

#### 5.2.1 UI surface

Two renderer variants exist in the binary: a classic list view and a `NEW_SERVER` variant that
adds a badge for newly added servers. The `NEW_SERVER_INDEX` Lua global controls which list entry
gets the badge. The window uses `msg.xdb` IDs 5001–5040 for localized server names (41-entry
table). (CODE-CONFIRMED)

#### 5.2.2 Network surface — lobby mini-protocol (CODE-CONFIRMED)

Server list is fetched by network query, not read from a local file. The protocol:

1. **IP resolution chain:** `ip.txt` → `list.dat` / `CIPList` structure (768-byte records: server
   name at +0, IP string at +256) → hardcoded fallback IP `211.196.150.4`.
2. **Server-list query:** sent to port 10000. Response contains 8-byte records per server:
   `server_id` (u16), `status_code` (i16), `load` (i16), `open_time` (i16). (CODE-CONFIRMED)
3. **Plate pick (handshake sub-state 37):** the server list is shown as selectable plates; the event
   handler routes a class-7 plate-pick — pager actions **400 (LEFT) / 401 (RIGHT)** — to a record at
   the 8-byte stride. A plate is **selectable only when `status_code == 0` and `load < 2400`**. On
   commit the chosen server is persisted as the Last-server value and the machine advances to
   sub-state 38. (CODE-CONFIRMED)
4. **Channel endpoint (handshake sub-state 38):** client opens a second request to port
   `10000 + server_id`. The server returns an ASCII `"host port"` string in the first 30 bytes of
   the response. The game socket is then opened to that `host:port`. (The `10000 + server_id`
   arithmetic lives in the fetch-thread body; CODE-CONFIRMED control-flow, exact wire bytes
   capture/debugger-pending.)

#### 5.2.3 Persistence

The selected server ID is saved to `DoOption.ini` key `OPTION_ID` and read back on next launch
to pre-select the previously used server. (Written by `LoginWindow_End`.)

#### 5.2.4 Data dependencies

- `ip.txt` (may be absent — fallback chain applies).
- `list.dat` for named server list with IP table.
- `msg.xdb` IDs 5001–5040 for server name localisation.
- `data/script/uiconfig.lua` `NEW_SERVER_INDEX` for new-server badge display.

---

### 5.3 Character Selection

> **Detail owner:** `specs/frontend_scenes.md §char_select`, `specs/ui_system.md §8.2 and §11.4`.

#### 5.3.1 UI surface

`SelectWindow` (~6280 bytes) contains a 5-element inline array of 880-byte `SpawnDescriptor`
structs. An empty slot is identified by the sentinel string `"@BLANK@"` in the name field.
The widget tree is built by `InitFromCharListAndBuildUI`, which is triggered by the network
`CharacterList (3/1)` packet — **the widget tree does not exist at scene creation time**.

**Atlas assignment** (CODE-CONFIRMED):

| Atlas DDS | Used for |
|---|---|
| `data/ui/loginwindow.dds` | Tab buttons, stat-icon grids, Create/Delete/Enter button strips |
| `data/ui/inventwindow.dds` | Popup panels + buttons (confirm/delete/name-entry chrome) |
| `data/ui/blacksheet.dds` | Corner close button; dim/blackout overlay |
| `data/ui/carrierpigeonperson.dds` | Appearance selector ±, gender/class preview swatches |

**Action-id map (CODE-CONFIRMED — corrected):**
The values 413 and 531 that appeared in earlier versions of this spec as action ids are the
**atlas src-X coordinates** of the HOVER frames, not action ids. The correct values are:

| actionId | Widget | Note |
|---------|--------|------|
| 4 | Create button | NORMAL src (354, 1004), HOVER src-X = 413 |
| 5 | Delete button | NORMAL src (472, 1004), HOVER src-X = 531 |
| 6 | Enter/select button | NORMAL src (236, 1004) |

Full 77-site widget table and layout coordinates: `specs/ui_system.md §8.2`.

The window renders a live 3D character preview for each populated slot using the same
`Actor_Factory_Create` pipeline as the World scene. Stage layout — character preview positions
(CODE-CONFIRMED):

| Slot | Stage X offset |
|------|---------------|
| 0 | −1560 |
| 1 | −1548 |
| 2 | −1536 |
| 3 | −1524 |
| 4 | −1512 |

Stage Z ≈ −3593. All previews rendered at scale ×3.0. Slots spaced 12 units apart on X. These
are driven by the **Select camera** (a multi-waypoint preview-path manipulator distinct from the
five in-world camera manipulators).

**Name validation rules** (CODE-CONFIRMED): minimum 2 characters; allowed character set:
lowercase a–z, digits 0–9, and CP949 Hangul double-byte sequences only.

**Delete cooldown**: same-day lock enforced; ready time stored as a future timestamp, displayed
in HH:MM format. (CODE-CONFIRMED)

#### 5.3.2 Network surface (CODE-CONFIRMED)

Five C2S opcodes govern character management:

| C2S opcode | Size | Purpose |
|-----------|------|---------|
| 1/6 | 52 bytes | Create character |
| 1/7 | 2 bytes | Select (cursor change, no state transition) |
| 1/9 | 40 bytes | Enter world (triggers state transition) |
| 1/13 | 18 bytes | Rename character |
| 1/14 | 1 byte | Delete character |

> **Opcode 1/6 "collision" — RESOLVED (CODE-CONFIRMED):** there is no collision. C2S **1/6 is
> create-character** (character-management protocol, 52-byte payload). The login credential path does
> **not** send a 1/6 packet — it uses the tab-separated key-string secure-context handoff (§5.1.2).
> The earlier "login operation at major=1 minor=6" was a mis-annotation. OQ-PROTO-01 is closed.

Beyond the five C2S opcodes above, the char/lobby char-management S2C side also carries the
**char-manage result S2C 3/7** (refresh / rename-applied / delete-confirmed subtypes plus the
same-day delete-cooldown notice) and the **generic char/lobby action-result S2C 3/100** (routes
result codes to the Load / Error / Quit state writes, see §4.3). These are distinct opcodes: 3/7
handles only the manage subtypes; 3/100 carries the action-result codes that the §4.3 transition
table keys on. (Wire-level opcode bytes: capture/debugger-pending.)

**Version token in 1/9:** field computed as `10 × game.ver[field5] + 9`. (CODE-CONFIRMED)

**Enter-world sequence:** C2S 1/9 is sent; the S2C generic action-result (3/100) acknowledgement
with result code 0 and the local player present sets the confirm-enter flag; the select-window end
path writes next-state = 5 (In-game) and calls the run-flag break.

**SpawnDescriptor cache:** on enter-world, the 880-byte SpawnDescriptor and an additional 96-byte
stats block are copied to a session-scoped buffer for use by `MainHandler` at world entry.

**Audio teardown on enter:** the select-window enter path **stops the loading BGM 920100200** as
part of leaving the character-select scene. (CODE-CONFIRMED)

#### 5.3.3 Data dependencies

- `CharacterList (3/1)` S2C packet (triggers the widget-tree build).
- `.skn`, `.bnd`, `.mot` for 3D preview rendering (same chain as World — see §5.4 and
  `specs/skinning.md`).
- `msg.xdb` for all UI labels and validation dialogs.
- `data/char/skin.txt`, `actormotion.txt`, texture atlases for preview character appearance.

---

### 5.4 World

> **Detail owner:** `specs/client_runtime.md §7–§9`, `specs/environment.md`,
> `specs/resource_pipeline.md §terrain_streaming`, `specs/skinning.md`.

#### 5.4.1 World build sequence (CODE-CONFIRMED)

On entering State 5, the case body constructs `MainHandler` and calls `BuildGameWorld`:

1. **`BuildGameWorld`** — allocates world-layer objects (physics grid, entity registry,
   cell-streaming queue). Approximately 17 world-manager singletons cached.
2. **`BuildSceneGraph`** — creates:
   - Camera: `GPerspectiveCamera`, FOV 65°, near plane 5, far plane 15000.
   - Five `GViewPlatform` render targets plus a **reserved sixth slot** (allocated but never
     assigned — provision for a future mode not in this build).
   - `GScene` node labelled "charater scene" (literal label; typo is authentic).
   - `GSwitch` node for toggling render branches.
   - Five camera manipulators (Third / First / Static / Gamble / Event).
3. **Five+ render-callback slots installed** (render pass order — see §5.4.4).
4. **World services started**: environment/day-night driver, cursor/3D-marker service,
   per-frame update callback. HUD panel tree activated (community panel, character-billboard
   panel, link-combo panel, rank-progress panel, slot panels).

#### 5.4.2 Spawn from network — the two-form 4/1 handler (CODE-CONFIRMED)

World state and player spawn are both carried by S2C opcode **4/1** (GameStateTick), body
approximately 9100 bytes (0x238C). **The handler is two-form, selected by body byte 0:**

- **Form 0 — position / status update.** A lightweight update of the existing world (player and
  actor positions / status). No spawn, no terrain (re)init; early return after applying the update.
- **Form 1 — world-entry full materialize.** Copies the actor / world mirror blocks to their global
  tables, builds or repositions the local player, and performs the spawn sequence below. First-entry
  vs re-entry is distinguished by whether the local-player pointer is already set.

There is **no separate S2C 4/3 BillingInfo materialize** — the materialize lives entirely in form 1
of this 4/1 handler. (The billing S2C family is on **major 1**, not major 4 — see §5.4.6.)

In form 1, player world position is extracted from:
- X coordinate: at body offset +0x2374 (f32)
- Z coordinate: at body offset +0x2378 (f32)
- Y coordinate: **always forced to 0.0** — ground height is determined later by terrain sampling.

The packet also carries map/scenario code (body +0x00C), the day / hour-of-day fields, area id, and
bulk actor mirror blocks.

**Area selection by date/time (CODE-CONFIRMED):** before terrain init, form 1 calls a
select-area-by-date/time routine, passing the day / hour fields from the 4/1 body. The selected area
governs which environment/terrain set is brought in (this is why the same map code can resolve to a
different seasonal/time variant).

Immediately after reading spawn coordinates, `Terrain_InitFirstRing_3x3` is called, loading the
3×3 cell ring centred on the spawn position. Log line emitted: "first terrain init (x, z, area)".

If the first-entry actor create fails, the handler routes back to Character Select (state 4) — see
§4.3.

> Form-arrival ordering — whether a client first sees a form-1 materialize or a form-0 update on
> world entry, and the relative ordering against the major-1 billing family — is
> **(capture/debugger-pending)**.

**Spawn effects and audio** (CODE-CONFIRMED):

| Event | ID |
|-------|----|
| Materialise FX | 310000001 |
| Spawn SFX | 862010105 |
| Entry BGM | 910066000 |
| Tutorial area cue (map=1, tutorial_state=12) | 910001000 |

#### 5.4.3 Per-frame world update (CODE-CONFIRMED)

Inside the world's per-frame update callback (logic before render), execution order is:

1. Environment / light state update from player position (sun/light direction, camera-relative
   light globals, sky colour, fog density).
2. Cursor and 3D-marker position update.
3. Cursor hit-test against scene geometry (feeds click-to-move and UI).
4. Ambient sound driver + 1000 ms world-clock accumulator (§1.4 of sound spec):
   - Every 1000 ms: ambient slot re-evaluate.
   - Every 3000 ms: second ambient sweep.

#### 5.4.4 Render pass order (CODE-CONFIRMED)

Six scene render-callback passes plus UI, executed each frame in the following order:

| Pass | Role |
|-----:|------|
| A | Environment / day-night / shadow setup (pre-draw only; no geometry) |
| B | Sky dome / star dome / cloud geometry (Z-test off, camera-centred) |
| C | Terrain ground-shadow stamp + actor shadows |
| D | Opaque world cull-walk: terrain → solid geometry → skinned actors → billboards |
| E | FX terrain layers (`.fx1`–`.fx7`), water/animated surfaces, alpha decals |
| F | Post-scene transparent overlay: billboards, particles, additive glow |
| (toon path) | Glow extract → bloom blur → `finaldx8` composite (c0 = edge weight, c1 = bloom weight) |
| (last) | UI / HUD (2D widget tree, screen-ortho space) |

After the final pass: `IDirect3DDevice9::Present`.

> Earlier versions of this spec listed 5 render-callback slots at named object-field offsets.
> The authoritative render-pass order above supersedes that reading; the raw field offsets are
> dirty-room details not reproduced here. (CODE-CONFIRMED — see `specs/client_runtime.md §9.3`.)

#### 5.4.5 Data dependencies

- S2C 4/1 GameStateTick for spawn coordinates.
- Terrain cell files: `.ted`, `.map`, `.sod`, `.bud`, `.mud`, `.exd` (see `formats/terrain.md`).
- Character assets: `.skn`, `.bnd`, `.mot` (see `specs/skinning.md`).
- Environment bins: `map_option`, `fog`, `light`, `material`, `point_light`, `weather`,
  `clouddome`, `stardome` (see `specs/environment.md`, `formats/environment_bins.md`).
- Sound tables: `.bgm`, `.bge`, `.eff`, `.wlk`, `.run` per area (see `formats/sound_tables.md`).
- Area spawn files: `npc{tag}.arr` (28-byte records), `mob{tag}.arr` (20-byte records).
- Effect files: `.xeff` descriptors under `data/effect/xeff/` (see `formats/effects.md`).
- Effect manifests: `bmplist.lst`, `xobj.lst`, `xeffect.lst`, `totalmugong.txt`,
  `itemjointeff.txt`, `mobjointeff.txt`, `itemswordlight.txt`, `mobswordlight.txt`.
- `msg.xdb` for all in-world dialog and HUD text.

#### 5.4.6 Billing S2C family (CODE-CONFIRMED — corrected location)

Billing notices arrive on **S2C major 1** (not major 4, and not a 4/3 alternate spawn). The observed
minors are: subscription **deactivated**, subscription **activated**, **expiry notice**, and
**letter received**. These are presentation/notification messages handled alongside the world
opcodes; they do **not** drive a scene-state transition or a spawn. (Wire-level opcode bytes and the
exact minor assignments: capture/debugger-pending; the prose family is static-confirmed.)

---

## 6. Module chapters

Each section below summarises the module's role and key constants, then cross-links to the
authoritative satellite spec. Engineers implement from the satellite spec; this section provides
orientation only.

### 6.1 UI / GUI toolkit

> **Satellite spec:** `specs/ui_system.md`.

The widget system uses a family of classes prefixed `GU*` sharing a 15-slot vtable. Key slots:

| Vtable slot | Method |
|------------|--------|
| 0 | Destructor |
| 1 | `SetShown` |
| 5 | `HitTest` |
| 6 | `OnEvent` |
| 7 | `Draw` |
| 9 | `UpdateTransform` |
| 10 | `GetActionId` — returns field **+0x10** |
| 11 | `OnHoverEnter` |
| 12 | `OnHoverExit` |
| 13 | `RemoveMarkedChildren` |
| 14 | `BuildScene` |

Rendering: a single shared `ID3DXSprite` instance. Each widget carries a live src-RECT at object
offset +0x34 (in atlas pixels, updated before every blit) and a translation matrix at +0x44.
(CODE-CONFIRMED)

**Field offset corrections (from `specs/ui_system.md §1.2`):**
- `+0x0C` is the **tint/colour RGB** (low 24 bits), NOT the action id.
- `+0x10` is the **actionId** (integer action identifier fired on click-release).
This distinction is critical: code that reads +0x0C as an action id will route to the wrong
handler.

`GUButton` stores up to four `(srcX, srcY)` frame-origin pairs. All three constructor variants
(2-state, 3-state, 7-state) produce **at most 3 distinct sprite frames** (NORMAL, HOVER, PRESSED);
the DISABLED origin always equals NORMAL from the constructor. The "7-state" label is the
state-count field value, not a count of distinct sprites. (CODE-CONFIRMED — see `specs/ui_system.md §1.5`.)

Z-order: paint order is front-to-back (child-vector insertion order). Hit-test order is the
reverse (end → front, so the topmost-painted widget receives events first).

Alpha fade: ±64 units per tick for `GUComponent`; ±32 for `GUComponentEx`. Forced-alpha byte at
+0x0F bypasses the fade entirely (used for blackout overlays). (CODE-CONFIRMED)

Single global click-capture pointer for drag semantics.

Canvas hardcoded at 1024×768 logical pixels; coordinates are fixed, not resolution-scaled.

Lua binding: `LuaTinker` C++ binding layer connects the Lua 5.1.2 VM to UI event handlers and
config. 33 Lua-bound functions confirmed in binary. (CODE-CONFIRMED — see `specs/lua_scripting.md`.)

In-game windows bind atlas by integer `uitex.txt` texture-id, not by DDS string. Resolve each id
via `formats/ui_manifests.md` before implementing per-widget atlas regions.

### 6.2 Effects system

> **Satellite spec:** `specs/effects.md` (runtime instantiation, update, attachment, triggers).
> **Format spec:** `formats/effects.md` (`.xeff` / `.eff` on-disk layouts, `particleEmitter.eff`,
> `effectscale.xdb`).

Class hierarchy: `XEffect` (abstract base) → `UserXEffect` / `JointXEffect` / `MapXEffect` /
`CoreXEffect` (file-backed parsed record, not a live instance). Two separate sub-systems coexist:
`ParticleEffectManager` (activated by elements with `resource_id ≥ 10000`) and `SwordLightManager`
(weapon trails). All instances are pool-allocated (three confirmed pools; fourth type unresolved).
(CODE-CONFIRMED — see `specs/effects.md §2–§4`.)

Boot-time manifests loaded from `data/effect/` in this order:
1. `bmplist.lst` — texture name pool (`u32 count` + `count × char[30]`).
2. `xobj.lst` — XObj primitive mesh manifest (`u32 count` + `count × (u32 id, char[30] name)`).
3. `xeffect.lst` — effect-id registry (stub registration; **full parse is deferred** to first spawn).
4. `EffectCache_LoadIDs` — LRU pre-warm for frequently used effects.
5. `totalmugong.txt` — martial-arts skill sound overlay table.
6. `itemjointeff.txt` — item joint-effect binding table.
7. `mobjointeff.txt` — mob joint-effect binding table.
8. `itemswordlight.txt` / `mobswordlight.txt` — weapon-trail descriptors.

(CODE-CONFIRMED — see `specs/effects.md §3`; full manifest schemas in §13.)

Key trigger IDs (CODE-CONFIRMED — see `specs/effects.md §7` for the complete trigger dispatch table):

| Event | Effect ID |
|-------|----------|
| PC materialise (spawn in world) | 310000001 |
| Level-up | 310000002 |
| Mob spawn | 360000001 |
| Death (PvE kill) | 360000003 |
| Death (PvP kill) | 350000010 |
| Attack hit / generic (primary) | 350000021 |
| Attack hit (secondary) | 350000022 |
| Skill cast burst | 350000026 |
| Trade aura toggle | 350000063 |
| PvP death — stand phase | 371003701 |
| PvP death — fall phase | 371003702 |

`JointXEffect` attachment: `bone_source_enum` 0 = explicit bone index; 1/2 = action table
lookup (table contents unresolved). `quat_source_enum` 1 = bone quaternion; 2 = actor root
quaternion. (CODE-CONFIRMED — see `specs/effects.md §9`.)

Damage number rendering uses atlas files `att-font.dds` (normal hit), `cri-font.dds` (critical),
and `miss.tga` (miss). Vertex stride 96 bytes; 520-vertex buffer. (CODE-CONFIRMED — `specs/effects.md §10`.)

Per-frame tick: advances elapsed time, selects keyframe (frame_index = elapsed_ms / anim_stride_ms),
lerps velocity/size/alpha, builds geometry by emitter_type (0 = billboard, 1 = mesh-particle,
2 = directional billboard), submits to transparent draw queue. No per-particle back-to-front sort.
(CODE-CONFIRMED — see `specs/effects.md §8`.)

`effectscale.xdb` (per-effect scale override): 8-byte records (`u32 effect_id` + `f32 scale`).
Whether the override applies in addition to or instead of `CoreXEffect.scale_default` is an open
item (OQ-EFX-05 below).

### 6.3 Sound system

> **Satellite spec:** `specs/sound.md`, `specs/client_runtime.md §1`.
> **Format spec:** `formats/sound_tables.md` (`.bgm`, `.bge`, `.eff`, `.wlk`, `.run` table layouts).

Audio engine: DirectSound (IDirectSound3DListener for 3D spatialisation). Codec: Ogg Vorbis
(libVorbis 1.3.2, statically linked). (CODE-CONFIRMED)

Five PCM `WAVEFORMATEX` templates. Hard codec rule: **3D audio must be MONO; 2D audio must be
STEREO.** A 3D clip with the wrong channel count is rejected at load. (CODE-CONFIRMED)

Size gating: 512 KiB scratch buffer. 3D sounds that overflow are silently dropped; 2D sounds
that overflow switch to a 1 MiB streaming ring (the BGM mechanism). (CODE-CONFIRMED)

Volume curve: `0.0 → −10000` millibels (silence); otherwise a steep nested-log taper.
(CODE-CONFIRMED — endpoints certain; exact log base/units approximate.)

Four named volume buses: music / terrain / character / mob. Two BGM track IDs exempt from the
music slider: 861010109 and 861010110. (CODE-CONFIRMED)

Ambient driver: throttled 600,000 ms minimum between forced area re-picks; per-mud-cell byte
lookup; hour-gated on the simulated day-night clock. Three ambient buses:
BGM (`.bgm` slot, mud-cell +0x02), looped ambient (`.bge` slots, mud-cell +0x03/+0x04),
3D point sources (`.eff` sound-table slots, mud-cell +0x05/+0x06/+0x07, volume ×0.7).
Indoor override BGM: 863500002. (CODE-CONFIRMED)

Footstep source: actor visual fields at offsets +108/+112. **Not** derived from the mud-cell byte.
(CODE-CONFIRMED — this corrects a common assumption.)

Async worker: 9-operation event queue (LOAD / DELETE / PLAY / PLAY2D / PLAY3D / STOP /
SET_VOLUME / RESET / CHANGE_STREAM), streaming refill polled every ~200 ms with Sleep(100).
(CODE-CONFIRMED)

### 6.4 Network runtime

> **Satellite spec:** `specs/client_runtime.md §network`, `specs/handlers.md`.

NetClient singleton label: `"Diamond_Network"`. 188 network-handler functions in binary.
(CODE-CONFIRMED)

Three cooperating threads (CODE-CONFIRMED):

| Thread | Role |
|--------|------|
| IO-completion | `WSAWaitForMultipleEvents`, overlapped recv/send, raw frame enqueue |
| Network worker | Recv-queue pop → message-bus hop → handler dispatch |
| Keepalive | Sends C2S **2/112** ping (1-byte payload) every ~20 s; gated by a master-enable global (see below) |

**Keepalive opcode (CODE-CONFIRMED — corrected):** the keepalive is **C2S 2/112** (major 2,
minor 112 = 0x70) with a **1-byte payload**, NOT 2/10000. It is governed by a dedicated
**master-enable global**: the sender enables the keepalive on **World entry** (state 5 case body)
and disables it on **world leave / logout**. This master-enable is a *distinct* mechanism from the
per-send suppress byte at NetClient object offset +82364 (that byte is set by every C2S send and
cleared at the start of the 4/1 and 3/1 handlers). (Wire-level: the 2/112 opcode is static-confirmed
from the send path; an on-the-wire capture remains capture/debugger-pending.)

All handler dispatch happens on the **frame thread** (message-bus hop), not on the IO thread.
(CODE-CONFIRMED — handlers may read/write game state without additional locks.)

Frame receive: raw frames queued from IO thread; decompressed in the dispatcher on the frame
thread. Compression: LZ4 (statically linked).

Game socket lifecycle: opened at Server Select (State 4 flow), kept open across Load and World
states, only closed on quit or hard error. (CODE-CONFIRMED)

Game socket `SO_RCVBUF`: probed 5 times after connect; initial value 24,576 bytes. (CODE-CONFIRMED)

Disconnect path: NULL sentinel → message-bus command 102 → scene-fallback → dialog msg 9025 →
state write → break. (CODE-CONFIRMED)

### 6.5 Resource / loading pipeline

> **Satellite spec:** `specs/resource_pipeline.md`.

Single file-open router with three resolution paths (CODE-CONFIRMED):
1. VFS path (binary search over sorted 144-byte TOC) — used when `vfsmode = true`.
2. Override filesystem path — developer hotswap.
3. Raw filesystem path — fallback.

**No file-level cache**: every `DiskFile` open is a `malloc` + `ReadFile`. Caching is the
responsibility of each subsystem's own cache layer. (CODE-CONFIRMED)

Boot loader (`BulkAssetLoader_Thread`): loads approximately 50 tables in a hardcoded order,
then sleeps 500 ms before clearing its completion flag. Progress denominator: 9,395,240 bytes
(CODE-CONFIRMED, hardcoded constant). Loading screen displays one of several DDS backgrounds;
see `specs/ui_system.md §9.4` for the confirmed file list (`loading.dds`, `loading01.dds`–
`loading08.dds`), `loadingbar.dds` (256×256). Sleep(100) per frame (~10 FPS during load).

Subsystem caches: lazy find-or-load on first access; never evicted during a scene session;
torn down during scene teardown. Uses D3DPOOL_MANAGED for GPU textures. (CODE-CONFIRMED)

Terrain streaming (CODE-CONFIRMED):
- Synchronous 3×3 ring load (`Terrain_InitFirstRing_3x3`) at spawn — runs inline in the spawn
  handler (confirmed by log line); whether per-cell load blocks on VFS or queues to the async
  worker is untraced (see OQ-WORLD-02).
- Async streamer thread: FIFO queue; initial Sleep(4,000 ms) after spawn; then Sleep(3,000 ms)
  between batches; Sleep(10 ms) polling when queue empty.
- Shared cell lock serialises main thread and streamer thread on per-cell access.

Area inventory: 63 areas (IDs 0–47, 100, 201–210, 300), approximately 2,505 cells total.
Full per-area file-coverage census: `specs/resource_pipeline.md §area_inventory` and
`formats/area_inventory.md`. (SAMPLE-VERIFIED)

### 6.6 Environment / day-night cycle

> **Satellite spec:** `specs/environment.md`, `formats/environment_bins.md`.

Day/night cycle (CODE-CONFIRMED):
- 48 keyframes, each 1,800 ms → full simulated day = 86,400 ms (86.4 s).
- Clock is server-synced via S2C opcode **5/18** (`SmsgGameClockUpdate`, 8-byte body).
- Clock tick is a pre-draw callback inside render pass A, gated ≥ 50 ms per tick (shared with
  terrain water UV-scroll animation — both run at ~20 Hz maximum cadence).
- Odd tick → sky/day-night driver (keyframe lerp → sun direction, sky colour, fog, ambient light).
- Even tick → weather/cloud branch.
- Weather re-check every **120 ticks** (~6 seconds at 20 Hz).

Sky and environment bins per area: `map_option`, `fog`, `light`, `material`, `point_light`,
`weather` exist for all 63 areas. `clouddome` and `stardome` are absent for indoor/dungeon areas.
`.up` (upper terrain layer) exists only in 17 water/indoor areas. (SAMPLE-VERIFIED)

### 6.7 Terrain streaming

> **Satellite spec:** `specs/resource_pipeline.md §terrain_streaming`, `formats/terrain.md`.

Coordinate system: cells are 1,024 units wide/deep on a 65×65 cell grid with 16-unit vertex
spacing. Cell-grid index: `10000 − floor(coord × −1 / 1024)`. Ground height for world (X, Z) is
computed by bilinear interpolation over the `.ted` 65×65 float heightmap (vertex spacing 16.0
units). No-terrain sentinel: −3.4028×10³⁸. (SAMPLE-VERIFIED)

World Z is negated relative to Godot Z: `(x, y, z) → (x, y, −z)` for engine coordinates.
Mesh-local `.skn` geometry negates X. (SAMPLE-VERIFIED)

Walkability: `.tol` files exist only for areas 9, 13, and 100. Area 100 (training) is a 1-cell
area with a 256×256 `.tol` grid. `.sod` files contain 2D XZ wall segments (108-byte per-solid
header, 48-byte segment records) in a 16×16 cell quadtree. (SAMPLE-VERIFIED)

### 6.8 Actor / skinning pipeline

> **Satellite spec:** `specs/skinning.md`, `formats/mesh.md`, `formats/animation.md`.

88 actor functions in binary. (CODE-CONFIRMED)

Asset resolution chain (CODE-CONFIRMED / SAMPLE-VERIFIED):
- `.skn` field `IdB` → `data/char/bind/g{IdB}.bnd` (bind-pose skeleton).
- `.skn` field `IdA` → `data/char/skin.txt` col4 → col5 `tex_id` →
  `data/char/tex{512512|10241024|…}/{id}.png` (character texture).
- Idle motion: `data/char/actormotion.txt` (col2 == IdB → col16) →
  `data/char/mot/g{id}.mot`.
- Mob → skin chain: `mob_id` → `actormotion.txt` col1 → col2 `skin_class` →
  `g{skin_class}.bnd` and the `.skn` whose `IdB == skin_class`.

Skinning math: CPU linear blend skinning (LBS). Rest-pose residual ≤ 1×10⁻⁶ in the reference
implementation. Bind pose is an inverse-bind transform baked at `.bnd` load time. Full deformation
equation and quaternion conventions in `specs/skinning.md`.

Skinned-actor vertex stride: **32 bytes** (XYZ + normal + UV). One composite WVP matrix uploaded
to vertex-shader constant c0. Toon shader bound per actor (§3.6 of `specs/client_runtime.md`).

World character coordinates: mesh-local X negated; world Z negated (see §6.7 above).

### 6.9 Anti-cheat presence

The binary exports a function `fcEXP`, which is the **XTrap** anti-cheat module's entry point.
(CODE-CONFIRMED — export is present in the PE export directory.) XTrap is loaded and invoked at
process startup before the game loop begins. Its specific behavioural contract is outside the
scope of clean-room RE. No implementation action is required or appropriate in the clean-room
revival (the revival runs without XTrap).

---

## 7. Module interconnection matrix

Rows = initiating module. Columns = target module. Cell = occasion / event that causes the
interaction. Empty cells = no direct interaction observed.

| Initiator ↓ \ Target → | UI/GU* | Effects | Sound | Network | Resource | Environment | Terrain | Actor/Skin | Anti-cheat |
|------------------------|--------|---------|-------|---------|----------|-------------|---------|------------|------------|
| **Scene machine** | Constructs scene handlers (Login/Select/World); widget trees via uiconfig.lua | — | Entry BGM trigger (910066000) | State transitions via S2C packets | BulkAssetLoader on states 2 → 4/5 | — | — | Preview in char-select | XTrap gate at state 1 |
| **Frame loop** | HitTest + Draw each frame | Per-frame tick (elapsed-ms / keyframe lerp / geometry build) | Ambient clock tick (1000/3000 ms accum) | Keepalive thread (C2S 2/112, parallel; master-enable global) | — | Pre-draw day/night tick (pass A, ≥50 ms gate) | — | LBS deform + animate | — |
| **UI/GU*** | — | Button hover FX (PLAUSIBLE) | Login SFX on sub-states (e.g. 861010105 at sub-state 2) | Login/select/chat packets sent on button actionId | msg.xdb string load | — | — | 3D preview render (GUCanvas3D) in char-select | — |
| **Effects** | Damage number overlay draw | — | SFX trigger on effect events (totalmugong.txt) | — | Lazy-load xeffect.lst, bmplist.lst, xobj.lst | — | MapXEffect world-space placement | JointXEffect bone attachment (bone_source_enum) | — |
| **Sound** | Volume slider UI binding | — | — | Clock sync via 5/18 (game-hour → ambient re-eval) | Load .bgm/.bge/.eff/.wlk/.run tables | Day/night hour gates ambient re-pick | Mud-cell ambient lookup (offsets +0x02–+0x07) | Footstep SFX from actor visual fields +108/+112 | — |
| **Network** | Disconnect dialog (msg 9025) | Spawn effects via trigger table | Spawn SFX (862010105) | — | — | Clock sync push (5/18) | — | Spawn from 4/1 GameStateTick (+0x2374/+0x2378) | — |
| **Resource** | — | Effect texture load; xeffect.lst manifest | Sound table load (.bgm etc.) | — | — | Environment bin load | .ted/.map/.sod/.bud cell load | .skn/.bnd/.mot load | — |
| **Environment** | — | — | BGM slot drive (mud-cell +0x02) | Clock sync receive (5/18) | Bin file load (fog/light/material/weather) | — | Sky/fog render before terrain pass (pass A/B) | Ambient light affects actor toon shader (c4–c10) | — |
| **Terrain** | — | MapXEffect placement on terrain | Ambient zone lookup (mud-cell bytes) | — | Cell streaming FIFO queue | Heightmap Y sampling (visual, not physics) | — | Ground-height Y clamp (terrain +3.8) | — |
| **Actor/Skin** | HP/name HUD data | JointXEffect bone attachment | Footstep/combat SFX (+108/+112) | Recv 4/1 (spawn), 5/* (move/anim) | .skn/.bnd/.mot loads | Light/fog toon uniforms (c4–c10) | Ground-height Y clamp; 3×3 ring at spawn | — | — |

---

## 8. Engine identity

These facts about the underlying technology are code-confirmed (extracted from embedded strings,
import tables, and structure signatures — no pseudo-code):

| Property | Value | Confidence |
|----------|-------|-----------|
| Engine name | **Diamond** | CODE-CONFIRMED — source path `d:\build\projects\do_korea_service_dx9\src\diamond\dGVector.h` in binary |
| Project slug | `do_korea_service_dx9` | CODE-CONFIRMED — same embedded path |
| Window class name | `"diamond engine application"` | CODE-CONFIRMED |
| Window title | `"Do"` | CODE-CONFIRMED |
| Compiler | MSVC 2005 (Visual C++ 8.0) | CODE-CONFIRMED — CRT / RTTI signatures |
| Graphics API | Direct3D 9 (D3D9), HAL device, hardware vertex processing + multi-threading, IMMEDIATE present | CODE-CONFIRMED |
| Back-buffer format | X8R8G8B8 | CODE-CONFIRMED |
| Depth format | D32 → D24X8 → D24S8 → D16 (fallback chain) | CODE-CONFIRMED |
| Scripting VM | Lua 5.1.2 (statically linked) | CODE-CONFIRMED — 33 bound functions, version string in binary |
| Lua binding layer | LuaTinker (C++ template bridge) | CODE-CONFIRMED |
| Packet compression | LZ4 (statically linked) | CODE-CONFIRMED |
| Audio codec | Ogg Vorbis, libVorbis 1.3.2 (statically linked) | CODE-CONFIRMED |
| Audio output | DirectSound (DirectSoundCreate, IDirectSound3DListener) | CODE-CONFIRMED |
| Korean text input | IMM32 IME (19 IME functions in binary) | CODE-CONFIRMED |
| Korean text encoding | CP949 (MS-949 code page) | SAMPLE-VERIFIED |
| Anti-cheat | XTrap (fcEXP export present) | CODE-CONFIRMED |
| Total binary functions | ~25,973 (2,904 named) | CODE-CONFIRMED — IDA function count |

---

## 9. Open questions register

Questions are grouped by area. Each entry states what is unknown, why it matters for
implementation, and which satellite spec owns the investigation.

### 9.1 Protocol / opcodes

**OQ-PROTO-01 — C2S 1/6 opcode collision. RESOLVED.**
There is no collision: C2S 1/6 is create-character (52-byte payload). The login credential path uses
the tab-separated key-string secure-context handoff (§5.1.2), not a 1/6 packet. The earlier
"login operation at 1/6" was a mis-annotation. (Closed by the 2026-06-16 re-confrontation.)

**OQ-PROTO-02 — Auth result wire format. (capture/debugger-pending)**
The login credential submission is statically traced as a tab-separated key-string consumed by the
secure-context builder (§5.1.2); success surfaces as `EnterGameAck` (S2C 3/5). The exact *on-the-wire*
byte form of the credential submission — including whether a literal C2S 2/1 packet is emitted — and
the field offsets of the auth result blob are not recoverable statically. Needs a live capture.
Owned by `packets/s2c_auth.yaml` + `packets/c2s_login.yaml` (pending).

**OQ-PROTO-03 — 4/1 form-arrival ordering. (capture/debugger-pending)**
The premise of an alternate S2C 4/3 BillingInfo materialize is dissolved: the materialize lives
entirely in **form 1 of the two-form 4/1 handler** (§5.4.2), and billing S2C is on major 1 (§5.4.6).
What remains open is the *runtime arrival ordering* on world entry — whether a client first observes
a form-1 materialize or a form-0 update, and how the major-1 billing notices interleave with it.
Needs a live capture. Owned by `specs/client_runtime.md §9.5` / `packets/s2c_billing.yaml` (pending).

**OQ-PROTO-04 — Keepalive gating mechanism. (mostly resolved; residual capture-pending)**
The keepalive opcode is C2S **2/112** (1-byte payload), gated by a **master-enable global** set on
World entry and cleared on world leave (§6.4). A *separate* per-send suppress byte at NetClient
object offset +82364 is set by every C2S send and cleared at the start of the 4/1 and 3/1 handlers.
What is not statically settled is whether any server-side instruction also influences these flags.
Owned by `specs/client_runtime.md §network`.

### 9.2 Scene machine / UI

**OQ-SCENE-01 — `OPENNING/SKIP` INI file path.**
The key is code-confirmed (section `[OPENNING]`, key `SKIP`, with the authentic double-N typo),
but the literal filename of the INI that holds it was not resolved in the analysis pass
(the path is built from a settings-object internal buffer, not a hardcoded string). Owned by
`specs/frontend_scenes.md`.

**OQ-SCENE-02 — SelectWindow back-button re-login path.**
Pressing the "back/leave" command in Character Select sends a network leave request; the server
reply drives the state change. It is not confirmed whether the game socket closes and reopens,
nor whether a logout packet is sent. Owned by `specs/frontend_scenes.md`.

**OQ-UI-01 — 1024×768 canvas scaling on higher resolutions.**
The canvas is hardcoded 1024×768. How the client handles running at 1280×1024 or 1920×1080
(stretch? letterbox? clamp at 1920?) is not confirmed. Owned by `specs/ui_system.md §scaling`.

**OQ-UI-02 — In-game HUD atlas-name join.**
115 of 117 in-game window builders bind atlas by integer `uitex.txt` texture-id, not by DDS
string. Resolving each window's per-widget atlas name requires the `uitex.txt` id→DDS manifest.
The destination rects and 4-frame src origins are recoverable from the binary now; only the DDS
name resolution is gated on `formats/ui_manifests.md`. **Implementation blocker for all in-game
HUD sub-windows.** Owned by `specs/ui_system.md §12.6` and `formats/ui_manifests.md`.

### 9.3 Resource / loading

**OQ-RES-01 — BulkAssetLoader exact table list.**
The ~50 tables loaded at states 2/4 are noted as "approximately 50, hardcoded order" but the
exact ordered list has not been promoted to a committed spec. Important for reproducing the
loading-screen progress bar denominator correctly. Owned by
`specs/resource_pipeline.md §boot_loader`.

**OQ-RES-02 — Override filesystem path rules.**
The second DiskFile resolution path (developer hotswap) is code-confirmed to exist but the root
path and precedence rules are not committed. Owned by `specs/resource_pipeline.md §vfs_router`.

**OQ-RES-03 — Cell eviction on scene teardown.**
The subsystem caches are torn down on scene teardown, but whether this is a synchronous flush or
an async deferred teardown is not confirmed. Relevant for the streaming thread join ordering.
Owned by `specs/resource_pipeline.md §streaming`.

### 9.4 Audio

**OQ-SND-01 — Indoor ambient BGM 863500002 trigger condition.**
The indoor override BGM ID is code-confirmed, but the exact condition that triggers the
indoor/outdoor swap (building bounding box? `.up` layer presence? special cell flag?) is not
confirmed. Owned by `specs/sound.md §ambient`.

**OQ-SND-02 — Footstep actor fields +108/+112.**
The footstep SFX reads from actor visual object fields at offsets +108 and +112. What these
fields contain (terrain-type enum? surface material?) and how they are set is not documented.
Owned by `specs/sound.md §footstep`.

### 9.5 Effects

**OQ-EFX-01 — Fourth effect pool type.**
The fourth pool (Pool D) type was not identified; candidates: a local-player-only subtype or an
absolute-world subtype. Owned by `specs/effects.md §4`.

**OQ-EFX-02 — `bone_source_enum` action tables A and B.**
Modes 1 and 2 perform table lookups; the tables themselves are not recovered. Likely per-action
effect-slot tables inside the actor structure, possibly linked to animation event descriptors.
Cross-reference `specs/skinning.md` for bone-mapping tables. Owned by `specs/effects.md §9`.

**OQ-EFX-03 — `MapXEffectManager` per-area manifest.**
When a player enters a new area, `MapXEffectManager` loads area-specific ambient effects. The
format of the per-area map-effect manifest (if any) was not traced. Owned by `specs/effects.md §5`.

**OQ-EFX-04 — `CoreXEffect.loaded_flag` race condition.**
The lazy-parse flag is checked at first spawn. With multiple actors potentially spawning in the
same frame, is there a race? No visible lock was found; thread affinity of the first-spawn path
was not confirmed. Owned by `specs/effects.md §5.1`.

**OQ-EFX-05 — `effectscale.xdb` application semantics.**
Whether the per-effect scale override is applied in addition to, or instead of,
`CoreXEffect.scale_default` during the effective-scale computation was not confirmed.
Owned by `specs/effects.md §14.9` / `formats/effects.md §D`.

**OQ-EFX-06 — `field_unknown_a` in `.xeff` element block.**
One per-element field in the `.xeff` sub-effect block layout remains unresolved in meaning.
Owned by `formats/effects.md §A.5`.

**OQ-EFX-07 — `particleEmitter.eff` record layout.**
File header u16 and record-stride hypothesis are PLAUSIBLE from sample observation; internal
field layout is UNVERIFIED. Do not implement the parser until confirmed. Owned by
`formats/effects.md §E.2`.

**OQ-EFX-08 — SwordLight trail geometry generation.**
The per-frame ribbon construction (bone position + color-offset → screen-space ribbon vertices)
was not traced. Trail width, fade duration, vertex count are entirely UNVERIFIED.
Owned by `specs/effects.md §12.3`.

### 9.6 World / environment

**OQ-ENV-01 — Y=0 spawn and terrain race.**
Player spawn always sets Y = 0.0; ground height comes from terrain sampling after
`Terrain_InitFirstRing_3x3`. Whether `Terrain_InitFirstRing_3x3` runs synchronously (blocking
VFS) or queues to the async worker is untraced; this governs the NPC ground-placement race.
Owned by `specs/client_runtime.md §9.5` / `specs/resource_pipeline.md §streaming`.

**OQ-ENV-02 — Water renderer method.**
The `.up` upper-terrain layer and the `WaterRenderer` are present in the binary. The original
engine's water-render method (projected planar reflection? UV-scroll only?) is not yet
reverse-engineered. Owned by `specs/environment.md §water`.

**OQ-WORLD-01 — Area ID 201–210 (dungeon) cell counts.**
Per-area cell counts for the dungeon range are marked partial in
`specs/resource_pipeline.md §area_inventory`. Total dungeon cell count affects streaming queue
sizing. Owned by `formats/area_inventory.md`.

**OQ-WORLD-02 — 3×3 ring: VFS-blocking vs async.**
Whether `Terrain_InitFirstRing_3x3` blocks the main thread on VFS reads or queues work to
the async asset thread is untraced. This is the root cause of the NPC spawn-at-fallback-Y race
noted in `CLAUDE.md §Debts`. Owned by `specs/resource_pipeline.md §streaming`.

### 9.7 Network threading

**OQ-NET-01 — Handler dispatch thread identity.**
All game-logic packet handlers dispatch on the frame thread (message-bus hop confirmed). It is
not confirmed that ALL opcodes follow this path — some emergency disconnect handling may
short-circuit directly on the IO thread. Owned by `specs/handlers.md`.

---

## Cross-references

- Engine runtime constants and full INI table: `specs/client_runtime.md`
- Frame loop timing and day/night details: `specs/game_loop.md`, `specs/client_runtime.md §8`
- Scene lifecycle (deep): `specs/frontend_scenes.md`
- Login flow (sub-states 2–41): `specs/login_flow.md`
- UI widget system (full toolkit + all screen layouts + corrections): `specs/ui_system.md`
- Effects runtime (spawn, attach, trigger, tick): `specs/effects.md`
- Sound runtime: `specs/sound.md`
- Environment/day-night: `specs/environment.md`
- Resource/loading pipeline: `specs/resource_pipeline.md`
- Skinning math: `specs/skinning.md`
- Opcode catalogue: `opcodes.md`
- Packet wire layouts: `packets/*.yaml`
- Singleton construction: `structs/runtime_singletons.md`
- Asset formats:
  - `formats/pak.md` — VFS container
  - `formats/terrain.md` — `.ted`, `.map`, `.sod`, `.bud`, `.mud`
  - `formats/mesh.md` — `.skn`
  - `formats/animation.md` — `.mot`, `.bnd`
  - `formats/environment_bins.md` — sky/fog/light bins
  - `formats/sound_tables.md` — `.bgm`, `.bge`, `.eff` (sound), `.wlk`, `.run`
  - `formats/effects.md` — `.xeff`, `.eff` (geometry), `particleEmitter.eff`, `effectscale.xdb`
  - `formats/ui_manifests.md` — `uitex.txt`, `skillicon.txt`, `crestlist.txt`
  - `formats/area_inventory.md` — per-area cell census
- Canonical glossary: `Docs/RE/names.yaml`
- Provenance audit trail: `Docs/RE/journal.md`
