---
verification: confirmed (re-confirmed against IDB SHA 263bd994, CYCLE 7 (2026-06-20))
ida_reverified: 2026-06-27   # CYCLE 14 re-anchor (f61f66a9): confirmatory — VFS mount / boot config / Hangul font slots cleanly relocated, 2 re-confirmed SAME, 0 corrected; prior 2026-06-22: CYCLE 12 Phase 0 3/100 select-mode transition correction (build 263bd994); prior 2026-06-18: scene re-confirmation campaign (build 263bd994)
ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
evidence: [static-ida, vfs-sample]   # sound tables, toonramp LUT, npc.arr, .sod/.ted byte-samples corroborate; the boot/loop/scene-machine/world-scene backbone is static-IDA CODE-CONFIRMED
conflicts: frame-limiter target-FPS source RESOLVED (display FRAMERATE config is dead — two writers, zero readers — cap is hardcoded 60.0; CYCLE 7); data.inf entry-count header offset (+0x0C, VFS byte-witness pending in formats/pak.md); reserved 6th view-platform slot (not in the world builder); GameTime opcode-5/18 apply site; per-area sky-rate floats; 200 ms move heartbeat hard-cap — remaining items (capture/debugger-pending)
status: confirmed
sample_verified: partial   # sound tables, toonramp LUT, npc.arr, .sod/.ted bytes verified; runtime logic IDA-derived; boot/loop/scene-machine/world-scene CODE-CONFIRMED
subsystems: [boot_startup, frame_loop, memory_allocator, sound_runtime, ui_system, render_pipeline, camera_constants, movement_constants, quest_data, scene_lifecycle, world_scene]
networked: partial         # sound/UI/render/camera are client-only; movement uses 2/13 & 5/13; quest uses 2/28, 5/68, 5/73; scene-machine uses 3/5, 3/1, 4/1, 4/3 (cataloged elsewhere)
encoding_note: Korean in-game/config/dialog text is CP949 (legacy MS949 code page), not UTF-8.
---

# Client Runtime Subsystems — Clean-Room Specification

> Neutral, rewritten behavioural specification. No legacy symbols, no addresses, no
> pseudo-code, no decompiler identifiers. Promoted from dirty-room analyst notes by the
> spec-author: every behaviour and constant below is re-expressed in this author's own words
> and tables. It documents the **presentation-and-client-runtime** side of the legacy client
> (boot and startup, per-frame loop, sound, UI widget tree + Lua binding, the "Diamond" render
> pipeline, the five camera modes, movement/collision tuning, the quest system, the master scene
> lifecycle, and the world-scene entry and render order) so the .NET core
> (`Client.Domain` / `Client.Application` / `Client.Infrastructure`) and the Godot
> presentation layer can be reimplemented from scratch.
>
> **Cross-spec ownership.** Where a topic already has its own committed spec, that spec is the
> authority and this file only summarises and cross-links:
> - Camera view-mode behaviour and movement pipeline: `specs/camera_movement.md`.
> - Lua VM version and `.lua` data-tree model: `specs/lua_scripting.md`.
> - Per-frame loop ordering (reference spec): `specs/game_loop.md`.
> - Sound on-disk table format: `formats/sound_tables.md`.
> - Shader file format: `formats/shaders.md`; terrain on-disk: `formats/terrain.md`.
> - Quest packet shapes (2/28, 5/68, 5/73): `opcodes.md` + `packets/*.yaml` + `specs/quests.md`.
> - Front-end scenes (login/select/create UI flow, sub-state machines, enter-world handoff):
>   `specs/frontend_scenes.md`.
> - Resource and loading pipeline (boot-load worker, loading-screen progress, terrain streaming):
>   `specs/resource_pipeline.md`.
>
> This spec adds the *runtime engine behaviour and pinned constants* that those specs reference
> but do not themselves enumerate. Packet byte layouts are never re-derived here.

---

## Status and verification banner

Per-section confidence model (used inline below):

- **(confirmed)** — re-derived from behaviour with corroborating static evidence; safe to implement.
- **(CODE-CONFIRMED)** — value lifted directly from binary immediate operands and confirmed by
  control-flow context; same strength as confirmed, used in §4 and §7 for Mission-E additions.
- **(sample-verified)** — additionally cross-checked against real shipped asset bytes (sound
  tables, `toonramp.bmp`, `.sod`/`.ted`, `npc.arr`); the strongest tier.
- **(plausible)** — single-source behavioural inference; implement but keep tunable / behind a flag.
- **(UNVERIFIED)** — hypothesis only; do **not** hard-code. Re-listed in each section's open list.

All numeric constants below were lifted from the legacy binary's literal pool / immediate operands.
They are reliable as *values present in the code* but, absent a live capture, treat the feel-governing
ones (volume curve, smoothing, send cadence) as a faithful starting tune to expose as configuration.

---

# 0. Boot and startup — (CODE-CONFIRMED)

The client is a single-threaded-driver Win32 application. Boot is **not** a monolithic init function —
it is spread across two distinct construction tiers followed by the state-0→1 transition inside the
main state-machine loop.

## 0.1 Top-level boot timeline — (CODE-CONFIRMED)

The ordered sequence from process start to the first interactive frame:

1. **CRT static initializers.** The CRT startup routine runs all C++ static constructors before
   transferring to the application entry point (`WinMain`). The most significant static object
   registered here is a **billing/anti-cheat scheduler proxy** that gates engine entry (see §0.5). There
   is **no `setlocale` or CP949 locale call anywhere in the boot path** — see §0.6.

2. **Power/termination setup.** The very first init step is a `SetThreadExecutionState` call with
   `ES_DISPLAY_REQUIRED | ES_CONTINUOUS` — it keeps the display awake for the duration of the
   process. The second init step installs a **C++ terminate-handler** (two handler installs follow).

3. **Bootstrap config (`game.lua`).** A Lua config singleton loads `game.lua` and reads three boolean
   globals that control fundamental engine behaviour. All three **default to `true`** if `game.lua`
   fails to load or is absent:

   | Lua global | Default | Effect |
   |---|---|---|
   | `vfsmode` | true | `true` → assets served from the packed VFS; `false` → loose files |
   | `launcher` | true | `true` → require the external launcher unless `-Start` was passed |
   | `debugmode` | true | stored in the engine-state struct (+0x0C byte); gates developer behaviour |

4. **VFS mount flag.** A global "mounted" byte is set from the `vfsmode` value. The VFS archive
   itself is opened just before the state loop (step 7).

5. **Crash reporter.** A **third-party crash-reporting SDK** is initialised: it installs an
   unhandled-exception (SEH) filter, opens a crash log, and is configured with an application
   identity and a remote crash-submission endpoint (see §0.10). It is a **crash-reporting** facility,
   **not** a frame profiler. An initial start entry is written to its log, and it loads a DirectX
   symbol-index file (an on-disk `symindex_dx9`-style index) used to resolve symbols for the client
   executable and its PDB.

6. **Launcher gate.** If `launcher` is `true` **and** the command line is **not** exactly `-Start`,
   the engine does **not** boot itself — it launches the external updater/launcher program
   (`dostart.exe`) via `WinExec` and returns immediately. The entire scene machine only runs when
   the client is invoked with `-Start` (or when `launcher` is set to `false`).

7. **VFS archive open.** The index file (`data.inf`) and the data archive (`data/data.vfs`) are
   opened and the table-of-contents is loaded into memory (see §0.4). This happens **once**, immediately
   before the state loop.

8. **State-machine loop begins.** `WinMain` **is** the state machine — the application entry point
   body is an infinite `while(1) switch(engineState)` over **exactly 8 cases (states 0..7)** plus a
   `default`. (The switch is bounds-checked `<= 7` against an 8-entry jump table; the value `8` is a
   *sub-state* value, not a 9th top-level case — see §7.) It starts at **state 0 (Initialisation)**,
   which transitions immediately to **state 1 (LoginWindow)** — the first real interactive screen.

## 0.2 `DoOption.ini` — persistent settings — (CODE-CONFIRMED)

The settings object default-constructs to **1024 × 768**, with a colour-depth field defaulting to
32-bpp, every visual-quality slider = 1, every sound volume = 100, and is then immediately overwritten
from INI.

> **Active colour depth is forced to 16-bit (CODE-CONFIRMED).** Despite the 32-bpp value in the
> settings object's colour-depth field, the engine **forces the active colour depth to 16-bit** during
> Init (§0.8 step 4), and the D3D9 device-creation path uses 16-bit. Do not assume 32-bit colour from
> the settings default; the device runs 16-bit.

**Five INI files** are located in the EXE directory at startup and their paths are stored for later
access. All five are marked hidden via `SetFileAttributesA`:

| File | Role |
|---|---|
| `DoOption.ini` | Master display / sound / quality settings (`[DO_OPTION]` section) |
| `option.ini` | Additional option storage |
| `panel.ini` | UI panel positions (see §2.5) |
| `combo.ini` | Gameplay-side combo/key bindings |
| `TSIDX.ini` | Tutorial state index |

The `[DO_OPTION]` key map — all keys read via `GetPrivateProfileIntA` (one consolidated loader, not
a per-key family), then range-clamped. `OPTION_ID` is the only string key.

| INI key | Default | Clamp | Object field index | Role |
|---|---:|---|---:|---|
| `OPTION_WIDTH` | 1024 | 800..1920 else 1024 | 0 | render width |
| `OPTION_HEIGHT` | 768 | 600..1200 else 768 | 1 | render height |
| `OPTION_COLORBIT` | 32 | 16 or 32 | 2 | colour depth |
| `OPTION_LANG` | 1 | 1..3 | 3 | language id |
| `OPTION_VIEW_CHAR` | 1 | 1..3 | 4 | draw-distance: characters |
| `OPTION_VIEW_BACK` | 1 | 1..3 | 5 | draw-distance: background |
| `OPTION_GROUND` | 1 | 1..3 | 6 | ground quality |
| `OPTION_SKY` | 1 | 1..3 | 7 | sky quality |
| `OPTION_WEATHER` | 1 | 1..3 | 8 | weather quality |
| `OPTION_WATER` | 1 | 1..3 | 9 | water quality |
| `OPTION_SHADOW` | 1 | 1..3 | 10 | shadow quality |
| `OPTION_DMGTEXT` | 1 | 1..3 | 11 | damage-text mode |
| *(forced)* | 1 | = 1 | 12 | **toon/cel-shading enable** (gates the bloom path — see §3.1) |
| `OPTION_TEX_CHAR` | 1 | 1..5 | 13 | character texture bucket |
| `OPTION_TEX_MOB` | 1 | 1..5 | 14 | mob texture bucket |
| `OPTION_TEX_ITEM` | 1 | 1..5 | 15 | item texture bucket |
| `OPTION_TEX_ETC` | 1 | 1..5 | 16 | misc texture bucket |
| `OPTION_SOUND_CHAR` | 1 | 1..2 | 17 | character-sound enable |
| `OPTION_SOUND_TERRAIN` | 1 | 1..2 | 19 | terrain-sound enable |
| `OPTION_SOUND_MUSIC` | 1 | 1..2 | 20 | music enable |
| `OPTION_SOUNDVOL_CHAR` | 100 | 0..100 | 21 | character volume |
| `OPTION_SOUNDVOL_MOB` | 100 | 0..100 | 22 | mob volume |
| `OPTION_SOUNDVOL_BACK` | 100 | 0..100 | 23 | ambient volume |
| `OPTION_SOUNDBOL_MUSIC` *(sic)* | 100 | 0..100 | 24 | music volume |
| `OPTION_STALL_NOTIFY` | 0 | 0..1 | 25 | private-shop notify |
| `OPTION_WHISPER_NOTIFY` | 0 | 0..1 | 26 | whisper notify |
| `OPTION_FORCE_NOTIFY` | 0 | 0..1 | 27 | force/PvP notify |
| `OPTION_EFFECT` | 100 | 1..100 | 28 | effect intensity |
| `OPTION_BRIGHT` | 100 | 1..100 | 29 | brightness |
| `OPTION_SCREENMODE` | 0 | 0..2 | 30 (byte offset +120) | **display/screen mode** (drives window style and device path) |
| `OPTION_ID` | `"(null)"` | ≤16 chars | 124 (string) | last-used account id (Save-ID) |

**`OPTION_SCREENMODE` values:** `1` = windowed (sized), `2` = fullscreen/borderless. Value `0` is
valid but its exact rendering behaviour was not byte-confirmed (see §0.9 open item 1). The Save-ID
field (`OPTION_ID`) is also written by the login scene — see `specs/frontend_scenes.md` §1.6.

> **`option.ini` path buffer (CODE-CONFIRMED).** The runtime-built path to `option.ini` is held in a
> character buffer at **settings-object offset +1165**. This is the buffer the state-2 opening-decision
> step reads when it fetches the `[OPENNING]`/`SKIP` key (§7.3, §7.9, `resource_pipeline.md §2.5`).

## 0.3 Window class and creation — (CODE-CONFIRMED)

The engine creates a single Win32 window with these properties:

| Property | Value |
|---|---|
| **Window class name** | `"diamond engine application"` |
| **Window title** | `"Do"` |
| **Window procedure** | the engine WndProc |
| **Single-instance guard** | in fullscreen mode, if another window of the same class already exists the new instance bails immediately |
| **Window style** | `OPTION_SCREENMODE < 1` → a popup/borderless style word; else → an overlapped/visible style word (the two exact style bit-words are pinned in §0.3.1) |
| **Client size** | from the renderer's configured width/height (the `OPTION_WIDTH`/`OPTION_HEIGHT` values) |
| **Centred** via | `GetSystemMetrics` |

After `CreateWindowExA`: the window is registered with the IME pre-translate hook, shown, and
focused. An **auto-reset event** is created and a **worker thread** is started (`_beginthreadex`);
if the event creation fails the engine message-boxes `"CreateEvent() is failed in main thread"` and
calls `exit(1)`. For `OPTION_SCREENMODE == 1` the window is additionally `SetWindowPos`'d to the
configured client size.

> **Event correction (confirmed).** The main-thread sync event is created with the manual-reset
> argument set to `0`, i.e. it is an **auto-reset** event (an earlier note read it as manual-reset).

### 0.3.1 Window-style and fullscreen-path constants — (CODE-CONFIRMED)

The window style word is selected directly from `OPTION_SCREENMODE`:

| Condition | Style word | Character |
|---|---|---|
| `OPTION_SCREENMODE < 1` | `0x10CA0000` | a popup/borderless family (no title bar / sizing border) |
| `OPTION_SCREENMODE ≥ 1` | `0x90000000` | a visible + popup family (`WS_POPUP \| WS_VISIBLE`-style) |

Fullscreen path (`OPTION_SCREENMODE == 2`): the window is additionally created with the
extended-style **topmost** bit (`WS_EX_TOPMOST`, value `0x00040000`), and the display device mode is
switched via `ChangeDisplaySettingsA` (the matching device path in §0.4). Windowed-sized path
(`OPTION_SCREENMODE == 1`): the post-create `SetWindowPos` resizes the client area to the configured
width/height. The exact behaviour of value `0` is not byte-pinned (see §0.9 open item 1).

> The window class name `"diamond engine application"` and title `"Do"` are the actual string literals
> in the code. The marketing name "Martial Heroes" is **not** the window title; the crash reporter
> carries its own separate application-identity string (§0.10). Trust the code over any comment.

## 0.4 Direct3D 9 device — (CODE-CONFIRMED)

Created on the **main thread** immediately after window creation, during the state-0→1 transition.
The D3D SDK version used is **32 (decimal)**.

**Adapter and depth/stencil format selection:**

For 32-bpp colour: a 2-entry adapter-format list is walked via `CheckDeviceType`. For 16-bpp: a
3-entry list. The back-buffer default format is **X8R8G8B8**.

Depth/stencil fallback chain (tried in order until `CheckDepthStencilMatch` succeeds):
**D32 → D24X8 → D24S8 → D16**.

**`D3DPRESENT_PARAMETERS` (zeroed 56-byte struct):**

| Field | Value |
|---|---|
| BackBufferFormat | X8R8G8B8 (overridden to the chosen adapter format) |
| BackBufferCount | 1 |
| SwapEffect | 1 (DISCARD) |
| PresentationInterval | `0x80000000` = `D3DPRESENT_INTERVAL_IMMEDIATE` → **no vsync** |
| hDeviceWindow | the global main HWND |
| Windowed | from the windowed/fullscreen choice |

**CreateDevice flags:** adapter 0, `D3DDEVTYPE_HAL`, **`D3DCREATE_HARDWARE_VERTEXPROCESSING | D3DCREATE_MULTITHREADED`**.

After device creation: `D3DXCreateSprite` for 2D HUD/UI; a default render-state init pass; then
**cel/toon-shading load**. The toon/bloom path is enabled only if the toon-shading loaded
successfully **and** settings object field index 12 (the forced-1 toon flag) equals `1`. This is
exactly the user-togglable graphics option described in §3.1.

See also §3.1 for the full device/present-mode description.

## 0.5 VFS mount (`data.inf` / `data/data.vfs`) — (CODE-CONFIRMED)

VFS mount is performed once, just before the state loop begins. Steps:

1. Open `data.inf` (the index file) with **`FILE_FLAG_RANDOM_ACCESS`** (combined with a read-only
   attribute). It is **not** `FILE_FLAG_SEQUENTIAL_SCAN` — an earlier note had this backwards.
2. Read a **24-byte header**; the **entry count is the 4th dword (byte offset +0x0C)** of that header.
   (An earlier note placed it at +0x08 / the 3rd dword.)
3. Allocate `144 × count` bytes for the TOC array and read the full TOC.
4. Close the index file handle.
5. Open `data/data.vfs` (the data archive) — also with `FILE_FLAG_RANDOM_ACCESS` — and **keep its
   handle open** for the lifetime of the process.

> **Byte-layout authority.** The on-disk header byte map (including the +0x0C entry-count field and
> the 144-byte TOC stride) is owned by `formats/pak.md`; the values above are confirmed via the mount
> routine's control flow, with the raw-byte witness of the header offset pending in that spec
> (capture/debugger-pending → VFS byte-witness).

TOC entry layout (144 bytes each):

| Offset | Size | Type | Field |
|---|---:|---|---|
| +0 | 100 | char[] | virtual path (stored lower-cased) |
| +100 | 4 | — | padding / unused |
| +104 | 8 | i64 | data offset within the archive file |
| +112 | 8 | i64 | data size in bytes |
| +120 | 24 | — | auxiliary / reserved |

The mounted flag drives the dual-path behaviour of all file-open calls throughout the engine (VFS
in-memory vs. loose `fopen`). See `formats/pak.md` for the full on-disk format spec (43,347-entry
sample byte-confirmed).

## 0.6 CP949 / Korean text model — (CODE-CONFIRMED)

There is **no `setlocale`, no `_setmbcp`, and no process-wide codepage call** anywhere in the boot
path. Korean text support is handled entirely at use-sites:

- **Rendered Hangul:** 15 font slots (ids 0..14) at fixed point sizes and weights are created during
  the state-0→1 transition using **`D3DXCreateFontA`** with Korean typefaces
  (`DotumChe`, `Dotum`, `BatangChe`). These Win32-backed fonts resolve the HANGEUL charset at render
  time. The exact slot table — (CODE-CONFIRMED), one row per `D3DXCreateFontA`-equivalent call (the
  three numeric columns are the registered height / a paired half-height / a render height; the last
  is the weight):

  | Slot | Typeface | Heights | Weight |
  |---:|---|---|---:|
  | 0 | DotumChe | 12 / 6 / 12 | 0 |
  | 1 | Dotum | 10 / 5 / 10 | 0 |
  | 2 | DotumChe | 32 / 16 / 32 | 800 |
  | 3 | DotumChe | 18 / 12 / 24 | 800 |
  | 4 | DotumChe | 12 / 6 / 12 | 800 |
  | 5 | BatangChe | 12 / 6 / 12 | 0 |
  | 6 | BatangChe | 18 / 12 / 24 | 700 |
  | 7 | BatangChe | 12 / 6 / 12 | 700 |
  | 8 | BatangChe | 12 / 6 / 12 | 700 |
  | 9 | DotumChe | 12 / 6 / 12 | 700 |
  | 10 | Dotum | 16 / 10 / 20 | 800 |
  | 11 | DotumChe | 10 / 5 / 10 | 400 |
  | 12 | DotumChe | 12 / 6 / 12 | 400 |
  | 13 | DotumChe | 14 / 7 / 14 | 400 |
  | 14 | DotumChe | 16 / 8 / 16 | 400 |
- **String conversions:** `MultiByteToWideChar(949, …)` is called at individual use-sites. There is
  no global narrow → wide conversion layer.
- **Conclusion for the reimplementation:** model CP949 as a **per-string decode** (code page 949),
  not a global locale switch. Register `CodePagesEncodingProvider` once (see `CLAUDE.md`) and call
  `Encoding.GetEncoding(949)` at each site that reads game text.

## 0.7 Singleton construction order — (CODE-CONFIRMED)

Two construction tiers:

**Tier A — C++ static initializers (run before `WinMain`).** The only load-bearing static object
traced end-to-end is a **billing/anti-cheat scheduler proxy** that the state-1 entry must pass
through. If the gate fails, `WinMain` calls `exit(1)`. The full static-init list is large; only
this gate is boot-critical.

**Tier B — lazy Meyers singletons (guard-flag protected; construction order = first-use order).**
The observed first-touch order during a normal boot:

| Order | Singleton | First touched | Note |
|---:|---|---|---|
| 1 | Lua config | `WinMain` top | reads `game.lua` |
| 2 | Engine-state struct | `WinMain` top | holds the engine-state int + debug flag |
| 3 | Crash reporter | `WinMain` | third-party crash-reporting SDK (SEH filter + crash log; see §0.10) |
| 4 | Settings object (DO_OPTION) | state 0 | loads `DoOption.ini` (window/device dims) |
| 5 | Engine/App object | state 0 | wires InputManager and renderer |
| 6 | Network handler | state 0 | network handler object |
| 7 | Input manager | Engine ctor + state 1 | global HWND/IME owner |
| 8 | Renderer/View object | state 0/1 | the large D3D renderer object |
| 9 | Texture manager (GHTexManager) | every state (flushed before each MainLoop) | texture cache |
| 10 | Billing-state object | state 1 (post-login) | account/billing/user-name state |
| 11 | Network client | error/teardown | socket client (WSARecv/WSASend tier) |
| 12 | Sound manager / DirectSound | first sound (~state 2) | lazily creates the DSound device; see §1.1 |

## 0.8 State-0 → state-1 transition in detail — (CODE-CONFIRMED)

**State 0 (Initialisation)** — executes once and immediately:

1. Writes engine state → 1.
2. Constructs the Engine/App and NetHandler singletons.
3. Reads the display-mode setting (settings index [30], byte offset +120 — see §0.2): if `== 2`
   (full-desktop), the renderer width is taken from `GetSystemMetrics` and the height is left to
   auto; otherwise the configured `OPTION_WIDTH`/`OPTION_HEIGHT` values are copied into the renderer.
   Two distinct width values are involved and **must not be conflated** (see §0.8.2):
   - the **per-renderer width setter** caps width at **1920** and the **per-renderer height setter**
     caps height at **1200** — these write the renderer's own width/height fields;
   - the **separate global width value** is written **only** in the full-desktop (display-mode == 2)
     branch and is *not* a height-setter width cap.
4. **Forces the active colour depth to 16-bit.** Although the settings object's constructor leaves a
   32-bit colour-depth default in field index [2], state 0 overwrites the engine's *active* colour
   depth to **16-bit**, and the device-creation path (§0.4) uses 16-bit, not the 32-bit default.

**State 1 (LoginWindow)** — the first real screen; built immediately after state 0 returns:

1. Loads the UI string database `data/script/msg.xdb` synchronously on the main thread.
2. Constructs the LoginWindow widget tree (~340 widgets from `data/script/uiconfig.lua`).
3. Registers the window as an event target.
4. Passes the billing/anti-cheat scheduler gate (Tier A static object — `exit(1)` on failure).
5. Creates the OS window (§0.3).
6. Creates the D3D9 device (§0.4).
7. Creates the **15 Korean font slots** (the exact table is in §0.6).
8. Reads the **addiction-warning check interval** (§0.8.1) and stores its millisecond value into the
   billing/scheduler singleton.
9. Enters **`Engine_MainLoop`** — the per-screen frame pump. (Note: `Engine_MainLoop` itself raises
   the timer resolution with `timeBeginPeriod(1)` on every entry — see §8.1.)

### 0.8.1 Addiction-warning check time — (CODE-CONFIRMED)

During state 1, the client reads the integer Lua global
**`DISPLAY_GAME_ADDICTION_WARNING_CHECK_TIME`** (a value in **seconds**), multiplies it by **1000**
to get milliseconds, and stores the result into a field of the billing/scheduler singleton. This is
the period of the mandated Korean play-time/health-warning prompt (a legal requirement for Korean
online games of the era). It is not a rendering or loop-pacing value — purely a billing-side timer.

### 0.8.2 Resolution setters and the display-mode promotion — (CODE-CONFIRMED — corrected)

> **Resolution-setter correction (the binary wins).** The client has two distinct per-renderer
> setters and one separate global width value:
> - **Width setter** — writes the renderer's width field (object offset +44465) and clamps the
>   maximum to **1920**.
> - **Height setter** — writes the renderer's height field (object offset +44466) and clamps the
>   maximum to **1200**.
> - **Separate global width value** — a width quantity used **only** in the full-desktop
>   (display-mode == 2) branch; it is **not** a width cap belonging to the height setter.
>
> An earlier note read the height setter as "caps width at 1920 via the separate global". That was
> wrong: the width/height caps live on the two per-renderer setters (1920 / 1200), and the separate
> global is the full-desktop-only width. The two must not be conflated.

**Display-mode 1 → 2 promotion.** Before the OS window and the D3D9 device are created in state 1,
the display-mode setting is re-read and a value of `1` is **promoted to `2`**, and that promoted mode
is the argument fed to both the window-creation and device-creation steps.

**Engine bring-up order (state 1, before the per-frame loop).** The fixed order is:

1. **Start the task scheduler** (the engine fails into error state 7 — see §0.9 open items — if the
   scheduler cannot start).
2. **Create the OS window** (window class named `"diamond engine application"` — §0.3).
3. **Create the Direct3D 9 device** (§0.4).
4. **Register the 15 font slots** (the table in §0.6).
5. **Reset the effect subsystem.**

## 0.9 Boot — open items

1. **`OPTION_SCREENMODE == 0` semantics.** Values 1 (windowed) and 2 (fullscreen) are proven by
   the window/device branches; what 0 selects (probably default windowed) is not byte-pinned.
2. **Full Tier-A static-initializer list.** Only the scheduler gate was traced end-to-end. Whether
   other managers are static vs lazy is not confirmed.
3. **`game.lua` full content.** Only the three bool keys read at boot are known; other globals
   the file may set are unknown.
4. **`OPTION_LANG` (field idx 3) effect.** Value is loaded/clamped 1..3 but where it branches
   text/asset selection was not traced.
5. **Worker thread identity.** The thread started during window creation stores its proc in the
   engine object; its exact body was not traced in this pass.
6. **First DirectSound touch.** SoundManager is a lazy singleton first observed near state 2 (Load),
   but the precise first-call site in the boot sequence was not byte-pinned.

## 0.10 Crash-reporting SDK — (CODE-CONFIRMED — corrected)

The init component previously characterised as a "logger" / "profiler" is in fact a **third-party
crash-reporting SDK** — a commercial SEH crash-dump library, **not** a frame profiler. During boot it:

- installs an **unhandled-exception (SEH) filter** so that an unhandled fault is intercepted and
  reported rather than silently terminating the process;
- opens a **crash log file** and writes log entries (an initial start entry is written during boot);
- is configured with an **application-identity string** and a **remote crash-submission endpoint**
  (a support host/port) to which crash reports can be sent;
- loads a **DirectX symbol-index file** (an on-disk `symindex_dx9`-style index) used to resolve
  symbols for **the client executable and its PDB** when formatting a crash report.

> **Non-distribution note.** The SDK's hard-coded application-identity string, its version string,
> its support contact, and its crash-submission host/port are deliberately **omitted** from this
> committed spec. They are recorded only in the gitignored dirty-room ledger and must never be
> reproduced in committed text or in code. Describe the component only in neutral terms, as above.

### 0.10.1 Pending / to confirm (Phase 5)

- **The literal on-disk INI filename behind the `[OPENNING]` section.** The path is built at runtime
  into the settings-object path buffer (offset +1165) and read by the state-2 opening-decision step;
  prior campaigns identify it as `option.ini` (§7.10 item 2), but the exact filename string at the
  +1165 buffer was not statically pinned this pass and remains debugger-pending.
- **Which Init failure transitions to error state 7.** The candidate failure codes are 1 (config /
  first init fail) and 3 (device / secondary init fail); confirming which Init failure raises which is
  debugger-pending.
- **The live display-mode value and the live resolution.** Static analysis gives the selection logic;
  the effective display-mode value (settings index [30]) and the resulting width × height depend on
  the user's `option.ini` and are debugger-pending.

---

# 1. Sound system (DirectSound runtime)

The audio engine is a DirectSound (DirectSound 8 era) wrapper with a 2D/3D split, OGG-Vorbis decoding,
a per-map five-table ambient driver, an actor-event SFX router, and a background worker thread for
streaming background music. The on-disk table format is owned by `formats/sound_tables.md`; this section
specifies the **runtime engine** that consumes it.

## 1.1 Device and buffer setup — (confirmed)

At startup the engine creates a DirectSound device, sets cooperative level to PRIORITY, creates a primary
buffer with 3D + volume control, and acquires a global 3D listener interface used for spatialisation.
A single global **master audio-enabled flag** gates the whole subsystem: when cleared, the create-sound
path and the worker thread both early-out, cheaply silencing all audio.

Five PCM `WAVEFORMATEX` templates are pre-built once and selected by channel count + sample rate:

| Role | Channels | Sample rate | Bits | Notes |
|---|---:|---:|---:|---|
| Stereo 44.1 kHz | 2 | 44100 | 16 | 2D alternate |
| Stereo 22.05 kHz | 2 | 22050 | 16 | **2D default** |
| Mono 44.1 kHz | 1 | 44100 | 16 | 3D alternate |
| Mono 22.05 kHz | 1 | 22050 | 16 | **3D default** |

**Hard codec rule (confirmed):** **3D sounds must be MONO; 2D sounds must be STEREO.** A 3D clip with the
wrong channel count is rejected at load; a 2D clip likewise. Implementations should enforce the same rule.

## 1.2 Sound object and the create path — (confirmed)

A sound is created from `(soundId, typeFlags, dirPrefix)`. The type-flags byte is a small bitfield:

| typeFlags | Meaning | Asset directory |
|---:|---|---|
| `1` | OGG-backed **2D** sound | `data/sound/2d/` |
| `3` | OGG-backed **3D** sound | `data/sound/3d/` |

Bit 0 ("is OGG-backed") is always set; bit 1 selects 3D. A second, list-based selector chooses 2D vs 3D by
a category byte (`category < 5 ⇒ 2D`, else 3D).

The on-disk filename is always built as `<dirPrefix><id>.ogg` — the id is formatted as plain decimal and the
`.ogg` extension is **unconditional**. (A few `.wav` assets exist in the 3D directory; they are reached only by
the separate list path, never by the table dispatch.) Loading prefers the mounted `.pak` VFS (in-memory
decode via Vorbis file callbacks) and falls back to a plain on-disk `fopen` in dev/editor environments.

## 1.3 Decode buffer and the streaming sentinel — (confirmed)

Decoding fills a single shared **512 KiB scratch buffer** (`0x80000` bytes — roughly 11.9 s of mono
22.05 kHz 16-bit PCM). This is a hard PCM cap with two outcomes:

- If a clip **fits** within 512 KiB, it is copied once into a fixed-size DirectSound buffer and the OGG handle
  is closed (a one-shot sound).
- If a clip **fills** the entire scratch (i.e. is longer than the cap):
  - a **3D** sound is **rejected** (3D point sounds must be short);
  - a **2D** sound switches to **streaming**: a **1 MiB ring buffer** (`0x100000`) is allocated, the OGG
    handle stays open, and the worker thread refills it incrementally. This is the background-music mechanism.

## 1.4 The five per-map tables and the ambient driver — (sample-verified)

When a map area becomes active, the loader opens five sibling files and reads exactly **12 288 bytes**
(256 entries × 48 bytes) from each, ignoring any trailing editor bytes. Extension → runtime role:

| File extension | Runtime table role | Indexed by |
|---|---|---|
| `.run` | footstep set, **running** | actor visual's run-footstep id |
| `.wlk` | footstep set, **walking** | actor visual's walk-footstep id |
| `.bgm` | background music zone | terrain mud-cell byte +0x02 |
| `.bge` | looped ambient (two slots) | terrain mud-cell bytes +0x03, +0x04 |
| `.eff` | 3D triggered point sounds (three slots) | terrain mud-cell bytes +0x05, +0x06, +0x07 |

A per-frame ambient driver reads the local player's world position, looks up the current terrain mud-cell,
and starts/stops BGM, looped ambience, and 3D point sounds when a cell byte changes (index `0` is the null
sentinel and is skipped). It caches the last-played index per slot so a sound restarts only on actual change.

Pinned ambient constants — (confirmed):

| Constant | Value | Role |
|---|---:|---|
| Ambient re-evaluation cadence | **600 000 ms (10 min)** | full forced re-pick interval (e.g. day/night / periodic) |
| Game-hour divisor | **3600** | hour-of-day = `gameSeconds / 3600`; each entry carries a 24-byte hour schedule |
| 3D ambient volume/distance scale | **× 0.7** | table volume scaled before the 3D min-distance / rolloff |
| Indoor/instanced BGM override id | **863500002** | forces this BGM when the local player's indoor flag is set |
| Music-slider-exempt cue ids | **861010109, 861010110** | played at full volume when a global toggle is set |

For a `.eff` (3D ambient) entry, the play position is `(entry.X, playerY, entry.Z)` — **X/Z from the table,
Y taken from the player**. Hour-of-day gating: an entry's 24-byte hour schedule is indexed by the current
game hour; a zero byte mutes the entry that hour.

## 1.5 Actor-event SFX router — (confirmed)

A separate router handles 3D SFX for actor / animation / network events (spawn, death, attack, level-up,
item-use, combat-effect spawn, stat update, footsteps). Behaviour:

- **Audibility cull:** for the local player it uses a fixed audible radius of **200.0 units**; for a remote
  actor it computes a cutoff from the actor's visual max-distance scaled by a context factor and does a
  **squared-distance cull** (drop if beyond the scaled radius).
- **Separate volume slots** for "my" sounds vs "others'" sounds.
- **Kind dispatch:** kinds 5/10/11 → a directional/voice pool (max 3 concurrent per key); kinds 7/8/9 →
  a footstep pool; other kinds rejected.

**Footstep selection (confirmed):** when an animation cycle wraps, the local player's footstep id is read
**from the per-character actor-visual** (separate walk-id and run-id fields), not from a mud-cell byte —
running fires kind 8 with the run id, walking fires kind 7 with the walk id. (This is why the sampled
`.wlk`/`.run` tables are empty: those sets are addressed per actor-visual, not per map cell here.)

## 1.6 Volume curve — (confirmed, endpoints certain)

Linear amplitude `X ∈ [0,1]` is mapped to a DirectSound millibel attenuation:

- `X == 0` → **−10000** (full silence; DirectSound minimum).
- otherwise → a steep perceptual taper using a nested-logarithm form, `mB ≈ int( log( log(X) · 3000 + 0.5 ) )`.

The endpoints (`0 → −10000`) are certain; the exact log base/units of the nested form are not byte-verified
(see open list). Implementations should reproduce the silence endpoint exactly and approximate the taper.

## 1.7 Asynchronous worker thread — (confirmed)

A background worker drains a mutex-guarded event queue; each event's first byte is an opcode:

| Op | Name | Action |
|---:|---|---|
| 1 | LOAD | load the sound (set dead flag on failure) |
| 2 | DELETE | release the sound |
| 3 | PLAY | play (with loop flag) |
| 4 | PLAY2D | set volume, then play |
| 5 | PLAY3D | set position + min-distance + volume, then play |
| 6 | STOP | immediate stop |
| 7 | SET_VOLUME | set volume only |
| 8 | RESET | reset playback position to 0 |
| 9 | CHANGE_STREAM | swap the streaming OGG (background-music track change) |

The worker ticks: every iteration where **more than 200 ms** elapsed it refills active streaming ring buffers,
then sleeps **100 ms** — so streaming/BGM refill runs roughly 5×/second. There are therefore **two playback
routes**: a synchronous create/play used by the ambient driver and the actor-event router, and this asynchronous
event-queue route used for streaming BGM and any caller that posts an event.

## 1.8 Sample-byte evidence — (sample-verified)

- Map-000 sound tables (`.wlk/.run/.bge` empty; `.bgm` has one entry at index 1: id 920100200, weight 1.0,
  hour schedule all-active) match the format spec exactly.
- The 3D-directory OGG samples are Vorbis **mono / 22 050 Hz**; the lone `.wav` sample is PCM **mono /
  22 050 Hz / 16-bit** — all consistent with the mono-3D rule.

## 1.9 Sound — open items

1. Exact units of the nested-log volume curve (endpoints certain; taper approximate).
2. Entry field at +0x24 (editor-fill in samples) — unused by any runtime path.
3. Entry `weight` field (+0x1C, always 1.0 in samples) — not consumed by any traced playback path.
4. 2D-directory stereo presence (runtime requires stereo; no 2D-dir sample exists to confirm).
5. How a table id whose asset is a `.wav` is played (table path always requests `.ogg`).
6. Which file format populates the actor-visual walk/run footstep-id fields (terrain/visual concern).
7. Exact voice-pool / footstep-pool concurrency sizing beyond the 3-concurrent voice cap.

---

# 2. UI widget tree and `.lua` binding

The UI is a retained-mode, single-inheritance widget tree branded "Diamond". A common base component carries
layout, flags, a transform matrix, and a timer; panels add a children vector; windows add a command-handler,
an auxiliary view, and a texture list. Layout and string content come from `.lua` config files (see
`specs/lua_scripting.md` for the VM and data-tree model).

## 2.1 Widget class hierarchy — (confirmed)

```
Component (base)
├── Panel
│   ├── Window            ── concrete: LoginWindow, SelectWindow, MainWindow, OpeningWindow, …
│   └── ScrollEx
├── ComponentEx           ── lightweight widget with an explicit float screen-rect for hit-testing
├── Button
│   └── CheckBox
├── Label
│   ├── ShortLabel
│   └── Labels
├── Textbox
├── List
├── Scroll
└── Canvas3D
```

Non-rendered helpers also exist: a string filter/validator and a chat-specific filter subclass.

## 2.2 Base component field layout — (confirmed)

Field offsets within the base component, for an implementer building an equivalent struct. (These are *engine
struct* offsets, not wire offsets — they describe the in-memory widget, and may be modelled as ordinary class
fields in the reimplementation; the table is the authoritative semantic map.)

| Offset | Size | Type | Role |
|---|---:|---|---|
| +0x00 | 4 | ptr | vtable / dispatch pointer |
| +0x04 | 4 | u32 | alpha / opacity (init 255); fades by ±64 per draw tick on show/hide |
| +0x08 | 4 | u32 | capability-flag bitmask (bit0 always set; bit2 set for panels; bit13 set for windows) |
| +0x0C | 4 | i32 | widget / action id (init −1) |
| +0x10 | 4 | i32 | panel-type id used by child lookup (init −1; set when added to a parent) |
| +0x14 / +0x18 | 4 / 4 | i32 | local X / local Y (pixels, relative to parent) |
| +0x1C / +0x20 | 4 / 4 | i32 | width / height (pixels) |
| +0x24 / +0x28 | 4 / 4 | i32 | source/default width / height (copies of width/height) |
| +0x2C / +0x30 | 4 / 4 | i32 | world (screen-space) X / Y, computed by the transform pass |
| +0x34 / +0x38 | 4 / 4 | i32 | world right / bottom edge (world + size); render-queue bounds |
| +0x3C / +0x40 | 4 / 4 | i32 | local right / bottom edge (local + size) |
| +0x44 | 64 | float[16] | 4×4 translation matrix built by the transform pass |
| +0x84 | 4 | ptr | parent pointer (NULL = root) |
| +0x88 | 1 | u8 | **hovered** flag (cursor-over result of hit-test) |
| +0x89 | 1 | u8 | hover-enter edge flag (1 on the first frame the cursor enters) |
| +0x8A | 1 | u8 | **enabled** flag (init 1) |
| +0x8B | 1 | u8 | **focused** flag (IME uses it to know the active textbox) |
| +0x8C | 1 | u8 | **visible** flag (init 1) — draw dispatch checks this `== 1` before drawing a child |
| +0x8D | 1 | u8 | pressed / drag-state flag |
| +0x90 | 4 | ptr | texture/sprite handle or action-callback context |
| +0x95 | 1 | u8 | timer-active flag |
| +0x98 | 4 | u32 | timer expiry (ms timestamp) |
| +0x9C | 4 | u32 | timer interval (default **3000 ms**) |
| +0xA0 | 4 | ptr | timer callback context |

> **Flag-byte correction (confirmed).** The four bytes at +0x88..+0x8D are a contiguous group. The
> authoritative meaning is: **+0x88 = hovered, +0x8A = enabled, +0x8B = focused, +0x8C = visible**. An earlier
> input-pipeline note inverted visible/enabled — this spec's ordering is the verified one (the draw dispatch
> proves `+0x8C == 1` means visible; the hit-test proves `+0x88` is the hovered result).

## 2.3 Panel and window additions — (confirmed)

**Panel** adds an MSVC-style child vector (begin/last/end pointers at +0xA4/+0xA8/+0xAC), a possible fourth
vector field at +0xB0 (UNVERIFIED), an **active-child index** at +0xB4 (init −1; used for tab/focus switching),
and a panel-subtype flag byte at +0xB8 (UNVERIFIED). Adding a child sets the child's parent pointer and panel-id
and pushes it into the parent's vector.

**Window** adds, after the panel fields: an embedded command-handler object (action-id callback dispatch,
~ +0xBC), an auxiliary view (~ +0xE8), and an embedded texture list (~ +0x220). Construction marks the
window-type capability bit (bit13).

**ComponentEx** adds an explicit float screen-rect for accurate hit-testing: X/Y scale factors and screen
X/Y origin (world left/top edges as floats). Its hit-test uses `screenX ≤ cursorX < screenX + width·scaleX`
(and the Y analogue), writing the result into the hovered byte.

Approximate struct sizes (UNVERIFIED as exact totals; natural 4-byte alignment, **no Pack=1 evidence** — these
are heap C++ objects, unlike the packed wire structs): component ≈ 164 B, panel ≈ 188 B, componentEx ≈ 180 B,
window ≥ ~0x240 B.

## 2.4 Draw dispatch, hit-test, focus, transform — (confirmed)

- **Draw dispatch (windows):** iterate the child vector from begin to end; for each child whose **visible**
  byte (+0x8C) is 1, call its transform-update then its draw. (Invisible children are skipped, not drawn dim.)
- **Hit-test:** two implementations. The integer base hit-test bounds-checks world position + integer size; the
  float ComponentEx hit-test bounds-checks the float screen-rect with scale. Both write the hovered byte and,
  on enter/exit, fire hover-enter / hover-exit callbacks. The top-level UI hit-test walks the tree from the root
  window and returns the topmost hit, **consuming mouse events before world-entity picking** (UI first, world
  second).
- **Focus** is tracked at two levels: an **IME/textbox focus** (sets the widget's focused byte and registers the
  widget with the IME context for composition input) and a **global click-focus** pointer (set on mouse-down for
  an enabled widget, cleared and a mouse-up event enqueued on release). The panel-level active-child index
  selects which child tab is current.
- **Transform:** world position = local position + parent world position; the result is stored and a 4×4
  translation matrix is written for the D3D render pass.

## 2.5 Lua and INI configuration binding — (confirmed; content UNVERIFIED)

- **`data/script/uiconfig.lua`** is loaded when the login scene is built. The build cascade creates roughly
  **340 widgets**. The only confirmed Lua global key is `NEW_SERVER_INDEX` (boolean, toggles server-list mode).
  Texture references seen in the same path include `data/ui/login_slice1.dds`, `data/ui/loginwindow.dds`,
  `data/ui/InventWindow.dds`, `data/ui/loginwindow_02.dds`.
- **`data/script/display.lua`** is a **renderer** config (float globals for glow range, framerate, character
  bloom/brightness multipliers) — not widget layout. See §3.
- **Chat-window config keys** read elsewhere: `CHAT_WINDOW_FONT_SIZE`, `CHAT_WINDOW_SIZE`,
  `CHAT_WINDOW_POS_X`, `CHAT_WINDOW_POS_Y`.
- **Panel-position persistence** is stored in `panel.ini` (one of the five INI files built at startup, §0.2).
  The INI section name is `"<billingStateIndex>_<spawnDesc>_PANELPOS"`; per panel index 0..8 the keys
  `PANEL_<i>_X` / `PANEL_<i>_Y` give saved positions. If a saved coordinate is −1, or if `SCREEN_WIDTH` /
  screen-height changed, the panel resets to its default position. Additional keys: `LINK_VERTICAL` (toolbar
  layout toggle), `MENU_OPEN` (initial open/close state).
- **Action-id → button mapping** (login window): roughly 200–201 = login OK, 202 = login + enter-load,
  203 = cancel, 204/207 = help, 205 = help2, 206/16 = server-list, 209/220 = quit, 221..245 = option tabs.
  Each action fires an SFX and mutates the window's state-machine variable.

## 2.6 UI — open items

1. Full bit definitions of the capability-flag mask beyond bits 0/2/13.
2. Whether the +0x24/+0x28 "source size" copies serve a distinct purpose (animation/clip rect).
3. The panel +0xB0 word and the +0xB8 panel-type flag values/roles.
4. Total window footprint (depends on the embedded texture-list layout at ~+0x220).
5. Scroll-widget internal pointer roles (up/down/thumb).
6. The actual `uiconfig.lua` / `tutor.lua` content — not in the binary; only `NEW_SERVER_INDEX` confirmed.

---

# 3. Diamond render pipeline and shaders

The renderer is an in-house Direct3D 9 scene-graph engine. The headline feature is a **toggleable
four-phase render-to-texture toon + bloom post-process**; when disabled, a plain fixed-function
back-buffer path runs. Per-frame loop ordering is owned by `specs/game_loop.md`; shader file format by
`formats/shaders.md`. This section pins the runtime pipeline behaviour and constants.

## 3.1 Device, present mode, and recovery — (confirmed)

- API: **Direct3D 9** (`d3d9.dll` + `d3dx9_42.dll`), created at SDK version 32, HAL device with
  **hardware vertex processing + multithreaded** behaviour flags.
- Back-buffer format default **X8R8G8B8**; depth/stencil chosen from the fallback chain
  **D32 → D24X8 → D24S8 → D16**.
- **Presentation interval = IMMEDIATE → NO GPU VSYNC.** The GPU does not gate on the vertical blank.
  **This does not mean the frame rate is uncapped:** the engine main loop applies a *software* frame
  cap with a QueryPerformanceCounter-based limiter that `Sleep()`s each iteration (see §8.1, §8.3.1).
  So presentation is vsync-off, but the CPU paces frames to a fixed target rate. (This corrects an
  earlier reading that called the loop "genuinely uncapped".)
- **Device-lost recovery:** on `DEVICELOST`, sleep ~1000 ms and retry; on `DEVICENOTRESET`, run a three-step
  recovery (release default-pool resources → reset device with saved present params → recreate default-pool
  resources, e.g. fonts).

The toon/bloom path is selected **at device-create time**: it turns on only if the toon-shading load succeeded
**and** a graphics-option config flag is set (settings object field idx 12 = 1). So it is a user-togglable
graphics option. See §0.4 for the device-creation call chain.

## 3.2 Plain path (toon/bloom OFF) — (confirmed)

Get the back-buffer, set the viewport from the scene rect, clear TARGET|ZBUFFER, begin scene, activate the
camera and set the VIEW transform, run optional pre-draw callbacks, configure stage 0/1/2 texture-stage state
(modulate/select chains) and linear samplers, draw the scene root, end scene, then draw the HUD/FPS overlay.
**No shaders** — pure fixed-function lighting + multitexture.

## 3.3 Toon + bloom path (ON) — the four phases — (confirmed)

Three render-target textures (TEX0 cel/scene, TEX1 edge/glow, TEX2 bloom-small) and three render-to-surface
helpers are created up front, plus a `toonramp.bmp` lookup texture (§3.6). The frame is:

- **Phase A — scene → TEX0.** Render the 3D scene into the offscreen colour target: clear, activate camera,
  set VIEW + identity WORLD, run the pre-draw callbacks, then invoke the scene-root draw callback (the
  scene-graph cull walk that draws world → actors → effects, §3.5). Resolve into TEX0.
- **Phase B — glow/edge extract + downsample → TEX1.** Full-screen ortho quad with a half-texel UV offset;
  Z/lighting/alpha-blend disabled; stage0 = SELECTARG1/TEXTURE; linear samplers; sample TEX0. **Fixed-function,
  no pixel shader bound.** Output TEX1.
- **Phase C — bloom blur/downsample → TEX2.** Smaller ortho sized by the bloom **downscale divisors**; binds a
  **config-supplied blur/glow pixel shader**; sample TEX1; output TEX2.
- **Phase D — composite TEX1+TEX2 over TEX0 with `finaldx8.psh`, then forward overlays.** Two-texture quad;
  upload two pixel-shader constants — **c0 = edge weight, c1 = bloom weight** (broadcast scalars) — matching the
  composite formula `saturate(2·edge·c0 + bloom·c1)`; bind `finaldx8.psh`; draw. Then re-establish forward 3D
  state (the toon uniforms, §3.6) and run the **post-scene transparent-overlay callback** (alpha-blended
  geometry — water/glass/decals/some FX — that must draw after the opaque composite).

**Back-buffer present stage:** clear the real back-buffer; blit the finished offscreen frame as a full-screen
quad (opaque copy); then draw the **2D UI / HUD callback LAST** in screen-ortho space; optionally draw the FPS
overlay; end scene; present.

**Net top-level draw order (confirmed):**
`[offscreen] clear → 3D scene (terrain/world → actors → effects) → glow extract → bloom blur → finaldx8
composite → post-scene transparent overlay`, then
`[back-buffer] clear → blit composited frame → UI/HUD → FPS → present`.

## 3.4 Pinned render constants — (confirmed)

| Constant | Value | Role |
|---|---:|---|
| Back-buffer format | X8R8G8B8 | default colour format |
| Depth fallback chain | D32 → D24X8 → D24S8 → D16 | depth/stencil selection |
| Present interval | IMMEDIATE (no GPU vsync) | frame rate is instead software-capped — see §8.1 |
| Skinned-actor vertex stride | **32 bytes** | XYZ + normal + UV |
| Toonramp LUT | **256 × 1, 24-bpp, uncompressed** | 1D cel-quantisation gradient, dark→bright |
| Terrain FVF | `0x152` | animated multi-texture terrain vertex format |
| Terrain water animation gate | **≥ 50 ms** timer | static vs UV-scrolling water variant (shared with day/night gate — see §8.4) |
| Composite weights | c0 = edge, c1 = bloom | `finaldx8.psh` scalars |
| Default ("missing") texture | 2×2, bytes per texel ~ (0x78,0x78,0x78,0x50) | grey/half-alpha placeholder when an actor has no texture |
| Render-target fallback dims | 1024 × 1024 | when back-buffer dims unavailable |

## 3.5 Scene-graph traversal and drawable layers — (confirmed)

The in-scene draw order is **not** a hard-coded "terrain then actors then FX" sequence; it is the order of nodes
in the visible cull set, grouped by the render state each node installs. The scene graph is authored so opaque
world geometry draws before alpha-blended FX. Distinct per-class render routines implement each layer:

| Layer | Behaviour (confirmed values) |
|---|---|
| Terrain (animated, multi-texture) | sets FVF `0x152`; walks visible cells, 4 corner sub-passes per cell; dest-blend toggles INVSRCALPHA↔ONE between passes; static vs ≥50 ms UV-scroll (animated water); fixed-function |
| Solid batched geometry | opaque mesh batch with explicit src/dest blend |
| Skinned actors (characters) | **CPU-side skinning** (the dynamic vertex buffer is rebuilt before draw); uploads **one composite WVP matrix** to vertex-shader constant c0; stride 32; binds the toon shader pair. **Not** a bone-matrix palette |
| Billboards / sprites | camera-facing quads |
| Particle effects | default blend SRCALPHA/INVSRCALPHA, additive mode SRCALPHA/ONE; **no per-particle back-to-front sort** (relies on alpha smear) |
| UI / HUD | drawn last, on the back-buffer, in screen-ortho space, after compositing (§3.3) |

## 3.6 Cel-shading bind contract — (confirmed)

Per skinned actor, after CPU skinning and WVP upload: bind the **base albedo texture to stage 0** and the
**toonramp LUT to stage 1**, set the toon vertex shader, and pick the pixel shader — a **normal cel tone** or a
**stealth/invisible variant** by a boolean. Once per frame the toon pass uploads **7 vec4 lighting/material
uniforms to vertex-shader registers c4..c10**: two model-space light directions (c4/c5), two light colours
(c6/c7), a positive clamp scalar (c8.x), a luminance-weight vector (c9), and material diffuse (c10). The vertex
shader transforms the already-skinned vertex by WVP and emits an N·L luminance scalar on an output texcoord; the
pixel shader uses that scalar to sample the 256×1 toonramp LUT, producing quantised cel bands.

## 3.7 Sample-byte evidence — (sample-verified)

`toonramp.bmp` is a real 824-byte file: a **256 × 1, 24-bpp, uncompressed RGB** ramp running from a dark band
to a bright band — exactly the 1D LUT the luminance scalar indexes. The toon vertex shader and the glow-falloff
"power" pixel shaders were re-read and are consistent with `formats/shaders.md`.

## 3.8 Render — open items

1. The config-supplied blur/glow pixel-shader filename used in Phase C.
2. Whether the shipped `power*` glow shaders are used by an alternate/legacy or quality-gated glow path
   (the live Phase B is fixed-function).
3. Exact render-state ids passed in the full-screen-quad passes (likely a colour-write/SRGB combination).
4. Exact authored intra-scene draw order vs purely render-state/node-order separation.
5. Precise vertex-declaration element table (the trailing N·L luminance channel).
6. The composite/cel pixel-shader byte math (`finaldx8.psh` / `dotoonshading*.psh` not in the sample set).
7. Runtime values of the bloom downscale divisors.

---

# 4. Camera constants (five view modes)

The camera is **purely client-side** — no camera state crosses the wire. The **camera object** owns
projection/FOV and produces the cull frustum; a family of per-mode **manipulators** position and orient it each
frame. View-mode behaviour and the input pipeline are specified in `specs/camera_movement.md`; this section pins
the exact per-mode constants. **Correction carried into this spec:** the camera is a **fixed-radius orbit**, not
a variable-length spring arm — there is **no per-mode follow-distance scalar and no min/max distance clamp**.
"Zoom" keys change **elevation**, not radius.

## 4.1 Shared base seed and orbit model — (confirmed)

All five in-world manipulators chain a common base seed, then override a few fields. Free parameters are **yaw**
and **elevation**; the follow radius is the fixed magnitude of the eye-offset vector.

| Parameter | Seed value | Notes |
|---|---:|---|
| Speed / time-scale factor | 1.0 | |
| Accumulated **yaw** | **−π/6 = −0.5236 rad (−30°)** | base for Third/First/Static/Event |
| Yaw-orbit angle | 0.0 | (First/Event override to π) |
| Up / axis basis | (0, 0, 1) | |
| **Eye-offset seed** | **(−750.0, 0.0, +500.0)** | magnitude **≈ 901.39 units** = the fixed orbit radius |
| Focus / look-at Z | −40.0 | base/Third |
| **Elevation / pitch** | −40.0 (overridden per mode) | the orbit elevation parameter |
| Pitch-overflow spill bucket | 10.0 | **not a follow distance** — a mouse-pitch overflow accumulator clamped to [0, 27] |

> The "spill bucket" value of 10.0 is explicitly **not** a follow distance; an earlier reading mislabelled it.
> The follow radius is the eye-offset magnitude (≈ 901 units), fixed for all non-gamble modes.

## 4.2 Shared smoothing / clamp pool — (confirmed)

| Constant | Value | Role |
|---|---:|---|
| Time-delta scale | **1e-3** | ms → s |
| Secondary (slow) time scale | **1e-4** | decay path while a mouse-drag accumulator is active |
| Keyboard input gain | **0.3** | per active frame |
| No-input friction | **0.6** | rate × 0.6 each idle frame (Static uses **0.8**) |
| Static zoom gain / occlusion pitch step | **50.0** | |
| Pitch-rate clamp | **±4.0** | rate bound on the pitch integrator |
| Zoom-rate / orbit-step clamps | **±0.1** | rate bounds |
| Decay-path clamp (orbit step) | **±1.0** | applied before the ±0.1 clamp |
| **Elevation angle clamp** | **[−90.0, −12.0]** | the real pitch limit (degrees-ish) |
| **Absolute yaw clamp** | **[−π/2, +π/2]** | base bound |
| Third yaw upper ease | **0.9** | Third upper-yaw = (π/2 · 0.9) ≈ **+1.4137** |
| Rate dead-zone | **1e-3** | snap rate to 0 below this |
| Terrain-collision camera lift | **3.8** | eye Y ≥ terrain + 3.8 |
| Collision Y-bias step | **2.0** | added to clamped terrain Y |
| Yaw-rate forced value on hard hit | **−0.01** | kills drift when grounded |
| Mouse-axis sensitivity | **1e-6** | raw axis delta → pitch rate |
| Mouse-drag pitch gain | **5e-4** | cursor-Y delta → pitch rate |
| Wheel / other-button zoom scale | **0.01** | |

## 4.3 Per-mode seeds and behaviour — (confirmed)

| Mode | Default pitch | Focus Z | Eye-offset | Yaw-orbit seed | Orbit radius | Terrain collision |
|---|---|---:|---|---|---:|---|
| **Third** (default, over-shoulder) | −π/6 (−30°) | −40 | (−750, 0, 500) | 0 | ≈ 901.39 | yes (height clamp + occlusion) |
| **First** (first-person) | −π/6 (−30°) | −55 | (−750, 0, 500) | π | ≈ 901.39 | no |
| **Static** (fixed-angle follow) | −π/6 (−30°) | −55 | (−750, 0, 500) | 0 | ≈ 901.39 | no |
| **Gamble** (orbit, betting minigame) | −π/3 (−60°) | −160 | **(24097.46, 0, 55694.43)** | 0 | ≈ **60684.08** | yes (world-AABB probe) |
| **Event** (scripted / cutscene) | −π/6 (−30°) | −55 | (−750, 0, 500) | π | ≈ 901.39 | data-driven (built-in curve) |

Per-mode notes:

- **Third** runs the full pipeline; yaw clamp **[−π/2, +1.4137]** (asymmetric, 0.9-eased upper); elevation
  **[−90, −12]**; friction 0.6, gain 0.3. The only mode using both collision behaviours.
- **First** shares Third's input pool; the eye sits at the player head so the trailing distance collapses; no
  terrain collision; yaw-orbit seeded to π (flips initial facing 180°).
- **Static** polls only the elevation keys; gain **50.0**, friction **0.8**; yaw fixed (no orbit); no terrain
  collision; tracks the local player position only.
- **Gamble** orbits at a large radius (≈ 6e4 units) to frame a betting board; yaw clamp **symmetric ±π/2** (no
  0.9 ease); fetches the world-AABB and has a collision/height response; orbit input is UI-driven.
- **Event** positioning comes from a **built-in 17-entry × 3-float orbit-curve table compiled into the client
  binary**, indexed by `mapRegionIndex % 17`. The map region index (0..31) is set from the server's world-state
  packet. A second sub-mode (cinematic integer = 2) orbits around the player's last network position for a fixed
  duration of **12 000 ms**. Duration otherwise comes from a motion-clip's length. The player loses manual
  control while this mode is active. **This camera is NOT driven by a `.scr` data file and NOT Lua-scripted.**
  (CODE-CONFIRMED; see §7.4 for the server-packet source of the region index)

A separate **Select** (character-select preview) manipulator is outside the in-world set: it clears the yaw /
elevation / eye-offset and builds a multi-waypoint preview camera path from a literal world-position table baked
into the constructor. Approximate waypoints: **(−1532, 137, −3254)**, **(−1705, −3508, 87)**,
**(−1577, −3590, 104)**, with span constants **2048 / 6144 / −1536**. (CODE-CONFIRMED for values)

## 4.4 Terrain collision push-in (Third; probed by Gamble) — (confirmed)

1. World (X,Z) → terrain grid: `index = 10000 − (int)(coord × −1/1024)`, cell size **1024.0**, with a −1024.0
   pre-bias for negative coords (floor emulation). (Matches `formats/terrain.md` tiling.)
2. Sample the bilinearly-interpolated terrain height at the eye (X,Z); the no-terrain sentinel is
   **−3.4028e38** (→ no hit).
3. On a hit, the eye **Y is clamped to `terrainHeight + 3.8`** with a `+2.0` bias step; on a hard hit the
   yaw-rate is forced to **−0.01**.
4. On occlusion (probe miss / line blocked), elevation is nudged by **50.0 · dt** toward keeping the target
   visible, plus the +2.0 bias and a small 0.01 correction.

So the "push-in" is a **vertical lift (+3.8, +2.0 bias)** plus an elevation nudge on occlusion — there is **no
horizontal radial pull** (the radius is fixed).

## 4.5 Projection / FOV — (CODE-CONFIRMED — reconciled)

**The authoritative in-world gameplay camera uses vertical FOV = 65°, near = 5.0, far = 15000.0.** These values
are constructed in the in-world scene-build path at the same time the five manipulators are wired to the camera
object. This is the camera engineers must implement. (CODE-CONFIRMED)

**Aspect correction — (CODE-CONFIRMED).** The 65° figure is the base vertical field of view, but the value
actually handed to the camera's projection setup is **aspect-corrected**: it is divided by the live render
aspect ratio `renderWidth / renderHeight` (the same configured render width/height the window/device use, from
`OPTION_WIDTH` / `OPTION_HEIGHT`, §0.2). The projection initializer receives `(65° / aspect, aspect, near 5.0,
far 15000.0)`. Implementations targeting a non-4:3 surface must reproduce this aspect division, or the apparent
vertical field of view will drift from the original at other resolutions.

Two additional projection values appear in the binary but belong to a **separate generic projection initializer**,
not the camera the in-world manipulators drive: a seed of **60°** vertical FOV with a near-constant of
**10000.0**, and a **π/8 half-angle constant** (consistent with 45° vertical FOV). These are the source of the
previously-unreconciled "60 / 45 / 65" ambiguity — the 60° and 45° figures belong to the generic path.

> **Open item §4.6.3 is closed.** Implement the gameplay camera at **65° vertical FOV, near 5, far 15000**.
> Live-feel still capture-unverified; expose as config but default to 65°.

## 4.6 Camera — open items

1. Action polarity / axis binding (which ± of each input pair is up/in vs down/out; field→semantic mapping).
2. The π yaw-orbit seed's visual effect (inferred 180° initial facing flip) — not proven against a running client.
3. ~~Authoritative runtime FOV (60 / 45 / 65 unreconciled)~~ — **RESOLVED**: 65° CODE-CONFIRMED (§4.5).
4. The constant mode-tag value present in every manipulator ctor — meaning unknown.
5. Gamble far-orbit numbers' UI driver (orbit angle source not traced).
6. ~~Event keyframe/path format~~ — **RESOLVED**: built-in 17-entry curve table (§4.3 Event note).
7. `CAMERA_XZ` / `CAMERA_XYZ` saved-option semantics (2-axis vs 3-axis follow; may relate to the eye-Y clamp).

---

# 5. Movement and collision constants

Client movement is click-to-move: a screen pixel is unprojected to a world XZ ground point, tested against the
2D solid map, integrated per frame, and reported to the server as move-request packets. **World Y is forced to 0
for simulation and never sent by the server** — the terrain heightmap is a *visual* vertical-placement surface
only. The behavioural pipeline and the move-request packet shapes are owned by `specs/camera_movement.md` and
the committed `packets/*.yaml` (move-request 2/13, actor-movement-update 5/13); this section pins the constants.

## 5.1 Speed model — (confirmed)

There is **no hard-coded numeric speed**. The per-frame integrator contains exactly one code-literal speed
factor: **× 4.0** (forward step distance per frame = `moverSpeedScalar × 4.0`). The actual walk/run scalar is
**data-driven** from a per-map config table keyed `MAP_SPEED` (load-error log "MAP_SPEED data load error"), so
concrete walk-vs-run units are map config data, not code constants (hand to the asset/config-table specs).

Walk vs run is expressed three ways: a lifecycle-state field (**2 = walk, 3 = run**; also 0 = uninit,
1 = refreshing, 8 = dead/scripted), a run-flag byte (`== 1` ⇒ running; packed onto the wire), and the per-frame
speed scalar feeding the ×4.0 step. The base move-speed multiplier defaults to **1.0**.

**Turn rate:** there is **no angular-velocity constant**. The client computes an instantaneous facing each step
and snaps to it (no yaw interpolation over time).

## 5.2 Heading — (confirmed)

**Heading = atan2(Δx, Δz) in radians**, standard CRT range **−π .. +π** (produced via the CRT atan2 path, not a
fixed-scale integer angle). Inputs are the interpolation-target XZ minus the current/last-network XZ in the
engine's XZ frame. The server movement-update echo carries a yaw in the same radian convention.

## 5.3 Move-issue path constants — (confirmed, sample-verified where noted)

| Constant | Value | Role |
|---|---:|---|
| Per-frame step multiplier | **4.0** | forward step distance = speed scalar × 4.0 |
| Move-issue clamp distance | **12.0** | when target is far, the unit delta is scaled to at most 12.0 per issue |
| Clamp threshold (squared) | **144.0** (= 12²) | compare `dist² > 144.0` before clamping |
| Move dead-zone (squared) | **4.0** (= 2 units) | ignore moves with `dist² ≤ 4.0` |
| Pick-ray / mover speed seed | **1000.0** | max screen-pick distance / mover seed |
| Click re-issue throttle | **100 ms** | minimum interval between re-issued click moves |
| Click-marker texture id | **380000000** | highlight/cursor texture dropped at the destination |
| Move-request payload size | **16 bytes** | matches the committed move-request packet spec |

**Move-issue flow:** click → unproject to XZ ground (pick seeded with 1000.0) → begin-move (clamp the delta to
12.0 when `dist² > 144.0`) → commit (only if `dist² > 4.0`, i.e. ignore sub-2-unit moves; gated by a cooldown
and a busy/lock flag) → drop the click marker → integrate per frame.

Two input ids drive the click handler: id **1013** = primary walk hold (left-mouse-hold, the main walk path);
id **1012** = a secondary input whose branch sends a **stop frame** at the current position (halt in place).

## 5.4 Send cadence (server-reconciliation heartbeat) — (confirmed; hard-cap UNVERIFIED)

A send-worker thread loops with **`Sleep(10 ms)`**. Two channels each only **warn** when overdue:

| Channel | Overdue-warning threshold |
|---|---:|
| Move | **200 ms** |
| Proxy | **400 ms** |

The move heartbeat fires at most once per distinct `timeGetTime()` millisecond. On each heartbeat a global
parity counter alternates an **X dither of ±20.0 units** (odd → +20, even → −20) so each report is a slightly
different point (keeps server position state fresh / defeats a static-position dedupe). **Important:** the
200 ms / 400 ms values are *overdue-warning thresholds*, **not** proven hard rate caps — no rate-limit literal
beyond the warnings was found. Whether 200 ms is also a hard period needs a capture (UNVERIFIED).

An anti-speedhack telemetry tolerance of **1025 ms** (server-time/cycle diff limit before flag) is carried from
a prior pass (HIGH confidence, not re-byte-verified here).

## 5.5 Per-frame integration and server snap — (confirmed)

Each frame the mover faces the destination, copies the facing quaternion, and advances by
`moverSpeedScalar × 4.0` rotated by the facing. If the candidate overshoots the destination **and** the collision
subsystem reported a block, it **snaps to the corrected hit point** (wall-slide / stop response); otherwise it
snaps to the destination. The resolved position is propagated to couple/mount partner actors. A move-request
frame is emitted each step.

Server reconciliation (cite the catalog, not re-derived): C2S move-request (16 B) — client emits, server
echoes; S2C actor-movement-update (40 B) — drives interpolation for remote actors and is the **authoritative
position correction** for the local player (a specific motion-code value triggers an instant-snap branch).
World Y is never on the wire. A local snap-to-valid helper rewrites current and destination to a corrected
point when a target cell is invalid/out-of-bounds.

## 5.6 Collision against solid map (`.sod`) — (sample-verified)

Collision is **strictly 2D in XZ** (no Y). It is a swept test of the movement line segment against static solid
line segments using `Z = slope·X + intercept` (or `X = constant` for vertical segments). The on-disk `.sod`
format is owned by `formats/terrain.md` / the asset specs; the verified record strides from real samples are:
a file header `u32 solidCount`, then per-solid records of **108 bytes** each (AABB at +0x00, then a `u32
segCount` and **48-byte** segment records: AABB at +0x00, slope at +0x20, fixed-X at +0x24, intercept at +0x28,
type-flag at +0x2C). Three real `.sod` samples passed the size check; all sampled segments were non-vertical
(`type_flag = 0`); the vertical path is code-present but sample-unconfirmed.

The query maps the move-segment AABB into a **16 × 16 grid of quadtrees** (clamped 0..15) and keeps the
**nearest** intersection. Cell-grid math (confirmed):

| Constant | Value | Role |
|---|---:|---|
| Cell size | **1024.0** | world units per terrain/collision cell |
| World→grid reciprocal | **−1/1024 = −0.0009765625** | with a −1024.0 pre-bias for negative coords |
| Cell index base | **10000** | base index for cell (x,z) ids; matches `d###x100##z100##` filenames |
| Per-cell neighbour stride | **5** | center + 4 neighbours; the move is asserted to cross at most one cell |

## 5.7 Visual height sampling (`.ted`) — (sample-verified, visual-only)

The `.ted` heightmap (owned by `formats/terrain.md`) is a **65 × 65 = 4225** float grid (the first 16 900 bytes
of the file), row-major with **rows = constant Z**, at a **vertex spacing of 16.0 world units** (64 quads ×
16 = one 1024-unit cell). World vertex position `= (mapX − 10000) × 1024.0 + col × 16.0`; heights are direct
world-space Y (no Y multiplier). Normals decode as `i8 / 127.0`. A seam-continuity test across the Z boundary
of two real adjacent tiles showed `max |Δ| ≈ 0.0012` units (seamless). **This surface is rendering-only**:
server movement and collision use the 2D `.sod` quadtree exclusively; simulation Y is 0.

## 5.8 Movement — open items

1. Per-map walk/run numeric speeds (data-driven via the `MAP_SPEED` table — needs config tables or a capture).
2. Whether 200 ms is a hard rate cap or only a warning threshold (needs a capture).
3. The two trailing padding bytes of the move-request payload (almost certainly filler; capture welcome).
4. The runtime bilinear height-sample-at-arbitrary-XZ function (heightmap format pinned; sampler not isolated).
5. Vertical `.sod` segments (`type_flag == 1`) — code path present, no sample.
6. Whether the local player's authoritative correction also arrives via a separate local-state-sync channel.
7. Re-confirm the exact anti-speedhack 1025 ms limit if a clean spec needs it.

---

# 6. Quest system

The quest system is **server-authoritative**: the client holds dialog/requirement data tables and renders a
quest log + completion verdicts, but kill/collect/talk objective types and reward grants live on the server. The
client sends a single unified quest-action packet and receives a quest-list snapshot and a completion verdict.
Packet byte layouts (2/28, 5/68, 5/73) are owned by `opcodes.md` + `packets/*.yaml` + `specs/quests.md`; this
section specifies the client data model and flow. **All quest/dialog text is CP949.**

## 6.1 Networked surface — (confirmed; on-wire widths UNVERIFIED)

| Direction | Message | Role |
|---|---|---|
| C2S | quest-action (proposed name; not yet cataloged — defer to the protocol spec-author) | unified accept / proceed / give-up |
| S2C | quest-list | quest-log snapshot (452-byte body) |
| S2C | quest-complete | completion + reward verdict (344-byte body) |

The client-side quest-action body is a 12-byte block: a **sub-action byte (+3 padding)**, then a **u32 npc-kind**
and a **u32 quest-id** (both full 32-bit on the client side; on-wire zero-extension is capture-unverified). The
sub-action enum (each at a distinct send site): **2 = ACCEPT, 3 = PROCEED/CONTINUE, 4 = GIVE-UP/ABANDON**
(values 0/1 not observed). This refines an earlier note that read the trailing fields as bytes.

## 6.2 Data tables — (confirmed loaders; field offsets IDA-derived)

Three lookup tables (keyed maps) feed quests; record sizes are hard facts from the loaders:

| Table | Source file | Record size | Key |
|---|---|---:|---|
| Quest templates | `data/script/quests.scr` | **4960 bytes** (~366 records) | quest id |
| NPC dialog/step records | `data/script/npc.scr` | **404 bytes** (~2510 records) | dialog/npc id |
| Mob/NPC templates | mobs-class template (`mobs.scr`, ~1.95 MB) | large (≥ 1168 B) | actor descriptor field |

**npc.scr dialog record (404 B):** a 20-byte header followed by **6 × 64-byte CP949 text lines** (the
"6 dialog lines"). The header carries the map key (+0x00), a **group-key (+0x04)**, and a **step-index (+0x08)**.
Multi-step dialog is a chain of sibling records sharing the group-key: PROCEED finds the sibling with
`stepIndex == cursor + 1`, BACK finds `cursor − 1`; the "%d / %d" objective counter is `(cursor+1) / total`
where total counts sibling records with the same group-key.

**quests.scr template (4960 B) — requirement block** (offsets within the record; player-state compared):

| Offset | Type | Field | Gate behaviour |
|---|---|---|---|
| +0x00 | u16 | quest id | matched in the active list ("already accepted") |
| +0x02 | u8 | category | duplicate-category gate vs other active quests |
| +4936 | u32 | prereq / chapter id | prerequisite gate |
| +4944 | u16 | min level | if nonzero and > player level ⇒ "need level" |
| +4946 | u16 | max level | if nonzero and < player level ⇒ "level too high" |
| +4948 | u8[5] | required class | per-index class flags |
| +4953 | u8 | required gender/faction | mismatch ⇒ blocked |
| +4954 / +4955 | u8 / u8 | required stat min / max | 1..7 stat gate |
| +4956 | u8 | required flag | flag gate |

**quests.scr dialog handles** (each is an npc.scr map key — distinct offer / in-progress / turn-in text sets):
offer handle at **+0x48**, active handle at **+0x54**, complete handle at **+0x58**.

**In-progress objective sub-array (PARTIAL):** an objective-entry count (u8) at +100 and an array at +104 with
**240-byte stride**, each entry carrying a target/state u16 (~ +124) and a value table (~ +76). This is the
closest thing to a client-side "objective" structure, but it does **not** label the kill/collect/talk type —
that distinction is server-authoritative. (Needs a `quests.scr` sample to byte-confirm.)

## 6.3 NPC ↔ quest binding — (confirmed)

The mob/NPC template carries two parallel arrays indexed by an NPC "kind" slot:
- **+1084**: a `u16[]` of **offered quest ids** → keys into `quests.scr`.
- **+1168**: a `u32[]` of **offered dialog handles** → keys into the npc.scr dialog map.

The full data chain:

```
npc.arr[mob_id] ──key──► mobs.scr template
                              ├ +1084 u16[] offered quest ids ──► quests.scr[quest_id]
                              └ +1168 u32[] dialog handles  ───► npc.scr dialog map (6 CP949 lines)
quests.scr[quest_id]
   ├ +0x48 / +0x54 / +0x58 dialog handles ──► npc.scr dialog map
   ├ +0x02 category, +0x00 id, +4936 prereq/chapter
   └ +4944/4946 level, +4948 class[5], +4953 gender, +4954/4955 stat gates
```

**npc.arr placement record (28 bytes, little-endian) — (sample-verified):**

| Offset | Type | Field | Confidence |
|---|---|---|---|
| +0x00 | u16 | mob_id (keys the mob template) | confirmed |
| +0x02 | u16 | field_1 (level? sub-type? — identical in both samples) | UNVERIFIED |
| +0x04 | f32 | world X | confirmed |
| +0x08 | f32 | world Z | confirmed |
| +0x0C | f32 | rotation about Y (radians) | partial |
| +0x10 | u32 | spawn type | confirmed |
| +0x14 / +0x18 | u32 / u32 | unknown | UNVERIFIED |

The arr record itself carries **no direct quest id**; the binding is via `mob_id → mobs.scr +1084`. A separate
selector returns an alternate (event/tutorial) dialog kind when an NPC's spawn-type field is **7** under a
global event state — i.e. event-gated dialog keyed off `spawn_type == 7` plus global event flags.

## 6.4 Accept / proceed / give-up flow — (confirmed)

State summary (client side):

```
offer dialog (phase byte; text from the quest offer-handle → npc.scr 6 lines)
  └ accept  → C2S quest-action sub=2 → (server) → S2C quest-list refresh (quest now active)
in-progress (a step cursor walks the npc.scr step chain by (group_key, step_index))
  ├ proceed → C2S quest-action sub=3   (only when the panel dialog phase is "ready-to-proceed")
  └ give up → C2S quest-action sub=4 + local clear
turn-in (quest complete-handle dialog) → (server) → S2C quest-complete verdict
```

**Accept gate (corrected):** the accept widget passes a prerequisite/duplicate gate, then a **billing gate**:
the send happens iff the player has a positive billing/cash status **or** `playerLevel < 26`. In other words,
**26 is the level at which the premium gate engages** — below 26 quests accept free; at level ≥ 26 a positive
billing status is required. (An earlier note read this backwards as a minimum level.) The availability gate
also rejects a quest already held and a quest whose category is already held.

## 6.5 Quest-log and completion — (confirmed; capture-marked offsets UNVERIFIED)

- **Quest-list (452-byte body)** populates a local quest-log mirror: two 10-entry slot tables (32-byte stride,
  flag at entry+8), a 20-entry quest-entries table (32-byte stride, u32 quest id + a CP949 name up to 17 bytes),
  and scalar active/panel flags. When the active/tracking flag transitions 0→nonzero the tracking panel opens
  (with SFX **862300001**); nonzero→0 closes it.
- **Quest-complete (344-byte body)** acts only when a "complete-mode" field equals 1, then on a reward-state
  byte: **1 = GRANT** (show the panel, positive SFX **910036000**), **2 = DENY/FAIL** (negative SFX). The body
  remainder (beyond the mode/state fields) is copied wholesale and not field-decoded by the client — **reward
  granting is server-side**; actual item/exp/gold arrive via side-channel opcodes. The completion panel
  double-buffers the previous body and uses a phase byte {0 idle, 1 showing, 2 closed}.

## 6.6 Tutorial subsystem and `tutor.lua` — (confirmed)

- **`data/script/Tutor.scr`** (note the capital T) is a definition table of **1660-byte records** (~86 lessons):
  an id (u32) at +0x00 and a description string at +28; loaded into a tutor map at startup.
- **`data/script/tutor.lua`** is loaded as a **plain on-disk file** (not via the `.pak` VFS) into the single
  global Lua state. Two bindings use it: one builds the tutorial panel widgets and (re)loads `tutor.lua` to
  supply panel content/layout; the other pulls a **numbered string table** out of `tutor.lua` (via the script's
  `getTableSize` / `getTableString` globals, UTF-8-decoded). Lessons paginate by numeric string-table id —
  next = id+1, previous = id−1.

This matches the overall Lua model (scripts = data/config/i18n; host C++ = logic). The mapping from a
`Tutor.scr` record id to a `tutor.lua` string-table id is assumed equal but not byte-confirmed.

## 6.7 Quest — open items

1. Objective **type** (kill/collect/talk) — no client-side type enum or target-id/count fields found; the
   distinction is server-authoritative (text = npc.scr lines, progress = server counter + step cursor).
2. The bulk of the 4960-byte quest record (between ~+90 and +4936, including the +104 objective array) is
   undecoded; no reward item/exp/gold fields are client-read. Needs a `quests.scr` sample.
3. npc.scr header bytes beyond the proven group-key/step-index/key.
4. mobs.scr +1084 / +1168 array lengths (how many quest slots per NPC) — stride proven, dimension not.
5. On-wire widths/zero-extension of the quest-action npc-kind / quest-id (no quest C2S capture).
6. Active-quest list A vs B semantics (both hold held ids; split unproven — e.g. main vs repeatable).
7. Whether the quest-complete body remainder carries a reward list or is pure display.
8. npc.arr field_1 (+0x02) role (level vs sub-type vs faction).
9. Whether a `Tutor.scr` record id equals its `tutor.lua` string-table id.

---
---

# 7. Scene state machine — (CODE-CONFIRMED)

The master scene lifecycle is implemented as the body of the application entry point itself — the
`WinMain` function **is** the state machine. It is an infinite dispatch loop over a single
engine-state integer. The dispatch is a **bounds-checked (`engineState <= 7`) `switch` with exactly
8 cases (states 0..7) plus a `default` arm** — an 8-entry jump table. Every interactive screen is
modelled as one of these 8 states.

> **State-count correction (confirmed).** Earlier text framed this as a "nine-value enumeration
> (0..8)". It is **8 top-level cases, states 0..7.** The value **8 is a *sub-state* value** (the
> `GameState` sub-field defaults to 8, and the terminal/teardown paths set field 0 to 7 with the
> sub-field at 8 — e.g. state 6 → sub 8, state 7 → sub 8). It is **not** a 9th top-level case: the
> exit/teardown logic lives in the shared tail reached from cases 6/7/`default`, keyed on the
> sub-field value 8, not on a `case 8:`. Read every "states 0..8 / nine-state lifecycle" mention in
> this and sibling docs as "states 0..7 (8 cases); 8 is a sub-state value".

Cross-spec ownership: the UI control flow inside each front-end screen is
detailed in `specs/frontend_scenes.md`; the resource loading pipeline driven by state 2 is
detailed in `specs/resource_pipeline.md`. This section pins the engine-level transition mechanics.

## 7.1 Engine-state struct — (CODE-CONFIRMED)

A three-integer struct (`GameState`) is the sole source of truth for which scene is live. Fields:

| Field index | Offset | Type | Role |
|---:|---:|---|---|
| 0 | +0x00 | i32 | **engine state** (0..7 — the current scene enum value; the `switch` dispatches on this) |
| 1 | +0x04 | i32 | **sub-state / error code** (transition context; **ctor default = 8**) |
| 2 | +0x08 | i32 | **error detail** (the offending result code, when state = 7) |
| — | +0x0C | u8 | **debugmode flag** (set once at startup from `game.lua`; read-only thereafter) |

The constructor initialises the struct to `{ state = 0, sub-state = 8, error-detail = 0, debugmode = 0 }`.

> **Sub-state default correction (confirmed).** The sub-state field (index 1) constructs to **8**,
> not 0 — an earlier "0 = none" framing was inaccurate at construction. The value 8 is the engine's
> "no specific sub-state" default and is also the sub-field carried into the terminal teardown tail
> (states 6/7 → sub 8). Because the field is overwritten on most transitions, the boot default is
> rarely observed, but it is **8**.

The debugmode byte gates floating damage-text overlays and other developer-visible UI. It is never
a transition driver; all transitions are driven by writes to field 0 (and optionally field 1).

## 7.2 Loop-run flag and the transition commit mechanism — (CODE-CONFIRMED)

`WinMain` runs the game loop via `Engine_MainLoop`. That loop spins while a **global one-byte
run-flag** is non-zero. A scene "finishes" when something writes the next engine-state value into
the `GameState` struct **and then clears the run-flag** (a one-liner helper function). Control
returns to `WinMain`, which reads the new engine-state and dispatches the next case of the switch.

This is the heartbeat of every transition: **write next-state → clear run-flag → WinMain
re-dispatches**. The run-flag is also cleared by `WM_QUIT` (window close), which causes the
active screen to exit and `WinMain` to read whatever state it holds.

## 7.3 Per-case behaviour — (CODE-CONFIRMED)

Each `switch` case is a **"build + run" block**, not a passive state. The case first writes the
*next* engine state, then constructs that phase's handler object, registers it as an event target,
calls `Engine_MainLoop`, and on loop-exit tears the handler down. The next iteration of the outer
`while(1)` then dispatches the case matching whatever state was written last.

The `switch` has **8 cases (states 0..7) plus a `default` arm** (bounds-checked `engineState <= 7`).
The teardown value **8 is not a switch case** — cases 6/7 and `default` write field 0 to **8** and
then the **shared exit tail** (reached when the loop returns) keys on field-0 `== 8` to run the final
teardown and `return` from `WinMain`. (A field-0 value of 8 also falls to `default` on any further
dispatch.)

| State | Name | Writes next state | Constructs (approx. size) | Loop | On exit / teardown |
|---|---|---|---|---|---|
| **0** | Initialisation | `= 1` | sizes window from display config; stores `16` in an engine constant | falls to state-1 body | — |
| **1** | Login | `= 2` | `LoginWindow` (~1368 B); loads `msg.xdb`; builds 15 Hangul font slots | `Engine_MainLoop` | `LoginWindow_End`; reads billing username/server; destructor |
| **2** | Load | `= 4` or `= 3` | `LoadHandler` (~536 B); spawns async worker thread; starts loading SFX | `Engine_MainLoop` | `LoadHandler_End`; stamps billing timestamp (billing field = current ms) |
| **3** | Opening | `= 4` | `COpeningWindow` (~720 B); builds intro window | `Engine_MainLoop` | `COpeningWindow_End` (unregisters scene view only); destructor |
| **4** | Select | `= 5` | `SelectWindow` (~6280 B); character-select UI + preview actor + Select camera | `Engine_MainLoop` | `SelectWindow_End`; clears SelectWindow singleton |
| **5** | In-game | `= 4` | `MainHandler` (~200 B) + `BuildGameWorld` (camera rig + scene graph + services + HUD); enables networking | `Engine_MainLoop` | `MainHandler_End`; unregisters three event targets; destructor |
| **6** | Quit | `= 8` (sub 8) | — (calls engine shutdown routine) | — | falls to shared exit tail |
| **7** | Error | `= 8` (sub 8) | — builds error string from sub-state/detail; hides window; drops net connection; writes `error.log` (module list, timestamp, language, build date); shows modal dialog | — | falls to shared exit tail |
| default | (unknown / field-0 == 8) | `= 8` | engine shutdown | — | shared exit tail |

**Shared exit tail (states 6/7/`default` converge here):** when the loop returns, if engine state
field 0 is `8` run the final teardown (scheduler release, crash-logger close, OS resource cleanup)
and `return` from `WinMain`; otherwise clear the per-iteration scene pointer and loop again.

> **`OPENNING/SKIP` INI key** (the typo is authentic): when state 2 pre-decides the next state,
> it reads an integer from an INI section `OPENNING`, key `SKIP`. If true, state 2 writes `= 4`
> (skip straight to Select); otherwise `= 3` (run the Opening intro first). This decision is made
> once, at the post-login load, not at subsequent re-entries to state 2.

## 7.4 What is constructed and destroyed at each transition — (CODE-CONFIRMED)

| Transition | Constructed | Destroyed at previous scene exit |
|---|---|---|
| → 1 Login | `LoginWindow` tree (~340 widgets, `data/script/uiconfig.lua`); `msg.xdb` string catalogue; 15 Hangul font slots (DotumChe/Dotum/BatangChe); net handler singleton wired | (nothing prior) |
| 1 → 2 Load | `LoadHandler` + async loader thread; loading sprite; SFX **920100100** | `LoginWindow` (via `_End` + destructor) |
| 2 → 3 Opening | `COpeningWindow` (intro) | `LoadHandler` |
| 2 → 4 Select | (opening skipped; next case handles construction) | `LoadHandler` |
| 3 → 4 Select | `SelectWindow` (see below) | `COpeningWindow` |
| → 4 Select | `SelectWindow` (~6280 B): character-list UI, preview actor, **Select camera** (own multi-waypoint preview path, distinct from the in-world five manipulators) | previous Opening or `LoadHandler` window |
| 4 → 5 In-game | `MainHandler` (~200 B) + **`BuildGameWorld`**: `GPerspectiveCamera` (FOV 65°/near 5/far 15000), **exactly 5** `GViewPlatform` slots (no sixth — CAMPAIGN 16 confirmed), `GScene` (root, labelled "charater scene"), `GSwitch`, 5 camera manipulators, 4 layer nodes {2004,2005,2006,2148}; HUD panel tree activated; networking enabled | `SelectWindow` + Select camera + preview actor |
| 5 → 4 | (rebuild `SelectWindow`) | `MainHandler`, full scene graph, all 5 manipulators, local player actor |
| → 6/7 | error string / modal dialog | net connection dropped; engine shutdown |
| → 8 | — | final teardown: scheduler, crash logger, OS |

A previously-noted **reserved sixth GViewPlatform slot** — supposedly allocated in the in-world
scene build but never assigned — was **not** confirmed in this pass: the world scene-graph builder
allocates exactly **five** view-platform objects plus one switch node. If a sixth slot exists it is a
field allocated elsewhere, not in this builder. (static-hypothesis / capture-debugger-pending — see
§9.4 and §9.5.)

## 7.5 Complete transition table — (CODE-CONFIRMED)

### 7.5.1 Engine-internal (WinMain case body) transitions

| From state | Trigger | Next state | Sub-state |
|---|---|---:|---:|
| 0 Init | always (one-time startup) | 1 | — |
| 1 Login | case body (pre-loop) | 2 | — |
| 1 Login | window configuration fails | 7 | 1 |
| 1 Login | device / secondary init fails | 7 | 3 |
| 2 Load | `OPENNING/SKIP` INI = true | 4 | — |
| 2 Load | `OPENNING/SKIP` INI = false/absent | 3 | — |
| 3 Opening | case body (after intro) | 4 | — |
| 4 Select | case body | 5 | — |
| 5 In-game | case body (default return-to-select) | 4 | — |
| 6 Quit | case body | 8 | — |
| 7 Error | case body | 8 | — |
| default | unknown engine state | 8 | — |

### 7.5.2 Network-driven transitions

Written by the network-receive path; each also clears the run-flag.

> **The table-driven engine-state transition handler is `CharActionResult` (3/100), NOT 3/7.**
> Re-confirmed CAMPAIGN 16. `SmsgCharActionResult` (major 3 / minor **100**) is the handler that reads
> a result/action code and writes the engine state (rows below). The separate `SmsgCharManageResult`
> (major 3 / minor **7**) is a **Character-Select UI result only** (char delete / rename / select slot
> face/refresh; delete-confirm decrements the account char count and clears the slot, then resets the
> select scene) and **writes NO engine-state field** — it must not be wired to any scene transition.
> The "result code" rows below are `CharActionResult` (3/100) codes. See `opcodes.md` (3/7, 3/100).

> **Canonical 3/100 code table: see `handlers.md` §23.1** (anchor `263bd994`) for the complete
> per-code (state, sub-state, error-detail, side effect, string-id, timer-arm) mapping in BOTH
> modes. The rows below are the engine-state-transition VIEW only — they group 3/100 codes by the
> engine state they write; they are NOT a re-listing of the code set. **Critical (corrected
> 2026-06-22):** in **select-mode** the recoverable error family is **{1,2,3,4,5,7,22,23} → 7/5**,
> NOT just 1..4/7 — codes **22 and 23 are RECOVERABLE (7/5), not the fatal 7/8 out-of-range arm.**
> The 7/8 "hard error" select-mode row is the TRUE default/out-of-range set only (the jumptable
> default codes 212-219/228-231 and any code with no arm). In-world, every non-zero code → 7/8.

| Live scene | Trigger | Next state | Sub-state | Notes |
|---|---|---:|---:|---|
| 1 Login | **EnterGameAck (3/5)** received | 2 | — | auth OK → begin load. **State-agnostic:** the handler forces state 2 unconditionally; "from Login" is just where it is observed to arrive |
| 4/2 Select/Load | **CharacterList (3/1)** received | 4 | 8 | (re)enter select with fresh character list |
| 5 In-game (bootstrap) | **GameStateTick (4/1)**, no local player **and re-spawn from the descriptor FAILED** | 4 | — | world-state pre-spawn fallback; reached only when the local-player object is absent *and* descriptor spawn returns null (on success it builds the world and does not change state) |
| 4 Select | **CharActionResult (3/100) code = 0**, no local player | 6 | 8 | char op succeeded → quit/return path |
| 4 Select | **CharActionResult (3/100) recoverable error: code ∈ {1,2,3,4,5,7,22,23}** | 7 | 5 | char operation failed → **recoverable** Error sub-state; error-detail = code. **Code 23 also shows notice string-id 1604 and is excluded from the retry timer.** This is NOT the fatal 7/8 arm — see §23.1 |
| 4 Select | **CharActionResult (3/100) code 202/203/232** | 2 | — | create/rename/billing accepted → reload |
| 4 Select | **CharActionResult (3/100) publish-only: code ∈ {10,11,16,200,201,204-211,220-227}** | (no state write) | — | publish-code only; no engine-state transition (see §23.1) |
| 4 Select | **CharActionResult (3/100) true default/out-of-range: code ∈ {212-219,228-231} or no arm** | 7 | 8 | hard char-op error; error-detail = result code |
| 5 In-game | **CharActionResult (3/100) code ≠ 0**, local player present | 7 | 8 | in-game char error; error-detail = result code (tooltip set {1-7,22} per §23.1) |
| 5 In-game | **CharActionResult (3/100) code = 0**, local player present | 6 | 8 | in-game char op succeeded |
| any (state 2) | connection/handshake error during load | 7 | 2 | load-time disconnect |
| any (state 4/5/6/other) | disconnect | 7 | 8 (or 6) | generic disconnect → error / quit |

### 7.5.3 User-action transitions

| From state | Trigger | Next state | Sub-state | Notes |
|---|---|---:|---:|---|
| 1 Login | quit button / `game.ver` VFS mismatch confirmed | 6 | 2 | SFX **861010106** |
| 1 Login | enter-load / continue (login network machine completes) | (loop-break only) | — | Login case already wrote state 2; loop-break re-dispatches |
| 1 Login | login network-machine fatal | 6 | 8 or 7 | depends on failure type |
| 4 Select | confirm an **occupied** slot (enter world) | 2 (via 3/5) | — | Sends the **EnterGame request (1/9)**; the server's **3/5 EnterGameAck → state 2 (Load)** drives the transition (then Load → … → InGame 5). The case-4 entry **pre-writes 5** as the *no-network default* only — with a live server the 3/5 overwrites it with 2. So the live enter-world path is **4 → 2 → … → 5**, NOT a direct 4 → 5 (§7.9.5 happy path). *(CAMPAIGN 16 correction.)* |
| 4 Select | confirm a **blank** (`@BLANK@`) slot | (no state write) | — | opens the in-place character-create modal; create result comes back via 3/100 / 3/23 |
| 4 Select | quit / exit command | 6 | 8 | character-select command handler |
| 4 Select | "back/leave" command | (net request) | — | sends network leave; server reply drives state change |
| 2 Load | quit hotkey | 6 | 2 | scene-aware quit dispatcher |
| 5 In-game | explicit quit / logout (menu confirm or window close) | 6 | 8 | SFX **861010106**; logout cleanup |

**Scene-aware quit dispatcher:** a single quit-router function reads the current engine state and
picks the appropriate teardown — state 2 → quit (state 6 / sub 2); state 5 → logout (state 6 /
sub 8); state 4 → sends a network leave message (no direct state write).

## 7.6 The LoginWindow handshake sub-state machine — (CODE-CONFIRMED — corrected CAMPAIGN 16)

The `LoginWindow` drives an internal handshake/UI sub-state counter, distinct from the engine state.
(Earlier text here paired this with a *separate* "UI page index" at `+0x17C`; re-confrontation proved
`+0x17C` and `+0x238` are the **same physical field** — the `+0x17C` accesses are made through the
embedded CommonLoginWindow sub-object pointer at base `+0xBC`, and `0xBC + 0x17C = 0x238`. So the
old "`+0x17C` UI-page vs `+0x238` network-machine" split is not two offsets. The fuller behavioural
model — whether the input/action router writes a distinct MAIN form/page state that gates widget
interactivity alongside this tick-driven drive state — is owned by `specs/frontend_scenes.md §1.5`;
this section covers the tick/drive field.)

- **Handshake/UI drive sub-state** (object field `+0x238`): a dedicated tick function advances through
  sub-states **1..41** covering the intro stinger, curtain, the resting credential form, the PIN
  modal, the server-list fetch/pick, and the channel-join hand-off. Key values:
  - **1** intro stinger → **2/3** curtain → **4..6** form idle (6 = the plain resting credential form)
  - **30** quit
  - **31** raise the PIN modal, **32** poll the PIN keypad (the modal older notes called "EULA")
  - **33/34/35** server-list fetch → **37** server-list interactive plate-pick → **38** select
  - **39/40** channel-endpoint fetch → **41** build the join key + secure context (hand-off to Load)

On submit (~sub-state 40) the window **pre-arms engine state 7** (Error) and schedules a 30 000 ms
connect-timeout, then starts the real net engine. The **server** drives the success path — the 3/1
`SmsgCharacterList` handler clears the run-flag and writes engine state **4** (Select), overwriting
the pre-armed 7. A failure/timeout leaves 7 → the WinMain error path. An explicit quit writes engine
state 6 (sub 2) with SFX 861010106. Full per-widget detail lives in `specs/frontend_scenes.md §11`.

## 7.7 Dead / hidden states — (CODE-CONFIRMED)

- **`SimpleLoadHandler`**: an RTTI class with a complete constructor exists in the binary but has
  **zero callers** — it is never instantiated. A dead alternate loader, not part of the live machine.
- **The `-Start` / `launcher` gate** (see §0.1): without `-Start` and with `launcher = true`,
  `WinMain` shells out to the external launcher and returns immediately — the entire scene
  machine (states 0..7) never executes.
- **State 7 error logging**: before the modal dialog, the error path writes `error.log` (module
  list, timestamp, language setting, build date).

## 7.9 Front-end scene state machine (states 0..7) — the load-bearing model — (CODE-CONFIRMED)

> **The application entry point IS the front-end scene state machine.** There is no separate
> scene-manager class. The entry point mounts the VFS exactly once, then runs an infinite
> `while(1) switch(engineState)` over **8 cases — states 0..7 — plus a `default`** (the value 8 is
> the terminal sub-state value handled by the shared exit tail, not a 9th case; §7.1, §7.3). Each
> case constructs **that state's window/scene object** and **re-enters the shared per-frame engine
> loop** to tick it until the engine-state value changes underneath it. The first window the user
> sees is the **LoginWindow** (state 1) — no splash, no separate EULA scene. This subsection states
> that model explicitly, with the state×event transition table and the catalogue of state writers;
> §7.1–§7.7 above hold the supporting per-case and per-machine detail. The engine bring-up that
> precedes the state loop (VFS mount, device, fonts) is in `specs/game_loop.md §0`; the per-frame
> loop the cases re-enter is in `specs/game_loop.md §7` and §8 below.

### 7.9.1 How the machine is built

The whole front-end is one driver loop living directly in the application entry point:

1. The VFS is mounted **once**, before the loop (§0.5, `specs/game_loop.md §0.1`).
2. `while(1) switch(engineState)` dispatches on the engine-state value (the `GameState` struct
   field 0, §7.1).
3. Each case writes the *next* state at the **top** of the case body (its pre-loop intent),
   constructs its scene window, registers that window for teardown, and enters the per-frame
   engine loop (`Engine_MainLoop`). The loop **blocks** until the scene asks to exit.
4. The scene's own tick, or an arriving **server message**, rewrites the engine-state value to
   redirect the outer `while(1) switch`. When the loop returns, control falls to the bottom of
   the case, which checks for the teardown state (8) and otherwise loops back and re-dispatches on
   the new state value.

So the per-frame loop **is** the engine heartbeat; the **scene object** or an **arriving server
message** is what mutates the engine state. There is **no separate scene-manager object** — the
switch statement is the manager. The first per-scene loop runs inside state 1 for the LoginWindow;
each later state constructs its own scene and runs its own instance of the same loop.

### 7.9.2 Disposable-tracking vs. widget-attach — (CODE-CONFIRMED — corrected)

Each case registers its freshly-constructed window/sub-object with a per-scene **cleanup-list push**
helper (it appends the object pointer into a vector held on the driver object) so the scene's owned
objects are released together when its tick loop returns. This is a **teardown/ownership
registration**, **not** how a widget joins a parent's child list.

> **Correction (Campaign 9D `_RECONCILE`).** Earlier notes described this per-case registration as a
> generic "widget-attach helper". It is **not** — it is a **disposable-tracking push** onto the
> driver's per-scene cleanup vector. The real widget-tree attach primitives are **AddChild** and
> **AddChildWithAction** (`specs/ui_system.md §1.4 / §7.4`); the per-case helper only registers a
> scene object for coordinated teardown. Read every "attach helper" mention in the startup lane as
> "register-for-teardown".

### 7.9.3 State × event transition table

The complete engine-internal, network-driven, and user-action transition tables are in §7.5.1–§7.5.3.
The condensed state×role view (state → role → scene object → tick handler → exit transitions):

| State | Role | Scene object (approx. size) | Tick handler | Exit transitions |
|---:|---|---|---|---|
| 0 | pre-init / display-config | inline (no window) | — | always → 1 |
| 1 | init / LoginWindow create + boot | `LoginWindow` (~1368 B); loads `msg.xdb`; builds 15 fonts | per-frame loop; LoginWindow internal sub-machine | window-create fail → 7 (reason 1); device fail → 7 (reason 3); login submit → 7 (connect-wait); quit → 6 |
| 2 | opening-decision / LoadingWindow | `LoadHandler` (~536 B) | per-frame loop; boot/data-table thread runs behind it | `OPENNING/SKIP` true → 4, false → 3; server messages (enter-game-ack / character-list / conn-state) flip state out of 2 |
| 3 | opening / scenario movie | opening window (~720 B) | per-frame loop; played once | pre-set next = 4; movie end returns from loop |
| 4 | character select | `SelectWindow` (~6280 B) | per-frame loop; per-frame post-tick + command handler | enter-world flag → 5; connect-timeout in select → 6; enter-game sends request then waits for enter-game-ack |
| 5 | in-game / world | in-game scene (~200 B) wired into the master window | per-frame loop; pre/post-tick; starts the net engine path | logout / leave-world → 6; natural loop return → 4 |
| 6 | quit request | inline (engine shutdown) | — | always → field-0 8 (shared exit tail) |
| 7 | error / message box | inline (build error string, drop net, modal) | — | always → field-0 8 (shared exit tail) |
| default | unknown state / field-0 == 8 | inline (final teardown in the shared exit tail) | — | returns from the entry point |

> **No `case 8:`.** Field-0 value **8** is the terminal value cases 6/7/`default` write; the actual
> final teardown + `return` is the **shared exit tail** keyed on field-0 `== 8` when the loop
> returns. The `switch` itself has 8 arms: cases 0..7 and `default`.

> **First window at launch.** State 0 runs first (the engine-state global is zero at process start),
> sizes the window and configures the display, and shows **no** window. The **first window the user
> sees is the LoginWindow (state 1)** — the OS window and device are created in state 1 and the
> LoginWindow is the live scene for the first per-frame loop. There is **no splash/EULA scene** before
> login; what older notes called an "EULA" is the LoginWindow's own PIN/second-password sub-state
> (§7.6, `specs/frontend_scenes.md`).

> **Login does not jump straight to char-select.** The login submit kicks off the network connect
> (engine state → connect-wait, with a 30 s connect-timeout scheduled), and the **server** then drives
> loading → char-select via the character-list message (engine state → 4). The login window never
> writes the select state directly.

### 7.9.4 Catalogue of state writers

Every writer of the engine-state value, by trigger family (the engine-internal, network, and
user-action rows are tabulated in §7.5.1–§7.5.3). Summary of the writers **outside** the entry-point
driver itself:

| Writer (role) | Sets state | Trigger |
|---|---:|---|
| Login tick / internal sub-machine | 7 (connect-wait) or 6 (quit) | login submit (→ connect-wait, schedule connect-timeout) / login exit (→ quit) |
| Char-select per-frame post-tick | 5 | "enter world confirmed" flag set |
| Char-select command handler | 6 | connect-timeout event fires while in select |
| `SmsgCharacterList` handler | 4 | character roster received → show char-select |
| `SmsgEnterGameAck` handler | 2 | enter-world accepted → loading |
| `SmsgCharActionResult` handler | 6 / 7 / 2 | per result code: logout (6), error/kick (7, with the result code carried in the error-detail field), reconnect-to-loading (2) |
| `SmsgGameStateTick` handler | 4 | **only** on local-player spawn failure; on success it builds the world and does **not** change state (the select post-tick later promotes to 5) |
| Connection-state-event handler | 7 / 2 | low-level connection state: connecting (7, reason "waiting") / dropped (7, reason "disconnect"); clears the error-detail in states 1/2 |
| In-world leave/logout family | 6 | in-world logout / leave-world (entered-world flag set, state not 5/6); each schedules the loading-done event |
| Crash-handler | reads only | reads the engine state to log it on a crash; not a normal transition |

> **Net-driven transitions mutate the state from inside a tick.** A server message handler runs on the
> frame thread (the message-bus hop, §6.4 of `specs/client_workflow.md`) and can rewrite the engine
> state; the outer `while(1) switch` then re-dispatches when the current scene's loop returns. This is
> how the server drives the front-end forward (login → loading → char-select → world) without the
> client polling.

### 7.9.5 Confirmed happy path (launch → in world)

`0 (display config) → 1 (boot + LoginWindow) → [login tick: user logs in → connect-wait + 30 s
timeout] → [conn-state/loading: server connects] → 2 (loading) → 3 or 4 by OPENNING/SKIP → (if 3,
opening movie → 4) → 4 (char-select; character-list confirms the roster) → [select: enter-game request
→ enter-game-ack → 2 loading → game-state-tick seeds the world; the select post-tick flag flips → 5]
→ 5 (in world) → [logout → 6] → 8 → return`.

The loop intentionally re-enters char-select (state 5 pre-sets next = 4; world-leave sets 6), so
quitting the world returns the player toward the select/quit path rather than straight to exit.

## 7.10 Scene machine — open items

1. **LoginWindow sub-states 2..41 full enumeration.** The network/handshake machine transitions
   are partially traced; the exact action at each sub-state needs a focused pass or a live lobby
   capture.
2. **`OPENNING/SKIP` INI file path** — **RESOLVED (CAMPAIGN 16).** The `GetPrivateProfileIntA` call
   reads section `[OPENNING]` key `SKIP` from **`<exe-dir>\option.ini`**, whose path is held by the
   **DoOption settings singleton** (populated by the option-path builder) — not the per-account /
   network-config singleton. The reload (3/100 codes 202/203/232 → state 2) re-reads this key
   unconditionally; there is no reload-specific skip. spec: `resource_pipeline.md §2.5`.
3. **Opening (state 3) exit trigger.** Whether the intro ends on a movie-complete event, the
   loading-done event (type 13, id 10001), or a timer is not confirmed.
4. **Disconnect routing for states 0/1/3.** The scene-aware disconnect router handles states 2/4/5/6
   explicitly; the fallback for states 0/1/3 was not fully traced.
5. **Second-load path during enter-world.** Whether a distinct `LoadHandler` instance runs for the
   enter-world asset stream or the `MainHandler` supplies its own loading overlay is not confirmed.
6. **Menu dialog confirm ids.** The in-game quit-confirm dialog action ids (likely 50 = confirm,
   51 = cancel) are inferred from control flow, not label-confirmed.

---

# 8. Per-frame loop — (CODE-CONFIRMED)

The same `Engine_MainLoop` runs for every interactive screen — login, opening, character-select,
and in-game all share one loop body. A screen builds its handler, registers it as the loop's event
target, and calls `Engine_MainLoop`; on exit it tears the handler down. Cross-spec: the reference
spec for game-loop ordering is `specs/game_loop.md`; this section confirms and extends it.

## 8.1 Four-phase iteration order — (CODE-CONFIRMED)

> **Correction (this pass).** Earlier text described "exactly three top-level steps" and called the
> loop "genuinely uncapped". Both are wrong: the loop body makes **four** top-level calls per
> iteration, and the fourth is a **software frame-rate limiter** that `Sleep()`s to hold a fixed
> target period. Before the loop begins, `Engine_MainLoop` also raises the timer resolution with
> **`timeBeginPeriod(1)`** (so the limiter's `Sleep` has 1 ms granularity); this is re-issued on
> **every** per-scene loop entry, not once at boot.

Each loop iteration performs exactly four top-level steps in this fixed sequence:

1. **Message pump + deferred input dispatch.** Win32 messages are drained (`PeekMessage` /
   `GetMessage` / `TranslateMessage` / `DispatchMessage`). `WM_QUIT` clears the run-flag and
   exits the loop. After the Win32 drain, a **double-buffered deferred-event list** is swapped
   under a critical section and each queued UI/input event is dispatched through the input manager.
   A **raw mouse/keyboard DirectInput thread** is spawned once (one-shot guarded) at loop entry and
   runs concurrently thereafter.

2. **Frame step (render one frame).** For each active scene node: update the camera matrix and
   build the view frustum (frustum cull walk); execute the 3D draw pass (offscreen-RT toon/bloom
   path or direct-to-back-buffer path). After all scenes: `Present` (whole back-buffer, D3D9
   `IMMEDIATE` — no GPU vsync). Then device-lost recovery: on `DEVICELOST` sleep **1000 ms** and
   retry; on `DEVICENOTRESET` release default-pool resources → reset device → recreate default-pool
   resources (including fonts).

3. **Logic-tick sweep (round-robin scheduler).** Sample the millisecond clock once per frame;
   advance the round-robin cursor by **approximately 1.1% of the total subscriber count** (floor
   of `count × 0.011`); for each reached subscriber, fire its callback if elapsed time exceeds
   its configured interval. **No leftover-time carry:** a missed tick cannot be caught up the next
   frame. A "full sweep" override flag forces the count to equal the full subscriber list for one
   frame. (A DirectInput poll is folded into this phase via the input handle threaded through it.)

4. **Frame-rate limiter (the software FPS cap).** Reads `QueryPerformanceFrequency` /
   `QueryPerformanceCounter`, computes the real elapsed frame seconds, compares against the target
   period **`1.0 / targetFps`** (the target-FPS float on the engine-view object), and if the frame
   finished early `Sleep((targetPeriod − elapsed) × 1000)` ms, then re-stamps the QPC baseline for
   the next frame. See §8.3.1 for the cap rate.

**The frame rate is software-capped, not uncapped.** The GPU present interval is `IMMEDIATE` (no
vsync), but step 4 paces the CPU to a fixed target frame period each iteration. The 1000 ms `Sleep`
in step 2 is only the device-lost retry; the per-frame pacing `Sleep` lives in step 4.

**Net one-line ordering per iteration:**
`pump + deferred-input → [per scene: camera/cull → pre-draw callbacks → scene draw → (glow/bloom/composite) → transparent overlay → UI/HUD → FPS] → Present → device-recovery → logic-tick-sweep (+ DirectInput poll) → frame-rate-limiter (Sleep to target period) → repeat`.

## 8.2 Render-pass inner order — (CODE-CONFIRMED)

Both draw paths invoke scene callbacks in the same order. Full detail for the in-world case is in
§9.3; the same five callback roles apply in reduced form for non-world screens.

| Sub-step | What runs |
|---|---|
| Pre-draw A: environment / day-night | Environment/sky-light/day-night update + shadow setup; no geometry drawn |
| Pre-draw B: sky dome | Sky/star/cloud dome geometry (Z-test off, camera-centred world matrix) |
| Pre-draw C: shadow stamp | Terrain ground-shadow stamp + actor shadow projection |
| Scene root draw | Opaque cull walk: terrain → solid → skinned actors → billboards |
| FX/water/decal callback | Terrain FX layers, water/animated surfaces, alpha decals |
| Transparent overlay callback | Alpha-blended billboards + particles + additive glow |
| (toon path only) | Glow extract → bloom blur → finaldx8 composite (see §3.3) |
| UI/HUD callback | 2D widget tree, screen-ortho space, after compositing |
| FPS overlay (debug only) | Gated by debug flag; nanosecond-resolution profiling timer |

## 8.3 Millisecond clock and time-scale — (CODE-CONFIRMED)

The authoritative engine **logic** clock is `timeGetTime()` (Win32 multimedia millisecond timer).
Its raw return is optionally multiplied by a global **time-scale float** (default 1.0; values below
1.0 produce slow-motion; values above 1.0 produce fast-forward) and offset by a small float constant.
This is the single clock used by animation, network throttling, FX timers, and the logic scheduler.
`GetTickCount` does not feed any timing here.

> **QPC clarification (corrected).** An earlier blanket statement said "neither `GetTickCount` nor
> `QueryPerformanceCounter` feeds the logic delta". That is true **only for the logic/animation
> delta** (which is `timeGetTime`-based). `QueryPerformanceCounter` **does** drive the per-frame
> pacing limiter (§8.1 step 4, §8.3.1) — it is not purely a profiling concern. So: logic time =
> `timeGetTime`; frame pacing = QPC.

A **separate high-resolution (nanosecond-resolution) profiling timer** also exists in the scene-draw
path to accumulate average cull-time and draw-time readouts. That profiling timer does **not** feed
simulation.

## 8.3.1 Software frame-rate cap — (CODE-CONFIRMED; config source RESOLVED dead, CYCLE 7)

The per-frame limiter (§8.1 step 4) holds a **fixed target frame rate**. The target lives in the
**target-frame-rate field (offset +0x30) of the engine scene-machine driver object**, which that
object's constructor seeds **once** to the float **60.0** and which no path overwrites — so the
effective cap is a **hardcoded 60 FPS**. Each frame the limiter computes the target frame time as
`1.0 / rate` seconds, measures the real elapsed delta from `QueryPerformanceCounter` /
`QueryPerformanceFrequency`, stores that delta in a last-frame delta-time field (offset +0x34) on the
same object, and when the frame finished early sleeps `(targetTime − elapsedSeconds) × 1000` ms (the
`× 1000` is a seconds→milliseconds units conversion, not a second cap). With `timeBeginPeriod(1)`
in effect the sleep resolves at ~1 ms granularity, so the loop holds close to 60 FPS regardless of
how fast the GPU presents.

**Upper cap only.** Busy frames whose measured delta already meets or exceeds `1/60` skip the
`Sleep` and run uncapped — the limiter holds the ceiling, it does **not** pad slow frames. The
load / opening / character-select / in-game scene loops all pace on this driver object's hardcoded
60.0; the login scene runs the same loop on its own window-derived object's own +0x30 loop-rate
field (a per-object loop rate, **not** the dead display config field).

| Limiter constant | Meaning |
|---|---|
| the float **60.0** | hardcoded target frame rate, seeded once into the driver object's +0x30 field by its constructor |
| **1.0** | numerator of the target frame time `1.0 / rate` |
| **1000.0** | seconds→milliseconds conversion in the early-finish `Sleep` — units only, not a cap |
| **1 ms** | `timeBeginPeriod(1)` timer resolution (§8.1) giving the `Sleep` its granularity |

> **RESOLVED (CYCLE 7) — the display `FRAMERATE` config is dead.** The earlier residual ("is the
> display `FRAMERATE` config genuinely inert?") is now **closed: it is inert/dead.** The
> `DISPLAY_FRAMERATE` value (parsed from `data/script/display.lua` with the integer config getter) is
> stored **raw, with no zero-default**, into a field of a **different** object — the renderer/display
> singleton (the field is initialised to 0 in that singleton's constructor). An exhaustive static
> search of that field's displacement finds **exactly two sites, both writes** (the constructor
> zero-init and the config store) and **zero readers anywhere in the binary** — the stored value
> never reaches the limiter. The limiter's framerate field stays at its constructor-seeded 60.0.
> **Any port that treats the FPS cap as configurable is wrong; pace gameplay at a fixed 60 FPS upper
> bound.** (A live runtime confirmation that 60.0 actually paces at 60 FPS would be RUNTIME-ONLY and
> is not needed for the verdict.)

## 8.4 Day/night clock — location and mechanics — (CODE-CONFIRMED)

The day/night cycle does **not** tick in the logic scheduler. It ticks inside the **3D render pass**
as a scene pre-draw callback (pass A of §9.3 / §8.2).

Mechanics:

- Each frame the pre-draw callback accumulates an inter-frame delta from a raw `timeGetTime()`
  delta (no time-scale applied here).
- **Gate: only when the accumulator reaches ≥ 50 ms** does the environment step run; the
  accumulator is then reset and a frame counter incremented. This 50 ms gate is shared with
  terrain water UV-scroll animation — both run at the same ~20 Hz maximum cadence.
- On each qualifying tick the frame counter parity determines the branch:
  - **Odd tick → sky/day-night driver.** Reads the master game clock (server-synced via opcode
    5/18; locally advanced by frame delta between syncs); computes keyframe index as
    `floor(clock_ms / 1800)`, fractional blend as `(clock_ms mod 1800) / 1800`, wraps the index
    at **48**. Linear interpolation between adjacent keyframes updates sun direction, sky colour,
    light colour, and fog parameters.
  - **Even tick → weather/cloud branch.**
  - Cloud and star updates run every tick (both parities).
  - A weather re-check runs every **120 ticks** (~6 seconds at 20 Hz).
  - Frame counter wraps at 60 000.
- **Day length:** 48 keyframes × 1800 ms = **86 400 ms = 86.4 seconds** of simulated day.
  Matches `names.yaml` constants `SKY_KEYFRAME_COUNT = 48`, `SKY_KEYFRAME_MS = 1800`.
- **Fallback keyframes:** if the area light bins (`data/sky/dat/light%d.bin`, 5312 bytes = 83 × 64
  byte slots) are absent, a fallback builder constructs 48 default keyframes.
- **Sky re-bake rate gate:** an additional per-area quality multiplier from `OPTION_SKY` (settings
  field index 7) gates how frequently the sky interpolation re-bakes. Approximate multipliers:
  quality 1 ≈ 0.25, quality 2 ≈ 0.7, quality 3 ≈ 0.5 (exact floats unconfirmed — see §8.6).

## 8.5 GameTime struct and server sync — (CODE-CONFIRMED)

The master game clock lives in a **three-field struct**. Fields:

| Field offset | Type | Name | Role |
|---:|---|---|---|
| +0x00 | f32 or u32 | `time_scale` | local time scaling; relationship to the `timeGetTime` time-scale global is unconfirmed |
| +0x04 | u32 | `current_ticks` | current game time in ms; advanced locally between server syncs |
| +0x08 | u32 | `ticks_per_day` | total ms in one simulated day (nominally 86 400) |

All in-engine reads target `current_ticks (+0x04)`. The struct is advanced via member functions,
not by direct field writes. **Server synchronisation:** opcode **5/18** (`SmsgGameClockUpdate` in
`names.yaml`) carries an 8-byte body — two u32 fields `(time_a, time_b)` — and posts it into the
main handler's task queue. The exact field-to-struct mapping and whether the client free-runs
between syncs or snaps fully on each packet are not yet byte-confirmed (see §8.6).

For the .NET reimplementation: drive day/night phase from the server-synced game clock (opcode
5/18), advancing `current_ticks` locally by the frame delta between sync packets. Do not use a
free-running local timer as the sole source of day/night time.

## 8.6 Frame loop — open items

1. **Exact apply site of opcode 5/18 into `current_ticks`.** How `time_a` vs `time_b` maps to
   `current_ticks` / `ticks_per_day`, and whether the client free-runs or snaps, was not byte-traced.
2. **`time_scale (+0x00)` of the GameTime struct.** Whether this is the same or a distinct field
   from the `timeGetTime` time-scale global is unconfirmed.
3. **Per-area sky rate multiplier exact floats.** The approximate values 0.25/0.7/0.5 for sky
   quality 1/2/other are inferred from float immediates; exact decoded floats need confirmation.
4. **Scene-draw callbacks on non-in-world screens.** Character-select and opening screens likely
   run a reduced callback set (sky preview without in-world day/night). Not traced.
5. **WM_QUIT origin.** The window procedure posts quit on close; confirm no other path clears the
   run-flag that could interfere with the scene-machine transition.
6. **The `16` constant** stored in the engine struct during state-0 initialisation. Likely a
   default subscriber interval seed in ms, not a frame-rate cap; no proven per-frame reader found.
   (Confirmed not the frame cap — that is the constructor-seeded 60.0 framerate field, §8.3.1.)
7. **Frame-limiter target-FPS config source — RESOLVED (CYCLE 7).** The limiter reads the driver
   object's target-frame-rate field (+0x30), seeded once to **60.0** by that object's constructor and
   overwritten by nothing. The display-config `FRAMERATE` value is **dead**: stored into an unrelated
   renderer/display-singleton field whose displacement has exactly two writers and **zero readers** in
   the whole binary, so it never reaches the limiter. The cap is a fixed 60 FPS (§8.3.1). The only
   residue is a RUNTIME-ONLY reconfirmation that 60.0 paces at 60 FPS live — not required for the
   verdict.

## 8.7 Memory / allocator model and per-frame object churn — (CODE-CONFIRMED)

### 8.7.1 Allocator — plain CRT heap, no custom pool/arena

The client allocates through the **standard MSVC C runtime**: `operator new` → `malloc` → Win32
`HeapAlloc` on the process heap. There is **no custom game-authored pool, arena, or free-list
allocator** anywhere in the binary. Key properties:

- Allocation is **decentralised**: every class allocates its own members and children directly. The
  dominant `operator new` entry point is invoked from **hundreds of distinct functions** rather than
  funnelled through one central allocator — there is no single game-side memory broker.
- **Placement-new exists but only as STL behaviour.** The trivial placement form is used for in-place
  construction into pre-sized container storage (`std::vector<T>` growth), not for a custom
  fixed-block pool.
- The large statically-linked third-party libraries (the image/codec/scripting/compression/anti-cheat
  libraries) carry their **own internal allocators**, tagged separately in prior cycles; those are not
  the game's pattern. The game/engine path is the CRT one described here.

> **Allocator model (raised from PARTIAL/NONE to definitive):** **plain CRT heap
> (`new`/`delete`/`malloc`/`free`) on the Win32 process heap; placement-new only via STL containers;
> no custom pool/arena/free-list.** Some subsystems own a long-lived buffer object they refill (see
> §8.7.2), but that is buffer reuse inside an ordinary heap object, not a global allocator.

### 8.7.2 Per-frame object churn — pooled / reset-and-reuse (the steady render loop is alloc-light)

The steady per-frame render path (§8.1 step 2, §8.2) is **allocation-light**: transient render data is
pooled in persistent containers, cleared and refilled rather than freed and re-allocated each frame.

- The cull/draw pipeline accumulates drawables into **persistent containers** — a persistent vector /
  linked render-bin whose backing capacity is **retained across frames** (cleared and refilled, not
  freed).
- The per-frame draw loop **walks the already-collected render bin** and issues draw calls; it does
  **not** allocate per drawable inside the loop.
- Transient draw records are constructed into those persistent draw/render-bin vectors via
  placement-new growth, so their backing storage is **reused frame to frame**.
- The particle buffer is a **single long-lived buffer object** holding N particles that are recycled
  within it — particles are **not** `new`'d per frame or per particle.

So the dominant heap activity is at **scene-mutation time** (spawn/despawn actors, load an area, build
effects), **not** in the steady render loop. The exact reset-vs-reserve thresholds, and whether any
pooled container ever grows mid-frame, are **RUNTIME-ONLY** (value semantics needing a live
capture/debugger pass).

---

# 9. World scene (state 5) — entry timeline and render order — (CODE-CONFIRMED)

Engine state 5 is the in-game world scene, driven by the `MainHandler`. It is the only state with
a persistent 3D scene graph. Cross-spec: camera constants for the five manipulators are in §4;
movement and collision are in §5; sound ambient driver is in §1.4; terrain ring loading is in
`specs/resource_pipeline.md`.

## 9.1 Entry sequence — ordered timeline — (CODE-CONFIRMED)

Pre-condition: character-select confirmed a slot → 880-byte SpawnDescriptor is cached; the
enter-game request was sent; the engine advanced through state 2 (Load) to state 5 (In-game).

| Step | Phase | What happens (neutral) |
|--:|---|---|
| 1 | **World-build** (synchronous) | `WinMain` case 5 constructs the `MainHandler` and calls `BuildGameWorld`. |
| 2 | **Camera rig + scene graph** | `BuildSceneGraph` assembles: one `GPerspectiveCamera` (FOV 65° base, aspect-corrected by render width/height; near 5; far 15000 — §4.5), **five** `GViewPlatform` slots (Third/First/Static/Gamble/Event), a scene root node labelled "charater scene" (typo authentic), a `GSwitch` node, and five camera manipulators. Manipulator string-catalogue label ids: 2006/2004/2005/2148/2148. (A previously-claimed reserved sixth view-platform slot was **not** seen in this builder — see §9.5.) |
| 3 | **Environment node + render callbacks** | An environment/light node group and a second node group are parented into the scene. Five scene render-callback function-pointer/context pairs are installed. Approximately 17 world-manager singletons are cached. |
| 4 | **World services + per-frame update** | Four world service managers (terrain, actor, effect, sound) are activated. A **per-frame world-update callback** is installed into the handler. HUD panel tree activated: community panel, character-billboard panel, link-combo panel, rank-progress panel, and slot panels. |
| 5 | **Network: GameStateTick (4/1)** | Server sends the world-state packet (~9100-byte body). Spawn **X** at body offset +0x2374 (f32); spawn **Z** at +0x2378 (f32). **Y is never on the wire.** Packet also carries map/scenario code (+0x00C), hour-of-day, area id, and bulk actor mirror blocks. |
| 6 | **Local player actor created** | First entry (`local_player == NULL`): `Actor_Factory_Create` allocates the local player from the SpawnDescriptor. On failure, engine state is written to 4 (back to Select). |
| 7 | **Player placed at (spawnX, 0, spawnZ)** | World position set to `(spawnX, 0.0, spawnZ)`. **Y is always forced to zero** — terrain height is a visual surface only; server-side simulation Y is 0. |
| 8 | **3×3 terrain ring streamed** | `Terrain_InitFirstRing_3x3` loads the centre cell and 8 neighbours. Cell-to-grid index: `10000 − floor(coord × −1 / 1024)` (cell size 1024 world units — consistent with §4.4 and `formats/terrain.md`). Client logs "first terrain init (x, z, area)" when this runs. |
| 9 | **Materialize FX** | A timed spawn-in visual effect (id **310000001**) is spawned on the player. |
| 10 | **Spawn SFX** | A 3D directional spawn sound (id **862010105**, kind 5) is fired at the player position. Audible radius for the local player: 200 units. |
| 11 | **BGM start / cross-fade** | Entry BGM cue **910066000** is started or cross-faded via the 2D streaming path. In tutorial scenario, the same function is called with a cross-fade flag. The "fade-in" at world entry is **this BGM cue** — no screen-space alpha-fade overlay was found in the spawn path. |
| 12 | **Tutorial cue** (conditional) | If map area = 1 and tutorial-state variable = 12: extra cue **910001000** is also started. |
| 13 | **Scenario wiring** | If scenario code (4/1 body +0x00C) = 6 (tutorial): reads tutorial script index and stop-index from INI keys `TUTORSCRIPTINDEX` / `STOPIDX` keyed by character name. |
| 14 | **Player visual / motion init** | Actor visual built; idle motion started. |
| 15 | **HUD / rank / quest finalise** | Rank-progress panel state updated; billing-gated panels activated; system text and social roster swept. |
| 16 | **Camera follows player** | Active manipulator (Third by default) begins orbiting the placed player at orbit radius ≈ 901 world units, pitch −30°. No separate enter-world camera placement exists. |
| 17 | **World live** | Per-frame loop renders and ticks normally. The `LoadHandler` had already broken its loop on loading-done (event type 13, id 10001), so state 5 is live immediately. |

### 9.1.1 Alternate entry path — BillingInfo (4/3) — (CODE-CONFIRMED)

The BillingInfo handler (opcode 4/3) runs the **identical materialize sequence** (steps 7–11):
forces `world_pos = (spawnX, 0, spawnZ)`, streams the 3×3 terrain ring, spawns materialize FX
**310000001**, fires SFX **862010105**, re-syncs rank/vital globals. It reads spawn X/Z from a
60-byte body structure and gates on a "ready" byte. Both 4/1 and 4/3 can drive the visible
materialize depending on the login/billing flow; the exact ordering of the two packets is an open
item (see §9.5).

### 9.1.2 Re-entry vs first-entry split

- **First entry** (`local_player == NULL`): create the actor (step 6), then steps 7–15.
- **Re-entry / map change** (`local_player != NULL`): write spawn X/Z to world-mirror fields,
  re-run the 3×3 ring, re-place at (spawnX, 0, spawnZ), re-fire FX/SFX/BGM, refresh panels. No
  new actor allocation.

## 9.2 Per-frame world-update order — (CODE-CONFIRMED)

| Step | Phase | Description |
|--:|---|---|
| 1 | Input / message pump + deferred input | Win32 messages + UI events; raw mouse/keyboard on a separate DirectInput thread. |
| 2 | **Per-frame world-update logic** | Env/light node recomputed from player position (sun/light direction, camera-relative light globals); cursor/3D-marker state; cursor hit-test (feeds click-to-move and UI); ambient sound driver + 1000 ms world-clock accumulator (§9.2.1). |
| 3 | Scene camera + frustum cull | `GSwitch` selects active view platform; `GPerspectiveCamera` builds view frustum; cull-and-draw walk runs. (Engine frame-step — not the world-update callback.) |
| 4 | 3D scene render pass | Five render callbacks in the order of §9.3. |
| 5 | UI / HUD draw | 2D widget tree, screen-ortho space, after the 3D composite. |
| 6 | FPS overlay (debug) | Optional; debug flag gated. |
| 7 | Present | D3D9 `Present`, whole back-buffer, `IMMEDIATE` (no GPU vsync). |
| 8 | Device-lost recovery | Reset on lost device; 1000 ms retry sleep. |
| 9 | Logic-tick sweep | Round-robin ~1.1% of subscribers per frame (network apply, AI, FX timers). |
| 10 | Frame-rate limiter | QPC software cap: sleep to hold the fixed ~60 FPS target period (§8.1 step 4, §8.3.1). |

### 9.2.1 Ambient sound + world-clock tick (inside world-update, step 2)

Each frame when the sound system is active: the per-position ambient driver re-evaluates the
player's terrain mud-cell and starts/stops BGM/ambient/3D sounds on cell-byte change (§1.4).
Streaming sound refill is kicked. A **1000 ms accumulator** uses the world-clock delta to run a
slot update every 1000 ms and a second update every 3000 ms (every third 1000 ms tick). This is
the logic-side day-progression heartbeat; the **visual** sky/day-night interpolation runs
separately inside the render pass (§8.4, §9.3 pass A).

## 9.3 Render pass order — five scene callbacks — (CODE-CONFIRMED)

Both draw paths (toon/bloom offscreen-RT and direct-to-back-buffer) invoke the five scene
render-callbacks in the following fixed order. The view transform is set before each callback. This
is the authoritative intra-scene layer sequence.

| Pass | Role | What draws / what state is set |
|---|---|---|
| **A** | **Environment / day-night / shadow setup** | Reads camera view transform and player position; computes sun/light direction in view space; updates the ≥ 50 ms day/night driver (§8.4); sets up shadow-map projection. No geometry drawn — setup only. |
| **B** | **Sky dome / star dome / cloud** | Sky, star, and cloud dome geometry. Z-test disabled; lighting disabled; fog enabled; world matrix centred on the camera's XZ position. Drawn before any world geometry. |
| **C** | **Terrain ground-shadow stamp + actor shadows** | Stamps ground cells; enables lighting and Z-test; draws ground-projected actor shadows via the shadow manager (texture stage 17). |
| **D** | **Opaque world cull walk** | Scene-graph cull walk: **terrain** (multi-texture, FVF `0x152`) → **solid/batched geometry** → **skinned actors** (CPU-skinned, toon shader, §3.5/§3.6) → **billboards**. All opaque before any alpha. |
| **E** | **FX terrain layers / water / decals** | `.fx1`–`.fx7` terrain FX layers, water/animated surfaces, alpha decals. Lighting on for first sub-passes then off; re-invokes a transparent sub-pass at its tail. |
| **F** | **Post-scene transparent overlay** | Camera-facing billboards/sprites + particle effects (SRCALPHA/INVSRCALPHA default; SRCALPHA/ONE additive glow). Drawn after opaque composite; no per-particle back-to-front sort. |
| (toon path only) | Glow extract → bloom blur → finaldx8 composite | RT→RT downsample passes; edge/bloom composite via `finaldx8.psh` (c0 = edge weight, c1 = bloom weight). See §3.3. |
| (last) | **UI / HUD** | 2D widget tree, screen-ortho space, after compositing. `Present` follows. |

**Net intra-scene draw order (one line):**
environment/day-night setup → sky/star/cloud dome → ground-shadow + actor shadows → opaque terrain → solid geometry → skinned actors → billboards → FX/water/decals → transparent billboards/particles → *(toon path: glow/bloom/composite)* → UI/HUD → Present.

> **Correction note.** An earlier analysis read renderer-object callback offsets from a partial
> decompilation. The byte-verified pass from the world-scene lane supersedes that reading. The
> intra-scene draw order above is the authoritative version. Raw byte offsets of the five function
> pointers within the renderer struct are dirty-room details and are not reproduced here.

## 9.4 Key world-scene constants — (CODE-CONFIRMED)

| Constant | Value | Role |
|---|---|---|
| Spawn player Y | **0.0** | always; never on the wire; terrain height is visual only |
| Terrain ring on spawn | **3×3** (centre + 8 neighbours) | `Terrain_InitFirstRing_3x3` |
| Cell-grid index formula | `10000 − floor(coord × −1 / 1024)` | cell size 1024 world units; consistent with §4.4 and `formats/terrain.md` |
| Materialize FX id | **310000001** | timed spawn-in `UserXEffect` on the player |
| Spawn SFX id | **862010105** | 3D directional spawn sound, kind 5; 200-unit audible radius (local player) |
| Entry BGM cue | **910066000** | BGM cross-fade on world entry (no screen-space alpha fade) |
| Tutorial intro cue | **910001000** | extra cue when map = 1 and tutorial state = 12 |
| Tutorial scenario code | **6** | value at 4/1 body +0x00C |
| GameStateTick (4/1) body size | ~9100 bytes (0x238C) | spawn X at body +0x2374, Z at body +0x2378 (both f32) |
| Camera FOV / near / far | 65° vertical (base; aspect-corrected, §4.5) / 5.0 / 15000.0 | built in `BuildSceneGraph` |
| View platforms built | **5** (Third/First/Static/Gamble/Event) | a 6th "reserved" slot was not confirmed in this builder (capture/debugger-pending — §9.5) |

## 9.5 World scene — open items

1. **Screen-space fade-in.** No alpha-fade overlay was found in the spawn path. A possible camera
   or UI fade in the `LoadHandler`→`MainHandler` transition (loading-sprite teardown) was not
   isolated.
2. **Ordering of GameStateTick (4/1) vs BillingInfo (4/3).** Both run the materialize sequence.
   Which arrives first, whether 4/3 is a billing-confirmation re-trigger after 4/1, and whether
   double-firing the materialize FX 310000001 is observable needs a live capture.
3. **3×3 terrain ring: synchronous or deferred.** `Terrain_InitFirstRing_3x3` runs inline in the
   spawn handler (confirmed by the log line), but whether per-cell load blocks on VFS or queues to
   the async asset worker is not traced. This governs the NPC ground-placement race (the
   pending-snap Y timing debt in `CLAUDE.md`).
4. **Per-frame world-update callback invocation site.** The callback is installed at a known handler
   field; the precise engine call-site (scheduler subscriber vs frame-step hook vs handler OnEvent)
   was not pinned. Order relative to `Scene_UpdateCameraAndCull` assumed "logic before render".
5. **Scenario / map-mode integer full enum.** Values 1 (normal field) and 6 (tutorial) are
   confirmed. Other values (instanced dungeon, PvP, event) are not enumerated.
6. **Local player actor world-position struct.** The full actor-position field layout belongs to
   the struct/cartographer lane.
7. **Reserved sixth view-platform slot** — **RESOLVED (CAMPAIGN 16): there is no sixth.** A
   re-confrontation of the world scene-graph builder confirmed exactly **five** `GViewPlatform`
   allocations (Third/First/Static/Gamble/Event) plus one `GSwitch`. The earlier "allocated-but-never-
   assigned sixth slot" claim is dropped. (The five camera manipulators are labelled from the four
   distinct layer-node msg ids {2004, 2005, 2006, 2148} — id 2148 is reused for the fifth, which is
   what produced the "2148/2148" duplicate in §9.1 step 2; it is real reuse, not a typo.)
