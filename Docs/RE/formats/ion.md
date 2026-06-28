# Format: `.ion`  (effect-texture source descriptor — art-tool output; NOT loaded by the shipped client)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Promoted from dirty-room notes under EU Software Directive 2009/24/EC Art. 6, solely to
> achieve interoperability. No decompiler output and no binary addresses appear below.
> Consumed (optionally) by Assets.Parsers — but see the HEADLINE: the shipped client does
> NOT read this format at runtime. Every offset an engineer cites must reference this file:
> `// spec: Docs/RE/formats/ion.md`

---

## Status

```
verification:   parser-verified (absence proof) + sample-verified (layout only)
ida_reverified: 2026-06-24
ida_reverified: 2026-06-27
# CYCLE 14 re-anchor (f61f66a9): confirmatory - descript.ion confirmed not loaded (zero .ion / descript.ion literals); bmplist.lst path present and cleanly relocated, 1 re-confirmed SAME, 0 corrected
ida_anchor:     f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
evidence:       [static-ida (absence proof), vfs-sample]
conflicts:      none-open
# Runtime-DEAD is parser-verified: an exhaustive string + raw-byte scan of the shipped client
#   for the `descript.ion` filename, the `.ion` extension, and a `%s/descript`-style path builder
#   returns ZERO hits — no loader exists. The per-record field LAYOUT below is sample-verified
#   structurally (one real `descript.ion` instance, 29 bytes) but the three NUMERIC field MEANINGS
#   are UNVERIFIABLE — there is no runtime parser whose behavior could pin them down.
#   CYCLE 2026-06-24: field_c = 17 exactly matches the name byte-length in the sole sample;
#   promoted from "unknown flag" to "plausible: name byte-length" (MEDIUM — single sample).
#   .mayaswatches sibling claim removed: a full listing of data/effect/texture/ (1 458 files)
#   shows no .mayaswatches file; co-resident siblings are .tga sources, bgtexture.lst/txt,
#   and descript.ion only.
```

> **⚠️ HEADLINE — the shipped client does NOT load `.ion`.** `descript.ion` is a **per-directory
> art-tool descriptor** that rides alongside the source `.tga` files inside `data/effect/texture/`.
> It is an **asset-pipeline / authoring artifact**, not a client-consumed format. The runtime obtains
> its effect textures from a **different** file — the binary `data/effect/bmplist.lst` (with the human
> text twin `bmplist.txt`) — never from `descript.ion`. **Do NOT wire a `.ion` loader in
> `Assets.Parsers`.** The load-bearing runtime format documented here is `bmplist.lst` (Section 3);
> `.ion` is recorded only so no engineer mistakes it for a runtime descriptor.

---

## Identification

- **Extension:** `.ion`
- **Canonical instance:** `data/effect/texture/descript.ion` (a single per-directory descriptor; only
  one instance exists in the VFS).
- **Container:** plain text-with-binary-tail — an ASCII filename, a space, three little-endian
  numeric fields, terminated by a `CRLF` line. No magic, no header, no count prefix.
- **Endianness:** little-endian (consistent with the 32-bit x86 client and with `bmplist.lst`).
- **Companions in the same directory:** the `.tga` source textures it names, together with
  `bgtexture.lst` and `bgtexture.txt` (the terrain-texture index family — a distinct format; see
  `bgtexture_lst.md`). Their co-presence confirms `data/effect/texture/` is the **art-source**
  directory, not a runtime pack. (An earlier note citing `.mayaswatches` was incorrect — no such
  file is present in the shipped VFS extract.)

---

## On-disk layout (`descript.ion`)

One logical record per line, `CRLF`-terminated. Recovered from the single 29-byte sample; the stride
across siblings could not be confirmed because only one `.ion` exists on disk.

| Field     | Type                    | Bytes | Meaning                                            |
|-----------|-------------------------|------:|----------------------------------------------------|
| `name`    | ASCII, space-terminated |   var | source texture filename (e.g. a `…-NN.tga` name)   |
| `sep`     | `u8` (`0x20`)           |     1 | space delimiter                                    |
| `field_a` | `u32` LE                |     4 | UNVERIFIED (see note)                              |
| `field_b` | `u32` LE                |     4 | UNVERIFIED (see note)                              |
| `field_c` | `u8`                    |     1 | plausible: byte-length of `name` (MEDIUM — one sample exact match; see note) |
| `eol`     | `CRLF`                  |     2 | line terminator (`0x0D 0x0A`)                      |

The numeric tail is fixed at **9 bytes** (`u32` + `u32` + `u8`) between the space and the `CRLF`; the
`name` field is variable-length and ends at the first space.

**Note on `field_a` / `field_b` / `field_c` (meanings unverifiable — no runtime parser exists):**
- `field_a` and `field_b` are **not** the `.tga` file size (witnessed values do not match the named
  texture's on-disk size). Interpreting the pair as a 64-bit `FILETIME` (in either word order) yields
  implausible timestamps (far future or post-shutdown year), so neither reading holds. Treat them as
  **structure-known, meaning-unknown** — plausible candidates: tool-internal tokens, a split 64-bit
  id, or editor metadata.
- `field_c` **plausibly equals the byte-length of `name`**: in the sole sample, `field_c = 17` and the
  ASCII name is exactly 17 bytes (`hit-ring05-01.tga`). This is a single-sample correlation (MEDIUM
  confidence); it cannot be verified against the client because nothing in the client parses this file.
- All three numeric fields remain **structure-known, meaning-unverified**: there is no runtime loader
  from which to pin semantics.

---

## Read algorithm

**There is no runtime read algorithm — the shipped client never opens `.ion`.**

If the file's own grammar is ever reconstructed for preservation/tooling **only** (not for the port):

1. Read one `CRLF`-terminated line.
2. Split on the first `0x20` (space): everything before it is the ASCII `name`.
3. The remaining 9 bytes are `{ u32 LE field_a, u32 LE field_b, u8 field_c }`.

This grammar is **sample-only** and **unverifiable** against the client.

---

## Section 3 — the ACTUAL runtime effect-texture pipeline (`bmplist.lst`)

This is the load-bearing answer for "effect-texture descriptor": the descriptor the engine truly uses.
`.ion` is parallel art metadata; **`bmplist.lst` is the runtime list.**

### `bmplist.lst` — binary effect-texture name pool

Read once at boot by the effect-asset loader (the routine that primes the effect subsystem). A second
near-identical loader variant exists for the same list.

**Read algorithm (neutral):**
1. Open `data/effect/bmplist.lst` through the VFS disk-file wrapper.
2. Read a leading `u32` count `N`.
3. Loop `N` times: read one fixed-width **30-byte (`0x1E`)** NUL-padded ASCII name record; build the
   full path by concatenating the fixed prefix `data/effect/texture/` with the name.
4. Allocate and construct one effect texture handle (the engine's `GHTex` object, see below) for that
   path and push it into the effect manager's texture pool. The **list index = the runtime texture id**.
5. Close, then continue the effect-asset boot chain (Section 4).

**`bmplist.lst` on-disk layout** (sample-verified — `4 + count*0x1E` equals the file size exactly):

| Offset | Type            | Field   | Notes                                                   |
|-------:|-----------------|---------|---------------------------------------------------------|
| `0x00` | `u32` LE        | `count` | number of name records `N`                              |
| `0x04` | `record[count]` | `names` | each record = **30 (`0x1E`) bytes**, NUL-padded ASCII   |

- **Record stride:** 30 bytes. Names are ASCII (CP949-safe; no multibyte was observed).
- A texture witnessed in a `descript.ion` is **also present in `bmplist.lst`** — confirming the runtime
  sources that texture from `bmplist`, not from `.ion`.

### `bmplist.txt` — the human/text twin

`data/effect/bmplist.txt` is the text counterpart: **line 1 = count**, then per entry two lines
`<index>` then `<name>`. It can drift slightly stale relative to the binary `.lst` (the witnessed text
count was lower than the `.lst` count), so the **binary `.lst` is authoritative** for runtime.

### `GHTex` — the effect texture handle (factory target)

Each `bmplist.lst` name is materialized into one engine effect-texture handle object (the `GHTex`
texture handle, **76 bytes / `0x4C`**), constructed with the built `data/effect/texture/<name>` path.
Field hints recovered from its constructor (semantics partial):

- `+0` — vtable pointer (the texture-handle class).
- `+4` — embedded string (the built texture path).
- `+36` — registration flag; when set, the handle is enrolled into the shared effect-texture pool.
- `+52` — pool slot handle (`-1` sentinel until enrolled).
- `+56` — second resource handle (`-1` sentinel until bound).
- `+60` — source size/cap parameter.
- `+64` / `+68` — cleared bound-resource handles.
- `+72` — back-pointer into the texture pool.

### `.xeff` / `.xobj` — the other consumers of `data/effect/texture/`

The effect-particle formats bind their billboard/particle textures by **name** into the same
`data/effect/texture/` directory: the `.xeff` parser (magic `XEFF`) and the `.xobj` mesh path both
build `data/effect/texture/<name>` (appending `.tga` for texture bindings). See `xobj.md` and the
effect subsystem overview in `effects.md` Section A for the full particle/mesh load chains.

---

## Section 4 — effect-asset boot order (context)

The effect subsystem primes its assets at boot in this order (recorded for the effect-format family):

```
bmplist.lst → xobj.lst → xeffect.lst (+ effect.cache prime) → totalmugong.txt
            → itemjointeff.txt → mobjointeff.txt → itemswordlight.txt → mobswordlight.txt
```

`descript.ion` is **not** part of this chain.

---

## Linkages (join keys)

- **`.ion` → `.tga` (name join, NON-runtime):** a `descript.ion` record names a `.tga` by **filename**;
  that `.tga` lives in the same `data/effect/texture/` directory. This join is **art-tool-only** —
  `.ion` participates in **no runtime join**.
- **`bmplist.lst` → texture (the runtime join):** `bmplist` index → 30-byte name →
  `data/effect/texture/<name>` → effect texture handle (`GHTex`). The `bmplist` index **is** the
  effect texture id used elsewhere.
- **`.xeff` / `.xobj` → texture:** both bind into `data/effect/texture/` by name (+`.tga`); they
  reference effect textures by the same names that `bmplist.lst` enumerates. See `xobj.md`.

**Runtime builder / factory / manager (the real consumers — `.ion` has none):**
- The effect-asset boot loader (reads `bmplist.lst`, drives the boot chain in Section 4).
- The effect texture-handle factory (the `GHTex` constructor).
- The `.xeff` texture binder (magic `XEFF`) and the `.xobj` manifest/mesh loaders.

---

## Engineering guidance (for the C#/Godot port)

- **Do NOT** add a `.ion` parser to `Assets.Parsers`. It is runtime-ignored art metadata.
- The promotable, load-bearing effect-texture format is **`bmplist.lst`** (Section 3): `u32` count +
  `count`×30-byte NUL-padded ASCII names → `data/effect/texture/<name>` → effect texture handle.
- If the `formats/` tree lacks a dedicated `bmplist` / effect-texture-list spec, that is the real gap
  to fill; this document captures the layout in the interim. For the broader effect subsystem, see
  `effects.md` (Section A) and `xobj.md`.

---

## Cross-references

- `xobj.md` — `.xobj` effect primitive mesh + its `xobj.lst` manifest (same `data/effect/` family).
- `effects.md` — effect subsystem overview (`.xeff`/`.xobj`/particle paths, boot order).
- `texture.md` — the underlying texture/image format the named `.tga` source files target.
- `tol.md` — sibling precedent for a tool-side artifact the shipped client does not load.
