# Format: .lua  (embedded Lua 5.1 configuration / bootstrap scripts)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset / constant an engineer cites must reference this file.
>
> verification: sample-verified (loader control flow read from the binary; the three shipped scripts
>   `data/script/uiconfig.lua`, `data/script/display.lua`, and `data/script/config.lua` corroborate
>   that the on-disk artifact is plain Lua 5.1 source text in CP949 — metadata / short windows only,
>   no payload copied). The embedded-interpreter identification is [confirmed] (the PUC-Rio Lua 5.1.2
>   copyright banner and the `lua_tinker` binding strings are present in the binary, and the standard
>   Lua 5.1 pseudo-indices / type tags are observed at the read/set/call sites). Per-file global key
>   SETS are [confirmed] for `game.lua`, `uiconfig.lua`, and `display.lua`; `config.lua` sample-
>   observed keys are noted (consumer read-back site not yet chased); `tutor.lua`'s key set is
>   unverified (no sample).
> ida_reverified: 2026-06-24
> ida_anchor: 263bd994c927c20a38624cf0ca452eaef365057fa9db1543d8f668c14a6fd8ee
> evidence: [static-ida, vfs-sample]
> conflicts: none.
> status: sample_verified
> sample_verified: true (uiconfig.lua, display.lua, config.lua — confirmed plain Lua 5.1 source
>   text, CP949)

---

## Headline — this is NOT a bespoke key=value format

The `.lua` files in this client are **real Lua 5.1.2 source text** (the unmodified PUC-Rio
interpreter is embedded in the client — its `$Lua: Lua 5.1.2 Copyright (C) 1994-2007 Lua.org,
PUC-Rio $` banner string is present in the binary). The C++ side drives the interpreter through a
thin `lua_tinker`-style binding layer (its `dobuffer()` and "attempt to call global '%s' (not a
function)" diagnostic strings are present).

Consequently there is **no on-disk record layout in the binary-blob sense.** The on-disk format
**is a Lua chunk** (plain Lua source). The client reads the whole file into a NUL-terminated buffer
and loads + protected-calls it (`luaL_loadbuffer` + `lua_pcall`) inside **one shared, persistent
`lua_State`**. A configuration "field" is just a **Lua global variable** that the C++ code reads
back **by name**.

A naive line-splitting `key = value` parser would be WRONG in the general case: values may be Lua
expressions or even functions (the client supports function-valued config — see §5), and Lua
comments use `--`. A faithful re-implementation needs a Lua 5.1 evaluator (or, pragmatically for
the small shipped files, a tolerant expression evaluator that understands assignments, `--`
comments, numeric/float literals, and a one-argument function call form).

---

## Identification

- **Extension:** `.lua`
- **Found in:**
  - `game.lua` — a **loose-disk bootstrap** file, opened by bare name with **no `data/script/`
    prefix** and loaded **before** the VFS is mounted (see §4 — it is the file that decides whether
    the VFS is even mounted).
  - `data/script/*.lua` — VFS-resident config scripts, loaded **after** the VFS mount and resolved
    through the VFS by-name open. Known instances: `uiconfig.lua`, `display.lua`, `config.lua`,
    `tutor.lua`.
- **Magic / signature:** none. No file-level magic bytes, no version header, no checksum. The file
  is a Lua chunk and is passed verbatim to the Lua loader.
- **Endianness:** N/A (the artifact is text; the only structure is Lua syntax).
- **String encoding:** **CP949 / EUC-KR** (the legacy Korean code page). Both shipped samples carry
  CP949 comment lines (Lua `--` comments) that mojibake if read as Latin-1 / UTF-8 — read as CP949.
- **Compression / encryption:** none. Bytes go straight from the file to the Lua loader untouched.

---

## On-disk layout

There is **no binary header, no record stride, no fixed field offsets.** The on-disk artifact is a
**Lua 5.1 source chunk** (plain text). The only structure is Lua syntax. Endianness is not
applicable.

What the consuming C++ reads back is a **set of named Lua globals**, so the effective "schema" of
any given `.lua` is exactly **the set of global names the consuming code looks up** — not a fixed
on-disk layout. Different `.lua` files have entirely different "schemas" because different C++
consumers read different globals back. The known global-name sets are tabulated in §4.

Observed shapes (sample-verified, illustrative — not a layout contract):

- `uiconfig.lua` — one CP949 comment line followed by a single integer-global assignment
  (`NEW_SERVER_INDEX`).
- `display.lua` — dozens of `KEY = <float>` global assignments grouped under CP949 comment
  headers (a character-brightness / glow tuning family plus a few standalone keys).

These could equally legally be Lua expressions or functions; the binary handles function-valued
globals (§5).

---

## Read algorithm (the loader contract)

The whole subsystem is a singleton "Lua config manager" wrapping **one** `lua_State`:

1. **Manager construction (once).** Create the `lua_State` (Lua open/new-state), open the standard
   libraries (`luaL_openlibs`), then register one native C function into the global table under the
   name **`cpp_load`** (the include/require hook — see §5).

2. **Loading a file** (`Load(path)`):
   1. Open the file by name through the unified by-name file open used by every text-table loader
      (the same VFS / loose-disk open wrapper as `skin.txt`, `actormotion.txt`, `items.scr`,
      `npcs.scr`, etc.), in read mode.
   2. If the open succeeded and the size is non-negative:
      - Allocate `size + 1` bytes, read the full payload into the buffer, and **NUL-terminate**
        (`buffer[size] = 0`).
      - Run the buffer through the `lua_tinker`-style **dobuffer**: it pushes a protected-call error
        handler, then `luaL_loadbuffer(L, buffer, size, chunkname)` + `lua_pcall`. On error it reads
        the message off the stack and prints a `dobuffer()`-context diagnostic; it does not abort the
        client.
      - Free the buffer.
   3. Close the file. **No magic check, no checksum, no transform** — the bytes are handed straight
      to Lua.

   Effect: the file's globals are now **resident in the shared `lua_State`.** Because the state is
   shared and persistent, a later load can overwrite globals set by an earlier one, and a script can
   chain-load another via `cpp_load` (§5).

3. **Reading a value back** (the public `GetInt` / `GetFloat` accessors):
   - Push the global's name string, do a global-table lookup
     (`lua_gettable` against the standard Lua 5.1 globals pseudo-index `LUA_GLOBALSINDEX = -10002`),
     convert the top of stack to a number / float (or boolean), then pop.
   - **Function-valued config:** if the looked-up global is a Lua **function** (Lua type tag
     `LUA_TFUNCTION = 6`), the accessor instead pushes a single argument (an int or a string,
     depending on the accessor) and `lua_pcall`s it with one argument and one result, then reads the
     numeric result. If a non-function is encountered where a function call was requested, the
     "attempt to call global … (not a function)" diagnostic is printed.
   - A value read with the `!= 0` idiom is treated as a boolean (used for the `game.lua` mode flags
     in §4).

The standard Lua 5.1 constants are confirmed present at independent call sites — the globals
pseudo-index `LUA_GLOBALSINDEX = -10002` appears at all three of the read-global / set-global /
call-global sites, and the function type tag `LUA_TFUNCTION = 6` appears at the call site — which
corroborates that this is stock Lua 5.1, not a custom variant.

---

## Linkages — who references it, the join key, the consuming managers

There is **no foreign-key / table join** here. The "join key" is the **Lua global-name string** the
C++ consumer passes to `GetInt` / `GetFloat` / the function-call accessor. All `.lua` files share
the **one** singleton `lua_State`, so they are not independent of one another (load order and
`cpp_load` chaining decide final values).

### Consumers and the global keys they read back

| Consumer (role) | Loads | Global keys read back |
|---|---|---|
| Scene / boot state machine (WinMain-side) | `game.lua` (loose, **pre-VFS-mount**) | `vfsmode`, `launcher`, `debugmode` (each read as `!= 0` → bool). `vfsmode` selects the VFS mount mode and then drives the VFS mount itself. The same consumer later reads `DISPLAY_GAME_ADDICTION_WARNING_CHECK_TIME` from the already-loaded display config. |
| Login window scene builder | `data/script/uiconfig.lua` | `NEW_SERVER_INDEX` — selects which entry in the login server-list strip is the highlighted "new" server. Ties `uiconfig.lua` to the login server name-strip (captions are MessageDB ids 4001..4022). |
| Display / framerate config parser | `data/script/display.lua` | `DISPLAY_GLOW_RANGE_X`, `DISPLAY_GLOW_RANGE_Y`, `DISPLAY_FRAMERATE` (dead store — frame rate hard-coded to 60), `DISPLAY_BASE_BRIGHT_MULTI`, `DISPLAY_GLOW_BRIGHT_MULTI`, `DISPLAY_LIGHT_RATIO` (floats), `DISPLAY_POWERSHADER` (string), plus the full 72-key `DISPLAY_CHAR_BRIGHT_{MULTI,ADD}_{R,G,B}_<STATE>` and `DISPLAY_CHAR_BRIGHT_ALPHA_<STATE>` float family for character brightness / glow tuning. The 9 state suffixes are: `DEFAULT`, `CHOICE`, `HIT`, `ALPHA`, `HIDDEN`, `POISON`, `TYPE`, `ANGER`, `AUTO`. |
| Debug / VFS mode config | `data/script/config.lua` | `CONFIG_NO_VFS` (bool, mirrors the `game.lua` `vfsmode` decision family — VFS vs loose-disk), `CONFIG_DEBUG_LEVEL` (integer, CP949 comments indicate range 0–3). Consumer read-back site not yet chased; keys are sample-observed (MEDIUM confidence). |
| Game-addiction-warning panel | `data/script/tutor.lua` (resolved via a config global) | tutorial config — the specific global key set is **not yet captured** (no sample in the extract set). |

### Reverse / structural linkage

- **`cpp_load` (Lua global C function) → the manager's `Load`** — any `.lua` chunk may call
  `cpp_load("other.lua")` to recursively load and execute another `.lua` (the client's include /
  require mechanism). This is why load order and chaining matter.
- **`game.lua` is the root bootstrap:** it is loose-disk and pre-mount, and its `vfsmode` /
  `launcher` / `debugmode` flags decide whether the VFS is mounted at all and which run mode the
  client enters. The `data/script/*.lua` files only resolve **after** that decision mounts the VFS.
- **Shared state:** all `.lua` populate the **same** singleton `lua_State`; a later file's globals
  can shadow an earlier file's.

---

## Re-implementation notes (for Assets.Parsers)

- Treat a `.lua` as **Lua 5.1.2 source text in CP949**, executed by an embedded Lua VM — NOT as a
  binary blob and NOT as a flat `key = value` table. The `Assets.Parsers` side needs a Lua-5.1
  evaluation strategy (a real interpreter, or a tolerant evaluator covering the shipped files'
  subset: assignments, numeric / float / boolean literals, `--` comments, and the one-argument
  function-call accessor form), **not** a line parser.
- The loader contract is: read whole file → NUL-terminate → execute as a chunk into a **shared**
  global environment → read each config value by **global variable name** (as number / float /
  bool, or by calling it as a one-argument function).
- `game.lua` is the loose / pre-mount bootstrap and owns `vfsmode` / `launcher` / `debugmode`; the
  rest are VFS `data/script/*.lua`. Honour the shared-state + `cpp_load` include / override
  semantics for correct ordering behaviour.
- Cite this spec at any constant: `// spec: Docs/RE/formats/lua.md`.

---

## Known unknowns

- The exact internals of the **float accessor** (`GetFloat`) are assumed to be the same
  global-lookup + `lua_tonumber`-as-float path as the integer accessor; safe to assume but not fully
  dumped — marked UNVERIFIED.
- The **`config.lua`** consumer read-back site (the C++ code that calls `GetInt`/`GetFloat` for
  `CONFIG_NO_VFS` and `CONFIG_DEBUG_LEVEL`) has not been chased in IDA; the key names are
  sample-observed only (MEDIUM confidence). The relationship between `CONFIG_NO_VFS` and
  `game.lua`'s `vfsmode` flag is inferred from key naming and CP949 comments, not confirmed at
  the binary consumer.
- The **`tutor.lua`** global key set is not captured (no sample in the extract).
- Whether any **shipped** `.lua` actually uses the **function-valued** config path. The client
  supports it (the function-call accessors exist), but all three extracted samples (`uiconfig.lua`,
  `display.lua`, `config.lua`) use plain literals only.
- The **chunkname** passed to `luaL_loadbuffer` is confirmed: it is the literal string
  **`"lua_tinker::dobuffer()"`**, used for error-message context only. This is set inside the
  `dobuffer` runner and is identical for every config-script load.

---

## Cross-references

- Related formats: `config_tables.md` (the `.scr` / `.do` / `.xdb` binary config catalogues — note
  `game.lua`'s `vfsmode` is the flag that selects VFS vs loose-disk reads for those loaders too),
  `pak.md` (the VFS container the `data/script/*.lua` files resolve through).
- Glossary: see `Docs/RE/names.yaml` (the orchestrator owns proposed names for the Lua config
  manager, its `cpp_load` include hook, the `dobuffer` runner, and the global-read / function-call
  accessors).
- Provenance: see `Docs/RE/journal.md` (orchestrator-owned).
