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
