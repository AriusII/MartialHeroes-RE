# RE Provenance Journal

This is the provenance audit trail for the clean-room reverse-engineering of the legacy
Martial Heroes client. Each entry records **what was examined**, **against which build**, **what
changed in the committed `Docs/RE/` corpus**, and **how the firewall was upheld**. It is a
derived-truth record: the single absolute authority remains the binary `doida.exe` observed in
IDA Pro (tier 1); these notes describe how the committed specs (tier 2) were brought into line
with it.

Entries are append-only and newest-first. The journal carries no decompiler output, no raw
autonames, and no addresses-as-truth — only neutral provenance.

---

## CYCLE 16 — VFS Master Manual, DiskFile Structure, and Format Loader Census (2026-06-27)

**Build under analysis:** `doida.exe`, sha256 `f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963` (imagebase `0x400000`).

**Method:** IDA static only. Sequence of targeted static queries via IDAPython and decompilation on the core VFS manager (`Diamond::CVFSManager`) and file abstraction (`Diamond::DiskFile`).

**Committed corpus change:**
- Created `Docs/RE/vfs/vfs_master_manual.md` containing the consolidated VFS specifications, including the `data.inf` index header and TOC stride layout, the 88-byte `DiskFile` in-memory structure map, the 3-branch file I/O dispatcher (`DiskFile_ReadBytes` at `0x60900d`), progress loading calculation, the 49 file formats census directory, and the cross-asset linkage rules.
- Updated `Docs/RE/vfs/README.md` to index the new Master Manual.
- Updated `names.yaml` with enriched function notes for VFS mount, lookup, and DiskFile read/open primitives.
- Sorted `names.yaml` function addresses globally.

**Firewall status:** Clean-room firewall intact. No decompiler snippets or proprietary symbol signatures committed; only neutral struct maps, logic flows, and function addresses were recorded in `Docs/RE/`.

---

## CYCLE 15 — Anti-Cheat, SEED-128/RSA Crypto Consumers, .bud Vertex Layout, .fx7 Format (2026-06-27)


**Build under analysis:** `doida.exe`, sha256 `f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963` (imagebase `0x400000`).

**Method:** IDA static only. All three analysis threads (anti-cheat, cryptography, asset formats) performed directly by the primary agent using MCP-hosted IDAPython scripts, pseudocode decompilation, and RTTI/cross-reference scanning. No debugger, no dynamic analysis.

**Symbols renamed this cycle: 34** (8 net-new inserts in names.yaml; remainder had prior names confirmed).

### Anti-Cheat Subsystem

Mapped the complete `GXProtect` / `GProtect` / `GGGProtect` class triad. RTTI confirmed three distinct vtables at `0x72E390`, `0x72E3B0`, `0x72E3D0`. The 3555 ms (0xDE3) integrity-monitor thread at `AntiCheat_MonitorThread` was located and fully decompiled: it calls `AntiCheat_CheckApiHooks` (compares saved IAT snapshots for `QueryPerformanceCounter`, `GetTickCount`, `timeGetTime`) and `AntiCheat_CheckNetworkHooks` (`WSASend`, `send`), then `AntiCheat_CheckDebuggerPresence` (PEB.BeingDebugged or PID leak). Fatal exit codes mapped: 1581 (timer hook), 2225 (network hook), 500 (debugger). `GXProtect__XTrapInit` passes a 232-char hex XTrap token to `XTrap__006CF610`. `GGGProtect__TimeDeltaQuery` and `GGGProtect__StackCookieCheck` documented. The `VFunc_00` slots across both `GProtect` and `GXProtect` operate as a **polymorphic UI panel factory** (GreetPanel, LoginPanel, ErrorPanel, etc.) — not protection code.

### Crypto Consumers

Confirmed two SEED-128 call-graph consumers: (1) `Net_EncryptOutboundPacket` (outbound cipher gate — passes to the keyless XOR-ROL-3-round documented in cycle 12, not raw SEED) and (2) `Secure_BuildSecureAuthReply` (RSA + PKCS#1 v1.5 login credential encryption with `VirtualProtect(PAGE_NOACCESS)` page guard). Mapped 11-function ADVAPI32 CryptoAPI wrapper cluster (`CryptWrap_*`) for server RSA+SHA-1 packet signature verification. Binary-wide RC4 S-box scan returned no standalone RC4 implementation — RC4 is absent from this binary.

### .bud Vertex Format (bytes 12–31 resolved)

The `.bud` MassObject vertex stride is 32 bytes per vertex = `D3DFVF_XYZ | D3DFVF_NORMAL | D3DFVF_TEX1`. Bytes 12–31 carry `norm_xyz` (3×f32) + `tex_uv` (2×f32). The AABB-computation code only reads bytes 0–11 (position); normals and UVs are consumed by the D3D renderer at draw time. LOD budget tiers documented (5 tiers from 90K to 3.24M).

### .fx7 Format

Fully mapped `Fx7_DecodeGroups` and `Map_BuildCellFxLayerFx7`. The `.fx7` file format is structurally identical to `.fx6`: `[u32 groupCount] + per-group [48B header + vertex_count × VF_32 + index_count × u16]`. In-memory group record stride is 112 bytes (0x70) with double-buffered vertex arrays. Texture registry holds up to 32 texture IDs. Groups are spatially binned into a 16×16 cell tile grid at 64 world-unit resolution.

**Committed corpus change:**
- Created `Docs/RE/specs/anticheat.md` — full GXProtect/GProtect/GGGProtect class hierarchy, vtable layouts, 3555 ms monitor thread, API hook detector, network hook detector, debugger check, XTrap init, log obfuscation.
- Updated `Docs/RE/specs/crypto.md` — Section 10: SEED-128 Feistel core confirmed, two consumers mapped, RSA auth chain documented, CryptAPI cluster inventoried, RC4 absence confirmed.
- Updated `Docs/RE/formats/terrain.md` — Section 17: `.bud` vertex layout (VF_32, 32B stride), per-object file read sequence, in-memory BudObject layout (116B stride), LOD budget tiers.
- Updated `Docs/RE/formats/terrain_layers.md` — Section 15: `.fx7` full format spec (48B group header, 112B in-memory record, VF_32 vertex, 16×16 tile grid, 32-slot texture registry).
- Updated `Docs/RE/names.yaml` — 8 new entries inserted; 26 existing entries confirmed present.

**Firewall status:** All raw pseudocode, addresses, and decompiler output remain in `Docs/RE/_dirty/`. Clean-room documents contain only neutral prose, structural tables, and offset listings.

---

## CYCLE 14 — VFS & GHTex Object Model Static RE & Symbol Promotion (2026-06-27)

**Build under analysis:** `doida.exe`, input sha256 `f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963` (imagebase `0x400000`).

**Method:** IDA static only, verified using parallel subagent workflows querying decompilation and references.

**Measured build delta:** Renamed 5 GHTex/GTextureManager related functions in the database: `0x52f610` (`Diamond_GTextureManager_dtor`), `0x52efce` (`Diamond_GTextureManager_ClearTextures`), `0x52f5db` (`Diamond_GTextureManager_FreeMapSentinel`), `0x52f4d8` (`Diamond_GTextureManager_GetTexture`), and `0x445761` (`Diamond_GHTex_deleting_dtor`). Mapped `Sod_CompileOutline` at `0x4912b0`.

**Committed corpus change:**
- Created `Docs/RE/structs/ghtex.md` mapping structure layouts and vtables of the `GHandle`/`GHTex` resource handles and `GTextureManager` cache registry.
- Created `Docs/RE/formats/cell_pre.md` documenting `.pre` sidecar format layouts (`*.bud.pre`, `*.sod.pre`) and offline collision compilation.
- Created `Docs/RE/formats/cell_post.md` documenting `.ted.post` backup layout and terrain copy-then-patch save workflow.
- Updated `Docs/RE/formats/mud.md` with verified `Mud_ReadBlob` loader internals, coordinate indexing math, all-zero BSS fallback defaults, and double-gated updates (10-minute timer / 2.0-unit movement threshold).
- Updated `Docs/RE/formats/tol.md` with re-verification banner and proof of complete runtime absence of `.tol` references.
- Updated `Docs/RE/vfs/io_subsystem.md` with VFS-to-GTextureManager cache registration details and offline compiling integrations.
- Updated `names.yaml` with the newly named/corrected functions.

**Firewall:** Clean-room firewall fully maintained. No raw decompiler pseudocode, address-as-truth, or internal binary names used. Raw research outputs placed strictly in the gitignored `_dirty/` directory (`ghtex_analysis.md`, `patches_analysis.md`, `mud_tol_analysis.md`).

---

## CYCLE 14 — VFS Subsystem Dossier created & GHandle/GHTex symbols synced (2026-06-27)

**Build under analysis:** `doida.exe`, input sha256 `f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963` (imagebase `0x400000`).

**Method:** IDA static only, verified using `domain_function_pseudocode` and IDAPython vtable mapping scripts in `_dirty/`.

**Measured build delta:** No binary modifications; mapped vtable structures of `Diamond::GHandle` (0x72ffc0) and `Diamond::GHTex` (0x72ffd8) to document the object model.

**Committed corpus change:**
- Created dossier directory `Docs/RE/vfs/` with 5 specifications documenting VFS container byte formats, runtime I/O subsystem mechanics, sub-asset extensions census, and linkage/consumer mappings.
- Re-aligned `names.yaml` with the live IDB by renaming `0x60addb` (`UpgradeResultPanel__VFunc_00_5` -> `Diamond_GHandle_deleting_dtor`) and adding new entries for `0x60adc8` (`Diamond_GHandle_dtor`) and `0x60afee` (`Diamond_GHTex_LoadWrapper`) in address-sorted order.

**Firewall:** Neutral, descriptive documentation only. No decompiler code blocks or internal signatures are copied into the committed files. All IDA script developments reside inside the gitignored `_dirty/` directory.

---
## CYCLE 14 — Re-anchor campaign opened: names.yaml reconciled & re-pinned to f61f66a9 (2026-06-27)

**Build under analysis:** `doida.exe`, input sha256 `f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963` (imagebase `0x400000`, 3,897,992 B, 25,801 functions). This SUPERSEDES the retired anchor `263bd994…` (3803 KB) as the canonical target, per maintainer direction — the open reconciliation item recorded in `specs/binary_coverage_map.md §8` (2026-06-26) is hereby **resolved: f61f66a9 is canonical; each spec re-pins to it as it is re-verified.**

**Method:** IDA static only (no debugger), driven by `_dirty/reverify-f61f66a9/rebuild_names.py` (read-only against the IDB; writes only `_dirty/` analytics + the committed glossary).

**Measured build delta:** the ~4 KB growth is one ~0x1000 data-page shift plus a ~128-byte `.text` code insertion. Data globals shifted +0x1000; **1846 of 1915** relocated functions shifted by a uniform **+0x80/+0x7e** (clean relocation, behavior unchanged — so the specs describing them stay valid, since specs cite offsets/constants, never addresses). The genuine change surface is small: 69 anomalous-shift functions (30 likely cross-build mis-matches), 116 real functions whose canonical name did not carry (incl. `Cipher_XorRolEncrypt`, `AuthSession_BuildLoginPacket43`), 258 synthetic vtable-slot labels reverted to defaults, ~9 net-new functions, 13 lost data globals.

**Committed corpus change:** `names.yaml` rebuilt FROM the live IDB — `binary:` re-pinned to f61f66a9 (`prior_sha256: 263bd994…`); `functions:` regenerated at current addresses (5129 entries = 3755 game + 1374 library-tagged), notes preserved by name-join; the eight non-functions sections (globals/crypto/client_mechanics/gameplay_systems/file_extensions/asset_constants/runtime_constants/opcodes) spliced through **verbatim**; `globals:` addresses re-anchored for the 20 carried symbols. **No spec `verification:`/`ida_anchor:` banner is re-stamped yet** — that happens per-spec inside the CYCLE 14 lane waves as each subsystem is re-verified.

**Firewall:** all recovery analytics (delta map, lost/suspect worklists, the rebuild script) are under `_dirty/reverify-f61f66a9/` (gitignored). The only committed write is the glossary `names.yaml` (addresses are permitted there by design) — no decompiler pseudo-C, no autonames, no addresses-as-truth crossed into any spec. Lane-routed worklists track every lost/suspect symbol so nothing is dropped.

## Promotion — Frontend debugger-session confirmations (2026-06-24)

**Build under analysis:** `doida.exe`, IDB sha256 `263bd994c927c20a38624cf0ca452eaef365057fa9db1543d8f668c14a6fd8ee` (imagebase `0x400000`).
**Method:** dirty→clean firewall promotion (re-promote Mode A) of a completed **live `?ext=dbg` debugger-session**
validation note (`_dirty/validation/2026-06-24-frontend-debugger-session.md`) into the committed front-end
specs. The note is debugger-authoritative; a static spot-check was unnecessary (facts confirmed live).
**Firewall:** rewrite-not-copy; no addresses, no decompiler identifiers/pseudo-C, no raw sample bytes beyond
neutral u32 version values; opcodes cited as (major,minor). Dirty source left intact.

**Committed specs amended (5) — surgical dated `2026-06-24 debugger-session` notes added, no restructuring:**

- `formats/game_ver.md` — added a "Server-side enter-game validation" section: a SECOND, server-side version
  check at enter-game (the server validates the transmitted `10 × index5 + 9` token and rejects on mismatch
  → inbound net handler → Error scene → CP949 "client version does not match server" modal → quit),
  distinct from the client-local login gate (msg 2204); recorded a second concrete on-disk witness
  (index5 = 2114 → token 21149). Five opaque field semantics still capture-pending.
- `scenes/scene_state_machine.md` — §3: the Select(4)→Error(7) version-mismatch edge is now
  debugger-confirmed AT THE ENTER-GAME STEP, server-driven, additional to the local login gate; the full
  live sequence 0→1→2→4 (Opening 3 SKIPped) →5 and teardown 5→7→6→8 debugger-confirmed end-to-end.
- `scenes/charselect.md` — §2.1: live `dbg_read` confirmed slot-record stride 880 with name@+568 (CP949),
  occupancy@+614, class@+620 (1=Musa/2=Salsu/3=Dosa/4=Monk), default-equip ids ≈+656; a 3/5-occupied
  sample; GAP recorded (the 3/1 CharacterList handler did not fire while slots were populated —
  delivery-timing vs NetHandler-persistence follow-up).
- `specs/character_creation.md` — §3.1: create is the SAME SelectWindow scene entered by a visibility
  toggle + preview swap (NOT a rebuild); added the buttonIdx→classId→KeyedNode-key→create-BGM table
  (BGM = 910062000+(class-1)*1000 on the category-0 slot, replacing scene BGM; KeyedNode key = (class%4)+1).
- `specs/login_flow.md` — §4.2a: the PIN keypad re-scramble-on-show (screen position ≠ digit value) and
  the worker-thread server-roster fetch are now debugger-confirmed (previously static CAMPAIGN-9).

**Open follow-up carried:** exact `3/1` CharacterList delivery timing vs NetHandler persistence
(charselect §2.1 GAP).

---

## CYCLE 13 — Static cartography & corpus re-verification sweep (2026-06-24)

**Build under analysis:** `doida.exe`, IDB sha256 `263bd994c927c20a38624cf0ca452eaef365057fa9db1543d8f668c14a6fd8ee`
(MD5 `a1437026e6eefeba94702909cd9a33b9`, imagebase `0x400000`, 25,792 functions, 6 segments).
**Method:** IDA Pro 9.3 via the IDA MCP, **static analysis only — no debugger** was used this cycle.
**Tooling:** four multi-agent Workflows, each massively parallel internally, running the clean-room
pipeline *IDA read → neutral `_dirty/` notes → clean rewrite into committed docs*. The archive
front cross-checked the on-disk bytes of the flat-extracted `data.vfs` (`extract/data/`) against the
loader/parser routines in the binary.

**Scope & deliverables:**

- **Archive cartography (WF-A):** censused `extract/data/` (43,347 VFS entries / ~3.8 GB across 47+
  `mapNNN` zones plus the global `char`/`effect`/`item`/`ui`/`cursor` trees), verified/extended ~44
  format families against on-disk bytes + their parsers, and authored the master archive guide at
  `extract/ARCHIVE.md` (local-only; the `/extract/` tree is gitignored per non-distribution rules).
- **Binary spec-audit (WF-B):** audited 13 subsystem clusters (network/dispatch, crypto, VFS/resource,
  actor/world, combat/stats/skills, inventory/item/trade, UI/GUComponent, scene-lifecycle,
  rendering/terrain/camera, Lua, quests/NPC/social, sound/effects, skinning) against ground truth.
- **Packet byte-audit (WF-C):** verified the ~190 committed packet YAMLs byte-exact against their
  handlers across 14 opcode-family lanes and reconciled `opcodes.md`.
- **Coverage cartography (WF-D):** ran 12 multi-modal discovery probes over the ~18.7k unnamed
  functions (import clusters, call-graph neighborhoods, string themes, RTTI/vtable groups) and
  authored the new `specs/binary_coverage_map.md`.

**Key binary-wins corrections (tier 1 overrode the spec):**

- `specs/handlers.md` — dispatch-table install counts corrected to **102 stores / 100 occupied slots /
  99 distinct handlers / 2 NULL slots (minors 0 and 27) / 65 Push stores** (prior "98/96" figures were
  stale).
- `structs/net_client.md` — three offset drifts corrected: init/connected gate `+0x141FC → +0x14178`,
  `secure_context_ptr` `+0x141F8 → +0x141B8`; `worker_stop_event` HANDLE confirmed at `+0x141B4`.
- `specs/network_dispatch.md` — reassembly buffer geometry documented; keepalive timer proven to be
  **5000 ms** on most branches, **10000 ms** on the state-2 non-clear branch.
- `structs/secure_context.md` / `specs/crypto.md` — RSA pad-block size source and modexp argument order
  confirmed; the keyed **SEED-128 16-round Feistel** block cipher documented as non-wire (anti-cheat /
  config scope, out of `Network.Crypto`).
- `specs/vfs_overview.md` / `specs/asset_pipeline.md` — **`bgtexture` "kind" polarity corrected** (was
  inverted): `kind == 1` ⇒ static render object; other non-zero ⇒ non-static (scroll/animated);
  `0` ⇒ skipped.
- `formats/pak.md` — `CreateFileA` open flags `0x10000001`
  (`FILE_FLAG_RANDOM_ACCESS | FILE_ATTRIBUTE_READONLY`) confirmed for both `data.inf` and `data.vfs`;
  mount via `CVFSManager` (24-byte index header + flat 144-byte TOC, lowercase-normalized
  binary-search lookup, no compression/encryption).
- `formats/cell_exd.md` — the prior "no `.exd` loader in the shipped client" verdict **refuted**: the
  cell `.map` EXTRA_TERRAIN block drives a confirmed EXD triangle decoder (4-byte count, 40-byte
  records, per-record XZ bbox + normalized plane via `cross(v0−v1, v2−v1)`).
- `formats/sod.md` — CYCLE-12 "REFUTED/DEBUGGER-PENDING" verdict on the QuadRecord `+0x10..+0x2C`
  field table **overturned** via on-disk float arithmetic; multi-solid ordering CONFIRMED.
- `formats/terrain_layers.md` — `.fx7` decoder upgraded to CONFIRMED (zero-residual sample parse).
- `opcodes.md` + packet YAMLs — **26 catalog rows changed** (7 handler renames incl. `2/6
  CmsgStanceSwap → CmsgEmote`, `2/137 → CmsgPostureToggle`, `2/141 → CmsgCubeGambleSubmit`,
  `4/40 → SmsgMotionWeaponFxSwap`, `4/75 → SmsgProductPurchaseResultPanel`,
  `4/78 → SmsgRentalItemExpirySweep`; 16 size resolutions of `var → fixed`); opcode `2/19`
  dual-ownership retired in favor of `2-19_npc_buy_or_acquire.yaml`; `2/5` Context field and `2/44`
  five-written-bytes layout corrected.

**New committed doc:** `specs/binary_coverage_map.md` — ~5,110 named functions (~20 %), ~18.7k
unnamed; an estimated 35–45 % of the function count is covered by at least one committed spec;
**34 dark/un-mapped clusters** catalogued as a prioritized RE backlog with 7 escalations.

**Open items carried forward:** the prioritized gap backlog in `specs/binary_coverage_map.md`
(notably AI/pathing, anti-cheat internals, and the map-level `.bin` descriptor family flagged in
`formats/terrain_scene.md`); `names.yaml` glossary not yet synced to the CYCLE 13 opcode renames.

**Firewall & integrity:** clean-room upheld throughout — every analyst wrote only neutral
`_dirty/` notes; clean rewrites carry no `sub_`/`loc_`/`_DWORD`/`__thiscall`/raw-address tokens
(full corpus scrub passed). **No `.cs` source was modified.** No originals were committed. All edited
docs carry a `verification:` banner pinned to IDB sha256 `263bd994…`.

---

## Promotion — VFS & Cell Asset Cartography verification sweep (2026-06-27)

**Build under analysis:** `doida.exe`, IDB sha256 `f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963` (imagebase `0x400000`).
**Method:** Static analysis and verification of VFS and cell asset loaders (`.bud`, `.fx7`) via IDA Pro MCP server.
**Firewall:** Clean-room firewall maintained. All intermediate scripts and decompiler dumps remain in gitignored `_dirty/`. Spec documents updated with neutral format definitions and structure maps.

**Committed specs updated:**
- `formats/terrain.md` — Updated `.bud` static building mesh vertex format. Verified the 32-byte layout as FVF `0x112` (`D3DFVF_XYZ | D3DFVF_NORMAL | D3DFVF_TEX1`) where bytes 12–31 carry surface normals (3×f32) and texture UV coordinates (2×f32). Updated the `In-Memory BudObject Layout` table offsets to reflect verified C++ structures: `aabb_min` (+0x14), `aabb_max` (+0x20), `vtx_bytes` (+0x30), `lod_budget` (+0x40), `type_byte` (+0x34), `texture_type` (+0x46), `anim_scale` (+0x5C), and `anim_pivot` (+0x68).
- `formats/terrain_layers.md` — Updated `.fx7` terrain layer format. Verified the 48-byte group header layout and the 32-byte VF_32 vertex format. Updated the `In-Memory Group Record` offsets: `aabb_min` (+0x3C), `aabb_max` (+0x48), `vtx_byte_size` (+0x58), and `lod_budget` (+0x68). Documented the 16×16 spatial binning grid, including min/max Y accumulators (`g_TileMinY` and `g_TileMaxY`) and the texture index registry limits (max 32 slot-based texture IDs).
- `journal.md` — Added this journal entry documenting the promotion.
