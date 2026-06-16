---
status: confirmed
verification: confirmed   # the entire C++ consumption surface (VM, single binding, loader, reader family, table API, boot triad, script source path) is control-flow-confirmed on build 263bd994; nothing here is server-authored or on-wire, so no item is capture/debugger-pending
ida_reverified: 2026-06-16
ida_anchor: 263bd994
evidence: [static-ida]
sample_verified: true    # C++ consumption surface CODE-CONFIRMED; real config.lua / display.lua / uiconfig.lua samples inspected on disk (§9). NOTE: config.lua / tableString / CONFIG_* are NOT host-referenced by name in this build — they arrive only via cpp_load (see §2.3)
subsystems: [lua_vm, config_scripts, string_tables, boot_flags]
networked: false          # Lua is a client-side config/text-table engine; nothing on the wire
encoding_note: The shipped .lua source files are CP949 (code page 949), NOT UTF-8 — this CORRECTS the earlier UTF-8 claim. The in-binary tutorial row-decode runs code page 65001 ONLY on the name-based loader; the ID-based loader pushes raw bytes. See §0, §5.2, §9.
conflicts: lua_scripting.md §6.2's old "plain on-disk, NOT the VFS" claim is refuted (this file's §2.2 was already correct and lua_scripting.md is now reconciled to it); the int-reader "bool" name is a misnomer (§6). RESOLVED this pass (F11 re-verification): the two addiction-timing knobs are disentangled (§3 — the ×1000 site reads the DISPLAY_* key, not a game.lua global); the name-vs-ID decode asymmetry and the 102400-byte name-loader scratch buffer pinned (§5.1); msg.xdb boot adjacency noted (§8 item 7).
---

# Lua configuration & string-table engine (in-process VM, config scripts, table API)

> **Clean-room specification.** Neutral description only — no decompiler identifiers, no binary
> virtual addresses, no pseudo-code. Promoted from dirty-room analyst notes under EU Software
> Directive 2009/24/EC Art. 6 (decompilation solely to achieve interoperability). No decompiler
> output appears in this file. Re-expressed entirely in the spec-author's own words.
>
> **Relation to `specs/lua_scripting.md`:** that file documents the VM identity (stock Lua 5.1.2 +
> the open-source `lua_tinker` binding), the single `cpp_load` global, the host-pulls-from-scripts
> control direction, and the reimplementation trade-off (interpreter vs. direct-parse). **This file
> is the operational companion**: it pins the *exact* config-script set, the *named integer/string
> globals* each script defines, the boot-flag semantics, and the `getTableSize` /
> `getTableString` / `getTableStringByID` table-read contract. Two corrections to the older file
> are carried here as load-bearing facts (§2.2 script source path, §6 the integer reader misnomer).
> Engineers implementing the config/localization loader should treat this file as the primary
> reference for *what the scripts must define* and the older file as the VM-identity reference.
>
> **Engineer note (encoding):** `LuaConfigReader` (and any loader that reads the shipped `.lua`
> source files) **must decode the file bytes as CP949 (code page 949)**, register the CP949 provider
> once, and cite `// spec: Docs/RE/specs/lua-config.md`. See §0.

---

## 0. Encoding correction — the shipped `.lua` files are CP949, NOT UTF-8 (LOAD-BEARING)

> **This supersedes the earlier UTF-8 claim.** An earlier note (carried in the front-matter and in
> §5.2 below as the original UTF-8 assertion) stated that Lua text decodes as UTF-8 (code page
> 65001). **Direct inspection of the real shipped config files refutes that for the file source
> itself.** The shipped `.lua` source files — `config.lua`, `display.lua`, `uiconfig.lua` — are
> **CP949 (code page 949, EUC-KR)** for their inline Korean comment strings, consistent with the
> project-wide "all game text is CP949" rule.

**Why the prior UTF-8 claim was wrong.** The earlier UTF-8 assertion came from reading the *C++
host's row-decode path* in the binary (the tutorial string-table conversion that ran the row bytes
through a wide-char conversion configured for code page 65001) and generalising it to the file
encoding. The actual config files were never byte-inspected at that time. When the real files are
read, the Korean comment bytes are unambiguously **double-byte CP949 syllables, not UTF-8**: a
Korean syllable in CP949 occupies two bytes in the high range, whereas the same syllable in UTF-8
would begin with a three-byte lead-byte sequence. The observed comment bytes match the CP949
two-byte pattern and do not match the UTF-8 three-byte pattern. Therefore the file source encoding
is CP949.

**Scope of the correction.**

- The **file source encoding** of `config.lua` / `display.lua` / `uiconfig.lua` (and, by the
  project-wide rule, other shipped `.lua` source) is **CP949** — this is the corrected, authoritative
  fact for `LuaConfigReader`.
- The **C++ host's tutorial row-decode path** (§5.2) was observed configured for code page 65001 in
  the binary. That remains a separate, narrower observation about one in-binary conversion routine and
  is NOT the file encoding. Where the two disagree, **the on-disk file bytes (CP949) are ground truth
  for a clean-room reader**: a reimplementation reads the bytes off disk and must decode them as the
  encoding they were actually written in (CP949). The §5.2 paragraph is retained verbatim below for an
  honest record, annotated as superseded for file-source purposes.

**File-format facts confirmed by inspection (all three files):** no byte-order mark (BOM); line
endings are CRLF; ASCII key names and numeric literals are 7-bit safe (only the inline Korean
comments are double-byte). The VM is stock Lua 5.1.2 (statically linked).

**Implementation contract:** `LuaConfigReader` registers the CP949 code-page provider once and
decodes every shipped `.lua` file as CP949. Do not decode these files as UTF-8 — doing so corrupts
the inline Korean comments and any Korean string literal.

---

## Status banner

> Re-verified on build **263bd994** (2026-06-16, static IDA). Every C++ consumption-surface row
> below is control-flow-confirmed; the only `static-hypothesis` items are read sites not individually
> re-walked this pass (noted inline). Nothing in this subsystem is server-authored or on-wire, so no
> item is capture/debugger-pending.

| Area | Confidence |
|---|---|
| One process-wide Lua state, lazily created, standard libraries opened | CODE-CONFIRMED |
| Exactly one custom C function registered (`cpp_load`); no gameplay objects exposed | CODE-CONFIRMED |
| `cpp_load` = script-side include (chain-loads another `.lua` through the same loader) | CODE-CONFIRMED |
| Four named config scripts (`game.lua`, `uiconfig.lua`, `display.lua`, `tutor.lua`) + their loader call sites | CODE-CONFIRMED |
| Boot globals `vfsmode` / `launcher` / `debugmode` (read, gate, and downstream effect) | CODE-CONFIRMED |
| `uiconfig.lua` global `NEW_SERVER_INDEX` | CODE-CONFIRMED / SAMPLE-CONFIRMED |
| `display.lua` `DISPLAY_*` global set (~71 keys: ints, the per-status float brightness matrix, one string) | CODE-CONFIRMED / SAMPLE-CONFIRMED |
| `getTableSize` / `getTableString` / `getTableStringByID` Lua API contract | CODE-CONFIRMED |
| **Shipped `.lua` source files are CP949 (code page 949), NOT UTF-8** (corrects prior claim) | SAMPLE-CONFIRMED |
| **Encoding split: in-binary 65001 (UTF-8) round-trip is applied ONLY on the name-based table loader; the ID-based loader pushes raw bytes** | CODE-CONFIRMED |
| The global config reader returns a Lua *number as a full int* (the "bool" name is a misnomer) | CODE-CONFIRMED |
| Scripts are sourced through the same VFS-vs-disk router as every other asset (gated by `vfsmode`) | CODE-CONFIRMED |
| **`config.lua` / `tableString` / `CONFIG_*` are NOT referenced by name in this build's binary — they arrive only via `cpp_load`** | SAMPLE-CONFIRMED (on-disk) / NOT IDB-present |
| `config.lua` developer bootstrap keys + the `tableString` lookup library | SAMPLE-CONFIRMED |
| `DISPLAY_POWERSHADER` host buffer is 260 bytes (MAX_PATH), filled via a bounded string copy | CODE-CONFIRMED |
| `DISPLAY_GLOW_RANGE_X` / `_Y` host fallback to **2** when read as 0 | CODE-CONFIRMED |
| Concrete shipped values inside each `.lua` (matrix numbers, exact key values) | SAMPLE-OBSERVED (single sample) |

---

## 1. The in-process Lua VM (one state, one binding)

The client embeds a **statically-linked stock Lua 5.1.2** interpreter (version and `lua_tinker`
binding identity are documented in `specs/lua_scripting.md`). The application uses it strictly as a
**configuration and text-table engine**, never as a general gameplay scripting host:

- There is **no runtime hot-reload**, **no per-frame Lua tick**, and **no exposure of gameplay
  objects** to scripts in this build.
- A handful of named `.lua` files are run at boot / scene-build time purely for their **side
  effects on the global table** (they define global numbers, strings, and table-read helper
  functions); the host reads those globals back into C++ fields afterward.

### 1.1 The single process-wide state

A lazily-initialised global singleton wraps exactly **one** Lua state, shared by all config and
text loads across the whole client. On first access it:

1. Creates the Lua state.
2. Opens the **full standard library** (string / table / math / os / io / debug / coroutine /
   package).
3. Registers **exactly one** custom global C function, named **`cpp_load`** (see §1.2). That single
   registration is the entire native surface exposed to scripts — no gameplay/object binding is
   registered in this build.
4. Registers a process-exit cleanup so the state is torn down on shutdown.

There are several call sites that obtain this singleton (boot, the login-window scene build, the
display-config load, the two tutorial-panel loaders, a billing/letter panel, and `cpp_load`
itself). They **all share the one state**.

### 1.2 The one binding — `cpp_load` (script-side include)

The single registered C function is the global **`cpp_load`**. Its body resolves the shared VM
singleton and runs another named `.lua` file through the same load-and-run path the host uses.

```
cpp_load("<relative-script-path>.lua")   -- runs that script in the shared VM
```

It is the script-side **`#include` / `dofile`** mechanism: any of the named config scripts may
chain-load further config scripts through `cpp_load`. It is re-entrant. The binding is registered
through `lua_tinker`'s generic single-upvalue trampoline (one shared C dispatcher carries the bound
C function pointer as an upvalue), but that is binding plumbing, not a semantic extension — see
`specs/lua_scripting.md` §3.

---

## 2. Script load path and the named config scripts

### 2.1 Load-and-run mechanism

A named script is loaded by: opening the file → reading its entire contents into a freshly
allocated buffer → NUL-terminating it → handing the buffer to the protected load-and-run path
(load the buffer as a Lua chunk, then a protected call). The chunk is run **for its side effects
only** — it returns nothing to C++; the host reads the globals the script defined afterward.

Load-time (compile) errors and runtime errors are both routed to the client log through the
`lua_tinker` print/alert path; on error the message is logged and the Lua stack is restored. A
failed script therefore does **not** abort the client — it simply leaves the expected globals
undefined (the host then falls back to its compiled-in defaults, see §3).

### 2.2 Script source path — through the VFS-vs-disk router (CORRECTION)

> **Load-bearing correction to `specs/lua_scripting.md` §6.2.** The older spec described the script
> loader as a *plain on-disk* file reader that bypasses the `.pak` VFS. The deeper trace shows the
> opposite: the script file is opened through the **same disk-file abstraction the rest of asset
> I/O uses**, which tail-calls the **same VFS-vs-disk open router** every other asset goes through.
> That router picks the mounted-VFS path or a direct-file path based on whether the VFS is mounted.
>
> Concretely, `.lua` scripts are sourced **exactly like any other asset**, and the choice between
> reading from the packed VFS and reading loose files from disk is governed by the **`vfsmode`**
> boot global (§3). When `vfsmode = 1` the scripts come from the mounted VFS; when `vfsmode = 0`
> they are read as loose files from disk. Implementers must route script loading through the same
> VFS abstraction as other assets, not a separate plain-filesystem reader.

### 2.3 The named config scripts

| Script (relative path) | Loaded at | What the host reads back |
|---|---|---|
| `game.lua` | Boot (early startup) | Boot flags `vfsmode`, `launcher`, `debugmode` (§3); later a game-addiction warning timing value. `game.lua` defines these as plain global numbers. |
| `data/script/config.lua` | Boot / pre-script environment — **no direct host load site in this build's binary; arrives only via `cpp_load` from another script** (the string `config.lua` is absent from the binary's string table on build 263bd994 — see the caveat below) | Developer bootstrap flags `CONFIG_*` (§3.1) plus the shared `tableString` table-read library (§5 / §5.3). The leading comment marks these as developer defaults not to be patched to users. |
| `data/script/uiconfig.lua` | Login-window scene build | Exactly one global: **`NEW_SERVER_INDEX`** (an integer index used to pre-select the default / newest server entry in the login server list). The remaining login widgets are built from compiled-in constants + the message database, not from Lua. |
| `data/script/display.lua` | Renderer display-config load | A large `DISPLAY_*` global set fed into renderer state (§4), including a derived shader-path string. |
| `data/script/tutor.lua` | Tutorial panel open/refresh | Tutorial text content, pulled row-by-row via the Lua table-read helpers `getTableString` / `getTableStringByID` (§5). |

> The C++ consumption surface fully constrains *which* globals/functions each script must define.
> The concrete *values* in `config.lua` / `display.lua` / `uiconfig.lua` are now corroborated by a
> single on-disk sample (§9); `game.lua` / `tutor.lua` shipped contents remain UNVERIFIED.
>
> **Build-263bd994 caveat (load-bearing).** Only **four** script paths are referenced *by name* in
> the binary string table: `game.lua`, `data/script/uiconfig.lua`, `data/script/display.lua`, and
> `data/script/tutor.lua` (plus `data/script/msg.xdb`, the message database — which is **not** a
> `.lua` file; see §8.5). **`config.lua` is NOT among them**, nor are `tableString` or any `CONFIG_*`
> key string. `config.lua`'s keys/library are recovered from the on-disk file (§9), not from the
> binary; the host therefore does **not** load `config.lua` directly — if it is loaded at all, it is
> chain-loaded via `cpp_load` from one of the named scripts (not visible statically). A reader must
> not expect a `config.lua` load call site in the binary. (See §9 for the on-disk sample provenance.)

---

## 3. Boot flags read from `game.lua` (CODE-CONFIRMED)

Three integer globals are read out of `game.lua` once at boot, behind a one-shot gate; each
**defaults to `1` (true)** if the read branch is skipped. Each is read as a number and compared
`!= 0`, so in `game.lua` they are written as plain `0` / `1` globals used as booleans.

| Global | Type (as read) | Effect at runtime |
|---|---|---|
| **`vfsmode`** | int used as bool (0/1) | Selects the asset source for the **whole client** (including `.lua` scripts via the router in §2.2). **`1` = use the mounted VFS archive** (packed assets); **`0` = read loose files from disk**. This is the flag that flips the script source path. |
| **`launcher`** | int used as bool (0/1) | Launcher bounce gate. With `launcher = 1` the client **re-launches through the external launcher** (`dostart.exe`) and exits — *unless* the client itself was started with the `-Start` command-line argument, in which case it proceeds directly into the engine state machine. With `launcher = 0` the client runs standalone. |
| **`debugmode`** | int used as bool (0/1) | Stored into game state and later consumed by the window/device setup: **`1` = windowed / debug presentation**; **`0` = fullscreen**. (The device-setup site selects windowed when this value is non-zero.) |

These three are the operational reason a config-script author would touch `game.lua`: VFS-vs-loose
asset source, whether to bounce through the launcher, and windowed-vs-fullscreen.

A fourth value read out of `game.lua` later is a game-addiction-warning timing number (a plain
integer, read by the same number reader as the flags above); it is a localisation/legal timing knob
and is outside the boot-flag triad.

> **Disentanglement of the two addiction-timing knobs (CORRECTION, build 263bd994).** There are
> **two distinct** addiction-warning timing values and they must not be conflated:
> 1. a `game.lua` integer read alongside the boot triad (the legal timing knob described above), and
> 2. the **`DISPLAY_GAME_ADDICTION_WARNING_CHECK_TIME`** key, which lives in `display.lua` (§4.2) and
>    is read in the **boot/login glue** by the same number-as-int reader and **scaled ×1000** at that
>    read site. The ×1000 site reads the **`DISPLAY_*` key**, not a `game.lua` global. §6's mention of
>    a "×1000 timing" refers to this `DISPLAY_*` read, not to the `game.lua` value.

### 3.1 Developer bootstrap flags read from `config.lua` (SAMPLE-CONFIRMED)

`config.lua` carries a separate set of **developer/system bootstrap** globals (distinct from the
`game.lua` boot triad). The file's leading Korean comment marks them as developer defaults that must
not be shipped to users in a patch. These are global constants set before other scripts evaluate.
Each is read by the number-as-int reader (§6).

| Key | Type | Default (observed) | Range | Effect |
|---|---|---|---|---|
| `CONFIG_NO_VFS` | bool (0/1) | `false` | `false` / `true` | `false` = use the VFS archive; `true` = legacy loose-filesystem mode (no VFS). A `config.lua`-level mirror of the asset-source choice. |
| `CONFIG_DEBUG_LEVEL` | int | `3` | `0`–`3` | Debug verbosity; lower = more output. `3` = minimal / no debug output (production). |
| `CONFIG_LOAD_SIMPLE_EFFECT` | int | `0` | `0`–`1` | `1` = fast / simplified effect loading; `0` = full effect loading. |
| `CONFIG_LOAD_MESH` | int | `1` | `0`–`1` | `1` = load via converted mesh instead of skin; `0` = load skin directly. Selects the character render-load path. |
| `CONFIG_SAVE_MESH` | int | `0` | `0`–`1` | `1` = after a skin loads, persist the converted mesh to disk (developer cache tool); `0` = do not persist. |

**Confidence:** SAMPLE-CONFIRMED for the keys and their inline-documented effects (decoded cleanly
via CP949). UNVERIFIED: the exact code path that honours `CONFIG_SAVE_MESH = 1` (the mesh-cache
write) and the precise interaction between `CONFIG_LOAD_MESH` and `CONFIG_SAVE_MESH` are not
confirmable from the file alone.

---

## 4. `display.lua` renderer globals (CODE-CONFIRMED / SAMPLE-CONFIRMED)

The renderer display-config load reads a set of `DISPLAY_*` globals from `display.lua` into renderer
state. All keys follow the pattern `DISPLAY_<CATEGORY>_<CHANNEL>_<STATE>`. The tint model throughout
is the affine form `out = MULTI * in + ADD`, applied per RGB channel, plus an alpha value. The
sampled file defines on the order of seventy keys.

### 4.1 Per-status character-brightness matrix

For each character status variant the file defines an 8-key block: a multiply factor and an additive
offset for each of R / G / B, plus a single alpha value (the inline comment notes alpha below roughly
0.6 renders fully transparent).

```
DISPLAY_CHAR_BRIGHT_MULTI_{R|G|B}_<STATE>   (float, per-channel multiply)
DISPLAY_CHAR_BRIGHT_ADD_{R|G|B}_<STATE>     (float, per-channel additive offset)
DISPLAY_CHAR_BRIGHT_ALPHA_<STATE>           (float, alpha)
```

The status variants and their inline-documented triggers:

| State suffix | Trigger (inline comment) |
|---|---|
| `DEFAULT` | Normal / idle state. |
| `CHOICE` | This NPC / monster is currently selected (targeted). |
| `HIT` | Character tint at the moment of receiving a hit. |
| `ALPHA` | Meaning unknown to the original authors ("ignoring for now") — defined but its trigger condition is not documented. |
| `HIDDEN` | Own hide / stealth mode, or a summoned creature of the same faction. |
| `POISON` | Character afflicted by poison. |
| `TYPE` | A damage-reduction "property type" buff is applied. |
| `ANGER` | Anger / rage mode active. |
| `AUTO` | Auto-attack penalty active. |

> The concrete per-state multiply/add/alpha numbers exist in the sampled file; they are renderer
> tuning values and are intentionally not transcribed here. An implementer reads them straight from
> the `.lua` file at runtime via `LuaConfigReader`.

### 4.2 Global display scalars

| Global | Type | Effect |
|---|---|---|
| `DISPLAY_BASE_BRIGHT_MULTI` | float | Global background (terrain / scene) brightness multiplier (the `MULTI` factor of the affine model). |
| `DISPLAY_GLOW_BRIGHT_MULTI` | float | Glow-pass brightness multiplier. |
| `DISPLAY_GLOW_RANGE_X` | int | Horizontal glow downsample factor (valid set: 1, 2, 4, 8; higher = coarser). |
| `DISPLAY_GLOW_RANGE_Y` | int | Vertical glow downsample factor (same scale). |
| `DISPLAY_FRAMERATE` | int | Show FPS counter: `0` = off, `1` = on. |
| `DISPLAY_POWER` | int | Glow shader intensity level (valid set: 1, 2, 4, 8, 16, 32); selects which glow pixel-shader file is used (§4.3). |
| `DISPLAY_LIGHT_RATIO` | float | Character light colour-correction factor (range 0.0–1.0). |
| `DISPLAY_GAME_CLASS_VIEW_TIME` | int | Minutes between game-rating (age-rating) UI notifications. |
| `DISPLAY_GAME_ADDICTION_WARNING_CHECK_TIME` | int | Seconds between addiction-warning UI checks. |
| `DISPLAY_GAME_ADDICTION_WARNING_VIEW_TIME` | int | Seconds the addiction-warning UI stays visible. |
| `DISPLAY_COMBO_COOL_TIME` | int | Combo-chain cooldown, in seconds. |
| `DISPLAY_POWERSHADER` | string | Path to the active glow pixel-shader `.psh` file — **derived, not a bare assignment** (§4.3). |

### 4.3 The derived `DISPLAY_POWERSHADER` key

`DISPLAY_POWERSHADER` is **computed** at the end of the file by an `if`/`elseif` chain over
`DISPLAY_POWER`, mapping the power level to a glow pixel-shader path of the form
`data/shader/power<N>dx8.psh`. It is the only `DISPLAY_*` key that is set programmatically rather
than as a bare scalar assignment; a config author must not pre-assign it. The host reads it back via
the string reader (§6) and copies it into a fixed-size buffer. This establishes a dependency from
`display.lua` onto the shader assets under `data/shader/`.

The integer members are read by the number-as-int reader (§6); the float members by the float reader
sibling; `DISPLAY_POWERSHADER` by the string reader. `display.lua` must define each member as the
appropriate Lua value type.

---

## 5. Tutorial string tables — the Lua table-read API (CODE-CONFIRMED)

The tutorial panel is the one feature that pulls **numbered text rows** out of Lua. After building
its (compiled-in) widget skeleton it runs `tutor.lua`, then fills a host-side string list by calling
Lua-side helper functions that the script must define.

### 5.1 The Lua functions a string-table script must define

These global function names are the **Lua API contract** the host depends on; a string-table
script (e.g. `tutor.lua`) must define them. `config.lua` ships the same family as a shared library
(§5.3).

| Lua function (called by host) | Shape | Role |
|---|---|---|
| `getTableSize(name)` | `(string) -> int` | Number of rows in the named table. |
| `getTableString(name, i)` | `(string, int) -> string` | The *i*-th row string of the named table. |
| `getTableStringByID(id)` / `getTableStringByID(id, i)` | `(int) -> int` and `(int, int) -> string` | ID-keyed variant: first form returns the row count for table `id`; second returns the *i*-th row string of that table. |

The host's name-based loader iterates `getTableSize(name)` then `getTableString(name, i)` for each
row. The ID-based loader iterates `getTableStringByID(id)` for the count then
`getTableStringByID(id, i)` per row. Each returned string is pushed into a host-side string list.

> **Decode asymmetry (build 263bd994, load-bearing — see §0/§5.2).** The two loaders convert rows
> differently: the **name-based** loader runs each returned row through a wide-char round-trip
> configured for code page 65001 (UTF-8) into a fixed 102400-byte scratch buffer before pushing it;
> the **ID-based** loader pushes the returned row bytes **directly**, with no wide-char round-trip.
> Regardless of either in-binary conversion, the shipped `.lua` *file* bytes are CP949 — a clean-room
> reader treats the on-disk bytes as ground truth and decodes the file as CP949.

Calls into these globals follow the standard `lua_tinker` typed-call pattern: the host looks up the
global by name, verifies it is a function (logging "attempt to call global … (not a function)" if
not), pushes the arguments, performs a protected call, and reads the typed return.

### 5.2 String decoding — original host-path observation (SUPERSEDED for file source; see §0)

> **Retained verbatim for an honest record.** *Original claim:* each row string returned by the Lua
> table-read API is decoded by the C++ host as UTF-8 (Windows code page 65001), via a wide-char
> conversion configured for that code page.
>
> **Status:** this describes one in-binary conversion routine and was previously over-generalised to
> the *file* encoding. **§0 supersedes that generalisation:** the shipped `.lua` *files* are CP949,
> confirmed by byte inspection. A clean-room reader treats the on-disk file bytes as ground truth and
> decodes them as **CP949**. The 65001-configured host routine is noted only so the record is
> complete; it does not override the confirmed file encoding for `LuaConfigReader`.

### 5.3 The `tableString` lookup library shipped in `config.lua` (SAMPLE-CONFIRMED)

`config.lua` defines a generic Lua string-table lookup library operating on a global `tableString`
variable (a nested table keyed by either name or numeric id). It exposes the §5.1 family plus
index-based siblings:

| Function | Signature | Role |
|---|---|---|
| `getTableSize(name)` | `(string) -> int` | Count of sub-entries under a named key. |
| `getTableString(name, num)` | `(string, int) -> string` | Fetch the *num*-th sub-entry of a named key. |
| `getTableSizeByIndex(index)` | `(int) -> int` | Count sub-entries by numeric position in `tableString`. |
| `getTableStringByIndex(index, num)` | `(int, int) -> string` | Fetch a sub-entry by table position and sub-index. |
| `getTableSizeByID(id)` | `(int) -> int` | Count sub-entries for a numeric id key. |
| `getTableStringByID(id, num)` | `(int, int) -> string` | Fetch a sub-entry for a numeric id key + sub-index. |

In the sampled file the `tableString` content is a **developer stub / test fixture** (placeholder
entries under dummy ids), not production data. **UNVERIFIED:** whether production usage replaces
`tableString` at load time with real content from another script, or whether this library is seeded
externally / is vestigial. This cannot be resolved without tracing the Lua `cpp_load` / `dofile`
call graph. A commented-out block in the file shows a name-keyed variant of the same structure.

---

## 6. The global config reader — a number-as-int reader, NOT a boolean (CORRECTION)

> **Naming correction (load-bearing).** The global-reading helper that the boot flags and the
> integer `DISPLAY_*` globals flow through was previously labelled as reading a *boolean*. That label
> is a **misnomer**: the reader pushes the global, fetches it from the global table, then converts
> the **Lua number to a full signed integer** and returns that entire integer value — it does **not**
> clamp to 0/1.

Proof that it returns the full integer (not a clamped bool): the very same reader is used for genuine
multi-valued integers — the target frame-rate, the glow range, the addiction-warning timing values
(scaled by 1000), the `CONFIG_DEBUG_LEVEL` 0–3 level, the `DISPLAY_POWER` level, and the
`NEW_SERVER_INDEX` server selector. The boot flags merely *happen* to be 0/1 in their scripts, which
is why the misnomer arose.

There is in fact a small **polymorphic global-reader family**, selected by which sibling the host
calls:

| Reader variant | Returns | Used for |
|---|---|---|
| number-as-int reader | full signed `int` | `vfsmode`, `launcher`, `debugmode`, the `CONFIG_*` ints, `DISPLAY_FRAMERATE`, `DISPLAY_GLOW_RANGE_X/Y`, `DISPLAY_POWER`, the addiction-warning timing ints, `NEW_SERVER_INDEX` |
| string reader | string value | `DISPLAY_POWERSHADER` |
| float reader (sibling) | `float` | the `DISPLAY_*` brightness-matrix floats and the float scalars (`DISPLAY_BASE_BRIGHT_MULTI`, `DISPLAY_GLOW_BRIGHT_MULTI`, `DISPLAY_LIGHT_RATIO`) |

**Implementation contract:** treat a config global read as **"read the Lua number as a full
integer"**, not as a 0/1 boolean. For 0/1 flags, compare the returned integer `!= 0`. Do not clamp.

---

## 7. Implementation guidance (clean-room reimplementation)

| Item | Guidance |
|---|---|
| VM scope | One process-wide Lua 5.1.x-compatible state; open the standard library; register exactly one global, `cpp_load`. No gameplay/object bindings in this build. |
| `cpp_load` | Implement as a script-side include that runs another script through the same loader (re-entrant). |
| Script source | Route script loading through the **same VFS-vs-disk abstraction** as other assets; the `vfsmode` flag selects packed-VFS vs loose-disk. Do **not** hard-code a plain-filesystem reader. |
| **File encoding** | **`LuaConfigReader` decodes every shipped `.lua` file as CP949 (code page 949).** Register the CP949 provider once; files have no BOM and use CRLF line endings. Do NOT decode as UTF-8. (§0) |
| Boot flags | `vfsmode` (VFS vs loose), `launcher` (launcher bounce unless `-Start`), `debugmode` (windowed vs fullscreen). Read as ints; default each to `1` if absent; interpret as `!= 0`. |
| Developer flags | `config.lua` defines `CONFIG_NO_VFS` / `CONFIG_DEBUG_LEVEL` / `CONFIG_LOAD_SIMPLE_EFFECT` / `CONFIG_LOAD_MESH` / `CONFIG_SAVE_MESH` (§3.1) — read as ints; these are developer defaults. |
| Config globals | Read each as the correct Lua type (int / float / string). The integer reader returns the **full int**, never a clamped bool. |
| `DISPLAY_POWERSHADER` | Treat as **derived** from `DISPLAY_POWER`; do not pre-assign. Maps to `data/shader/power<N>dx8.psh`. |
| String tables | The script must define `getTableSize` / `getTableString` / `getTableStringByID`. Decode every returned row using the file's CP949 encoding (§0/§5.2). |
| Interpreter vs. direct-parse | The data-vs-logic trade-off (embed a managed Lua 5.1 interpreter vs. direct-parse the data tables) is unchanged from `specs/lua_scripting.md` §7 — gated on recovering real `.lua` samples (now partially available, §9). |

---

## 8. Known unknowns

1. **`tableString` production seeding.** The sampled `config.lua` ships stub/test data. Whether
   production replaces it externally or whether the library is vestigial is UNVERIFIED without the
   `cpp_load` / `dofile` call graph.
2. **`config.lua` mesh-cache path.** The exact code path honouring `CONFIG_SAVE_MESH = 1` and the
   `CONFIG_LOAD_MESH` interaction are UNVERIFIED from the file alone.
3. **`DISPLAY_CHAR_BRIGHT_*` `ALPHA` state.** The original authors' comment marks the `ALPHA`
   status's trigger as unknown ("ignoring for now"); the values exist but the trigger does not.
4. **`NEW_SERVER_INDEX` target table.** Which server-list table the index `NEW_SERVER_INDEX` selects
   into is not visible in these files; it references a list loaded elsewhere.
5. **Source vs. precompiled bytecode.** Whether shipped `.lua` files are source text or precompiled
   Lua 5.1 bytecode is unconfirmed (the inspected samples were readable source text, but the full
   shipped set is not confirmed uniformly source).
6. **`cpp_load` include chains.** The named scripts may pull additional siblings via `cpp_load`; the
   included set is not enumerable from the binary, and no `cpp_load` / `dofile` calls were observed in
   the three inspected files (each is self-contained).
7. **The `.xdb` filename-pointer block** adjacent to the `tutor.lua` path pointer is **NOT
   Lua-bound** — out of scope here; defer to the `.xdb` / asset lanes. Separately, the message
   database **`data/script/msg.xdb`** is loaded at the same early boot/login point as the Lua scene
   build (right beside the `uiconfig.lua` login-window load), so it sits *adjacent in the boot flow*
   to the Lua config tree. It is **not** a `.lua` file and is not part of the Lua subsystem; a reader
   should not conflate it with the config scripts. (Cross-ref: the message-database `.xdb` spec.)

---

## 9. Sample provenance (file inspection, no IDA)

`config.lua`, `display.lua`, and `uiconfig.lua` were observed as on-disk files (black-box header /
hexdump inspection; no decompiler). Observed file characteristics: no BOM; CRLF line endings; CP949
double-byte Korean comments (§0). `config.lua` carries the five `CONFIG_*` developer flags plus the
`tableString` library; `display.lua` carries on the order of seventy `DISPLAY_*` keys and the derived
`DISPLAY_POWERSHADER`; `uiconfig.lua` is a minimal single-key file defining `NEW_SERVER_INDEX`.
These are **single-sample** observations: the schema (which globals/functions exist) is firmly
corroborated, but concrete numeric values are intentionally not transcribed and are read at runtime.

---

## Cross-references

- **VM identity, `lua_tinker` binding, host-pulls-from-scripts model, interpreter trade-off:**
  `specs/lua_scripting.md` (this file is its operational companion).
- **VFS container / mount that `vfsmode` selects:** `formats/pak.md`.
- **Login server-list scene that consumes `NEW_SERVER_INDEX`:** `specs/frontend_scenes.md` /
  `specs/login_flow.md`.
- **Renderer state fed by `display.lua` (brightness / glow / lighting):** `specs/environment.md`.
  Glow shader path `data/shader/power<N>dx8.psh` is consumed by the rendering layer.
- **Project-wide CP949 text rule:** all game text is CP949 — see `CLAUDE.md`; this spec's §0 confirms
  the `.lua` source files follow that rule.
- **Canonical names:** see `Docs/RE/names.yaml` (config-reader family, the trampoline, the tutorial
  widget-builder helpers; proposed `CONFIG_*` / `DISPLAY_*` / `NEW_SERVER_INDEX` glossary entries).
- **Provenance:** see `Docs/RE/journal.md`.
