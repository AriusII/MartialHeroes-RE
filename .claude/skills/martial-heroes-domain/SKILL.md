---
name: martial-heroes-domain
description: Use when working with Martial Heroes (D.O. Online) protocol, opcodes, frame headers, the cipher, LZ4 payloads, CP949 text, or the recovered render asset chains (terrain .ted/.map/bgtexture/.dds, character .skn/skin.txt/.bnd/.mot, .arr spawns, .sod collision) and coordinate conventions — a neutral INDEX pointing at CLAUDE.md and the committed Docs/RE specs.
user-invocable: false
paths: Docs/RE/**
---

# martial-heroes-domain

Neutral index of the recurring Martial Heroes (originally *D.O. Online*) technical conventions.
It restates only facts already public in `CLAUDE.md` and the committed `Docs/RE/` specs, and points
there for authoritative detail. It adds **no new reverse-engineered fact** and contains no decompiler
output or copyrighted data. For anything not below, read the cited spec — do not infer.

## Ground truth
Every fact below was **proved in `doida.exe` via IDA** and committed to the cited `Docs/RE/` specs
(`formats/`, `packets/`, `structs/`, `specs/`, `opcodes.md`) — those are authoritative; this skill only
**indexes** them and is never itself the source. To locate any spec not summarized below, start at
`Docs/RE/INDEX.md` — the navigable entry point to the corpus (164 specs; by-subsystem, by-file-extension,
and by-runtime-struct maps plus the definitive-negatives list). When the binary and a spec disagree, the binary wins
(fix the spec). C#/Godot are judged by fidelity to IDA + these specs — the official captures are the
oracle for rendered pixels.

## Wire protocol — see `Docs/RE/opcodes.md`, `Docs/RE/packets/*.yaml`, `Docs/RE/specs/`

- **Opcode** = packed `(major << 16) | minor`. Catalogue (no addresses): `Docs/RE/opcodes.md`.
- **Frame header** = 8 bytes: `[u32 size][u16 major][u16 minor]`, then payload.
- **Cipher** = a rolling XOR/ROL scheme (shape only here). Algorithm: `Docs/RE/specs/crypto.md`.
- **Payload** = LZ4 raw-block compression.
- **Routing** = source-generated opcode→handler switch (no reflection); handler map in
  `Docs/RE/specs/handlers.md`.
- **Wire-field layouts** are per-packet in `Docs/RE/packets/*.yaml`; subsystem behaviour in
  `Docs/RE/specs/*.md` (login, combat, chat, inventory, skills, quests, …).

## Text encoding

- **All game text is CP949 (Korean).** Register once:
  `Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)`, then `Encoding.GetEncoding(949)`.

## Recovered asset chains (render path) — see `CLAUDE.md` "Recovered asset mappings" + `Docs/RE/formats/*.md`

- **Terrain texture:** cell `.ted` `TextureIndexGrid` byte → cell `.map`
  `TERRAIN/BUILDING TEXTURES[idx-1].intTexId` → `bgtexture.LST[id]` → `data/map000/texture/<rel>.dds`.
  Textures are **global under `map000`** for every area. `bgtexture.LST` is the binary runtime index
  (u32 count + 48-byte records); `bgtexture.txt` is an authoring mirror only, never the live path. See
  `Docs/RE/formats/terrain.md`, `bgtexture_lst.md`, `terrain_layers.md`.
- **Character skin:** `.skn` `IdA` → `data/char/skin.txt` → `tex_id` → `data/char/tex{...}/{id}.png`.
- **Character bind/idle:** deform skeleton = `data/char/bind/g{SkinClassId}.bnd`, `SkinClassId ∈ {1,2,3,4}`
  (the `.skn` header class — only `g1..g4.bnd` exist; the engine selects via the AnimCatalog map keyed by
  the appearance-slot `IdB`). Idle motion = `actormotion.txt` `motion_ids_a[1]` (**column 16 / record `+0x44`**;
  col 15 / `+0x40` is dead). `.mot` clips are registered from `motlist.txt`, keyed by the `.mot` header id —
  there is **no** `g{id}.mot` printf. See `Docs/RE/formats/animation.md`, `Docs/RE/specs/skinning.md`.
- **Spawns:** `npc{tag}.arr` = 28-byte records; `mob{tag}.arr` = 20-byte records. See
  `Docs/RE/formats/npc_spawns.md`.
- **Collision:** `.sod` = 2D XZ wall segments (**segment-intersection, NOT ray-parity**). Ground height from
  `.ted` **per-triangle plane interpolation** (NOT 4-corner bilinear lerp — the sampler finds the sub-quad
  triangle, then evaluates its plane equation). See `Docs/RE/formats/sod.md`, `terrain.md`.
- **VFS / container layout:** `Docs/RE/formats/pak.md`; table formats in `config_tables.md`,
  `sound_tables.md`, `ui_manifests.md`.

## Coordinate conventions (get wrong → world mirrors) — see `CLAUDE.md` "Coordinate conventions"

- **World geometry negates Z** (`Helpers/WorldCoordinates.ToGodot`: `(x,y,z) → (x,y,-z)`).
- **Mesh-local `.skn` geometry negates X.**
- Cells are **1024 units**, on a **65×65** grid, spacing **16**.

## Firewall

This skill is committed and clean-room-neutral: it only mirrors `CLAUDE.md` / committed `Docs/RE/`
specs. Never extend it with Hex-Rays pseudo-C, decompiler autonames (`sub_`/`loc_`/`_DWORD`/
`__thiscall`/mangled), raw addresses, or copyrighted bytes. New RE detail belongs in the specs, not
here.
