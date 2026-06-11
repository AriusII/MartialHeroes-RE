# Lua Scripting Subsystem Specification — VM, Binding Layer, and `.lua` Data Tree

> Clean-room neutral spec. Promoted from dirty-room analyst notes by the spec-author.
> No decompiler identifiers, no addresses, no pseudo-code. Interoperability facts only
> (standard Lua C API names, the open-source `lua_tinker` binding, the `cpp_load` global)
> are cited where load-bearing.
> Implementation context: the .NET client's configuration / localization / UI-layout pipeline.

---

## Status and verification banner

- `status: confirmed` — for the **VM version**. The interpreter is **Lua 5.1.2**, pinned from
  the exact compiled-in version banner (see Section 1). This is a hard fact.
- `sample_verified: false` — for everything concerning the **role, on-disk location, encoding,
  and content** of the `.lua` files. No actual `.lua` sample has been recovered and parsed; the
  file names, the `data/script/` directory, the cleartext/disk loading path, and the
  data-table semantics are inferred from string constants and observed loading behavior in the
  legacy client, not from inspecting shipped scripts.

### UNVERIFIED items (do not treat as settled)

1. Exact patch level beyond the printed `5.1.2`. Treat `5.1.2` as authoritative because it is the
   literal banner; no finer version evidence exists.
2. Whether any caller could redirect script loading through the `.pak` VFS. The observed loader
   reads plain on-disk files; a higher-level redirect was not ruled out.
3. Whether the game exposes **more** native script API than `cpp_load`. Only the one global custom
   C function was located at the global table. Additional gameplay bindings (UI / quest / item
   APIs), if any, would arrive via `lua_tinker` per-type registration that has not been mapped.
4. Whether shipped `.lua` files are source text or precompiled Lua 5.1 bytecode (`luac`). No
   sample was inspected. If precompiled, they would be standard Lua 5.1 bytecode
   (header `\x1bLua`, version byte `0x51`); not confirmed.
5. Exact UTF-8 encoding assumption for every string table — inferred from the observed decode
   step, not validated against a sample.

---

## 1. VM version — Lua 5.1.2 (confirmed)

The scripting VM is **stock Lua 5.1.2**, identified by the verbatim compiled-in banner:

```
Lua 5.1.2  Copyright (C) 1994-2007 Lua.org, PUC-Rio
```

Corroborating evidence, all consistent with a stock 5.1.x build:

- The standard Lua 5.1 protected C API is present and in use — notably `lua_pcall`,
  `lua_open`, `luaL_openlibs`, and `luaL_loadbuffer`. The presence of the **protected**
  call path (`lua_pcall`) plus the `luaL_*` auxiliary library rules out the 5.0-era
  `lua_dofile` / `lua_call` model.
- The standard 5.1 register-VM tokens appear (`GETGLOBAL`, `SETGLOBAL`, `_LOADED`), matching
  the 5.1 register-based virtual machine.
- The standard 5.1 runtime error strings are present unmodified (out-of-memory, nested-function,
  dead-coroutine, stack-overflow, yield-across-C-boundary, and the comparison/type errors that
  ship in the 5.1.x core).

**Implication for reimplementation:** target a **Lua 5.1.x-compatible** interpreter on the .NET
side. Do **not** target 5.2 or 5.3 — they differ in integer handling, the `_ENV` upvalue model,
and `goto`, which would change script semantics.

---

## 2. Virtual machine integrity — stock, not forked or obfuscated

There is no evidence of a custom, forked, or obfuscated VM:

- Verbatim version banner, stock error strings, and the stock panic message (Section 2.1).
- The only non-stock layer is the open-source **`lua_tinker`** C++↔Lua binding (Section 3).
- No custom bytecode opcodes, no bytecode obfuscation, and no renamed VM were found.

### 2.1 The "ANIC:" demystification — there is no custom VM

An earlier lead referenced a string `ANIC: unprotected error in call to Lua API (%s)` and was
read as a possible in-house VM brand (`"ANI…"`). **That is a false lead.** The actual constant is
the **standard Lua panic message**:

```
PANIC: unprotected error in call to Lua API (%s)
```

The `ANIC:` fragment is simply the stock `PANIC:` string read starting one byte too far in. There
is **no** `"ANI"` prefix, no custom VM branding, and no in-house Lua fork. Any reimplementation
notes that mention an "ANIC VM" should be corrected to "stock Lua 5.1.2 + `lua_tinker`."

---

## 3. C++ ↔ Lua binding layer — `lua_tinker`

Stock Lua 5.1.2 is wrapped with the well-known open-source **`lua_tinker`** binding. This is the
only bespoke glue between the host (C++) and the script VM, and it is identifiable from its own
characteristic strings (e.g. its `call()` and `dobuffer()` markers used as chunk/source names).

Roles `lua_tinker` provides in this client:

- **Buffer/script execution** — loads a script buffer and runs it under a protected call, routing
  any load-time or runtime error message through `lua_tinker`'s own print/alert path. The error
  path, in standard `lua_tinker` fashion, calls the Lua global `_ALERT` if it is a function,
  otherwise prints the message to standard output.
- **Host→script invocation helpers** — a small family of typed call wrappers that invoke a global
  Lua function and marshal the result. The observed call shapes are: zero arguments returning an
  integer; one string argument returning an integer; zero arguments returning a string; and one
  string argument returning a string. These are the channels through which the host pulls data out
  of scripts (Section 5).
- **64-bit integer support** — `lua_tinker`'s standard `__s64` / `__u64` userdata metatables are
  registered (with the usual `__name` / `__tostring` / `__eq` / `__lt` / `__le` metamethods). This
  is plumbing for passing signed/unsigned 64-bit integers across the C++↔Lua boundary (Lua 5.1
  numbers are doubles, so wide integers need this). It is infrastructure, **not** a gameplay API.

**Implication for reimplementation:** `lua_tinker` is an interop convenience, not a semantic
extension of the language. A managed reimplementation does not need `lua_tinker` itself — only the
two capabilities it provides here: (a) calling a small set of script-defined global functions and
reading script globals, and (b) representing 64-bit integers if any script actually exchanges them
(none was observed in the data-table path).

---

## 4. Native API surface exposed to scripts — minimal

At VM initialization the host opens the VM, loads the **full standard library** via the standard
`luaL_openlibs` (string, table, math, os, io, debug, coroutine, package — confirmed by stock
stdlib name strings such as `setmetatable`, `pcall`, `loadstring`, `dofile`, `collectgarbage`),
and then registers **exactly one** custom global C function:

| Global | Purpose |
|---|---|
| `cpp_load` | Source-include / run another `.lua` file from within a running script. It forwards to the same disk-file loader the host uses, so it is effectively a script-side `dofile` over the `data/script/` tree. Re-entrant. |

Only one registration call site for a custom global was found, so the **bespoke global C-function
surface is just `cpp_load`**. No `luaL_register` / `luaL_Reg{}` tables were located. Beyond
`cpp_load`, scripts see only the standard Lua stdlib and the `__s64` / `__u64` metatables.

> UNVERIFIED: `lua_tinker` also supports per-type class/`def` registration. No additional gameplay
> bindings (UI / quest / item) were found at global scope, but a deeper pass is warranted if
> real scripts are recovered. See UNVERIFIED item 3.

---

## 5. Direction of control — host pulls from scripts (data, not logic)

The dominant control flow is **C++ → script**: the host calls functions *defined in* the scripts
and reads *globals set by* the scripts. Scripts are **not** authoritative game logic; they are a
data/configuration and localization layer.

Observed host-driven access patterns:

- **Table accessors** — the host calls script-defined globals `getTableSize` (count of entries in
  a script-defined table) and `getTableString` (the *i*-th string of a script-defined table) to
  read out localized / UI string tables.
- **Config globals** — the host reads boolean and string globals that scripts assign at top level,
  i.e. scripts express configuration as plain global assignments that the host reads back.
- **String-table extraction** — a loader iterates the `getTableSize` / `getTableString` pair,
  decodes each entry as UTF-8, and fills a host-side string vector. This is the mechanism by which
  `.lua` files carry the game's UI/text string tables (i18n). A by-ID variant exists for selecting
  a specific table.

**Net effect:** scripts behave as **configuration + localization tables + UI-layout descriptors**.
They cannot drive gameplay beyond what `cpp_load` plus the standard libraries allow.

---

## 6. Role and location of `.lua` files

> `sample_verified: false` for this entire section.

### 6.1 Known files (from path string constants)

| File | Inferred role |
|---|---|
| `game.lua` | Top-level / boot script (referenced by a relative name). |
| `data/script/uiconfig.lua` | UI configuration. |
| `data/script/display.lua` | Renderer / display configuration. |
| `data/script/tutor.lua` | Tutorial panels / help content and its localized text tables. |

(The `.lua.org` fragment from the version banner is **not** a path and is ignored.)

### 6.2 Loading path — plain disk files, cleartext, not the `.pak` VFS

Scripts are loaded as **plain on-disk files in cleartext** from the `data/script/` directory,
using a raw filesystem file class (open / size / read), then handed to the protected
load-and-run path. There is **no decryption and no `.pak` VFS lookup** on the observed script
load path.

> A pre-existing analyst-tool comment claimed this loader is "VFS-backed." That comment is
> **inaccurate** versus the observed implementation, which reads plain disk files. Treat the
> script loader as a plain filesystem loader. (UNVERIFIED whether a higher-level caller could
> redirect to the VFS — UNVERIFIED item 2.)

### 6.3 Who triggers script loads

- Boot-time load of the top-level / UI config scripts during client startup.
- A graphics/display path that loads `display.lua`.
- A login / cash-shop window whose UI scene is built from a script.
- A tutorial subsystem that loads tutorial content and its localized string tables from
  `tutor.lua`.
- The `cpp_load` global, when a running script includes another script (re-entrant).

---

## 7. Reimplementation Note (.NET client) — interpreter vs. direct parsing

This is an **open trade-off for the checkpoint, not a decision.** Document both paths; do not pick
one here.

### 7.1 Why a trade-off exists

The native API exposed to scripts is tiny: a single custom global (`cpp_load`) plus the standard
library, with the host doing all the driving (read globals, call `getTableSize` / `getTableString`).
Because of that, the `.lua` corpus splits into two very different kinds of file:

- **Static data tables** — localization / string tables and flat config globals. These are
  effectively serialized data that happens to be written in Lua syntax. They could plausibly be
  read by a **direct parser** (or a one-time offline conversion to C#/JSON/resx) without running a
  VM at all, given the host only reads them back.
- **UI-logic scripts** — files that *build* UI scenes or compute layout (e.g. the login/cash-shop
  window and parts of `uiconfig.lua` / `display.lua` / `tutor.lua`). If any of these contain
  control flow, expressions, or call `cpp_load` to compose other scripts, faithfully running them
  requires an **actual Lua 5.1 interpreter**.

### 7.2 Option A — embed a managed Lua 5.1 interpreter

Use a Lua 5.1.x-compatible managed interpreter (for example **MoonSharp** in its 5.1-compatible
mode, or a Lua-C# library that targets 5.1 semantics). Re-expose the minimal host surface:
register a single `cpp_load` global, the `_ALERT`-style error sink, and the host-side readers for
script globals and the `getTableSize` / `getTableString` accessors. 64-bit integer marshalling is
only needed if a recovered script actually exchanges wide integers (none observed so far).

- Pros: faithful to any `.lua` that contains logic; one code path for both data and UI scripts;
  scripts remain editable as original assets.
- Cons: pulls a scripting runtime and its allocation/JIT profile into the client; must pin
  5.1 semantics (reject 5.2/5.3); a managed VM is harder to keep on the zero-allocation hot path
  (though scripts here run at load time, not per-frame, which softens this).

### 7.3 Option B — parse the data tables directly, no VM

Convert the static `.lua` data/localization tables to a native C# representation (parse at load,
or convert offline to a managed format) and skip the VM for those.

- Pros: no scripting runtime dependency for the bulk of the corpus; trivially testable;
  fits the engine-free core cleanly.
- Cons: does **not** cover any UI script that contains real logic or `cpp_load` composition —
  those would still need an interpreter or a hand-port to C#. Risk of silently mis-handling a
  file that looks like data but contains expressions.

### 7.4 Recommended framing for the checkpoint (not a decision)

Because the exposed API is minuscule (`cpp_load` + reading globals), a large fraction of the
corpus is likely **static data tables** that are candidates for **direct parsing / offline
conversion**, while the **UI-logic `.lua`** plausibly require a **managed 5.1 interpreter**. A
**hybrid** is therefore the natural shape: direct-parse the data/localization tables, and gate the
interpreter decision on how much real logic the UI scripts actually contain.

**This cannot be settled without `.lua` samples.** The blocker is UNVERIFIED items 1, 4, and 5
(version patch level, source-vs-bytecode, encoding) plus a content survey of `uiconfig.lua` /
`display.lua` / `tutor.lua` to measure the data-vs-logic ratio. Resolve those at the checkpoint
before committing to Option A, B, or the hybrid.

---

## 8. Open items / blockers

- Recover real `.lua` samples (`game.lua`, `uiconfig.lua`, `display.lua`, `tutor.lua`) to flip
  `sample_verified` to true and to settle UNVERIFIED items 1, 4, 5.
- Confirm whether script loading can be redirected through the `.pak` VFS (UNVERIFIED item 2).
- Confirm whether any gameplay API beyond `cpp_load` is exposed via `lua_tinker` class/`def`
  registration (UNVERIFIED item 3).
- Once samples exist, measure the static-data vs. UI-logic split to close the Section 7 trade-off.
