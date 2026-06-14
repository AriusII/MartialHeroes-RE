---
status: confirmed
sample_verified: false   # C++ consumption surface CODE-CONFIRMED; actual .lua file contents live in the VFS, not the binary
subsystems: [lua_vm, config_scripts, string_tables, boot_flags]
networked: false          # Lua is a client-side config/text-table engine; nothing on the wire
encoding_note: Lua string-table rows decode as UTF-8 (code page 65001), NOT CP949 — see §5
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

---

## Status banner

| Area | Confidence |
|---|---|
| One process-wide Lua state, lazily created, standard libraries opened | CODE-CONFIRMED |
| Exactly one custom C function registered (`cpp_load`); no gameplay objects exposed | CODE-CONFIRMED |
| `cpp_load` = script-side include (chain-loads another `.lua` through the same loader) | CODE-CONFIRMED |
| Four named config scripts and their loader call sites | CODE-CONFIRMED |
| Boot globals `vfsmode` / `launcher` / `debugmode` (read, gate, and downstream effect) | CODE-CONFIRMED |
| `uiconfig.lua` global `NEW_SERVER_INDEX` | CODE-CONFIRMED |
| `display.lua` `DISPLAY_*` global set (ints, the per-status float brightness matrix, one string) | CODE-CONFIRMED |
| `getTableSize` / `getTableString` / `getTableStringByID` Lua API contract | CODE-CONFIRMED |
| **Lua string-table rows decode as UTF-8 (code page 65001), NOT CP949** | CODE-CONFIRMED |
| The global config reader returns a Lua *number as a full int* (the "bool" name is a misnomer) | CODE-CONFIRMED |
| Scripts are sourced through the same VFS-vs-disk router as every other asset | CODE-CONFIRMED |
| The actual *contents/values* inside each `.lua` (they live in the VFS, not the binary) | UNVERIFIED |

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

It is the script-side **`#include` / `dofile`** mechanism: any of the four named config scripts may
chain-load further config scripts through `cpp_load`. It is re-entrant. The binding is registered
through `lua_tinker`'s generic single-upvalue trampoline (one shared C dispatcher carries the bound
C function pointer as an upvalue), but that is binding plumbing, not a semantic extension — see
`specs/lua_scripting.md` §3.

---

## 2. Script load path and the four config scripts

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

### 2.3 The four config scripts

| Script (relative path) | Loaded at | What the host reads back |
|---|---|---|
| `game.lua` | Boot (early startup) | Boot flags `vfsmode`, `launcher`, `debugmode` (§3); later a game-addiction warning timing value. `game.lua` defines these as plain global numbers. |
| `data/script/uiconfig.lua` | Login-window scene build | Exactly one global: **`NEW_SERVER_INDEX`** (an integer index used to pre-select the default / newest server entry in the login server list). The remaining login widgets are built from compiled-in constants + the message database, not from Lua. |
| `data/script/display.lua` | Renderer display-config load | A large `DISPLAY_*` global set fed into renderer state (§4). |
| `data/script/tutor.lua` | Tutorial panel open/refresh | Tutorial text content, pulled row-by-row via the Lua table-read helpers `getTableString` / `getTableStringByID` (§5). |

> UNVERIFIED: the exact contents/values of each `.lua` file are **not in the binary** — they live in
> the VFS. The above is the C++ *consumption surface*, which fully constrains the schema each script
> must satisfy (which globals/functions it must define) but not the concrete values it assigns.

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

---

## 4. `display.lua` renderer globals (CODE-CONFIRMED)

The renderer display-config load reads a set of `DISPLAY_*` globals from `display.lua` into renderer
state. The set is large; the structurally significant members are:

| Global | Type | Notes |
|---|---|---|
| `DISPLAY_GLOW_RANGE_X`, `DISPLAY_GLOW_RANGE_Y` | int | Glow kernel range (a small default such as 2). |
| `DISPLAY_FRAMERATE` | int | Target frame-rate cap. |
| `DISPLAY_CHAR_BRIGHT_{MULTI,ADD,ALPHA}_{R,G,B}_{state}` | float | A **per-status brightness matrix**: a multiply / add / alpha triple, each with R/G/B components, for each character status (DEFAULT / CHOICE / HIT / ALPHA / HIDDEN / POISON / TYPE / ANGER / AUTO). This is the bulk of the file. |
| `DISPLAY_BASE_BRIGHT_MULTI` | float | Base brightness multiplier. |
| `DISPLAY_GLOW_BRIGHT_MULTI` | float | Glow brightness multiplier. |
| `DISPLAY_LIGHT_RATIO` | float | Lighting ratio. |
| `DISPLAY_POWERSHADER` | string | A short string (read as a string global), copied into a fixed-size buffer — most plausibly a shader filename. |

The integer members are read by the number-as-int reader (§6); the float members are read by the
string/float reader sibling (the value is consumed as a float); `DISPLAY_POWERSHADER` is read by the
string reader. So `display.lua` must define each of these as the appropriate Lua value type.

---

## 5. Tutorial string tables — the Lua table-read API (CODE-CONFIRMED)

The tutorial panel is the one feature that pulls **numbered text rows** out of Lua. After building
its (compiled-in) widget skeleton it runs `tutor.lua`, then fills a host-side string list by calling
Lua-side helper functions that `tutor.lua` must define.

### 5.1 The three Lua functions a string-table script must define

These three global function names are the **Lua API contract** the host depends on; a string-table
script (e.g. `tutor.lua`) must define them:

| Lua function (called by host) | Shape | Role |
|---|---|---|
| `getTableSize(name)` | `(string) -> int` | Returns the number of rows in the named table. |
| `getTableString(name, i)` | `(string, int) -> string` | Returns the *i*-th row string of the named table. |
| `getTableStringByID(id)` / `getTableStringByID(id, i)` | `(int) -> int` and `(int, int) -> string` | ID-keyed variant: first form returns the row count for table `id`; second returns the *i*-th row string of that table. |

The host's name-based loader iterates `getTableSize(name)` then `getTableString(name, i)` for each
row. The ID-based loader iterates `getTableStringByID(id)` for the count then
`getTableStringByID(id, i)` per row. Each returned string is pushed into a host-side string list.

Calls into these globals follow the standard `lua_tinker` typed-call pattern: the host looks up the
global by name, verifies it is a function (logging "attempt to call global … (not a function)" if
not), pushes the arguments, performs a protected call, and reads the typed return.

### 5.2 String decoding — UTF-8, NOT CP949 (LOAD-BEARING)

> **Critical divergence from the project-wide "all game text is CP949" assumption.** Each row
> string returned by the Lua table-read API is decoded as **UTF-8 (Windows code page 65001)**, not
> CP949 (code page 949). The host converts the row from UTF-8 wide form to a multibyte buffer and
> stores the result. Any table loader that consumes Lua string-table rows **must decode them as
> UTF-8** — applying the project's default CP949 decode here would corrupt the text.
>
> This applies to the **Lua string-table text path specifically**. It does not change the encoding
> of the project's other (non-Lua) CP949 text tables; it is a per-path exception that the engineer
> must honour exactly for the `getTableSize` / `getTableString` / `getTableStringByID` rows.

---

## 6. The global config reader — a number-as-int reader, NOT a boolean (CORRECTION)

> **Naming correction (load-bearing).** The global-reading helper that the boot flags and the
> integer `DISPLAY_*` globals flow through was previously labelled as reading a *boolean*. That label
> is a **misnomer**: the reader pushes the global, fetches it from the global table, then converts
> the **Lua number to a full signed integer** and returns that entire integer value — it does **not**
> clamp to 0/1.

Proof that it returns the full integer (not a clamped bool): the very same reader is used for genuine
multi-valued integers — the target frame-rate, the glow range, the addiction-warning timing values
(scaled by 1000), and the `NEW_SERVER_INDEX` server selector. The boot flags merely *happen* to be
0/1 in `game.lua`, which is why the misnomer arose.

There is in fact a small **polymorphic global-reader family**, selected by which sibling the host
calls:

| Reader variant | Returns | Used for |
|---|---|---|
| number-as-int reader | full signed `int` | `vfsmode`, `launcher`, `debugmode`, `DISPLAY_FRAMERATE`, `DISPLAY_GLOW_RANGE_X/Y`, the addiction-warning timing ints, `NEW_SERVER_INDEX` |
| string reader | `const char*` (string pointer) | `DISPLAY_POWERSHADER` |
| float reader (sibling) | `float` (consumed off the FPU) | the `DISPLAY_*` brightness-matrix floats |

**Implementation contract:** treat a config global read as **"read the Lua number as a full
integer"**, not as a 0/1 boolean. For 0/1 flags, compare the returned integer `!= 0`. Do not clamp.

---

## 7. Implementation guidance (clean-room reimplementation)

| Item | Guidance |
|---|---|
| VM scope | One process-wide Lua 5.1.x-compatible state; open the standard library; register exactly one global, `cpp_load`. No gameplay/object bindings in this build. |
| `cpp_load` | Implement as a script-side include that runs another script through the same loader (re-entrant). |
| Script source | Route script loading through the **same VFS-vs-disk abstraction** as other assets; the `vfsmode` flag selects packed-VFS vs loose-disk. Do **not** hard-code a plain-filesystem reader. |
| Boot flags | `vfsmode` (VFS vs loose), `launcher` (launcher bounce unless `-Start`), `debugmode` (windowed vs fullscreen). Read as ints; default each to `1` if absent; interpret as `!= 0`. |
| Config globals | Read each as the correct Lua type (int / float / string). The integer reader returns the **full int**, never a clamped bool. |
| String tables | The script must define `getTableSize` / `getTableString` / `getTableStringByID`. **Decode every returned row as UTF-8 (cp 65001), not CP949.** |
| Interpreter vs. direct-parse | The data-vs-logic trade-off (embed a managed Lua 5.1 interpreter vs. direct-parse the data tables) is unchanged from `specs/lua_scripting.md` §7 — that decision is gated on recovering real `.lua` samples. |

---

## 8. Known unknowns

1. **Actual `.lua` contents/values.** Every concrete value (`vfsmode`'s shipped setting, the
   `DISPLAY_*` matrix numbers, `NEW_SERVER_INDEX`, the tutorial row text) lives in the VFS, not the
   binary. The C++ consumption contract above fully constrains *which* globals/functions each script
   must define, but the values themselves are UNVERIFIED until a `.lua` sample is recovered.
2. **Source vs. precompiled bytecode.** Whether the shipped `.lua` files are source text or
   precompiled Lua 5.1 bytecode is unconfirmed (carried over from `specs/lua_scripting.md`
   UNVERIFIED item 4).
3. **Whether any further config script is `cpp_load`-included.** The four named scripts may pull in
   additional siblings via `cpp_load`; the included set is not enumerable from the binary.
4. **The `.xdb` filename-pointer block** that sits adjacent to the `tutor.lua` path pointer in
   read-only data is **NOT Lua-bound** — only the `tutor.lua` pointer has live code references; the
   `.xdb` entries are loaded by their own asset loaders and are out of scope here (defer to the
   `.xdb` / asset lanes). Do not fold the `.xdb` table into this spec.

---

## Cross-references

- **VM identity, `lua_tinker` binding, host-pulls-from-scripts model, interpreter trade-off:**
  `specs/lua_scripting.md` (this file is its operational companion).
- **VFS container / mount that `vfsmode` selects:** `formats/pak.md`.
- **Login server-list scene that consumes `NEW_SERVER_INDEX`:** `specs/frontend_scenes.md` /
  `specs/login_flow.md`.
- **Renderer state fed by `display.lua`:** `specs/environment.md` (brightness / glow / lighting).
- **Canonical names:** see `Docs/RE/names.yaml` (e.g. the config-reader family proposed as
  `LuaConfig_GetGlobalInt` / `LuaConfig_ReadGlobalNumber` / `LuaTinker_ReadNumberAsInt`; the
  trampoline as `luaT_invoke_trampoline`; the tutorial widget-builder helpers by role).
- **Provenance:** see `Docs/RE/journal.md`.
